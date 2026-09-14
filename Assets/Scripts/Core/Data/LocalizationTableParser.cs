// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame),
// Assets/Scripts/Core/Data/LocalizationTableParser.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Data, and TryParse added so a
// downloaded or modded locale pack cannot take the game down with an exception. The JSON shape
// ({ "locale": ..., "strings": { ... } }) and the Result type are unchanged.

using System;
using System.Collections.Generic;

namespace ForgottenIsle.Core.Data
{
    /// <summary>Parses a locale file: <c>{ "locale": "en", "strings": { "key": "value", ... } }</c>.</summary>
    /// <remarks>
    /// WHY this exists alongside <see cref="CsvTableParser"/>: CSV is what translators are handed, because
    /// it opens in a spreadsheet; JSON is what a downloadable or patched language pack ships as, because it
    /// carries its own locale tag and so cannot be filed under the wrong language by a careless rename.
    /// Both funnel into <c>StringTableLocalization.AddTable</c>.
    /// </remarks>
    public static class LocalizationTableParser
    {
        /// <summary>One parsed locale file: the locale it declares and the rows it carries.</summary>
        public sealed class Result
        {
            /// <summary>The locale tag declared inside the file, for example <c>en</c>. Never empty.</summary>
            public string Locale { get; }

            /// <summary>
            /// Key to translated text, ordinal-compared. Keys are authored identifiers, so an
            /// ordinal comparer is required: a culture-aware one would match differently under a
            /// Turkish locale.
            /// </summary>
            public Dictionary<string, string> Strings { get; }

            /// <summary>Creates a result. Both arguments are taken as-is and are expected to be non-null.</summary>
            public Result(string locale, Dictionary<string, string> strings)
            {
                Locale = locale;
                Strings = strings;
            }
        }

        /// <summary>
        /// Parses a locale file.
        /// </summary>
        /// <exception cref="JsonParseException">
        /// The document is malformed, or declares no <c>locale</c>. An untagged table is rejected rather
        /// than defaulted, because guessing a locale silently installs the wrong language for a player.
        /// </exception>
        public static Result Parse(string json)
        {
            var root = JsonParser.Parse(json);
            var locale = root["locale"].AsString();
            if (string.IsNullOrEmpty(locale))
            {
                throw new JsonParseException("Localization table has no 'locale' field", 0);
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in root["strings"].Members)
            {
                strings[member.Key] = member.Value.AsString(string.Empty);
            }

            return new Result(locale, strings);
        }

        /// <summary>
        /// Parses without throwing.
        /// </summary>
        /// <remarks>
        /// NEW vs Nation. Locale files can arrive from outside the build — a patch, a community
        /// translation — and a bad one must cost the player that language, not the session.
        /// </remarks>
        /// <returns>True when <paramref name="result"/> holds a parsed table; false, with null, otherwise.</returns>
        public static bool TryParse(string json, out Result result)
        {
            try
            {
                result = Parse(json);
                return true;
            }
            catch (JsonParseException)
            {
                result = null;
                return false;
            }
            catch (ArgumentNullException)
            {
                result = null;
                return false;
            }
        }
    }
}
