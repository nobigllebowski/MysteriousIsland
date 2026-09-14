// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Bootstrap/GameBootstrap.cs.
// Adapted for Vardholm: the GameContext.Current/Clear statics are gone (ADR-0012) and the only static left is a
// boolean re-entry guard that holds no services; boot moved from BeforeSceneLoad to AfterSceneLoad so the already-
// open scene can be inspected and adopted; the UI document is not created here because ForgottenIsle.Game does not
// reference ForgottenIsle.UI; and a self-heal path was added so playing any scene directly lands in a valid state.

using System;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Audio;
using ForgottenIsle.Game.Diagnostics;
using ForgottenIsle.Game.Player;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// The application's single entry point. Creates the persistent host object, builds the graph, and
    /// puts the state machine into a mode that matches whatever scene the process actually started in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the entry point is an attribute rather than a scene object: a scene object only boots the
    /// game when that scene is the one that loaded, so a developer pressing Play in a zone scene gets
    /// a half-initialised game whose failures look like bugs in whatever they were working on. A
    /// <c>RuntimeInitializeOnLoadMethod</c> hook runs whichever scene opened, so every entry converges
    /// on the same graph. That is worth more than the tidiness of a Bootstrap scene, because the
    /// alternative silently taxes every hour of iteration for the life of the project.
    /// </para>
    /// <para>
    /// WHY <c>AfterSceneLoad</c> rather than <c>BeforeSceneLoad</c>: the self-heal below has to look at
    /// what is already open to decide whether it is booting into the menu or into a zone someone
    /// double-clicked. Before the first scene loads there is nothing to look at.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        /// <summary>Frame rate requested at boot. Mobile defaults to 30 without this.</summary>
        public const int TargetFrameRate = 60;

        /// <summary>Name of the persistent host object, as it appears in the hierarchy.</summary>
        public const string HostObjectName = "[Vardholm] App";

        /// <summary>
        /// True once this process has booted.
        /// </summary>
        /// <remarks>
        /// The only static in the Game assembly, and deliberately a bool rather than a reference: it
        /// answers "has boot already happened" and nothing else, so it cannot be used to reach a
        /// service and is not the service locator ADR-0012 forbids.
        /// </remarks>
        private static bool _booted;

        /// <summary>Fills in whatever an empty zone scene does not provide.</summary>
        private ZoneFurnisher _furnisher;
        private AudioDirector _audio;

        /// <summary>
        /// The camera that exists purely to clear the screen while no zone camera does.
        /// </summary>
        /// <remarks>
        /// WHY THIS EXISTS — and it is not the reason it looks like. UI Toolkit panels render in
        /// screen-space overlay with no camera at all, so the menu appeared to work: the buttons drew
        /// and responded. What has no camera is the CLEAR. With nothing clearing the colour buffer,
        /// each frame's UI was composited on top of the last one still sitting there, and the menu
        /// slowly smeared into itself — the title and subtitle from an earlier layout pass showing
        /// through behind the current one, at the wrong size, permanently.
        /// <para>
        /// So this camera renders nothing (<c>cullingMask</c> is zero) and exists only for its clear.
        /// It is disabled the moment a zone supplies a real one, which is also what keeps "exactly one
        /// enabled camera" true during play.
        /// </para>
        /// </remarks>
        private Camera _fallbackCamera;

        private AudioListener _fallbackListener;

        /// <summary>Handle for the state subscription that drives the fallback camera.</summary>
        private IDisposable _cameraStateSubscription;

        /// <summary>
        /// Raised once per boot, as soon as the object graph exists and before the first state
        /// transition. The argument is the booting behaviour, which carries both the graph
        /// (<see cref="Context"/>) and the persistent host object every later layer attaches to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WHY THIS EVENT EXISTS — the mechanism, stated plainly because it is the one piece of the boot
        /// path that is not a straight call. <c>ForgottenIsle.Game</c> must not reference
        /// <c>ForgottenIsle.UI</c> (the UI assembly references Game, and inverting that creates a cycle
        /// Unity will not compile), so this method cannot name the UI installer, construct
        /// <c>UIService</c>, or so much as mention a screen. The alternatives were: put the UI types in
        /// Game, which collapses the layering the whole project is built around; or have the UI poll for
        /// a booted bootstrap, which is a service locator by another name. An event inverts the
        /// dependency instead: Game announces, UI listens, and the arrow still points UI -&gt; Game.
        /// </para>
        /// <para>
        /// WHY A STATIC EVENT IS NOT THE SERVICE LOCATOR ADR-0012 FORBIDS: it holds a delegate list, not
        /// a service. Nothing can read a <see cref="GameContext"/> out of it — a subscriber is *handed*
        /// the graph at the one moment it is built, exactly as a constructor parameter would be, and a
        /// type that never subscribed has no route to it at all.
        /// </para>
        /// <para>
        /// SUBSCRIBE FROM AN EARLIER INITIALIZATION PHASE than the one that raises it.
        /// <c>RuntimeInitializeLoadType.SubsystemRegistration</c> is the phase to use: the order of two
        /// hooks *within* a phase is unspecified, so a listener registering in <c>AfterSceneLoad</c>
        /// alongside <see cref="BootAfterFirstScene"/> would be a coin flip, while an earlier phase is
        /// ordered by the engine. Subscribe idempotently (<c>-=</c> then <c>+=</c>): with domain reload
        /// disabled this delegate list survives into the next play session, and
        /// <see cref="ResetStatics"/> deliberately does NOT clear it, because clearing it could run
        /// after a listener had already subscribed and would silently unwire the UI.
        /// </para>
        /// </remarks>
        public static event Action<AppBootstrap> Booted;

        /// <summary>The composed graph. Owned by this behaviour and handed down, never looked up.</summary>
        public GameContext Context { get; private set; }

        /// <summary>The one ticker, created here.</summary>
        public Ticker Ticker { get; private set; }

        /// <summary>
        /// Clears the boot guard when the editor enters play mode with domain reload disabled.
        /// </summary>
        /// <remarks>
        /// With "Enter Play Mode Options" set to reload neither domain nor scene, statics keep their
        /// values from the previous play session, so without this the second Play press would decide
        /// the game was already booted and skip composition entirely.
        /// <para>
        /// <see cref="Booted"/> is pointedly NOT cleared here. This method and a listener's own
        /// subscribe hook both run in <c>SubsystemRegistration</c>, and the order between two hooks in
        /// one phase is unspecified, so clearing the list could erase a subscription that had already
        /// been made and leave the UI dead for that play session. Listeners are required to subscribe
        /// idempotently instead, which makes a surviving delegate list harmless.
        /// </para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _booted = false;
        }

        /// <summary>Creates the persistent host and boots the game, whichever scene opened.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootAfterFirstScene()
        {
            if (_booted)
            {
                return;
            }

            _booted = true;

            var host = new GameObject(HostObjectName);
            DontDestroyOnLoad(host);
            host.AddComponent<AppBootstrap>().Build();
        }

        private void Build()
        {
            Application.targetFrameRate = TargetFrameRate;

            Context = AppCompositionRoot.Build(this);
            _furnisher = new ZoneFurnisher(Context.Session, Context.Input, Context.Interactions, Context.Log);

            // Announced here, before the transitions below, so that a listener is already wired when
            // MainMenu (and, on the self-heal path, Loading and InGame) is published and does not have to
            // reconstruct the mode it missed.
            RaiseBooted();

            // Audio lives on the persistent host so an ambient bed survives a zone change instead
            // of being cut and restarted by the load. Silent until clips exist; see AudioDirector.
            _audio = new AudioDirector(Context.Log);
            _audio.Attach(gameObject, Context.Signals);

            // Before the first state transition, so the very first menu frame is drawn onto a
            // cleared buffer rather than onto whatever the editor left in it.
            CreateFallbackCamera();
            _cameraStateSubscription = Context.Signals.Subscribe<GameStateChangedSignal>(OnStateChangedForCamera);

            Ticker = gameObject.AddComponent<Ticker>();
            Ticker.Initialize(Context.Session, Context.States, Context.Signals, Context.Log);

#if DEBUG || UNITY_EDITOR
            gameObject.AddComponent<DevOverlay>().Initialize(Context, Ticker);
#endif

            // A newly loaded zone brings its own player rig with it. Binding on the loader's event keeps
            // the wiring in one place and means travel and the self-heal path share it.
            Context.SceneLoader.Loaded += OnSceneLoaded;

            Context.States.TryTransition(GameStateId.MainMenu);
            SelfHealIntoOpenScene();

            // Last, so it sees the finished graph and whatever the self-heal path adopted. The call is
            // [Conditional] on UNITY_EDITOR/DEBUG, so it compiles out of a release player
            // entirely — including the argument evaluation.
            VardholmStartupValidator.RunAndLog(Context);
        }

        /// <summary>
        /// Publishes <see cref="Booted"/>, absorbing anything a listener throws.
        /// </summary>
        /// <remarks>
        /// The catch is deliberate and narrow in effect: a listener is a different assembly's code
        /// attached to this one's boot, and a failure over there must not leave the game with a graph but
        /// no ticker, no input and no state machine — which is what an exception escaping mid-Build would
        /// produce. A build that boots with a broken UI can still be diagnosed from the logged exception;
        /// a build that does not boot at all reports nothing.
        /// </remarks>
        private void RaiseBooted()
        {
            var booted = Booted;
            if (booted == null)
            {
                return;
            }

            try
            {
                booted(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// Converges a process that started in some arbitrary scene onto a valid, playable state.
        /// </summary>
        /// <remarks>
        /// The menu and bootstrap scenes need nothing — <see cref="GameStateId.MainMenu"/> is already
        /// correct. A zone scene that is already open is adopted as resident and the machine is walked
        /// through the legal path <c>MainMenu -&gt; Loading -&gt; InGame</c>, with a run started in
        /// <see cref="SessionService.NoSlot"/> so that nothing this convenience path creates can
        /// ever overwrite a real save.
        /// </remarks>
        private void SelfHealIntoOpenScene()
        {
            var openZone = FindOpenZoneScene();
            if (string.IsNullOrEmpty(openZone))
            {
                return;
            }

            Context.Zones.AdoptResident(openZone);
            Context.Session.BeginNewRun(SessionService.NoSlot, SessionService.DefaultActId, openZone, Environment.TickCount);

            if (Context.States.TryTransition(GameStateId.Loading).Success)
            {
                Context.States.TryTransition(GameStateId.InGame);
            }

#if DEBUG || UNITY_EDITOR
            // Developer-facing only, and only in builds that have a console to read it in. The self-heal
            // is silent in a shipped build because it cannot happen there: a player always starts in the
            // scene the build starts in.
            Debug.Log(UnityCoreLog.Prefix + "self-heal booted into zone " + openZone + ".");
#endif

            FurnishZone(openZone);
        }

        /// <summary>Builds the clear-only camera on the persistent host.</summary>
        /// <remarks>
        /// Deliberately NOT tagged <c>MainCamera</c>: it renders nothing, so anything resolving
        /// <c>Camera.main</c> must find the player's camera or nothing at all, never this one.
        /// It carries the app's only <c>AudioListener</c> outside a zone, so the menu is not
        /// listener-less; <see cref="ZoneFurnisher"/> hands that role to the zone's own listener and
        /// takes it back here.
        /// </remarks>
        private void CreateFallbackCamera()
        {
            var go = new GameObject("Fallback Camera (clear only)");
            go.transform.SetParent(transform, false);

            _fallbackCamera = go.AddComponent<Camera>();
            _fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
            _fallbackCamera.backgroundColor = new Color(0.03f, 0.05f, 0.05f);

            // Renders no layers at all. The cost is one clear per frame and nothing else.
            _fallbackCamera.cullingMask = 0;

            // Behind everything, so if a zone camera is ever enabled alongside it the zone wins.
            _fallbackCamera.depth = -100f;
            _fallbackCamera.useOcclusionCulling = false;
            _fallbackCamera.allowHDR = false;
            _fallbackCamera.allowMSAA = false;

            _fallbackListener = go.AddComponent<AudioListener>();
        }

        /// <summary>
        /// Turns the clear camera off while a zone renders, and back on the moment one does not.
        /// </summary>
        /// <remarks>
        /// Driven from the state machine rather than from scene events because the state is the thing
        /// that actually answers "is a zone on screen right now": a failed load, a quit to menu and a
        /// pause-then-quit all converge here, where a scene callback would cover only some of them.
        /// <see cref="ZoneFurnisher"/> disables it too, on its own sweep — the two agree, and
        /// disabling twice is harmless, whereas leaving it enabled for one frame over a zone is a
        /// visible flicker.
        /// </remarks>
        private void OnStateChangedForCamera(GameStateChangedSignal signal)
        {
            var zoneOnScreen = signal.To == GameStateId.InGame || signal.To == GameStateId.Paused;
            SetFallbackCameraActive(!zoneOnScreen);

            if (!zoneOnScreen)
            {
                RestoreBootAsActiveScene();
            }
        }

        /// <summary>Hands the active scene back to the boot scene when no zone is on screen.</summary>
        /// <remarks>
        /// The counterpart to making a zone active on entry. Unity promotes some other scene by
        /// itself when the active one unloads, but which one is not something to leave to chance —
        /// and doing it on leaving gameplay means the menu's own runtime objects are never created
        /// inside a zone that is about to be destroyed.
        /// </remarks>
        private void RestoreBootAsActiveScene()
        {
            var boot = gameObject.scene;
            if (!boot.IsValid() || !boot.isLoaded)
            {
                // The host is on DontDestroyOnLoad, whose pseudo-scene cannot be made active. Fall
                // back to whatever non-zone scene is loaded rather than forcing an invalid one.
                boot = SceneManager.GetSceneByName(SceneKeys.Bootstrap);
            }

            if (boot.IsValid() && boot.isLoaded && SceneManager.GetActiveScene() != boot)
            {
                SceneManager.SetActiveScene(boot);
            }
        }

        private void SetFallbackCameraActive(bool active)
        {
            if (_fallbackCamera != null)
            {
                _fallbackCamera.enabled = active;
            }

            if (_fallbackListener != null)
            {
                _fallbackListener.enabled = active;
            }
        }

        private static string FindOpenZoneScene()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && SceneKeys.IsZone(scene.name))
                {
                    return scene.name;
                }
            }

            return string.Empty;
        }

        private void OnSceneLoaded(string sceneKey)
        {
            if (SceneKeys.IsZone(sceneKey))
            {
                FurnishZone(sceneKey);
            }
        }

        /// <summary>
        /// Makes a newly resident zone stand-in-able: ground, light, entry anchor, player and camera.
        /// </summary>
        /// <remarks>
        /// This replaces the previous bind-only behaviour, which searched for a rig, found none in an
        /// empty Phase 1 zone, and returned silently — leaving the player loaded into a zone with no
        /// body, no camera and nothing under their feet. Furnishing is what lets the scene assets stay
        /// empty (and therefore un-corruptible and never out of step with the code) while the first
        /// playable state is still real.
        /// <para>
        /// The furnisher only creates what the scene does not already provide, so authored art
        /// displaces these placeholders with no code change.
        /// </para>
        /// </remarks>
        private void FurnishZone(string sceneKey)
        {
            var scene = SceneManager.GetSceneByName(sceneKey);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Context.Log.Warn(LogCode.SceneLoadSlow, "furnish: scene not resident: " + sceneKey);
                return;
            }

            // THE ZONE BECOMES THE ACTIVE SCENE, and the log line saying "active scene 'Bootstrap'"
            // while ZoneRibcage was loaded was a real finding, not noise. Two things follow the
            // active scene in Unity and both belong to the zone rather than to the boot object:
            //
            //   1. A GameObject created with no scene specified lands in the ACTIVE scene. Every
            //      runtime object the zone makes for itself would otherwise accumulate in Bootstrap
            //      and outlive the zone it belongs to, because Bootstrap is never unloaded.
            //   2. RenderSettings -- ambient light, fog, skybox -- are PER SCENE, and the active
            //      scene's are the ones used. ZoneBuilder.ApplyAtmosphere writes the zone's fog and
            //      ambient through that API, so with Bootstrap active it was writing the Ribcage's
            //      atmosphere into the boot scene and leaving it there after the zone unloaded.
            //
            // Set before furnishing, so the furnisher's own objects are created in the right place.
            if (SceneManager.GetActiveScene() != scene)
            {
                SceneManager.SetActiveScene(scene);
            }

            // A null return means the zone came up without a player, a controller or a working
            // camera. The furnisher has already said exactly which, loudly; this records that the
            // zone the player is standing in is not the one the game thinks it furnished.
            if (_furnisher.Furnish(scene) == null)
            {
                Context.Log.Warn(LogCode.FurnishIncomplete, sceneKey);
            }
        }

        private void OnDestroy()
        {
            if (_cameraStateSubscription != null)
            {
                _cameraStateSubscription.Dispose();
                _cameraStateSubscription = null;
            }

            if (Context != null)
            {
                Context.SceneLoader.Loaded -= OnSceneLoaded;

                // The router subscribes to the state machine and to Input System callbacks, both of which
                // outlive this object. Disposing it here is what stops a dead router from being kept alive
                // through those delegate lists and re-enabling a disposed action map.
                Context.Input.Dispose();
            }

            // Application quit, or a domain reload tore the host down. Either way the next boot must be
            // allowed to build a fresh graph rather than believe one already exists.
            _booted = false;
        }
    }
}
