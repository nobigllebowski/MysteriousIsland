// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame),
// Assets/Scripts/Core/Localization/StringTableLocalizationService.cs.
// Adapted for Vardholm: renamed to StringTableLocalization and implements ILocalizedText with LocKey
// parameters; Nation's public MissingKey event replaced by an injected ICoreLog (one sink, no dangling
// subscriptions); and the missing-key form changed from "[key]" to "#key#" -- see the remarks below,
// that change is the point of this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Localization
{
    /// <summary>
    /// Dictionary-backed localization. Tables are added per locale by the platform layer — a CSV on disk
    /// now, a downloaded pack later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the missing-key form is <c>#key#</c> and not Nation's <c>[key]</c>, and above all not an empty
    /// string: the only bug report that ever arrives for a missing translation is a screenshot. A blank
    /// label is invisible in one and tells nobody anything. Square brackets are ambiguous — real prose uses
    /// them, and so do several of the fonts' fallback glyphs. Hashes on both ends are unmistakable in a
    /// screenshot AND the enclosed text is the literal key, so it can be pasted straight into a search of
    /// <c>en.csv</c>. Rendering something findable is worth more than rendering something tidy.
    /// </para>
    /// <para>
    /// WHY each missing key is logged exactly once: the key that is missing is missing on every frame that
    /// draws it. Logging per lookup turns one absent subtitle into thousands of lines a second, which
    /// drowns the log it was supposed to draw attention to and costs real frame time in a build with
    /// telemetry attached. A <see cref="HashSet{T}"/> of already-reported keys makes the warning a
    /// one-time event, which is the shape a human can actually act on.
    /// </para>
    /// <para>
    /// Not thread-safe. Tables are loaded during boot and read from the UI thread thereafter; adding a lock
    /// to every label lookup would cost more than the concurrency it would buy.
    /// </para>
    /// </remarks>
    public sealed class StringTableLocalization : ILocalizedText
    {
        /// <summary>The locale used when no table covers the requested one. English, the authoring language.</summary>
        public const string DefaultFallbackLocale = "en";

        /// <summary>
        /// Locale tag to that locale's table. Compared case-insensitively, because locale tags arrive from
        /// the OS in whatever casing the platform prefers (<c>en</c>, <c>EN</c>, <c>en-GB</c>) and a player
        /// must not lose their language to a capital letter.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Keys already reported through <see cref="ICoreLog"/>. Ordinal: keys are identifiers, and two
        /// keys differing only in case are two different keys, each worth its own warning.
        /// </summary>
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);

        private readonly string _fallbackLocale;
        private readonly ICoreLog _log;

        /// <inheritdoc />
        public string CurrentLocale { get; private set; }

        /// <param name="log">
        /// Where missing keys are reported. Null is accepted and means "report nowhere" — a headless test
        /// that only cares about resolution should not have to build a logger, and a null here must never
        /// be the reason a screen fails to draw.
        /// </param>
        /// <param name="fallbackLocale">
        /// Locale consulted when the current one has no entry. An empty value is normalised to
        /// <see cref="DefaultFallbackLocale"/>, because a service with no fallback silently degrades every
        /// partially-translated language to placeholders.
        /// </param>
        public StringTableLocalization(ICoreLog log, string fallbackLocale = DefaultFallbackLocale)
        {
            _log = log;
            _fallbackLocale = string.IsNullOrEmpty(fallbackLocale) ? DefaultFallbackLocale : fallbackLocale;
            CurrentLocale = _fallbackLocale;
        }

        /// <summary>
        /// The locales that currently have a table. Drives the language picker: offering a language with no
        /// rows behind it would show the player a screen full of <c>#key#</c>.
        /// </summary>
        public IEnumerable<string> AvailableLocales => _tables.Keys;

        /// <summary>
        /// Merges <paramref name="entries"/> into the table for <paramref name="locale"/>, creating it if
        /// needed.
        /// </summary>
        /// <remarks>
        /// Merging rather than replacing, last-write-wins, is what makes overrides work: load the shipped
        /// table, then load a patch or a platform-specific sheet on top of it, and only the rows the patch
        /// mentions change. Rows with an empty key are dropped; a null value becomes an empty string, so
        /// the table itself can never hand a lookup null.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="locale"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        public void AddTable(string locale, IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (string.IsNullOrEmpty(locale))
            {
                throw new ArgumentException("Locale must not be empty.", nameof(locale));
            }

            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            Dictionary<string, string> table;
            if (!_tables.TryGetValue(locale, out table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                _tables.Add(locale, table);
            }

            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Key))
                {
                    table[entry.Key] = entry.Value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Switches locale.
        /// </summary>
        /// <returns>
        /// False, leaving the locale unchanged, when no table exists for <paramref name="locale"/>. The
        /// caller decides what to do about it; silently switching to a language with no strings would
        /// leave the player unable to find the menu that would switch it back.
        /// </returns>
        public bool SetLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale) || !_tables.ContainsKey(locale))
            {
                return false;
            }

            CurrentLocale = locale;
            return true;
        }

        /// <inheritdoc />
        public bool Has(in LocKey key)
        {
            string ignored;
            return TryLookup(key.Value, out ignored);
        }

        /// <inheritdoc />
        public string Get(in LocKey key)
        {
            var raw = key.Value ?? string.Empty;

            string value;
            if (TryLookup(raw, out value))
            {
                return value;
            }

            ReportMissing(raw);

            // An empty key resolves to "##": still visible, still unmistakably a bug marker, and still not
            // an empty label. A default(LocKey) reaching a label is a caller error, and this is how it
            // announces itself rather than vanishing.
            return "#" + raw + "#";
        }

        /// <inheritdoc />
        public string Get(in LocKey key, params object[] args)
        {
            var pattern = Get(key);
            if (args == null || args.Length == 0)
            {
                return pattern;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                // A translator typed {0 or {2} where only one argument exists. Show the pattern: it is
                // wrong, but it is legible and it names the key when the lookup missed.
                return pattern;
            }
        }

        /// <summary>
        /// Looks up <paramref name="key"/> in the current locale, then in the fallback locale.
        /// </summary>
        /// <returns>True when found; <paramref name="value"/> is then non-null.</returns>
        private bool TryLookup(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            Dictionary<string, string> current;
            if (_tables.TryGetValue(CurrentLocale, out current) && current.TryGetValue(key, out value))
            {
                return true;
            }

            if (!string.Equals(CurrentLocale, _fallbackLocale, StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string, string> fallback;
                if (_tables.TryGetValue(_fallbackLocale, out fallback) && fallback.TryGetValue(key, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Warns about <paramref name="key"/> the first time it is found missing, and never again for the
        /// lifetime of this instance -- deliberately not reset by AddTable or SetLocale, so a key that is
        /// drawn every frame cannot produce a second warning under any sequence of calls.
        /// </summary>
        /// <remarks>
        /// The key travels as the log's detail string. It is an authored identifier, not player-facing
        /// prose, so it is safe to emit verbatim — and emitting it verbatim is the whole point: the warning
        /// has to be greppable against <c>en.csv</c>.
        /// </remarks>
        private void ReportMissing(string key)
        {
            if (!_reportedMissing.Add(key ?? string.Empty))
            {
                return;
            }

            if (_log == null)
            {
                return;
            }

            _log.Warn(LogCode.MissingLocKey, key ?? string.Empty);
        }
    }
}
