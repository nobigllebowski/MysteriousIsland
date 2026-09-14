// The UI assembly's own entry point. Nothing in ForgottenIsle.Game can construct a screen — Game does not
// reference UI, and must not, because UI already references Game — so the UI cannot be installed by the
// composition root the way every other subsystem is. This file is the seam: it listens for the graph to be
// built and then builds the UI on top of it, keeping the dependency arrow pointing UI -> Game.

using System;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.UI.Controllers;
using ForgottenIsle.UI.Core;
using ForgottenIsle.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace ForgottenIsle.UI.Bootstrap
{
    /// <summary>
    /// Builds the UI on top of an already-composed <see cref="GameContext"/> and keeps it in step with the
    /// application mode for the life of the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MECHANISM, AND WHY IT IS THIS ONE. <c>AppBootstrap</c> cannot call this class: naming it would
    /// make <c>ForgottenIsle.Game</c> reference <c>ForgottenIsle.UI</c>, and UI already references Game, so
    /// the two assemblies would form a cycle Unity refuses to compile. Inverting the reference instead —
    /// moving the UI under Game — would delete the layering the project is organised around. So the boot
    /// path is split: <c>AppBootstrap</c> raises <see cref="AppBootstrap.Booted"/> the moment the graph
    /// exists, and this class subscribes to it from its own
    /// <c>[RuntimeInitializeOnLoadMethod]</c>. Game announces; UI listens.
    /// </para>
    /// <para>
    /// WHY THE SUBSCRIPTION RUNS IN <c>SubsystemRegistration</c> AND NOT IN <c>AfterSceneLoad</c>: Unity
    /// orders initialization PHASES but says nothing about the order of two hooks inside one phase. A hook
    /// in <c>AfterSceneLoad</c>, where <c>AppBootstrap</c> boots, would therefore be a coin flip — half the
    /// time it would subscribe after the event had already been raised and the game would show an empty
    /// screen, which is precisely the failure this file exists to fix. <c>SubsystemRegistration</c> is an
    /// earlier phase, so the engine guarantees the listener is in place first. The subscription is also
    /// made idempotently, because with domain reload disabled the static delegate list survives into the
    /// next play session.
    /// </para>
    /// <para>
    /// WHY THE INSTALLER IS A <see cref="MonoBehaviour"/> rather than a static helper: it owns
    /// subscriptions that must be released, and the persistent host object's destruction is the only
    /// honest signal that the application is going away. Living on that object makes teardown automatic
    /// instead of something a caller must remember.
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT DO: it never mutates game state. It constructs controllers, hands them the
    /// dispatcher, and translates <see cref="GameStateChangedSignal"/> into which screen is up. The single
    /// transition it performs itself is the failure screen's <c>LoadFailed -&gt; MainMenu</c>, which is a
    /// mode change with no command in the Phase 1 set and no data behind it.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UiInstaller : MonoBehaviour
    {
        /// <summary>Caption shown on the curtain while the application is in the loading mode.</summary>
        private static readonly LocKey LoadingCaptionKey = new LocKey("ui.loading.streaming_zone");

        private GameContext _context;
        private UIService _ui;
        private SaveSlotService _slots;
        private MainMenuController _menu;
        private PauseController _pause;
        private IdleScreen _idleScreen;
        private HudController _hud;
        private IVisualElementScheduledItem _inputPump;
        private LoadFailedScreen _loadFailedScreen;
        private IDisposable _stateSubscription;

        /// <summary>The UI service this installer built, or null before <see cref="Install"/> succeeds.</summary>
        /// <remarks>Exposed for diagnostics and play-mode tests; nothing in the UI reads the service this way.</remarks>
        public UIService Ui => _ui;

        /// <summary>
        /// Puts the listener in place, one initialization phase before the event can be raised.
        /// </summary>
        /// <remarks>
        /// <c>-=</c> before <c>+=</c> so that a delegate list surviving a domain-reload-free play session
        /// cannot accumulate a second copy of the handler and install the UI twice.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Arm()
        {
            AppBootstrap.Booted -= OnBooted;
            AppBootstrap.Booted += OnBooted;
        }

        /// <summary>Attaches an installer to the persistent host as soon as the graph exists.</summary>
        private static void OnBooted(AppBootstrap bootstrap)
        {
            if (bootstrap == null || bootstrap.Context == null)
            {
                return;
            }

            var host = bootstrap.gameObject;

            // [DisallowMultipleComponent] turns a second AddComponent into a logged error rather than a
            // no-op, so the presence check is what keeps a re-boot quiet.
            if (host.GetComponent<UiInstaller>() != null)
            {
                return;
            }

            // The host check above only sees THIS host. A panel left behind by an earlier install --
            // another host, a document authored into a scene, a play session that did not reload the
            // domain -- renders its own complete copy of every screen on top of ours, at its own
            // panel scale, and takes the taps meant for ours. One application, one panel.
            RemoveForeignPanels(host);

            host.AddComponent<UiInstaller>().Install(bootstrap.Context);
        }

        /// <summary>Destroys every <see cref="UIDocument"/> in the application except the host's own.</summary>
        /// <remarks>
        /// Logged rather than silent, because a second panel is never something the project intended:
        /// either an install ran twice or a scene carries a document it should not, and both are worth
        /// hearing about once rather than discovering as an unclickable menu.
        /// </remarks>
        private static void RemoveForeignPanels(GameObject host)
        {
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            for (var i = 0; i < documents.Length; i++)
            {
                var document = documents[i];
                if (document == null || document.gameObject == host)
                {
                    continue;
                }

                Debug.LogWarning(
                    "[Vardholm] ui: destroying a second UIDocument on '" + document.gameObject.name +
                    "'. Two panels render two copies of the UI and fight over every tap.");

                UnityEngine.Object.Destroy(document);
            }
        }

        /// <summary>
        /// Creates the document, the service, the controllers and the screens, and binds them to the mode.
        /// </summary>
        /// <param name="context">The composed graph. Required.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
        public void Install(GameContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (_context != null)
            {
                return;
            }

            _context = context;

            var document = CreateDocument(context.Log);
            if (document == null)
            {
                return;
            }

            _ui = new UIService(document, context.Localization, context.Log);

            // Read-only slot access, so the menu can render CONTINUE from real slot metadata. This is the
            // single shared instance from the composition root — a second service over the same directory
            // would mean two caches that disagree the moment either writes. The UI only ever reads it;
            // every write still goes through a command.
            _slots = context.Slots;

            _idleScreen = new IdleScreen(_ui.Context);

            // The HUD replaces Phase 1's inert idle screen as what occupies the stack in-world. It
            // is a screen rather than a separate overlay so it participates in the same transitions
            // and z-order as everything else.
            _hud = new HudController(
                new HudScreen(_ui.Context, OnPauseRequested),
                context.Signals,
                context.Localization,
                context.Log);
            _loadFailedScreen = new LoadFailedScreen(_ui.Context, OnReturnToMenu);

            _menu = new MainMenuController(_ui, context.Commands, _slots, context.Log, Application.version);

            // The pause menu is given a snapshot factory, never the run itself: PauseSnapshot is a value,
            // so the controller can read the five facts it renders and can change none of them.
            _pause = new PauseController(_ui, context.Commands, context.States, CaptureSnapshot, context.Log);

            // The pause button is the one route into the pause menu that does not start on a screen. The
            // router only raises it from InGame, so there is no matching "unpause" edge here: RESUME on the
            // panel is what leaves, and it is a button the player can see.
            context.Input.Pause += OnPauseRequested;
            // The pause button is deliberately live in BOTH InGame and Paused (see VardholmControls'
            // System map). InputRouter decides which meaning applies and raises Pause or Resume; if we
            // only subscribed to Pause, the key would open the pause screen and then be inert — which is
            // exactly the bug the two-map split was written to prevent.
            context.Input.Resume += OnResumeRequested;

            // The menu is the UI's resting state, so it goes up before anything else can ask for a screen.
            _menu.Show();

            _stateSubscription = context.Signals.Subscribe<GameStateChangedSignal>(OnGameStateChanged);

            // Boot normally raises Booted before the first transition, so the subscription above catches
            // MainMenu live. Applying the current mode as well costs one switch and makes the installer
            // correct even when it is attached to a machine that has already moved on.
            ApplyState(context.States.Current);
        }

        /// <summary>
        /// Creates the application's one <see cref="UIDocument"/> on the persistent host.
        /// </summary>
        /// <remarks>
        /// The panel settings are built in code by <see cref="UIService.CreateFallbackPanelSettings"/>
        /// rather than referenced as an asset, because this object is created at runtime and has no
        /// inspector for anyone to assign one in. A <c>UIDocument</c> only builds its root element while
        /// enabled AND holding panel settings, and this one is added before the settings exist — hence the
        /// off/on cycle, which is what forces the panel to be rebuilt now that both conditions hold.
        /// </remarks>
        /// <param name="log">Diagnostics sink, used if the document refuses to produce a root.</param>
        /// <returns>The document, or null when no root element could be produced.</returns>
        private UIDocument CreateDocument(ICoreLog log)
        {
            var document = gameObject.GetComponent<UIDocument>();
            if (document == null)
            {
                document = gameObject.AddComponent<UIDocument>();
            }

            if (document.panelSettings == null)
            {
                document.panelSettings = UIService.CreateFallbackPanelSettings();
            }

            if (document.rootVisualElement == null)
            {
                document.enabled = false;
                document.enabled = true;
            }

            if (document.rootVisualElement == null)
            {
                // Nothing further can be built on a document with no panel, and throwing here would take
                // the rest of boot down with it. The game runs without a UI; the log says why.
                log.Warn(LogCode.CatalogMissing, "ui: UIDocument produced no root visual element");
                return null;
            }

            // THE MENU WAS BEING BUILT TWICE, AND THIS IS WHERE IT SHOWED. A UIDocument that already
            // holds a tree keeps it: building into it again ADDS a second copy rather than replacing
            // the first. Two copies of the same screen, each laid out by a panel with its own scale,
            // is exactly the doubled menu on screen -- the title drawn over the subtitle, CONTINUE
            // drawn over its own slot label -- and the copy on top swallows the taps aimed at the
            // one underneath, which is why nothing was clickable.
            //
            // A title and a subtitle are siblings in one flex column. They cannot overlap. Seeing
            // them overlap is proof there are two columns, not one.
            var existingChildren = document.rootVisualElement.childCount;
            if (existingChildren > 0)
            {
                Debug.LogWarning(
                    "[Vardholm] ui: the UI panel already held " + existingChildren +
                    " root element(s) before install; clearing them. This is a stale tree from an " +
                    "earlier install and would have rendered a second copy of every screen.");

                document.rootVisualElement.Clear();
            }

            return document;
        }

        /// <summary>
        /// Composes the read-only save-slot reader the main menu needs.
        /// </summary>
        /// <remarks>
        /// ⚠ VERIFY — this is a second <see cref="SaveSlotService"/>, and it is deliberate, read-only and
        /// temporary. <see cref="GameContext"/> exposes the session, the dispatcher and the scene loader
        /// but not the slot service, so there is no way to reach the composed one from here. The menu uses
        /// this instance for exactly two calls — <c>TryFindMostRecent</c> and <c>ReadMetadata</c> — both of
        /// which read a header off disk and touch no participant, so the two instances cannot disagree:
        /// every WRITE still goes through the composed service via <c>SaveGameCommand</c> and
        /// <c>ResumeSavedRunCommand</c>. The fix is one property on <see cref="GameContext"/> (see the
        /// report accompanying this change); when it lands, this method deletes and the context's own
        /// service is passed to the controller instead.
        /// </remarks>
        /// <param name="log">Diagnostics sink shared with the rest of the graph.</param>

        /// <summary>
        /// Takes a fresh read-only view of the run for the pause menu.
        /// </summary>
        /// <remarks>
        /// The one place in the UI assembly that reads the live run, and it reads only getters and copies
        /// the results into a value type. The zone key is derived the same way the save header derives it,
        /// so the pause panel and the save-slot card name a zone identically.
        /// </remarks>
        private PauseSnapshot CaptureSnapshot()
        {
            var run = _context.Session;
            return new PauseSnapshot(
                run.HasRun,
                run.BoundSlot,
                SceneKeys.ZoneDisplayKey(run.ZoneId),
                run.PlaytimeSeconds,
                run.RecordedPercent);
        }

        /// <summary>
        /// The player pressed pause.
        /// </summary>
        /// <remarks>
        /// Delegated to the controller rather than transitioned here, so that the one implementation of
        /// "pause" — transition, snapshot, panel, and a toast if the mode machine refuses — stays in one
        /// place and is the same whether the request came from a button or from this key.
        /// </remarks>
        /// <summary>
        /// Pumps the on-screen thumb controls into the input router, once per frame.
        /// </summary>
        /// <remarks>
        /// Scheduled on the panel rather than run from a <c>MonoBehaviour.Update</c>. The project
        /// keeps exactly one Update (the Ticker), and UI Toolkit's scheduler is the UI layer's own
        /// per-frame hook — so this costs no new engine loop and dies with the panel automatically.
        /// <para>
        /// Move is a held state and is written every frame, including zero. Look is a delta and is
        /// consumed, so a finger held still stops turning the camera.
        /// </para>
        /// </remarks>
        private void StartInputPump()
        {
            if (_inputPump != null)
            {
                return;
            }

            _inputPump = _hud.Screen.Root.schedule.Execute(() =>
            {
                var touch = _hud.Screen.Touch;
                if (touch == null || _context == null)
                {
                    return;
                }

                _context.Input.SetVirtualMove(touch.Move);
                _context.Input.AddVirtualLook(touch.ConsumeLook());
            }).Every(0);
        }

        private void OnPauseRequested()
        {
            _pause.Open();
        }

        /// <summary>
        /// Handles the pause button being pressed while already paused.
        /// </summary>
        /// <remarks>
        /// Routed through the controller's <c>Close</c> for the same reason as
        /// <see cref="OnPauseRequested"/>: one implementation of "unpause", whether the request arrived
        /// from the on-screen RESUME button or from this key.
        /// </remarks>
        private void OnResumeRequested()
        {
            _pause.Close();
        }

        /// <summary>Mirrors a completed mode change onto the UI.</summary>
        private void OnGameStateChanged(GameStateChangedSignal signal)
        {
            ApplyState(signal.To);
        }

        /// <summary>
        /// Puts the UI into the shape one application mode calls for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Total over the modes that have a UI, and deliberately silent about <c>Boot</c>: there is no
        /// screen for a game that has not finished starting, and showing the menu early would offer
        /// CONTINUE before the slots could be read.
        /// </para>
        /// <para>
        /// Every branch is idempotent — the curtain ignores a second Hide, the pause controller ignores a
        /// Show while it is already up — so arriving at the same mode twice cannot stack two copies of
        /// anything.
        /// </para>
        /// </remarks>
        /// <param name="state">The mode the application is now in.</param>
        private void ApplyState(GameStateId state)
        {
            if (_ui == null)
            {
                return;
            }

            switch (state)
            {
                case GameStateId.MainMenu:
                    _hud.SetGameplayActive(false);
                    _pause.Hide();
                    _ui.Curtain.Hide();
                    _menu.Show();
                    ReportMenuState();
                    break;

                case GameStateId.Loading:
                    _hud.SetGameplayActive(false);
                    // Bound to the loader's own progress, so the bar is determinate and a wedged load looks
                    // different from a slow one.
                    _ui.Curtain.Show(LoadingCaptionKey, _context.SceneLoader);
                    break;

                case GameStateId.InGame:
                    _pause.Hide();
                    _ui.Curtain.Hide();

                    // Phase 2 replaces Phase 1's inert idle screen with the real HUD. The stack is
                    // still cleared first -- the menu or the failure panel has to leave -- and the
                    // HUD is what remains, because ScreenStack refuses to be left empty.
                    _ui.Screens.ReplaceAll(_hud.Screen);
                    _hud.PrimeObjective(_context.Progress.ObjectiveKey);
                    _hud.SetGameplayActive(true);
                    StartInputPump();
                    break;

                case GameStateId.Paused:
                    // Controls down before the panel goes up: a stick still deflected behind a pause
                    // menu keeps the player walking into scenery they cannot see.
                    _hud.SetGameplayActive(false);
                    _pause.Show();
                    break;

                case GameStateId.LoadFailed:
                    _pause.Hide();
                    _ui.Curtain.Hide();
                    _ui.Screens.ReplaceAll(_loadFailedScreen);
                    break;

                default:
                    // Boot. No UI yet, on purpose.
                    break;
            }
        }

        /// <summary>
        /// States, in one console line, every condition that can make the menu look right and not work.
        /// </summary>
        /// <remarks>
        /// Each of these has already cost a round of guessing. A second panel renders a whole second
        /// copy of the UI and fights for taps; a stale tree in one panel does the same thing inside it;
        /// a curtain that never finished hiding covers the menu and eats every pointer event; and a
        /// stack stuck mid-transition refuses navigation while looking completely normal. None of them
        /// is visible in a screenshot, and all four are one line of state.
        /// </remarks>
        private void ReportMenuState()
        {
            var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            var roots = _ui.Root != null ? _ui.Root.childCount : -1;

            Debug.Log(
                "[Vardholm] ui: menu shown · panels " + documents.Length +
                " (must be 1) · root children " + roots +
                " · screens " + _ui.Screens.Count +
                " · transitioning " + _ui.Screens.IsTransitioning +
                " · curtain " + (_ui.Curtain.IsVisible ? "UP (blocks taps)" : "down"));

            // One frame later, because resolvedStyle is meaningless until layout has run. The
            // logical panel height is the number that mattered: at 219 against a design of 844 the
            // menu was crushed to a quarter size, which is what made it overlap and stop responding.
            _ui.Root?.schedule.Execute(ReportPanelSize);

            if (documents.Length != 1)
            {
                Debug.LogError(
                    "[Vardholm] ui: " + documents.Length + " UI panels exist. The menu is being drawn " +
                    "more than once and taps are going to the copy you are not looking at.");
            }
        }

        private void OnRootGeometryKnown(GeometryChangedEvent evt)
        {
            _ui.Root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryKnown);
            ReportPanelSize();
        }

        /// <summary>Reports the logical size the layout was actually given.</summary>
        private void ReportPanelSize()
        {
            if (_ui == null || _ui.Root == null)
            {
                return;
            }

            var width = _ui.Root.resolvedStyle.width;
            var height = _ui.Root.resolvedStyle.height;

            // One scheduled tick was not enough -- the first report printed NaN, because layout had
            // still not run. Waiting for the element to actually be given a size is the only
            // reliable signal; a fixed delay would just be a longer guess.
            if (float.IsNaN(width) || float.IsNaN(height))
            {
                _ui.Root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryKnown);
                return;
            }

            Debug.Log(
                "[Vardholm] ui: panel is " + width.ToString("F0") + " x " + height.ToString("F0") +
                " logical px against a " + UIService.ReferenceWidth + " x " + UIService.ReferenceHeight +
                " design (screen " + Screen.width + " x " + Screen.height + ").");

            if (height < UIService.ReferenceHeight * 0.75f)
            {
                Debug.LogError(
                    "[Vardholm] ui: the panel is only " + height.ToString("F0") + " logical px tall. " +
                    "The layout will be crushed: text overflows across its neighbours and buttons " +
                    "shrink away from their own labels, which reads as an unclickable menu.");
            }
        }

        /// <summary>
        /// Leaves the failure screen for the menu.
        /// </summary>
        /// <remarks>
        /// A transition rather than <c>QuitToMenuCommand</c>, because that command covers abandoning a live
        /// run and refuses <c>LoadFailed</c> outright: a failed load entered nothing, so there is nothing to
        /// tear down, and <c>LoadFailed -&gt; MainMenu</c> is the edge the transition table provides for
        /// exactly this exit. A refusal is surfaced rather than worked around.
        /// </remarks>
        private void OnReturnToMenu()
        {
            var result = _context.States.TryTransition(GameStateId.MainMenu);
            if (!result.Success)
            {
                _ui.ToastResult(result.Code);
            }
        }

        /// <summary>
        /// Releases every subscription when the persistent host goes away.
        /// </summary>
        /// <remarks>
        /// The bus and the input router outlive this component in the editor, where a stopped play session
        /// tears the host down while static delegate lists can survive. A subscription left behind would
        /// keep a dead installer — and through it a whole dead UI tree — alive, and would drive it from the
        /// next session's transitions. The input handler is detached defensively: component destruction
        /// order is unspecified, so the router may or may not already have disposed itself, and a
        /// <c>-=</c> for a handler that is no longer in the list is a no-op either way.
        /// </remarks>
        private void OnDestroy()
        {
            if (_stateSubscription != null)
            {
                _stateSubscription.Dispose();
                _stateSubscription = null;
            }

            if (_context != null)
            {
                _context.Input.Pause -= OnPauseRequested;
                _inputPump?.Pause();
                _hud?.Dispose();
                _context.Input.Resume -= OnResumeRequested;
            }
        }

        /// <summary>
        /// The screen that occupies the stack while the player is in the world.
        /// </summary>
        /// <remarks>
        /// Builds nothing and takes no input. It exists because <c>ScreenStack</c> guarantees it is never
        /// empty, so "no screen" has to be spelled as "a screen with nothing in it". When the HUD arrives
        /// it replaces this class outright rather than being added beside it.
        /// </remarks>
        private sealed class IdleScreen : UIScreen
        {
            /// <summary>Creates the inert screen and opts it out of picking.</summary>
            /// <param name="context">Text lookup and logging, unused but required by the base type.</param>
            public IdleScreen(IUiContext context)
                : base(context, "idle")
            {
                // Without this the full-bleed root would sit over the world and swallow every tap.
                Root.pickingMode = PickingMode.Ignore;
            }

            /// <inheritdoc />
            protected override void Build(VisualElement root)
            {
                // Intentionally empty: this screen's entire job is to occupy the stack.
            }
        }

        /// <summary>
        /// Shown when a load fails. Says so, and offers the one way out.
        /// </summary>
        /// <remarks>
        /// One button and no retry: <c>LoadFailed</c>'s only outgoing edge is to the menu, and a RETRY that
        /// the transition table would refuse is worse than no RETRY at all.
        /// </remarks>
        private sealed class LoadFailedScreen : UIScreen
        {
            private static readonly LocKey TitleKey = new LocKey("ui.loading.failed_title");
            private static readonly LocKey ReturnKey = new LocKey("ui.loading.return_to_menu");

            private readonly Action _onReturn;

            /// <summary>Creates the failure panel. Nothing is built until the stack shows it.</summary>
            /// <param name="context">Text lookup and logging.</param>
            /// <param name="onReturn">Raised when RETURN TO MENU is pressed. Required.</param>
            /// <exception cref="ArgumentNullException"><paramref name="onReturn"/> is null.</exception>
            public LoadFailedScreen(IUiContext context, Action onReturn)
                : base(context, "load-failed")
            {
                _onReturn = onReturn ?? throw new ArgumentNullException(nameof(onReturn));
            }

            /// <inheritdoc />
            protected override void Build(VisualElement root)
            {
                root.style.backgroundColor = Theme.Background;
                root.style.alignItems = Align.Center;
                root.style.justifyContent = Justify.Center;
                root.style.paddingLeft = Theme.Space24;
                root.style.paddingRight = Theme.Space24;

                var card = new VisualElement { name = "load-failed-card" };
                card.AddToClassList("card");
                card.style.width = Length.Percent(100f);
                card.style.maxWidth = 340f;
                card.style.flexDirection = FlexDirection.Column;
                card.style.paddingLeft = Theme.Space24;
                card.style.paddingRight = Theme.Space24;
                card.style.paddingTop = Theme.Space24;
                card.style.paddingBottom = Theme.Space24;
                card.style.backgroundColor = Theme.Surface;
                card.style.borderTopLeftRadius = Theme.RadiusLg;
                card.style.borderTopRightRadius = Theme.RadiusLg;
                card.style.borderBottomLeftRadius = Theme.RadiusLg;
                card.style.borderBottomRightRadius = Theme.RadiusLg;
                card.style.borderTopWidth = 1f;
                card.style.borderBottomWidth = 1f;
                card.style.borderLeftWidth = 1f;
                card.style.borderRightWidth = 1f;
                card.style.borderTopColor = Theme.Border;
                card.style.borderBottomColor = Theme.Border;
                card.style.borderLeftColor = Theme.Border;
                card.style.borderRightColor = Theme.Border;
                root.Add(card);

                var title = Typography.Title(Text(TitleKey), "load-failed__title");
                title.style.unityTextAlign = TextAnchor.MiddleCenter;
                title.style.color = Theme.Danger;
                title.style.marginBottom = Theme.Space24;
                card.Add(title);

                var back = Buttons.Primary(Text(ReturnKey), _onReturn);
                back.name = "load-failed-return";
                card.Add(back);
            }
        }
    }
}
