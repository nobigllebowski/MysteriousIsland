// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Components/Typography.cs.
// Adapted for Vardholm: the nine-step Nation scale is cut to the five steps this game actually needs
// (Display, Title, Body, Caption, Mono); sizes and colors are applied from Theme in code rather than from USS
// classes that may not be loaded; and every label wraps by default. Namespace is ForgottenIsle.UI.Core, not a
// .Components namespace, because the contract's namespace list has no UI.Components entry.

using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Label factories for the type hierarchy. Five steps, deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY only five: a scale with nine steps is a scale nobody can hold in their head, so screens end up
    /// picking by eye and the hierarchy stops meaning anything. Display for the one thing a screen is about,
    /// Title for sections, Body for reading, Caption for things that may be skipped, Mono for values that
    /// must line up.
    /// </para>
    /// <para>
    /// WHY the factories take resolved text: same reason as <see cref="Buttons"/> — these components must
    /// not know about localization. Screens resolve keys through <c>UIScreen.Text</c>.
    /// </para>
    /// </remarks>
    public static class Typography
    {
        /// <summary>The one statement a screen is about. Used once per screen, at most.</summary>
        /// <param name="text">Already-localized text.</param>
        /// <param name="extraClass">Optional extra USS class for later styling.</param>
        public static Label Display(string text, string extraClass = null)
        {
            var label = Make(text, "type-display", extraClass);
            label.style.fontSize = 34f;
            label.style.color = Theme.Text;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;

            // Tightened because at display size the default line gap reads as a paragraph break.
            label.style.letterSpacing = 1.5f;
            return label;
        }

        /// <summary>Section heading inside a screen.</summary>
        /// <param name="text">Already-localized text.</param>
        /// <param name="extraClass">Optional extra USS class for later styling.</param>
        public static Label Title(string text, string extraClass = null)
        {
            var label = Make(text, "type-title", extraClass);
            label.style.fontSize = 22f;
            label.style.color = Theme.Text;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        /// <summary>Default reading size. Anything the player is expected to actually read.</summary>
        /// <param name="text">Already-localized text.</param>
        /// <param name="extraClass">Optional extra USS class for later styling.</param>
        public static Label Body(string text, string extraClass = null)
        {
            var label = Make(text, "type-body", extraClass);
            label.style.fontSize = 15f;
            label.style.color = Theme.TextSecondary;
            return label;
        }

        /// <summary>Supporting detail that may be skipped without losing meaning.</summary>
        /// <param name="text">Already-localized text.</param>
        /// <param name="extraClass">Optional extra USS class for later styling.</param>
        public static Label Caption(string text, string extraClass = null)
        {
            var label = Make(text, "type-caption", extraClass);
            label.style.fontSize = 12f;
            label.style.color = Theme.TextMuted;
            return label;
        }

        /// <summary>
        /// Values that must align across rows — timestamps, coordinates, slot summaries, build strings.
        /// </summary>
        /// <remarks>
        /// The default runtime font is proportional, so alignment comes from the tracking and the
        /// <c>type-mono</c> class rather than from the glyph advance. Assigning a real monospace font asset
        /// to that class later is a stylesheet change with no code change, which is why the class is here
        /// even though nothing consumes it yet.
        /// </remarks>
        /// <param name="text">Already-localized or machine-formatted text.</param>
        /// <param name="extraClass">Optional extra USS class for later styling.</param>
        public static Label Mono(string text, string extraClass = null)
        {
            var label = Make(text, "type-mono", extraClass);
            label.style.fontSize = 13f;
            label.style.color = Theme.TextSecondary;
            label.style.letterSpacing = 1f;
            return label;
        }

        /// <summary>Creates the shared label shape: wrapping, no picking, class-tagged.</summary>
        private static Label Make(string text, string typeClass, string extraClass)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList(typeClass);
            if (!string.IsNullOrEmpty(extraClass))
            {
                label.AddToClassList(extraClass);
            }

            // Labels never take input. A wide label sitting over a button would otherwise eat its taps, and
            // that bug is invisible until someone tries to press the button.
            label.pickingMode = PickingMode.Ignore;
            label.style.whiteSpace = WhiteSpace.Normal;

            // Flex children shrink by default, and a shrunk Label does NOT shrink its text -- the
            // glyphs overflow the box and draw across whatever is next to it. That is what stacked
            // the menu's title and subtitle on top of each other. A line of type is a fixed amount
            // of space or it is unreadable; let the spacers absorb a short screen instead.
            label.style.flexShrink = 0f;
            return label;
        }
    }
}
