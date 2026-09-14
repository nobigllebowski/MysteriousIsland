// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/UI/Core/UIService.cs.
// Adapted for Vardholm: the GameDataCatalog dependency is gone — the constructor is (UIDocument, ILocalizedText,
// ICoreLog) per ADR-0012, and the base styling comes from Theme in code instead of from a catalog stylesheet
// that could be missing; Nation's sheet and modal layers are not carried over (Phase 1 has no bottom sheets or
// dialogs), and the loading layer becomes a real LoadingCurtain; toasts take LocKeys rather than raw strings;
// diagnostics go to ICoreLog instead of UnityEngine.Debug.

using System;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Owns the single persistent UI document and its layers, bottom to top: screens, the loading curtain,
    /// toasts — all inside the device safe area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY one document for the whole application: every additional <see cref="UIDocument"/> is another
    /// panel, another draw-call boundary, and another place for input to be swallowed by an invisible
    /// full-screen element. One document with explicit layers makes the z-order a property of this file
    /// rather than an emergent property of scene load order.
    /// </para>
    /// <para>
    /// WHY the service exposes an <see cref="IUiContext"/> instead of itself: screens receive
    /// <see cref="Context"/>, which is a private implementation carrying only text lookup and logging. A
    /// screen therefore cannot reach the stack, the curtain or the game, which is ADR-0002's "the UI never
    /// mutates state" made structural. Controllers hold the service; screens hold the context.
    /// </para>
    /// </remarks>
    public sealed class UIService
    {
        /// <summary>Design width in dp. The whole UI is authored against a 390 x 844 phone.</summary>
        public const int ReferenceWidth = 390;

        /// <summary>Design height in dp.</summary>
        public const int ReferenceHeight = 844;

        private readonly UiContext _context;
        private Label _debugOverlay;

        /// <summary>
        /// Builds the layer stack inside the document's root element.
        /// </summary>
        /// <param name="document">The application's single UI document. Must already have a panel.</param>
        /// <param name="loc">Text lookup. Every player-visible string in the UI comes from here.</param>
        /// <param name="log">Diagnostics sink.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        /// <exception cref="ArgumentException">The document has no root visual element.</exception>
        public UIService(UIDocument document, ILocalizedText loc, ICoreLog log)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            Loc = loc ?? throw new ArgumentNullException(nameof(loc));
            Log = log ?? throw new ArgumentNullException(nameof(log));

            var root = document.rootVisualElement;
            if (root == null)
            {
                // Almost always a missing PanelSettings asset on the UIDocument. Failing here, loudly, beats
                // a null-reference from inside the first screen that tries to build.
                throw new ArgumentException(
                    "UIDocument has no rootVisualElement; assign PanelSettings before constructing UIService.",
                    nameof(document));
            }

            Root = root;
            Root.name = "app-root";
            Root.AddToClassList("app-root");
            Root.style.flexGrow = 1f;
            Root.style.backgroundColor = Theme.Background;
            Root.style.color = Theme.Text;

            SafeArea = new SafeAreaElement();
            Root.Add(SafeArea);

            // Order is z-order. Screens are covered by the curtain, and the curtain is covered by toasts, so
            // a failure reported while a zone loads is still readable.
            var screenLayer = Layer("screen-layer");
            var curtainLayer = Layer("curtain-layer");
            var toastLayer = Layer("toast-layer");

            Screens = new ScreenStack(screenLayer);
            Curtain = new LoadingCurtain(curtainLayer, Loc);
            Toasts = new ToastLayer(toastLayer);

            _context = new UiContext(Loc, Log);
        }

        /// <summary>The document's root element. Owns the safe area and nothing else.</summary>
        public VisualElement Root { get; }

        /// <summary>The padded region every layer lives inside.</summary>
        public SafeAreaElement SafeArea { get; }

        /// <summary>Navigation between full-screen views.</summary>
        public ScreenStack Screens { get; }

        /// <summary>The loading cover, with its determinate progress bar and minimum display time.</summary>
        public LoadingCurtain Curtain { get; }

        /// <summary>Queued, non-blocking notifications.</summary>
        public ToastLayer Toasts { get; }

        /// <summary>Text lookup, for controllers that need to compose text before handing it to a screen.</summary>
        public ILocalizedText Loc { get; }

        /// <summary>Diagnostics sink.</summary>
        public ICoreLog Log { get; }

        /// <summary>The narrow surface handed to every screen. Never hand a screen the service itself.</summary>
        public IUiContext Context => _context;

        /// <summary>Shows a localized notification.</summary>
        /// <param name="key">The message key. An empty key is ignored rather than shown as <c>##</c>.</param>
        /// <param name="kind">Severity, which selects the toast's edge color.</param>
        public void Toast(in LocKey key, ToastKind kind = ToastKind.Info)
        {
            if (key.IsEmpty)
            {
                return;
            }

            Toasts.Show(Loc.Get(key), kind);
        }

        /// <summary>Shows a localized notification whose pattern takes arguments.</summary>
        /// <param name="key">The pattern key.</param>
        /// <param name="kind">Severity.</param>
        /// <param name="args">Arguments, already formatted by the caller if they need locale-aware rendering.</param>
        public void ToastFormat(in LocKey key, ToastKind kind, params object[] args)
        {
            if (key.IsEmpty)
            {
                return;
            }

            Toasts.Show(Loc.Get(key, args), kind);
        }

        /// <summary>
        /// Reports the outcome of a dispatched command to the player, if it needs reporting.
        /// </summary>
        /// <remarks>
        /// WHY this lives here and not in each controller: every controller ends up needing the same
        /// result-to-message decision, and three copies of it drift within a release. A success is silent —
        /// the change on screen is the feedback, and a toast for every successful tap is noise.
        /// </remarks>
        /// <param name="code">The result of the command.</param>
        public void ToastResult(ResultCode code)
        {
            if (code == ResultCode.Ok)
            {
                return;
            }

            Toast(KeyForResult(code), KindForResult(code));
        }

        /// <summary>
        /// Shows developer statistics in a corner; pass null or empty to hide.
        /// </summary>
        /// <remarks>
        /// The one place in the UI that takes a raw string, because its content is engineering text —
        /// frame times, resident zones, state ids — that must never be translated and is never shown to a
        /// player in a shipped build.
        /// </remarks>
        /// <param name="text">Developer text, or null to hide the overlay.</param>
        public void SetDebugOverlay(string text)
        {
            if (_debugOverlay == null)
            {
                _debugOverlay = new Label { name = "debug-overlay", pickingMode = PickingMode.Ignore };
                _debugOverlay.AddToClassList("debug-overlay");
                _debugOverlay.style.position = Position.Absolute;
                _debugOverlay.style.left = Theme.Space8;
                _debugOverlay.style.top = Theme.Space8;
                _debugOverlay.style.fontSize = 11f;
                _debugOverlay.style.color = Theme.TextMuted;
                _debugOverlay.style.whiteSpace = WhiteSpace.Normal;

                // Parented to Root rather than to the safe area: it is a developer read-out, and seeing it
                // clipped by a notch is more informative than seeing it politely inset.
                Root.Add(_debugOverlay);
            }

            _debugOverlay.text = text ?? string.Empty;
            _debugOverlay.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Builds panel settings at runtime for a document that shipped without them.
        /// </summary>
        /// <remarks>
        /// A safety net, not a configuration path: a document with no panel settings renders nothing at all,
        /// so an unstyled-but-visible UI is strictly better than a black screen. The authored asset should
        /// always win; this exists so a missing reference is a cosmetic bug rather than an unplayable build.
        /// </remarks>
        /// <returns>Panel settings scaled to the 390 x 844 reference resolution.</returns>
        public static PanelSettings CreateFallbackPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(ReferenceWidth, ReferenceHeight);

            // Shrink rather than Expand: on a taller phone this keeps the design's side gutters intact and
            // adds height, instead of scaling the whole layout up until the type is oversized.
            settings.screenMatchMode = PanelScreenMatchMode.Shrink;
            return settings;
        }

        /// <summary>Maps a failure to the message the player sees.</summary>
        private static LocKey KeyForResult(ResultCode code)
        {
            switch (code)
            {
                case ResultCode.UnknownCommand:
                case ResultCode.NoHandler:
                    return new LocKey("ui.toast.error.unsupported");
                case ResultCode.IllegalStateTransition:
                case ResultCode.NotAllowedInState:
                    return new LocKey("ui.toast.error.notNow");
                case ResultCode.InvalidArgument:
                    return new LocKey("ui.toast.error.invalid");
                case ResultCode.NotFound:
                case ResultCode.SlotEmpty:
                    return new LocKey("ui.toast.error.slotEmpty");
                case ResultCode.SaveWriteFailed:
                    return new LocKey("ui.toast.error.saveFailed");
                case ResultCode.SaveCorrupt:
                    return new LocKey("ui.toast.error.saveCorrupt");
                case ResultCode.SaveVersionTooNew:
                    return new LocKey("ui.toast.error.saveTooNew");
                case ResultCode.SceneNotFound:
                    return new LocKey("ui.toast.error.sceneMissing");
                case ResultCode.AlreadyLoading:
                    return new LocKey("ui.toast.error.busy");
                case ResultCode.LoadTimedOut:
                    return new LocKey("ui.toast.error.loadTimeout");
                default:
                    return new LocKey("ui.toast.error.generic");
            }
        }

        /// <summary>
        /// Severity for a failure: a refusal is a warning, losing or failing to write data is an error.
        /// </summary>
        private static ToastKind KindForResult(ResultCode code)
        {
            switch (code)
            {
                case ResultCode.SaveWriteFailed:
                case ResultCode.SaveCorrupt:
                case ResultCode.SaveVersionTooNew:
                case ResultCode.SceneNotFound:
                case ResultCode.LoadTimedOut:
                    return ToastKind.Error;
                default:
                    return ToastKind.Warning;
            }
        }

        /// <summary>Creates one full-bleed layer inside the safe area.</summary>
        private VisualElement Layer(string layerName)
        {
            var layer = new VisualElement { name = layerName };
            layer.AddToClassList("layer");
            layer.AddToClassList(layerName);

            // Absolute so layers stack instead of sitting in a column; picking passes through by default so
            // an empty layer cannot eat a tap meant for the screen underneath it.
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.right = 0f;
            layer.style.top = 0f;
            layer.style.bottom = 0f;
            layer.pickingMode = PickingMode.Ignore;

            SafeArea.Add(layer);
            return layer;
        }

        /// <summary>
        /// The only implementation of <see cref="IUiContext"/>.
        /// </summary>
        /// <remarks>
        /// Private and nested on purpose: a screen holding an <see cref="IUiContext"/> has nothing to cast
        /// it to, so the narrow surface cannot be widened at runtime by a determined caller.
        /// </remarks>
        private sealed class UiContext : IUiContext
        {
            /// <summary>Captures the two services a screen is allowed to see.</summary>
            public UiContext(ILocalizedText loc, ICoreLog log)
            {
                Loc = loc;
                Log = log;
            }

            /// <inheritdoc />
            public ILocalizedText Loc { get; }

            /// <inheritdoc />
            public ICoreLog Log { get; }
        }
    }
}
