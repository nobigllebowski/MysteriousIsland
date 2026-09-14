// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/SafeAreaElement.cs.
// Adapted for Vardholm: the dependency on Nation's PanelCoordinates helper is gone — the one conversion this
// element needs (panel units per screen pixel) is computed inline, so the UI assembly does not carry a
// coordinate-math type that nothing else uses; the minimum bottom inset is now a named, overridable property
// rather than a hidden constant.

using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Pads its content by the device safe area — notch, Dynamic Island, punch-hole camera, gesture bar —
    /// converting screen pixels into panel units so it stays correct under any panel scale mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY nothing here hardcodes an inset: a fixed "44 at the top, 34 at the bottom" is right for exactly
    /// one phone and wrong everywhere else, and it is wrong in a way that only shows up on hardware nobody
    /// on the team is holding. Every inset below comes from <see cref="Screen.safeArea"/>.
    /// </para>
    /// <para>
    /// The one exception is <see cref="MinimumBottomInset"/>, which is a comfort floor and not a device
    /// measurement: on a device that reports no bottom inset at all, a button flush with the physical edge
    /// is hard to hit. It raises the padding, it never lowers what the device asked for.
    /// </para>
    /// <para>
    /// Recomputed on every geometry change, which covers rotation, window resize and the Device Simulator
    /// switching device profiles at runtime.
    /// </para>
    /// </remarks>
    public sealed class SafeAreaElement : VisualElement
    {
        /// <summary>USS hook for stylesheets layered on later.</summary>
        public const string UssClass = "safe-area";

        private const float DefaultMinimumBottomInset = 12f;

        private float _minimumBottomInset = DefaultMinimumBottomInset;

        /// <summary>Creates the element and subscribes to the events that can invalidate the insets.</summary>
        public SafeAreaElement()
        {
            name = UssClass;
            AddToClassList(UssClass);

            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            RegisterCallback<GeometryChangedEvent>(_ => Apply());
            RegisterCallback<AttachToPanelEvent>(_ => Apply());
        }

        /// <summary>Resolved top padding in panel units, after the last <see cref="Apply"/>.</summary>
        public float TopInset { get; private set; }

        /// <summary>Resolved bottom padding in panel units, after the last <see cref="Apply"/>.</summary>
        public float BottomInset { get; private set; }

        /// <summary>Resolved left padding in panel units, after the last <see cref="Apply"/>.</summary>
        public float LeftInset { get; private set; }

        /// <summary>Resolved right padding in panel units, after the last <see cref="Apply"/>.</summary>
        public float RightInset { get; private set; }

        /// <summary>
        /// Comfort floor for the bottom padding, in panel units. Never shrinks a device-reported inset.
        /// </summary>
        public float MinimumBottomInset
        {
            get => _minimumBottomInset;
            set
            {
                _minimumBottomInset = value < 0f ? 0f : value;
                Apply();
            }
        }

        /// <summary>
        /// Re-reads the device safe area and applies it as padding. Safe to call at any time; a no-op until
        /// the element is attached to a laid-out panel.
        /// </summary>
        public void Apply()
        {
            if (panel == null)
            {
                return;
            }

            var screenWidth = (float)Screen.width;
            var screenHeight = (float)Screen.height;
            if (screenWidth <= 0f || screenHeight <= 0f)
            {
                return;
            }

            var safe = Screen.safeArea;
            var scale = UnitsPerPixel(screenWidth);

            // Screen.safeArea uses a bottom-left origin; panel padding is top-left. The top inset is
            // therefore the gap above yMax, and the bottom inset is yMin itself.
            var left = safe.xMin * scale;
            var right = (screenWidth - safe.xMax) * scale;
            var top = (screenHeight - safe.yMax) * scale;
            var bottom = safe.yMin * scale;

            if (bottom < _minimumBottomInset)
            {
                bottom = _minimumBottomInset;
            }

            LeftInset = left;
            RightInset = right;
            TopInset = top;
            BottomInset = bottom;

            style.paddingLeft = left;
            style.paddingRight = right;
            style.paddingTop = top;
            style.paddingBottom = bottom;
        }

        /// <summary>
        /// Panel units per screen pixel, or 1 when the panel has not been laid out yet.
        /// </summary>
        /// <remarks>
        /// A runtime panel maps the whole screen onto its visual tree with a uniform scale, so the ratio of
        /// the tree's laid-out width to the screen width is the whole conversion. Doing the division here
        /// rather than through RuntimePanelUtils keeps this independent of helper overloads that have
        /// changed shape between Unity versions.
        /// </remarks>
        private float UnitsPerPixel(float screenWidth)
        {
            var layout = panel.visualTree.layout;
            if (layout.width <= 0f)
            {
                return 1f;
            }

            return layout.width / screenWidth;
        }
    }
}
