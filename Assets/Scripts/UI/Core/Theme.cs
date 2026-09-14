// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/Palette.cs.
// Adapted for Vardholm: renamed Palette -> Theme, the cold blue/navy palette is replaced by the Vardholm
// green-black palette, Nation's map tokens are gone, and the spacing/radius/duration scales moved here from
// Nation's Theme.uss because Vardholm builds its UI entirely in code (there is no authored stylesheet asset
// to hold the tokens, and two sources of truth for a spacing value is exactly the drift this file prevents).

using UnityEngine;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// The single source of truth for every color, spacing step, corner radius and animation duration
    /// used by the UI layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY tokens instead of literals at the call site: a screen that writes <c>12f</c> for a padding
    /// encodes a decision nobody can find again. Every visual constant the UI needs is named here, so a
    /// palette or rhythm change is one edit rather than a search across every screen.
    /// </para>
    /// <para>
    /// WHY the values are <see cref="Color"/> and not a stylesheet: Nation loaded a <c>Theme.uss</c> asset
    /// from its data catalog and fell back to an unstyled UI when the asset was missing. Vardholm has no
    /// catalog (ADR-0012), and an unstyled fallback is a shipped bug waiting to happen, so the styling is
    /// applied in code from these tokens and cannot go missing. USS class names are still attached to every
    /// element, so a stylesheet can be layered on later without touching the screens.
    /// </para>
    /// </remarks>
    public static class Theme
    {
        // ---- Surfaces -------------------------------------------------------------------------------

        /// <summary>Page ground: a near-black with a green cast, so the island reads as damp, not cold.</summary>
        public static readonly Color Background = Hex("#0A0F0D");

        /// <summary>Raised panel fill. Translucent on purpose: cards should sit in the dark, not cut it out.</summary>
        public static readonly Color Surface = Rgba(18, 32, 28, 0.55f);

        /// <summary>Hairline separator and card outline.</summary>
        public static readonly Color Border = Rgba(120, 160, 140, 0.16f);

        /// <summary>Deep ocean blue, used for the horizon band behind the menu and for water framing.</summary>
        public static readonly Color Ocean = Hex("#0C1A24");

        // ---- Text -----------------------------------------------------------------------------------

        /// <summary>Primary reading color.</summary>
        public static readonly Color Text = Hex("#E8EFE9");

        /// <summary>Supporting copy: subtitles, row values, captions that still need to be read.</summary>
        public static readonly Color TextSecondary = Rgba(200, 215, 205, 0.72f);

        /// <summary>Text the player may ignore: version strings, hints, disabled labels.</summary>
        public static readonly Color TextMuted = Rgba(150, 175, 160, 0.55f);

        // ---- Accents --------------------------------------------------------------------------------

        /// <summary>Deep green. The only color that says "this is the thing to press".</summary>
        public static readonly Color Accent = Hex("#2E6F5E");

        /// <summary>Accent at fill strength, for pressed states and soft backgrounds.</summary>
        public static readonly Color AccentSoft = Rgba(46, 111, 94, 0.22f);

        /// <summary>Reserved for discoveries. Nothing routine may use it, or discoveries stop feeling rare.</summary>
        public static readonly Color Gold = Hex("#D9B45B");

        /// <summary>Destructive actions and hard failures.</summary>
        public static readonly Color Danger = Hex("#C4453D");

        /// <summary>Sunset orange: recoverable problems and time pressure.</summary>
        public static readonly Color Warning = Hex("#E08B3C");

        // ---- Spacing --------------------------------------------------------------------------------
        // A 4dp rhythm. Anything that needs a gap picks the nearest step rather than inventing one.

        /// <summary>4dp. Gap between a label and the value it describes.</summary>
        public const float Space4 = 4f;

        /// <summary>8dp. Gap between tightly related rows.</summary>
        public const float Space8 = 8f;

        /// <summary>12dp. Default inner padding of small controls.</summary>
        public const float Space12 = 12f;

        /// <summary>16dp. Default inner padding of cards and the screen side gutter.</summary>
        public const float Space16 = 16f;

        /// <summary>20dp. Gap between a card and the next card.</summary>
        public const float Space20 = 20f;

        /// <summary>24dp. Screen-level block separation.</summary>
        public const float Space24 = 24f;

        /// <summary>32dp. Separation between major regions, e.g. a title block and its actions.</summary>
        public const float Space32 = 32f;

        // ---- Radii ----------------------------------------------------------------------------------

        /// <summary>8dp. Chips, toggles, inline tags.</summary>
        public const float RadiusSm = 8f;

        /// <summary>14dp. Buttons and toasts.</summary>
        public const float RadiusMd = 14f;

        /// <summary>22dp. Cards, sheets and the pause panel.</summary>
        public const float RadiusLg = 22f;

        // ---- Durations ------------------------------------------------------------------------------

        /// <summary>140 ms. Press feedback and other changes that must feel instant.</summary>
        public const long DurationFast = 140;

        /// <summary>260 ms. Screen transitions, curtain fades, toast entries.</summary>
        public const long DurationNormal = 260;

        // ---- Helpers --------------------------------------------------------------------------------

        /// <summary>
        /// Parses an <c>#RRGGBB</c> / <c>#RRGGBBAA</c> string into a color.
        /// </summary>
        /// <remarks>
        /// A bad string yields magenta rather than a silent black: an unparseable token must be impossible
        /// to miss on screen, because a black-on-black mistake ships.
        /// </remarks>
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;
        }

        /// <summary>Returns <paramref name="color"/> at a different alpha, leaving the original untouched.</summary>
        /// <param name="color">The source color; copied, since <see cref="Color"/> is a value type.</param>
        /// <param name="alpha">Target alpha in 0..1. Values outside the range are clamped.</param>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        /// <summary>
        /// Builds a color from 0..255 channels, which is how the palette is specified in the design doc.
        /// </summary>
        /// <remarks>
        /// Kept private: converting by hand at every call site is how a channel ends up off by one, and
        /// nothing outside this file should be authoring new palette entries anyway.
        /// </remarks>
        private static Color Rgba(int r, int g, int b, float a)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }
    }
}
