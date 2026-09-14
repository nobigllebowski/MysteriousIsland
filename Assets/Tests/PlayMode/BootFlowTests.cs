using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ForgottenIsle.Tests.PlayMode
{
    /// <summary>
    /// The four boot-path behaviours that only exist once the engine is actually running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY this suite is deliberately tiny: every test here costs real seconds of scene streaming and
    /// can only run on a device or in the Editor's Play mode, so the suite is a smoke test for the
    /// seams between <c>ForgottenIsle.Game</c> and the engine — scene residency, the state machine's
    /// response to a load completing, and teardown. Everything that can be decided without a frame
    /// loop (transition legality, validate/execute outcomes, save round-trips, string table lookups)
    /// belongs in EditMode tests, which run in milliseconds and can be run on every keystroke.
    /// </para>
    /// <para>
    /// WHY there is no per-test composition: <see cref="AppBootstrap"/> boots from a
    /// <c>RuntimeInitializeOnLoadMethod</c>, so by the time the first test body runs the real graph
    /// already exists and is <c>DontDestroyOnLoad</c>. Building a second graph here would test a
    /// fixture rather than the game — two <c>SceneLoader</c>s would then race over the same additive
    /// scene list. Each test instead drives the live graph through public commands and returns it to
    /// the menu in teardown.
    /// </para>
    /// <para>
    /// WHY the zone tests self-ignore: the four <c>.unity</c> files are authored by hand in the Editor
    /// (see <c>Assets/Scenes/README.md</c>) and are not in the repository until someone does that. A
    /// test that fails because an asset has not been created yet trains people to ignore red, so the
    /// scene-dependent tests report Ignored — which is visibly different from both green and red —
    /// until the build's scene list contains the zones.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class BootFlowTests
    {
        /// <summary>Budget for boot to leave <see cref="GameStateId.Boot"/>, per the Phase 1 plan.</summary>
        private const float BootTimeoutSeconds = 2f;

        /// <summary>
        /// Budget for one zone load to settle. Deliberately far above
        /// <c>SceneLoader.WatchdogSeconds</c> (20s) so that a watchdog trip is reported as the loader's
        /// own <c>ResultCode.LoadTimedOut</c> — a diagnosable failure — rather than as this test
        /// timing out first and saying nothing about why.
        /// </summary>
        private const float LoadTimeoutSeconds = 25f;

        /// <summary>Budget for teardown paths, which unload but never load.</summary>
        private const float UnloadTimeoutSeconds = 15f;

        /// <summary>
        /// Manual slot the tests bind runs to.
        /// </summary>
        /// <remarks>
        /// Binding a slot writes nothing: only <c>SaveGameCommand</c> touches the disk, and no test in
        /// this file dispatches one. The last manual slot is used anyway, on the assumption that a
        /// developer running these on their own device has their real run in slot 0.
        /// </remarks>
        private const int TestSlot = SaveSlotService.SlotCount - 1;

        /// <summary>Frames to let the engine settle destroyed scene objects before counting them.</summary>
        private const int SettleFrames = 3;

        /// <summary>Brings every test to the same starting line: booted, in the menu, no zones resident.</summary>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return WaitForBoot();
            yield return ReturnToMenu();
        }

        /// <summary>
        /// Leaves the live graph in the menu regardless of how the test ended.
        /// </summary>
        /// <remarks>
        /// The graph survives between tests, so a test that ended mid-run would otherwise hand the
        /// next test a populated <c>ZoneRegistry</c> and a state machine in <c>InGame</c> — and
        /// <c>StartNewGameHandler.Validate</c> refuses a second run while zones are resident, so the
        /// failure would land on an unrelated test.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return ReturnToMenu();
        }

        /// <summary>
        /// Boot must reach <see cref="GameStateId.MainMenu"/> within two seconds of the process
        /// starting.
        /// </summary>
        /// <remarks>
        /// WHY this cannot be an EditMode test: the thing under test is
        /// <c>[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]</c>. That hook does not fire in EditMode
        /// — there is no player loop and no scene load — so an EditMode test could only call
        /// <c>AppCompositionRoot.Build</c> by hand, which is the one part of boot that is already
        /// known to work. The risk being covered is that the hook never runs, runs twice, or runs
        /// before something it depends on: all three are properties of the engine's startup sequence.
        /// </remarks>
        [UnityTest]
        public IEnumerator Bootstrap_ReachesMainMenu_WithinTwoSeconds()
        {
            // SetUp has already waited for boot, so this normally passes on frame one. The wait is
            // repeated rather than assumed because a regression that makes boot slow must fail HERE,
            // with this test's name on it, instead of inside SetUp where it reads as a harness fault.
            var bootstrap = FindBootstrap();
            Assert.IsNotNull(bootstrap, "No AppBootstrap in the scene: the RuntimeInitializeOnLoadMethod boot hook did not run.");
            Assert.IsNotNull(bootstrap.Context, "AppBootstrap exists but its GameContext is null: composition did not complete.");

            var states = bootstrap.Context.States;
            yield return WaitFor(() => states.Current != GameStateId.Boot, BootTimeoutSeconds);

            Assert.AreEqual(
                GameStateId.MainMenu,
                states.Current,
                "Boot did not settle in MainMenu within " + BootTimeoutSeconds + "s. Current state: " + states.Current + ".");

            Assert.AreEqual(0, bootstrap.Context.Zones.ResidentCount, "Booting into the menu must leave no zone resident.");
            Assert.IsFalse(bootstrap.Context.Session.HasRun, "Booting into the menu must not start a run.");
        }

        /// <summary>
        /// Starting a new game makes the opening zone resident and takes the menu scene out of memory.
        /// </summary>
        /// <remarks>
        /// WHY this cannot be an EditMode test: the assertion is about
        /// <c>SceneManager</c>'s loaded scene list after an asynchronous additive load has completed.
        /// EditMode has no frame loop to advance <c>AsyncOperation.progress</c>, so
        /// <c>SceneLoader.LoadRoutine</c> never runs past its first yield and the completion callback
        /// that drives <c>Loading -&gt; InGame</c> never fires. Stubbing the loader to make this run in
        /// EditMode would delete the only part of the flow that has ever been wrong.
        /// </remarks>
        [UnityTest]
        public IEnumerator NewGame_LoadsRibcage_AndUnloadsMenuScene()
        {
            RequireZoneScenesInBuild();

            var context = RequireContext();
            var result = context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            Assert.IsTrue(result.Success, "StartNewGameCommand was refused with " + result.Code + ".");

            yield return WaitFor(() => context.States.Current != GameStateId.Loading, LoadTimeoutSeconds);

            Assert.AreEqual(
                GameStateId.InGame,
                context.States.Current,
                "New game did not reach InGame; the load reported failure and the machine went to " + context.States.Current + ".");

            Assert.IsTrue(
                context.Zones.IsResident(SceneKeys.ZoneRibcage),
                "ZoneRibcage is not resident after a new game. Resident zones: " + string.Join(", ", context.Zones.Resident) + ".");

            Assert.AreEqual(SceneKeys.ZoneRibcage, context.Session.ZoneId, "The session's zone was not updated to the loaded zone.");
            Assert.IsTrue(IsSceneLoaded(SceneKeys.ZoneRibcage), "ZoneRibcage is tracked as resident but is not in SceneManager's loaded list.");

            // Assets/Scenes/README.md: MainMenu exists only to be "what gets unloaded when a run
            // starts". Leaving it loaded costs a scene's worth of memory for the whole run and puts a
            // second set of roots under the player's feet.
            Assert.IsFalse(IsSceneLoaded(SceneKeys.MainMenu), "The MainMenu scene is still loaded after entering a run.");
        }

        /// <summary>
        /// Travelling between zones never puts a third zone in memory, not even for one frame.
        /// </summary>
        /// <remarks>
        /// WHY this cannot be an EditMode test: the ADR-0004 cap is a statement about peak memory
        /// during an overlap that exists only between two engine frames — the old zone is evicted and
        /// the new one streamed in by coroutines. The interesting failure is transient (three zones
        /// resident for a few frames on a device with 2 GB of RAM), so the test has to sample the
        /// scene list every frame while the transition is in flight, which requires a frame loop.
        /// </remarks>
        [UnityTest]
        public IEnumerator ZoneTransition_NeverExceedsTwoResidentZones()
        {
            RequireZoneScenesInBuild();

            var context = RequireContext();
            Assert.IsTrue(context.Commands.Dispatch(new StartNewGameCommand(TestSlot)).Success, "Could not start the run this test travels from.");
            yield return WaitFor(() => context.States.Current != GameStateId.Loading, LoadTimeoutSeconds);
            Assert.AreEqual(GameStateId.InGame, context.States.Current, "The run did not start, so the transition under test never happened.");

            var cap = context.Zones.MaxResident;
            var peakRegistry = context.Zones.ResidentCount;
            var peakScenes = LoadedZoneSceneCount();

            // Out and back. The return trip matters: it is the one that evicts a zone that was active a
            // moment ago, which is where an eviction order bug shows up.
            var hops = new[] { SceneKeys.ZoneFernmaw, SceneKeys.ZoneRibcage };
            for (var i = 0; i < hops.Length; i++)
            {
                var travel = context.Commands.Dispatch(new TravelToZoneCommand(hops[i]));
                Assert.IsTrue(travel.Success, "Travel to " + hops[i] + " was refused with " + travel.Code + ".");

                var deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
                while (context.States.Current == GameStateId.Loading && Time.realtimeSinceStartup < deadline)
                {
                    peakRegistry = Math.Max(peakRegistry, context.Zones.ResidentCount);
                    peakScenes = Math.Max(peakScenes, LoadedZoneSceneCount());
                    yield return null;
                }

                peakRegistry = Math.Max(peakRegistry, context.Zones.ResidentCount);
                peakScenes = Math.Max(peakScenes, LoadedZoneSceneCount());

                Assert.AreEqual(GameStateId.InGame, context.States.Current, "Travel to " + hops[i] + " did not settle in InGame.");
                Assert.AreEqual(hops[i], context.Session.ZoneId, "Travel to " + hops[i] + " completed but the session still reports " + context.Session.ZoneId + ".");
            }

            Assert.LessOrEqual(peakRegistry, cap, "ZoneRegistry held " + peakRegistry + " zones at peak; ADR-0004 caps residency at " + cap + ".");
            Assert.LessOrEqual(peakScenes, cap, "SceneManager held " + peakScenes + " zone scenes at peak; the registry's count and the engine's disagree.");
        }

        /// <summary>
        /// Quitting to the menu unloads every zone scene and leaves no objects behind.
        /// </summary>
        /// <remarks>
        /// WHY this cannot be an EditMode test: "no leaked GameObjects" is only a meaningful question
        /// when there are GameObjects, which means a real additive load and a real
        /// <c>SceneManager.UnloadSceneAsync</c> — an engine operation that completes over several
        /// frames and is the only thing that destroys a scene's contents. An EditMode test can prove
        /// <c>QuitToMenuHandler</c> calls <c>ZoneRegistry.UnloadAll</c>; only a PlayMode test can prove
        /// the objects are actually gone afterwards, which is the failure that matters because it
        /// accumulates across menu/run cycles until the process dies.
        /// </remarks>
        [UnityTest]
        public IEnumerator QuitToMenu_UnloadsSessionScenes_WithoutLeakingObjects()
        {
            RequireZoneScenesInBuild();

            var context = RequireContext();
            var baselineObjects = CountLiveTransforms();

            Assert.IsTrue(context.Commands.Dispatch(new StartNewGameCommand(TestSlot)).Success, "Could not start the run this test quits out of.");
            yield return WaitFor(() => context.States.Current != GameStateId.Loading, LoadTimeoutSeconds);
            Assert.AreEqual(GameStateId.InGame, context.States.Current, "The run did not start, so quitting proves nothing.");
            Assert.Greater(LoadedZoneSceneCount(), 0, "The run started with no zone scene loaded; there is nothing for the quit to unload.");

            var quit = context.Commands.Dispatch(new QuitToMenuCommand());
            Assert.IsTrue(quit.Success, "QuitToMenuCommand was refused with " + quit.Code + ".");

            yield return WaitFor(() => context.Zones.ResidentCount == 0 && LoadedZoneSceneCount() == 0, UnloadTimeoutSeconds);

            // Unload is asynchronous and destroys objects at the end of the frame it completes in.
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            Assert.AreEqual(GameStateId.MainMenu, context.States.Current, "Quit did not return the machine to MainMenu.");
            Assert.IsFalse(context.Session.HasRun, "Quit returned to the menu but the session still reports a live run.");
            Assert.AreEqual(0, context.Zones.ResidentCount, "Zones still resident after quit: " + string.Join(", ", context.Zones.Resident) + ".");
            Assert.AreEqual(0, LoadedZoneSceneCount(), "Zone scenes still loaded after quit: " + DescribeLoadedScenes() + ".");
            Assert.IsFalse(context.SceneLoader.IsLoading, "The scene loader is still busy after the quit settled.");

            // Compared with <= rather than == on purpose: the test runner allocates its own objects
            // while a test runs, so demanding an exact match would make this flaky in a way that
            // teaches people to rerun it. Anything the run created and failed to destroy still shows
            // up, because a leak only ever pushes this number the other way.
            var afterQuit = CountLiveTransforms();
            Assert.LessOrEqual(
                afterQuit,
                baselineObjects,
                "Quitting leaked objects: " + baselineObjects + " live transforms before the run, " + afterQuit + " after quitting.");
        }

        /// <summary>Polls for the boot hook to produce a usable graph.</summary>
        private static IEnumerator WaitForBoot()
        {
            yield return WaitFor(
                () =>
                {
                    var bootstrap = FindBootstrap();
                    return bootstrap != null && bootstrap.Context != null && bootstrap.Context.States.Current != GameStateId.Boot;
                },
                BootTimeoutSeconds);
        }

        /// <summary>
        /// Drives the live graph back to the menu with no zones resident, whatever state it is in.
        /// </summary>
        /// <remarks>
        /// Uses the real command rather than poking the state machine, so that a test cannot pass by
        /// leaving the game in a state only the test harness knows how to escape.
        /// </remarks>
        private static IEnumerator ReturnToMenu()
        {
            var bootstrap = FindBootstrap();
            if (bootstrap == null || bootstrap.Context == null)
            {
                yield break;
            }

            var context = bootstrap.Context;

            // A load in flight must settle first: the loader refuses overlapping operations, so an
            // unload issued now would come back AlreadyLoading and the zones would stay resident.
            yield return WaitFor(() => !context.SceneLoader.IsLoading, LoadTimeoutSeconds);

            if (context.States.Current == GameStateId.InGame || context.States.Current == GameStateId.Paused || context.States.Current == GameStateId.LoadFailed)
            {
                context.Commands.Dispatch(new QuitToMenuCommand());
            }

            yield return WaitFor(() => context.Zones.ResidentCount == 0 && !context.SceneLoader.IsLoading, UnloadTimeoutSeconds);
        }

        /// <summary>Yields until <paramref name="condition"/> holds or the budget runs out. Never asserts.</summary>
        /// <remarks>
        /// Returning quietly on timeout is deliberate: the caller owns the message, and a helper that
        /// failed on its own would report every timeout as the same anonymous "wait expired".
        /// </remarks>
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

        /// <summary>Returns the live graph, failing the calling test rather than throwing a null reference.</summary>
        private static GameContext RequireContext()
        {
            var bootstrap = FindBootstrap();
            Assert.IsNotNull(bootstrap, "No AppBootstrap in the scene: the boot hook did not run.");
            Assert.IsNotNull(bootstrap.Context, "AppBootstrap exists but composition produced no GameContext.");
            return bootstrap.Context;
        }

        /// <summary>
        /// Ignores the calling test when the zone scenes are not in the build.
        /// </summary>
        /// <remarks>
        /// <c>SceneManager.LoadSceneAsync</c> answers a scene missing from the build with
        /// <c>ResultCode.SceneNotFound</c>, which would fail these tests for a reason that has nothing
        /// to do with the code they cover. See the class remarks.
        /// </remarks>
        private static void RequireZoneScenesInBuild()
        {
            var zones = SceneKeys.Zones;
            for (var i = 0; i < zones.Count; i++)
            {
                if (!IsSceneInBuild(zones[i]))
                {
                    Assert.Ignore("Scene '" + zones[i] + "' is not in the build's scene list. Create the scenes per Assets/Scenes/README.md, then re-run.");
                }
            }
        }

        private static bool IsSceneInBuild(string sceneKey)
        {
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), sceneKey, StringComparison.Ordinal))
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
                if (scene.isLoaded && string.Equals(scene.name, sceneKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Counts loaded scenes the engine has open that <see cref="SceneKeys"/> calls zones.</summary>
        private static int LoadedZoneSceneCount()
        {
            var count = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && SceneKeys.IsZone(scene.name))
                {
                    count++;
                }
            }

            return count;
        }

        private static string DescribeLoadedScenes()
        {
            var names = new string[SceneManager.sceneCount];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = SceneManager.GetSceneAt(i).name;
            }

            return string.Join(", ", names);
        }

        /// <summary>
        /// Counts every live transform, including inactive ones and objects marked
        /// <c>DontDestroyOnLoad</c>.
        /// </summary>
        /// <remarks>
        /// Transforms are counted rather than GameObjects because every GameObject has exactly one and
        /// the typed query is the cheapest way to reach objects the scene list cannot enumerate — a
        /// leaked object that was reparented out of its scene is precisely the case a per-scene root
        /// count would miss.
        /// </remarks>
        private static int CountLiveTransforms()
        {
            return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }
    }
}
