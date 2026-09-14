using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ForgottenIsle.Core.Save;

namespace ForgottenIsle.Core.Data
{
    /// <summary>
    /// Engine-free JSON serializer: the write half of the pair whose read half is <see cref="JsonParser"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY this exists at all (ADR-0013): saves are written through Core's own JSON, not through
    /// <c>JsonUtility</c> or <c>System.Text.Json</c>. <c>JsonUtility</c> lives in UnityEngine, which Core
    /// may not reference; <c>System.Text.Json</c> is banned. More importantly, a save format the engine
    /// owns is a save format that changes when the engine changes — and a player's progress must not
    /// depend on a Unity upgrade note.
    /// </para>
    /// <para>
    /// WHY output is deterministic: <c>SaveCodec</c> checksums the serialized text. A
    /// <see cref="Dictionary{TKey,TValue}"/> makes no ordering guarantee across runs or runtimes, so
    /// serializing the same document twice could produce two different byte sequences and therefore two
    /// different CRC32s — a save would fail its own integrity check on reload. Every member map written
    /// here is emitted in ordinal key order for that reason.
    /// </para>
    /// <para>
    /// Output is compact: no indentation, no spaces. A save is read by a machine, and on a phone the
    /// bytes are the budget.
    /// </para>
    /// </remarks>
    public static class JsonWriter
    {
        /// <summary>
        /// Serializes a flat string-to-string map as a JSON object.
        /// </summary>
        /// <param name="members">
        /// The map. Null yields <c>{}</c>. A null key is skipped — it has no JSON representation. A null
        /// value is written as an empty string rather than as <c>null</c>, so a round trip through
        /// <see cref="JsonParser"/> hands back a string, matching what the reader promised its callers.
        /// </param>
        /// <returns>A complete JSON object, for example <c>{"a":"1","b":"2"}</c>. Never null.</returns>
        public static string WriteObject(Dictionary<string, string> members)
        {
            var builder = new StringBuilder();
            AppendObject(builder, members);
            return builder.ToString();
        }

        /// <summary>
        /// Appends a flat string-to-string map to <paramref name="builder"/> as a JSON object, in ordinal
        /// key order.
        /// </summary>
        public static void AppendObject(StringBuilder builder, Dictionary<string, string> members)
        {
            if (builder == null)
            {
                return;
            }

            builder.Append('{');
            if (members != null && members.Count > 0)
            {
                var keys = SortedKeys(members);
                for (var i = 0; i < keys.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    var key = keys[i];
                    AppendString(builder, key);
                    builder.Append(':');
                    AppendString(builder, members[key] ?? string.Empty);
                }
            }

            builder.Append('}');
        }

        /// <summary>
        /// Serializes a whole save document: header, checksum, metadata, and every participant section.
        /// </summary>
        /// <remarks>
        /// The <see cref="SaveDocument.Checksum"/> field is written verbatim, exactly as it stands on the
        /// document. That is deliberate and is what makes the two-pass protocol in <c>SaveCodec</c>
        /// possible: zero the checksum, serialize, CRC32 the result, store it, serialize again. A writer
        /// that computed the checksum itself could not express the zeroed pass, and the verifier would
        /// have nothing stable to compare against.
        /// </remarks>
        /// <param name="document">The document. Null yields <c>{}</c>.</param>
        /// <returns>The document as compact JSON. Never null.</returns>
        public static string WriteSaveDocument(SaveDocument document)
        {
            var builder = new StringBuilder(1024);
            if (document == null)
            {
                builder.Append("{}");
                return builder.ToString();
            }

            builder.Append('{');

            AppendMemberName(builder, "schemaVersion");
            AppendInt(builder, document.SchemaVersion);

            builder.Append(',');
            AppendMemberName(builder, "buildVersion");
            AppendString(builder, document.BuildVersion ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "savedAtIso");
            AppendString(builder, document.SavedAtIso ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "checksum");
            AppendUInt(builder, document.Checksum);

            builder.Append(',');
            AppendMemberName(builder, "metadata");
            AppendSaveMetadata(builder, document.Metadata);

            builder.Append(',');
            AppendMemberName(builder, "sections");
            AppendObject(builder, document.Sections);

            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>
        /// Serializes the slot header.
        /// </summary>
        /// <remarks>
        /// Written as a nested object rather than flattened into the document so
        /// <c>SaveSlotService.ReadMetadata</c> can stop reading once it has this member — drawing the slot
        /// list must not pay for deserializing three whole games.
        /// </remarks>
        /// <param name="metadata">The header. Null is written as an empty object, not omitted, so the member always exists.</param>
        public static void AppendSaveMetadata(StringBuilder builder, SaveMetadata metadata)
        {
            if (builder == null)
            {
                return;
            }

            if (metadata == null)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');

            AppendMemberName(builder, "slot");
            AppendInt(builder, metadata.Slot);

            builder.Append(',');
            AppendMemberName(builder, "actId");
            AppendString(builder, metadata.ActId ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "zoneId");
            AppendString(builder, metadata.ZoneId ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "zoneDisplayKey");
            AppendString(builder, metadata.ZoneDisplayKey ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "playtimeSeconds");
            AppendDouble(builder, metadata.PlaytimeSeconds);

            builder.Append(',');
            AppendMemberName(builder, "recordedPercent");
            AppendInt(builder, metadata.RecordedPercent);

            builder.Append(',');
            AppendMemberName(builder, "savedAtIso");
            AppendString(builder, metadata.SavedAtIso ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "buildVersion");
            AppendString(builder, metadata.BuildVersion ?? string.Empty);

            builder.Append(',');
            AppendMemberName(builder, "schemaVersion");
            AppendInt(builder, metadata.SchemaVersion);

            builder.Append('}');
        }

        /// <summary>Returns <paramref name="value"/> as a quoted, fully escaped JSON string literal.</summary>
        public static string EscapeString(string value)
        {
            var builder = new StringBuilder((value?.Length ?? 0) + 2);
            AppendString(builder, value);
            return builder.ToString();
        }

        /// <summary>
        /// Appends <paramref name="value"/> as a quoted JSON string literal.
        /// </summary>
        /// <remarks>
        /// Escapes the two characters JSON reserves (<c>"</c> and <c>\</c>), the four common whitespace
        /// controls as their short forms, and EVERY remaining character below U+0020 as <c>\u00XX</c>.
        /// That last clause is the one that matters: RFC 8259 forbids a raw control character inside a
        /// string, and a save carrying one would parse on a lenient reader and fail on a strict one — this
        /// project's own reader is the strict kind. Forward slash is left unescaped; escaping it is
        /// permitted but not required, and the extra backslashes only cost bytes.
        /// A null value is written as an empty string, never as the literal <c>null</c>.
        /// </remarks>
        public static void AppendString(StringBuilder builder, string value)
        {
            if (builder == null)
            {
                return;
            }

            builder.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '"':
                            builder.Append('\\').Append('"');
                            break;
                        case '\\':
                            builder.Append('\\').Append('\\');
                            break;
                        case '\b':
                            builder.Append('\\').Append('b');
                            break;
                        case '\f':
                            builder.Append('\\').Append('f');
                            break;
                        case '\n':
                            builder.Append('\\').Append('n');
                            break;
                        case '\r':
                            builder.Append('\\').Append('r');
                            break;
                        case '\t':
                            builder.Append('\\').Append('t');
                            break;
                        default:
                            if (c < ' ')
                            {
                                AppendUnicodeEscape(builder, c);
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }

        /// <summary>Appends a signed 32-bit integer in invariant form.</summary>
        public static void AppendInt(StringBuilder builder, int value)
        {
            builder?.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Appends an unsigned 32-bit integer — a CRC32 checksum — in invariant form.</summary>
        public static void AppendUInt(StringBuilder builder, uint value)
        {
            builder?.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Appends an unsigned 64-bit integer, for example a serialized PRNG state.</summary>
        public static void AppendULong(StringBuilder builder, ulong value)
        {
            builder?.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Appends a double in a form that parses back to the identical value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Round-tripping is not cosmetic here: <c>SessionState.SimHours</c> and
        /// <c>SaveMetadata.PlaytimeSeconds</c> accumulate over a whole run, and a value that shifts by one
        /// unit in the last place on every save/load cycle drifts visibly over a long game.
        /// </para>
        /// <para>
        /// "R" is the shortest round-trippable form on current runtimes, but it has a documented history of
        /// failing to round-trip on some values on older ones, and Unity's scripting backend is not a
        /// runtime this project gets to choose. The output is therefore parsed back and compared; only if
        /// that fails does it fall back to the always-sufficient 17 significant digits. The check costs a
        /// parse on a code path that runs a handful of times per save.
        /// </para>
        /// <para>
        /// NaN and the infinities have no JSON representation. They are written as <c>0</c>, because a save
        /// that refuses to load is worse than a save with one wrong number — and a NaN reaching here is a
        /// bug upstream that the zero will make obvious.
        /// </para>
        /// </remarks>
        public static void AppendDouble(StringBuilder builder, double value)
        {
            if (builder == null)
            {
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                builder.Append('0');
                return;
            }

            var text = value.ToString("R", CultureInfo.InvariantCulture);
            double parsed;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || !parsed.Equals(value))
            {
                text = value.ToString("G17", CultureInfo.InvariantCulture);
            }

            builder.Append(text);
        }

        /// <summary>Appends a float, widened to double so the emitted form is unambiguous.</summary>
        public static void AppendFloat(StringBuilder builder, float value)
        {
            if (builder == null)
            {
                return;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                builder.Append('0');
                return;
            }

            var text = value.ToString("R", CultureInfo.InvariantCulture);
            float parsed;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || !parsed.Equals(value))
            {
                text = value.ToString("G9", CultureInfo.InvariantCulture);
            }

            builder.Append(text);
        }

        /// <summary>Appends <c>true</c> or <c>false</c>.</summary>
        public static void AppendBool(StringBuilder builder, bool value)
        {
            builder?.Append(value ? "true" : "false");
        }

        /// <summary>Appends <c>"name":</c>, the punctuation every member of an object needs.</summary>
        public static void AppendMemberName(StringBuilder builder, string name)
        {
            if (builder == null)
            {
                return;
            }

            AppendString(builder, name ?? string.Empty);
            builder.Append(':');
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char c)
        {
            builder.Append('\\').Append('u');
            builder.Append(HexDigits[(c >> 12) & 0xF]);
            builder.Append(HexDigits[(c >> 8) & 0xF]);
            builder.Append(HexDigits[(c >> 4) & 0xF]);
            builder.Append(HexDigits[c & 0xF]);
        }

        private static readonly char[] HexDigits =
        {
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f'
        };

        /// <summary>
        /// Copies the map's keys and sorts them ordinally, so serialization order — and therefore the
        /// checksum — does not depend on dictionary internals.
        /// </summary>
        private static List<string> SortedKeys(Dictionary<string, string> members)
        {
            var keys = new List<string>(members.Count);
            foreach (var pair in members)
            {
                if (pair.Key != null)
                {
                    keys.Add(pair.Key);
                }
            }

            keys.Sort(System.StringComparer.Ordinal);
            return keys;
        }
    }
}
