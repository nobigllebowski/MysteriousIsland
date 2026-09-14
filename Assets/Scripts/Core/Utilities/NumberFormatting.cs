// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Utilities/NumberFormatting.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Utilities and FormatPercent /
// FormatInteger added, since Vardholm's HUD shows discovery percentages and small counts rather than
// Nation's trillion-scale economy figures. FormatCompact itself is unchanged.

using System;
using System.Globalization;

namespace ForgottenIsle.Core.Utilities
{
    /// <summary>
    /// Localized magnitude suffixes, for example K, M, B, T in English.
    /// </summary>
    /// <remarks>
    /// WHY the suffixes are passed in rather than baked into the formatter: they are player-facing text
    /// and therefore belong in the string table. Core has no access to the localization service at the
    /// point of formatting, so the caller resolves the four keys and hands them over as data.
    /// </remarks>
    public readonly struct CompactSuffixes
    {
        public string Thousand { get; }
        public string Million { get; }
        public string Billion { get; }
        public string Trillion { get; }

        public CompactSuffixes(string thousand, string million, string billion, string trillion)
        {
            Thousand = thousand;
            Million = million;
            Billion = billion;
            Trillion = trillion;
        }
    }

    /// <summary>
    /// Compact number formatting for HUD values.
    /// </summary>
    /// <remarks>
    /// Uses the invariant culture throughout. WHY: the digits produced here are concatenated into a
    /// localized format string by the UI layer, and letting the number carry a second culture's separators
    /// would mix conventions inside one label. Locale-aware separators, when they arrive, belong in the
    /// localization layer that owns the surrounding sentence.
    /// </remarks>
    public static class NumberFormatting
    {
        /// <summary>
        /// Formats <paramref name="value"/> with a magnitude suffix once it reaches a thousand.
        /// </summary>
        /// <param name="decimals">Digits after the point on the scaled value. Negative input is treated as 0.</param>
        public static string FormatCompact(double value, CompactSuffixes suffixes, int decimals = 1)
        {
            var magnitude = Math.Abs(value);
            double scaled;
            string suffix;

            if (magnitude >= 1e12)
            {
                scaled = value / 1e12;
                suffix = suffixes.Trillion;
            }
            else if (magnitude >= 1e9)
            {
                scaled = value / 1e9;
                suffix = suffixes.Billion;
            }
            else if (magnitude >= 1e6)
            {
                scaled = value / 1e6;
                suffix = suffixes.Million;
            }
            else if (magnitude >= 1e3)
            {
                scaled = value / 1e3;
                suffix = suffixes.Thousand;
            }
            else
            {
                // Below a thousand the exact figure fits, so show it rather than a rounded "0.9K".
                return value.ToString("F0", CultureInfo.InvariantCulture);
            }

            var places = decimals > 0 ? decimals : 0;
            return scaled.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                   + (suffix ?? string.Empty);
        }

        /// <summary>
        /// Formats a whole number with no grouping separators.
        /// </summary>
        /// <remarks>
        /// No separators because grouping conventions differ per locale and the UI composes these digits
        /// into a localized sentence; a hard-coded comma would be wrong in half the target markets.
        /// </remarks>
        public static string FormatInteger(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats a 0-100 percentage as digits only, with no percent sign.
        /// </summary>
        /// <remarks>
        /// The sign and its spacing are part of the localized format string, so this returns the number
        /// alone. Input is clamped to [0, 100] because discovery progress is reported from counters that
        /// a mid-migration save can briefly push out of range, and "-3% discovered" must never ship.
        /// </remarks>
        public static string FormatPercent(int percent)
        {
            var clamped = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
            return clamped.ToString(CultureInfo.InvariantCulture);
        }
    }
}
