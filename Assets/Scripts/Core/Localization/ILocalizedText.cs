// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame),
// Assets/Scripts/Core/Localization/ILocalizationService.cs.
// Adapted for Vardholm: renamed ILocalizationService -> ILocalizedText, keys are LocKey rather than
// raw strings so a display string can never be passed where a key belongs, and the documented
// missing-key form changed from Nation's "[key]" to "#key#".

using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Localization
{
    /// <summary>
    /// Key-based text lookup. All player-facing text goes through this interface; nothing in the UI is
    /// hardcoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY <see cref="LocKey"/> instead of <c>string</c> (the change from Nation): with a string parameter,
    /// <c>loc.Get("Continue")</c> compiles and quietly ships an English literal wrapped in brackets. With a
    /// key type, that line does not compile at all. The type is the enforcement mechanism for the
    /// no-user-facing-literals rule; a code review is not.
    /// </para>
    /// <para>
    /// Implementations MUST NOT throw from any member and MUST NOT return null or empty from
    /// <see cref="Get(in LocKey)"/>. UI code binds the result straight into a label, and a silently empty
    /// label is a bug that survives all the way into a shipped build.
    /// </para>
    /// </remarks>
    public interface ILocalizedText
    {
        /// <summary>The locale currently being served, for example <c>en</c>. Never null or empty.</summary>
        string CurrentLocale { get; }

        /// <summary>
        /// True when <paramref name="key"/> resolves in the current locale or in the fallback locale.
        /// </summary>
        /// <remarks>
        /// For conditional content — an optional subtitle, a platform-specific hint — that should be
        /// omitted entirely when untranslated rather than shown as a placeholder. It does NOT count as a
        /// lookup: a probe must not mark a key as missing, or optional content would fill the log with
        /// warnings about text nobody expected to exist.
        /// </remarks>
        bool Has(in LocKey key);

        /// <summary>
        /// Resolves <paramref name="key"/> in the current locale, falling back to the fallback locale.
        /// </summary>
        /// <returns>
        /// The translated text, or — when nothing matches — the literal form <c>#key#</c>. Never null,
        /// never empty, never throws.
        /// </returns>
        string Get(in LocKey key);

        /// <summary>
        /// Resolves <paramref name="key"/> and fills its <c>{0}</c>, <c>{1}</c>, ... placeholders.
        /// </summary>
        /// <remarks>
        /// Formatting is invariant. Arguments that need locale-aware rendering — counts, times, percentages
        /// — must be formatted by the caller before they get here, because this method cannot know which
        /// of them is a number the player reads and which is an id that must not be touched.
        /// If the pattern's placeholders do not match the arguments given, the unformatted pattern is
        /// returned rather than an exception thrown: a malformed translation must not crash a screen.
        /// </remarks>
        string Get(in LocKey key, params object[] args);
    }
}
