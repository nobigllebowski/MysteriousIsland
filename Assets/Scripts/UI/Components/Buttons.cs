// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Components/Buttons.cs.
// Adapted for Vardholm: the icon and small variants are gone (no icon atlas in Phase 1, and a "small" button
// cannot satisfy the touch-target rule below); the variants are restyled from Theme in code rather than from
// USS classes; press feedback is wired here; and the 48dp minimum touch target is enforced and verified at
// runtime instead of being left to a stylesheet nobody checks. Namespace is ForgottenIsle.UI.Core, not a
// .Components namespace, because the contract's namespace list has no UI.Components entry.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Button factories. Every variant shares one base shape; a variant only changes color weight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the factories take resolved text: they are the dumb end of the UI and must not know about
    /// localization. Screens call <c>Text(key)</c> from <see cref="UIScreen"/>, so the short path to a label
    /// is a key and passing a literal is the awkward one.
    /// </para>
    /// <para>
    /// WHY the touch target is enforced rather than documented: 48dp is the smallest control most people can
    /// hit reliably, and it is the first thing a layout change silently breaks — a button squeezed into a
    /// tight row still looks fine in the editor at 1920x1080 and is unusable on a phone. Every button made
    /// here carries a minimum size and checks its own laid-out rect, so the failure is reported at the
    /// moment it happens rather than in a play-test weeks later.
    /// </para>
    /// </remarks>
    public static class Buttons
    {
        /// <summary>Smallest permitted width and height, in dp.</summary>
        public const float MinimumTouchTargetDp = 48f;

        /// <summary>Opacity applied while a button is held, as press feedback.</summary>
        private const float PressedOpacity = 0.72f;

        /// <summary>Layout tolerance, in dp. Absorbs sub-pixel rounding rather than real shortfalls.</summary>
        private const float Epsilon = 0.5f;

        /// <summary>
        /// The single most important action on a screen. Filled with the accent color.
        /// </summary>
        /// <param name="text">Already-localized label text.</param>
        /// <param name="onClick">Click handler. Null makes an inert button, which is almost always a bug.</param>
        public static Button Primary(string text, Action onClick)
        {
            var button = Make(text, "btn--primary", onClick);
            button.style.backgroundColor = Theme.Accent;
            button.style.color = Theme.Text;
            SetBorder(button, Theme.WithAlpha(Theme.Accent, 0.9f));
            return button;
        }

        /// <summary>
        /// A real but secondary action. Outlined, filled only faintly, so it reads as available but quieter.
        /// </summary>
        /// <param name="text">Already-localized label text.</param>
        /// <param name="onClick">Click handler.</param>
        public static Button Secondary(string text, Action onClick)
        {
            var button = Make(text, "btn--secondary", onClick);
            button.style.backgroundColor = Theme.Surface;
            button.style.color = Theme.Text;
            SetBorder(button, Theme.Border);
            return button;
        }

        /// <summary>
        /// A low-emphasis action — back, dismiss, settings. No fill, no outline, just text at full size.
        /// </summary>
        /// <param name="text">Already-localized label text.</param>
        /// <param name="onClick">Click handler.</param>
        public static Button Ghost(string text, Action onClick)
        {
            var button = Make(text, "btn--ghost", onClick);
            button.style.backgroundColor = Color.clear;
            button.style.color = Theme.TextSecondary;
            SetBorder(button, Color.clear);
            return button;
        }

        /// <summary>Builds the shared button shape and attaches the touch-target guard.</summary>
        private static Button Make(string text, string variantClass, Action onClick)
        {
            var button = new Button(onClick) { text = text ?? string.Empty };
            button.AddToClassList("btn");
            button.AddToClassList(variantClass);

            button.style.minHeight = MinimumTouchTargetDp;
            button.style.minWidth = MinimumTouchTargetDp;
            button.style.paddingLeft = Theme.Space20;
            button.style.paddingRight = Theme.Space20;
            button.style.paddingTop = Theme.Space12;
            button.style.paddingBottom = Theme.Space12;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.marginTop = 0f;
            button.style.marginBottom = 0f;
            button.style.fontSize = 16f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.whiteSpace = WhiteSpace.Normal;

            button.style.borderTopLeftRadius = Theme.RadiusMd;
            button.style.borderTopRightRadius = Theme.RadiusMd;
            button.style.borderBottomLeftRadius = Theme.RadiusMd;
            button.style.borderBottomRightRadius = Theme.RadiusMd;

            button.RegisterCallback<PointerDownEvent>(_ => button.style.opacity = PressedOpacity);
            button.RegisterCallback<PointerUpEvent>(_ => button.style.opacity = 1f);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.opacity = 1f);
            button.RegisterCallback<PointerCancelEvent>(_ => button.style.opacity = 1f);

            button.RegisterCallback<GeometryChangedEvent>(evt => EnforceTouchTarget(button, evt.newRect));
            return button;
        }

        /// <summary>Applies a uniform 1dp border in one color.</summary>
        private static void SetBorder(Button button, Color color)
        {
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopColor = color;
            button.style.borderBottomColor = color;
            button.style.borderLeftColor = color;
            button.style.borderRightColor = color;
        }

        /// <summary>
        /// Verifies the laid-out rect against <see cref="MinimumTouchTargetDp"/>, repairs it, and reports it.
        /// </summary>
        /// <remarks>
        /// The repair is deliberate but is not the point: growing the button keeps the build playable, while
        /// the logged error is what gets the layout fixed. Repairing silently would let every screen drift
        /// below the minimum and each one would look acceptable on a desktop editor window.
        /// </remarks>
        private static void EnforceTouchTarget(Button button, Rect rect)
        {
            // A zero rect means the button is hidden or not laid out yet, which is not a violation.
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            var tooShort = rect.height + Epsilon < MinimumTouchTargetDp;
            var tooNarrow = rect.width + Epsilon < MinimumTouchTargetDp;
            if (!tooShort && !tooNarrow)
            {
                return;
            }

            if (tooShort)
            {
                button.style.height = MinimumTouchTargetDp;
            }

            if (tooNarrow)
            {
                button.style.width = MinimumTouchTargetDp;
            }

#if UNITY_EDITOR || DEBUG
            Debug.LogError(
                "[UI] Button '" + button.text + "' laid out at " + rect.width.ToString("0.#") + "x" +
                rect.height.ToString("0.#") + "dp, below the " + MinimumTouchTargetDp.ToString("0") +
                "dp touch target. It has been grown to fit; fix the parent layout.");
#endif
        }
    }
}
