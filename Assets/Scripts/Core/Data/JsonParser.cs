// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Data/JsonParser.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Data; surrogate-pair handling added to
// \uXXXX unescaping so a save containing an emoji or CJK extension character round-trips byte-for-byte
// through SaveCodec (ADR-0013). Scanning strategy, error positions and public API are unchanged.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForgottenIsle.Core.Data
{
    /// <summary>
    /// Thrown when a document is not well-formed JSON.
    /// </summary>
    /// <remarks>
    /// WHY parsing throws while nearly everything else in Core returns a <c>ResultCode</c>: a malformed
    /// document has no partial reading. Callers that must not fail — the save loader in particular — catch
    /// this at their single entry point and translate it into <c>ResultCode.SaveCorrupt</c>, so the
    /// exception never escapes into gameplay code.
    /// </remarks>
    public sealed class JsonParseException : Exception
    {
        /// <summary>
        /// Zero-based character offset where parsing gave up. Carried separately from the message so
        /// tooling can point at the offending character instead of re-parsing the prose.
        /// </summary>
        public int Position { get; }

        /// <summary>Creates the exception, appending the offset to <paramref name="message"/>.</summary>
        public JsonParseException(string message, int position) : base(message + " (at character " + position + ")")
        {
            Position = position;
        }
    }

    /// <summary>
    /// Small, strict, allocation-conscious JSON reader for static game data and save files. Engine-free so
    /// the same data files load identically in the Unity client, in EditMode tests and on a future server.
    /// </summary>
    /// <remarks>
    /// WHY a hand-written parser rather than a library: <c>System.Text.Json</c> is banned in Core (ADR-0003)
    /// and <c>JsonUtility</c> lives in UnityEngine, which Core may not reference at all. This reader is
    /// deliberately strict — it rejects trailing commas and trailing content — because silently accepting a
    /// malformed save is how half a game gets loaded.
    /// </remarks>
    public static class JsonParser
    {
        /// <summary>
        /// Parses a complete JSON document.
        /// </summary>
        /// <param name="text">The document. Must contain exactly one value, optionally surrounded by whitespace.</param>
        /// <returns>The root node, never null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="JsonParseException">The document is malformed, truncated, or has trailing content.</exception>
        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw new JsonParseException("Unexpected content after the JSON document", reader.Position);
            }

            return value;
        }

        /// <summary>
        /// Parses without throwing, for callers that treat a malformed document as an expected outcome
        /// rather than a bug — chiefly the save loader deciding whether to fall back to the .bak file.
        /// </summary>
        /// <returns>True when <paramref name="value"/> holds the parsed root; false, with <c>JsonValue.Null</c>, otherwise.</returns>
        public static bool TryParse(string text, out JsonValue value)
        {
            if (text == null)
            {
                value = JsonValue.Null;
                return false;
            }

            try
            {
                value = Parse(text);
                return true;
            }
            catch (JsonParseException)
            {
                value = JsonValue.Null;
                return false;
            }
        }

        /// <summary>
        /// Single-pass cursor over the document text. Not reentrant and not shared: one instance per
        /// <see cref="Parse"/> call, so the reusable <see cref="StringBuilder"/> is safe.
        /// </summary>
        private sealed class Reader
        {
            private readonly string _text;
            private readonly StringBuilder _buffer = new StringBuilder();

            public int Position { get; private set; }

            public Reader(string text)
            {
                _text = text;
            }

            public bool AtEnd => Position >= _text.Length;

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = _text[Position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        Position++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public JsonValue ReadValue()
            {
                if (AtEnd)
                {
                    throw new JsonParseException("Unexpected end of JSON", Position);
                }

                var c = _text[Position];
                switch (c)
                {
                    case '{':
                        return ReadObject();
                    case '[':
                        return ReadArray();
                    case '"':
                        return new JsonValue(ReadString());
                    case 't':
                        ExpectLiteral("true");
                        return new JsonValue(true);
                    case 'f':
                        ExpectLiteral("false");
                        return new JsonValue(false);
                    case 'n':
                        ExpectLiteral("null");
                        return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw new JsonParseException("Unexpected character '" + c + "'", Position);
                }
            }

            private JsonValue ReadObject()
            {
                Position++;
                var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}')
                {
                    Position++;
                    return new JsonValue(members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw new JsonParseException("Expected a property name", Position);
                    }

                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    members[key] = ReadValue();
                    SkipWhitespace();
                    var next = Peek();
                    Position++;
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == '}')
                    {
                        return new JsonValue(members);
                    }

                    throw new JsonParseException("Expected ',' or '}' in object", Position - 1);
                }
            }

            private JsonValue ReadArray()
            {
                Position++;
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    Position++;
                    return new JsonValue(items);
                }

                while (true)
                {
                    SkipWhitespace();
                    items.Add(ReadValue());
                    SkipWhitespace();
                    var next = Peek();
                    Position++;
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == ']')
                    {
                        return new JsonValue(items);
                    }

                    throw new JsonParseException("Expected ',' or ']' in array", Position - 1);
                }
            }

            private string ReadString()
            {
                Expect('"');
                _buffer.Length = 0;
                while (true)
                {
                    if (AtEnd)
                    {
                        throw new JsonParseException("Unterminated string", Position);
                    }

                    var c = _text[Position++];
                    if (c == '"')
                    {
                        return _buffer.ToString();
                    }

                    if (c != '\\')
                    {
                        _buffer.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw new JsonParseException("Unterminated escape sequence", Position);
                    }

                    var escaped = _text[Position++];
                    switch (escaped)
                    {
                        case '"': _buffer.Append('"'); break;
                        case '\\': _buffer.Append('\\'); break;
                        case '/': _buffer.Append('/'); break;
                        case 'b': _buffer.Append('\b'); break;
                        case 'f': _buffer.Append('\f'); break;
                        case 'n': _buffer.Append('\n'); break;
                        case 'r': _buffer.Append('\r'); break;
                        case 't': _buffer.Append('\t'); break;
                        case 'u':
                            ReadUnicodeEscape();
                            break;
                        default:
                            throw new JsonParseException("Invalid escape '\\" + escaped + "'", Position - 1);
                    }
                }
            }

            /// <summary>
            /// Consumes the four hex digits of a <c>\uXXXX</c> escape and appends the code unit.
            /// </summary>
            /// <remarks>
            /// A high surrogate is joined with an immediately following <c>\uXXXX</c> low surrogate so the
            /// pair lands in the buffer as one astral character. Appending each half separately happens to
            /// produce the same UTF-16 string, but doing it explicitly means a LONE high surrogate (which a
            /// corrupted file can contain) is appended as-is rather than swallowing the next escape.
            /// </remarks>
            private void ReadUnicodeEscape()
            {
                var high = ReadFourHexDigits();
                if (high >= 0xD800 && high <= 0xDBFF
                    && Position + 6 <= _text.Length
                    && _text[Position] == '\\'
                    && _text[Position + 1] == 'u')
                {
                    var resume = Position;
                    Position += 2;
                    var low = ReadFourHexDigits();
                    if (low >= 0xDC00 && low <= 0xDFFF)
                    {
                        _buffer.Append((char)high);
                        _buffer.Append((char)low);
                        return;
                    }

                    // Not a valid pair after all: rewind so the second escape is read normally.
                    Position = resume;
                }

                _buffer.Append((char)high);
            }

            private int ReadFourHexDigits()
            {
                if (Position + 4 > _text.Length)
                {
                    throw new JsonParseException("Invalid unicode escape", Position);
                }

                var hex = _text.Substring(Position, 4);
                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    throw new JsonParseException("Invalid unicode escape", Position);
                }

                Position += 4;
                return code;
            }

            private JsonValue ReadNumber()
            {
                var start = Position;
                if (Peek() == '-')
                {
                    Position++;
                }

                while (!AtEnd)
                {
                    var c = _text[Position];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        Position++;
                    }
                    else
                    {
                        break;
                    }
                }

                var slice = _text.Substring(start, Position - start);
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    throw new JsonParseException("Invalid number '" + slice + "'", start);
                }

                return new JsonValue(number);
            }

            private char Peek()
            {
                if (AtEnd)
                {
                    throw new JsonParseException("Unexpected end of JSON", Position);
                }

                return _text[Position];
            }

            private void Expect(char expected)
            {
                if (Peek() != expected)
                {
                    throw new JsonParseException("Expected '" + expected + "'", Position);
                }

                Position++;
            }

            private void ExpectLiteral(string literal)
            {
                if (Position + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, Position, literal, 0, literal.Length) != 0)
                {
                    throw new JsonParseException("Invalid literal", Position);
                }

                Position += literal.Length;
            }
        }
    }
}
