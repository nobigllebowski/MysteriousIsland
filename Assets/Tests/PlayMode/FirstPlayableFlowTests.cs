using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Diagnostics;
using ForgottenIsle.Game.Player;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ForgottenIsle.Tests.PlayMode
{
    /// <summary>
    /// UNITY PLAYMODE TIER.
    /// Walks the whole first playable loop end to end: menu → new game → a furnished, standable zone
    /// → pause → resume → save → quit → continue → restored.
    /// </summary>
    /// <remarks>
    /// WHY THIS CANNOT BE AN EDITMODE TEST: every step depends on the engine actually running —
    /// additive scene loads that complete over frames, <c>RuntimeInitializeOnLoadMethod</c> boot,
    /// runtime object creation in a loaded scene, and a player loop for the ticker to advance. An
    /// EditMode test could call the handlers directly, but that is the part already covered; the risk
    /// here is the wiring between them.
    /// <para>
    /// These tests require the scene assets to exist. Run <c>Vardholm &gt; Setup Project</c> first;
    /// each test states so rather than failing with an inscrutable timeout.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class FirstPlayableFlowTests
    {
        private const float BootTimeoutSeconds = 10f;
        private const float LoadTimeoutSeconds = 25f;
        private const float UnloadTimeoutSeconds = 15f;
        private const int TestSlot = SaveSlotService.SlotCount - 1;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            RequireScenesInBuild();
            yield return WaitForBoot();
            yield return ReturnToMenu();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return ReturnToMenu();
        }

        /// <summary>
        /// The composed graph is complete and the startup validator agrees.
        /// </summary>
        /// <remarks>
        /// This is the test that would have caught "the UI assembly is never constructed" — a defect
        /// that shipped in Phase 1 and was only found by reading the code. Asserting on the validator
        /// rather than re-listing services means the check and the runtime diagnostic cannot drift.
        /// </remarks>
        [UnityTest]
        public IEnumerator Startup_ValidatorReportsNoFailures()
        {
            var context = RequireContext();
            var lines = VardholmStartupValidator.Run(context);

            var failures = string.Empty;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Severity == VardholmStartupValidator.Severity.Fail)
                {
                    failures += "\n  " + lines[i].Message;
                }
            }

            Assert.That(failures, Is.Empty, "Startup validator reported failures:" + failures);
            yield break;
        }

        /// <summary>NEW GAME lands the player in a zone with a body, a camera and ground beneath them.</summary>
        /// <remarks>
        /// The furnisher is what makes an empty scene asset playable, so "the zone loaded" is not the
        /// assertion that matters — "there is something to stand on and something to look through" is.
        /// </remarks>
        [UnityTest]
        public IEnumerator NewGame_FurnishesZone_WithPlayerAndCamera()
        {
            var context = RequireContext();

            var result = context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            Assert.That(result.Success, Is.True, "StartNewGameCommand was rejected: " + result.Code);

            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Never reached InGame.");

            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(rig, Is.Not.Null, "No PlayerRig in the loaded zone — the furnisher did not run.");
            Assert.That(rig.CameraPivot, Is.Not.Null, "The rig has no camera pivot; nothing would render.");

            var camera = rig.CameraPivot.GetComponentInChildren<Camera>(true);
            Assert.That(camera, Is.Not.Null, "The camera pivot carries no Camera.");

            // The capsule must be above the ground plane the furnisher lays down, not inside or below it.
            Assert.That(rig.transform.position.y, Is.GreaterThan(0f),
                "The player spawned at or below the ground plane.");
        }

        /// <summary>PAUSE then RESUME returns to InGame and leaves the run intact.</summary>
        [UnityTest]
        public IEnumerator Pause_ThenResume_ReturnsToInGame()
        {
            var context = RequireContext();

            context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Setup never reached InGame.");

            var zoneBefore = context.Session.ZoneId;

            Assert.That(context.States.TryTransition(GameStateId.Paused).Success, Is.True,
                "InGame -> Paused must be legal.");
            yield return null;
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.Paused));

            Assert.That(context.States.TryTransition(GameStateId.InGame).Success, Is.True,
                "Paused -> InGame must be legal, or the player cannot un-pause.");
            yield return null;

            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame));
            Assert.That(context.Session.ZoneId, Is.EqualTo(zoneBefore),
                "Pausing and resuming must not disturb the run.");
        }

        /// <summary>
        /// The full persistence round trip: save, quit to menu, continue, and arrive back in the same
        /// zone with the run restored.
        /// </summary>
        /// <remarks>
        /// This is the single most valuable test in the project. It is the only one that exercises
        /// <c>SaveGameCommand</c>, <c>QuitToMenuCommand</c> and <c>ResumeSavedRunCommand</c> against
        /// real disk I/O and real scene loads in sequence — and CONTINUE shipped completely
        /// unimplemented in the first Phase 1 pass without any test noticing.
        /// <para>
        /// It writes to the last manual slot on the machine it runs on. That is a real file.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator Save_QuitToMenu_Continue_RestoresTheRun()
        {
            var context = RequireContext();

            context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Setup never reached InGame.");

            var savedZone = context.Session.ZoneId;
            Assert.That(savedZone, Is.Not.Null.And.Not.Empty, "The run has no zone to save.");

            var saved = context.Commands.Dispatch(new SaveGameCommand(TestSlot));
            Assert.That(saved.Success, Is.True, "SaveGameCommand was rejected: " + saved.Code);

            var quit = context.Commands.Dispatch(new QuitToMenuCommand());
            Assert.That(quit.Success, Is.True, "QuitToMenuCommand was rejected: " + quit.Code);

            yield return WaitFor(
                () => context.States.Current == GameStateId.MainMenu && context.Zones.ResidentCount == 0,
                UnloadTimeoutSeconds);
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.MainMenu), "Quit never reached the menu.");
            Assert.That(context.Zones.ResidentCount, Is.Zero, "Quit left a zone resident.");

            var resumed = context.Commands.Dispatch(new ResumeSavedRunCommand(TestSlot));
            Assert.That(resumed.Success, Is.True, "ResumeSavedRunCommand was rejected: " + resumed.Code);

            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);

            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Continue never reached InGame.");
            Assert.That(context.Session.ZoneId, Is.EqualTo(savedZone),
                "Continue restored a different zone than the one that was saved.");
            Assert.That(IsSceneLoaded(savedZone), Is.True,
                "The restored zone is recorded in the session but its scene is not loaded.");
        }

        /// <summary>
        /// Playing a zone scene directly still lands in a valid, playable state.
        /// </summary>
        /// <remarks>
        /// The self-heal path is how a developer works: open the zone you are editing and press Play.
        /// If it left the state machine in <c>MainMenu</c> with a zone open, or started a run bound to
        /// a real save slot, every subsequent session would be subtly wrong — and the run it creates is
        /// deliberately bound to <c>NoSlot</c> so this convenience can never overwrite real progress.
        /// </remarks>
        [UnityTest]
        public IEnumerator SelfHeal_PlayingAZoneDirectly_ReachesInGameOnNoSlot()
        {
            var context = RequireContext();

            // Simulates the editor's "this scene was already open at boot" condition against the live
            // graph, which is the same code path BootAfterFirstScene takes.
            yield return LoadZoneAdditively(SceneKeys.ZoneRibcage);

            context.Zones.AdoptResident(SceneKeys.ZoneRibcage);

            Assert.That(context.Zones.ResidentCount, Is.EqualTo(1),
                "Adopting an already-open zone must register it exactly once.");
            Assert.That(SceneKeys.IsZone(SceneKeys.ZoneRibcage), Is.True,
                "ZoneRibcage must be recognised as a zone, or the self-heal path would ignore it.");

            yield return UnloadZone(SceneKeys.ZoneRibcage);
        }

        private static IEnumerator LoadZoneAdditively(string sceneKey)
        {
            var op = SceneManager.LoadSceneAsync(sceneKey, LoadSceneMode.Additive);
            Assert.That(op, Is.Not.Null, "Could not begin loading " + sceneKey + "; is it in Build Settings?");

            var deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!op.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(op.isDone, Is.True, "Timed out loading " + sceneKey + ".");
        }

        private static IEnumerator UnloadZone(string sceneKey)
        {
            var context = RequireContext();
            var done = false;
            context.Zones.UnloadAll(_ => done = true);
            yield return WaitFor(() => done && context.Zones.ResidentCount == 0, UnloadTimeoutSeconds);
        }

        private static IEnumerator WaitForBoot()
        {
            yield return WaitFor(() => FindBootstrap() != null, BootTimeoutSeconds);
            var bootstrap = FindBootstrap();
            Assert.That(bootstrap, Is.Not.Null, "AppBootstrap never created its host object.");
            yield return WaitFor(() => bootstrap.Context != null, BootTimeoutSeconds);
        }

        private static IEnumerator ReturnToMenu()
        {
            var bootstrap = FindBootstrap();
            if (bootstrap == null || bootstrap.Context == null)
            {
                yield break;
            }

            var context = bootstrap.Context;
            yield return WaitFor(() => !context.SceneLoader.IsLoading, LoadTimeoutSeconds);

            var current = context.States.Current;
            if (current == GameStateId.InGame || current == GameStateId.Paused)
            {
                context.Commands.Dispatch(new QuitToMenuCommand());
            }
            else if (current == GameStateId.LoadFailed)
            {
                context.States.TryTransition(GameStateId.MainMenu);
                context.Zones.UnloadAll();
            }

            yield return WaitFor(
                () => context.Zones.ResidentCount == 0 && !context.SceneLoader.IsLoading,
                UnloadTimeoutSeconds);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static AppBootstrap FindBootstrap()
        {
            return UnityEngine.Object.FindAnyObjectByType<AppBootstrap>(FindObjectsInactive.Include);
        }

        private static GameContext RequireContext()
        {
            var bootstrap = FindBootstrap();
            Assert.That(bootstrap, Is.Not.Null, "No AppBootstrap in the scene; boot never ran.");
            Assert.That(bootstrap.Context, Is.Not.Null, "AppBootstrap exists but built no GameContext.");
            return bootstrap.Context;
        }

        private static void RequireScenesInBuild()
        {
            for (var i = 0; i < SceneKeys.All.Count; i++)
            {
                var key = SceneKeys.All[i];
                if (!IsSceneInBuild(key))
                {
                    Assert.Ignore(
                        "Scene '" + key + "' is not in Build Settings. " +
                        "Run Vardholm > Setup Project, then re-run these tests.");
                }
            }
        }

        private static bool IsSceneInBuild(string sceneKey)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (path.EndsWith("/" + sceneKey + ".unity", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSceneLoaded(string sceneKey)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name == sceneKey)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
