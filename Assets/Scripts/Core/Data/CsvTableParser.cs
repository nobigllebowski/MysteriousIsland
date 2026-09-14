using System.Collections.Generic;
using System.Text;

namespace ForgottenIsle.Core.Data
{
    /// <summary>
    /// Reads the two-column <c>key,value</c> CSV that carries Vardholm's string tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY CSV and not JSON for the shipped string table: translators work in spreadsheets. A format that
    /// opens in Excel, Numbers or Sheets and survives a round trip through them is worth more than a format
    /// that is tidier to parse, because the alternative is a translator hand-editing braces and shipping a
    /// file the game cannot load.
    /// </para>
    /// <para>
    /// WHY malformed rows are skipped instead of throwing: a string table is the one asset most likely to
    /// be edited by someone who is not a programmer, and it is loaded during boot. One bad row must cost
    /// the game one <c>#key#</c> placeholder, not a black screen. Everything that parsed is returned, and
    /// the rows that did not are simply absent — <c>StringTableLocalization</c> then reports them through
    /// <c>LogCode.MissingLocKey</c> the first time the UI asks for one, which is exactly the signal a
    /// tester needs.
    /// </para>
    /// <para>
    /// Dialect handled: a UTF-8 BOM at the head of the file, CRLF and LF line endings, blank lines,
    /// whole-line <c>#</c> comments, double-quoted fields containing commas or newlines, and <c>""</c> as
    /// an escaped quote inside a quoted field.
    /// </para>
    /// </remarks>
    public static class CsvTableParser
    {
        /// <summary>U+FEFF, written as an escape so the constant itself is visible in a diff.</summary>
        private const char ByteOrderMark = '\uFEFF';

        /// <summary>
        /// Parses <paramref name="csvText"/> into ordered key/value pairs.
        /// </summary>
        /// <param name="csvText">
        /// The whole file as text. Null or empty yields an empty list.
        /// </param>
        /// <returns>
        /// Every well-formed row, in file order. Never null. Duplicate keys are preserved in order and
        /// left for the consumer to resolve — <c>StringTableLocalization.AddTable</c> takes the last one,
        /// which is what makes an override table appended after the base table work.
        /// </returns>
        public static List<KeyValuePair<string, string>> Parse(string csvText)
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(csvText))
            {
                return rows;
            }

            var text = csvText;
            var position = 0;

            // A BOM survives every text editor a translator is likely to use, and would otherwise become
            // part of the very first key — a bug that shows up as exactly one missing string.
            if (text[0] == ByteOrderMark)
            {
                position = 1;
            }

            var buffer = new StringBuilder();

            while (position < text.Length)
            {
                position = SkipBlankAndCommentLines(text, position);
                if (position >= text.Length)
                {
                    break;
                }

                string key;
                if (!TryReadField(text, ref position, buffer, true, out key))
                {
                    // Unterminated quote: the rest of the file is one runaway field, so there is nothing
                    // left to recover. Stop rather than emit garbage rows.
                    break;
                }

                key = key.Trim();

                // A row must have a separator. Without one there is no value, so the row is malformed.
                if (position >= text.Length || text[position] != ',')
                {
                    position = SkipToNextLine(text, position);
                    continue;
                }

                position++; // consume the separator

                string value;
                if (!TryReadField(text, ref position, buffer, false, out value))
                {
                    break;
                }

                position = SkipToNextLine(text, position);

                if (key.Length == 0)
                {
                    continue;
                }

                rows.Add(new KeyValuePair<string, string>(key, value));
            }

            return rows;
        }

        /// <summary>
        /// Advances past any run of empty lines, whitespace-only lines and <c>#</c> comment lines.
        /// </summary>
        /// <remarks>
        /// A comment marker is only honoured as the first non-whitespace character of a line. Mid-line it
        /// is ordinary text, because <c>#</c> is a perfectly reasonable character to appear in a
        /// translation — and because the missing-key placeholder itself is spelled <c>#key#</c>.
        /// </remarks>
        private static int SkipBlankAndCommentLines(string text, int position)
        {
            while (position < text.Length)
            {
                var scan = position;
                while (scan < text.Length && (text[scan] == ' ' || text[scan] == '\t'))
                {
                    scan++;
                }

                if (scan >= text.Length)
                {
                    return text.Length;
                }

                var c = text[scan];
                if (c == '\n')
                {
                    position = scan + 1;
                    continue;
                }

                if (c == '\r')
                {
                    position = scan + 1;
                    if (position < text.Length && text[position] == '\n')
                    {
                        position++;
                    }

                    continue;
                }

                if (c == '#')
                {
                    position = SkipToNextLine(text, scan);
                    continue;
                }

                return scan;
            }

            return position;
        }

        /// <summary>Moves the cursor past the end of the current line, consuming a CRLF pair as one break.</summary>
        private static int SkipToNextLine(string text, int position)
        {
            while (position < text.Length && text[position] != '\n' && text[position] != '\r')
            {
                position++;
            }

            if (position < text.Length && text[position] == '\r')
            {
                position++;
                if (position < text.Length && text[position] == '\n')
                {
                    position++;
                }

                return position;
            }

            if (position < text.Length && text[position] == '\n')
            {
                position++;
            }

            return position;
        }

        /// <summary>
        /// Reads one field, quoted or bare.
        /// </summary>
        /// <param name="stopAtComma">
        /// True for the key field, which ends at the first comma. False for the value field, which runs to
        /// the end of the line: a bare value may therefore contain commas, so
        /// <c>ui.hint.tide,Wait for the tide, then cross</c> reads the way its author intended instead of
        /// being silently truncated at the second comma.
        /// </param>
        /// <returns>
        /// False only when a quoted field never closes, which makes the remainder of the file
        /// unparseable. A bare field always succeeds, even when empty.
        /// </returns>
        private static bool TryReadField(string text, ref int position, StringBuilder buffer, bool stopAtComma, out string field)
        {
            // Leading spaces before an opening quote are formatting, not content.
            var scan = position;
            while (scan < text.Length && (text[scan] == ' ' || text[scan] == '\t'))
            {
                scan++;
            }

            if (scan < text.Length && text[scan] == '"')
            {
                position = scan;
                return TryReadQuotedField(text, ref position, buffer, out field);
            }

            return ReadBareField(text, ref position, stopAtComma, out field);
        }

        /// <summary>
        /// Reads a <c>"..."</c> field, turning each <c>""</c> into one literal quote and preserving
        /// commas, whitespace and newlines inside the quotes verbatim.
        /// </summary>
        /// <remarks>
        /// Whitespace inside quotes is deliberately NOT trimmed. Quoting is the only way an author can ask
        /// for a leading or trailing space in a translation — a separator before a unit, say — and
        /// trimming it would make that request unexpressible.
        /// </remarks>
        private static bool TryReadQuotedField(string text, ref int position, StringBuilder buffer, out string field)
        {
            position++; // consume the opening quote
            buffer.Length = 0;

            while (position < text.Length)
            {
                var c = text[position++];
                if (c != '"')
                {
                    buffer.Append(c);
                    continue;
                }

                if (position < text.Length && text[position] == '"')
                {
                    buffer.Append('"');
                    position++;
                    continue;
                }

                // Closing quote. Anything between it and the separator or line end is stray formatting.
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
                {
                    position++;
                }

                field = buffer.ToString();
                return true;
            }

            field = string.Empty;
            return false;
        }

        /// <summary>
        /// Reads an unquoted field up to a comma (keys) or the end of the line (values), trimming the
        /// surrounding whitespace that spreadsheet exports add around separators.
        /// </summary>
        private static bool ReadBareField(string text, ref int position, bool stopAtComma, out string field)
        {
            var start = position;
            while (position < text.Length)
            {
                var c = text[position];
                if (c == '\n' || c == '\r')
                {
                    break;
                }

                if (stopAtComma && c == ',')
                {
                    break;
                }

                position++;
            }

            field = text.Substring(start, position - start).Trim();
            return true;
        }
    }
}
