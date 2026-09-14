// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Game/Bootstrap/GameBootstrap.cs.
// Adapted for Vardholm: the GameContext.Current/Clear statics are gone (ADR-0012) and the only static left is a
// boolean re-entry guard that holds no services; boot moved from BeforeSceneLoad to AfterSceneLoad so the already-
// open scene can be inspected and adopted; the UI document is not created here because ForgottenIsle.Game does not
// reference ForgottenIsle.UI; and a self-heal path was added so playing any scene directly lands in a valid state.

using System;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.State;
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
            _furnisher = new ZoneFurnisher(Context.Session, Context.Input, Context.Log);

            // Announced here, before the transitions below, so that a listener is already wired when
            // MainMenu (and, on the self-heal path, Loading and InGame) is published and does not have to
            // reconstruct the mode it missed.
            RaiseBooted();

            Ticker = gameObject.AddComponent<Ticker>();
            Ticker.Initialize(Context.Session, Context.States, Context.Signals, Context.Log);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            gameObject.AddComponent<DevOverlay>().Initialize(Context, Ticker);
#endif

            // A newly loaded zone brings its own player rig with it. Binding on the loader's event keeps
            // the wiring in one place and means travel and the self-heal path share it.
            Context.SceneLoader.Loaded += OnSceneLoaded;

            Context.States.TryTransition(GameStateId.MainMenu);
            SelfHealIntoOpenScene();

            // Last, so it sees the finished graph and whatever the self-heal path adopted. The call is
            // [Conditional] on UNITY_EDITOR/DEVELOPMENT_BUILD, so it compiles out of a release player
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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Developer-facing only, and only in builds that have a console to read it in. The self-heal
            // is silent in a shipped build because it cannot happen there: a player always starts in the
            // scene the build starts in.
            Debug.Log(UnityCoreLog.Prefix + "self-heal booted into zone " + openZone + ".");
#endif

            FurnishZone(openZone);
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

            _furnisher.Furnish(scene);
        }

        private void OnDestroy()
        {
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
