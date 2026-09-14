using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
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
    /// The camera exists, is enabled, is the only one, and is the one <c>Camera.main</c> returns.
    /// </summary>
    /// <remarks>
    /// WHY THIS SUITE IS SEPARATE from <c>GameplayLoopTests</c>: that suite asserts the rig has *a*
    /// camera pivot, which is what let the real failure through — a pivot is not a camera, and the
    /// furnisher used to return early the moment one existed. Everything here is about the camera as
    /// a rendering fact rather than as a hierarchy fact: enabled, active, tagged, and alone.
    /// <para>
    /// Requires the scene assets. Run <c>Vardholm &gt; Setup Project</c> first; these self-skip with
    /// a clear reason otherwise.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class CameraLifecycleTests
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
        /// New Game into the Ribcage produces exactly one enabled camera, and it is <c>Camera.main</c>.
        /// </summary>
        [UnityTest]
        public IEnumerator NewGame_ProducesExactlyOneEnabledGameplayCamera()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(rig, Is.Not.Null, "No PlayerRig after entering the Ribcage.");
            Assert.That(rig.GetComponent<CharacterController>(), Is.Not.Null,
                "The rig has no CharacterController, so it cannot move.");

            Assert.That(rig.CameraPivot, Is.Not.Null, "The rig has no camera pivot.");

            var camera = rig.CameraPivot.GetComponentInChildren<Camera>(true);
            Assert.That(camera, Is.Not.Null,
                "The pivot carries no Camera. A pivot is not a camera -- this is the exact gap that "
                + "produced 'Display 1 - No cameras rendering'.");
            Assert.That(camera.enabled, Is.True, "The gameplay camera is disabled.");
            Assert.That(camera.gameObject.activeInHierarchy, Is.True,
                "The gameplay camera's object is inactive.");

            Assert.That(Camera.main, Is.Not.Null,
                "Camera.main is null: the furnished camera is not tagged MainCamera.");
            Assert.That(Camera.main, Is.EqualTo(camera),
                "Camera.main resolves to some other camera than the player's.");

            Assert.That(EnabledCameras(), Is.EqualTo(1),
                "Exactly one camera may render. More than one means two views are stacked.");
        }

        /// <summary>The camera survives a zone change, and travel never leaves two rendering.</summary>
        /// <remarks>
        /// Two zones are briefly resident during a transition (ADR-0004's cap of two), so this is the
        /// one moment the project can legitimately hold two cameras. Only one may be enabled.
        /// </remarks>
        [UnityTest]
        public IEnumerator AfterTravel_StillExactlyOneEnabledCamera()
        {
            var context = RequireContext();
            yield return StartRun(context);

            Assert.That(context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag)).Success,
                Is.True, "Could not unlock Fernmaw, so travel cannot be exercised.");
            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw)).Success,
                Is.True);

            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneFernmaw, LoadTimeoutSeconds);
            Assert.That(context.Session.ZoneId, Is.EqualTo(ContentIds.ZoneFernmaw), "Never arrived in Fernmaw.");

            Assert.That(EnabledCameras(), Is.EqualTo(1),
                "Travel left more than one camera enabled; the outgoing zone is still rendering.");
            Assert.That(Camera.main, Is.Not.Null, "Camera.main is null after travelling.");
            Assert.That(EnabledAudioListeners(), Is.EqualTo(1),
                "Exactly one AudioListener may be enabled, or Unity picks one arbitrarily.");
        }

        /// <summary>A camera the furnisher finds already disabled is put back into service.</summary>
        /// <remarks>
        /// The furnisher used to adopt any camera it found without checking whether it was usable, so
        /// a disabled one silently became the zone's view. Re-entering the zone must repair that
        /// rather than inherit it.
        /// </remarks>
        [UnityTest]
        public IEnumerator ADisabledCamera_IsRepairedOnReentry()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "Test needs a camera to disable.");
            camera.enabled = false;

            Assert.That(context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag)).Success, Is.True);
            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneFernmaw, LoadTimeoutSeconds);

            Assert.That(EnabledCameras(), Is.EqualTo(1),
                "The rebuilt zone did not produce a working camera after the previous one was disabled.");
            Assert.That(Camera.main, Is.Not.Null);
        }

        /// <summary>The menu is never left with nothing clearing the screen.</summary>
        /// <remarks>
        /// UI Toolkit renders the menu in overlay with no camera, which is why this looked fine and
        /// was not: with nothing clearing the colour buffer, each frame composited onto the last and
        /// the menu smeared into itself. A camera must be enabled here even though no world is drawn.
        /// </remarks>
        [UnityTest]
        public IEnumerator AtTheMenu_SomethingIsStillClearingTheScreen()
        {
            var context = RequireContext();
            yield return WaitFor(() => context.States.Current == GameStateId.MainMenu, LoadTimeoutSeconds);

            Assert.That(context.States.Current, Is.EqualTo(GameStateId.MainMenu), "Never reached the menu.");
            Assert.That(EnabledCameras(), Is.EqualTo(1),
                "The menu has no enabled camera, so nothing clears the frame buffer between frames.");
            Assert.That(EnabledAudioListeners(), Is.EqualTo(1),
                "The menu has no AudioListener.");
        }

        /// <summary>Entering a zone hands the screen to the zone camera, not to both.</summary>
        [UnityTest]
        public IEnumerator EnteringAZone_RetiresTheFallbackCamera()
        {
            var context = RequireContext();
            yield return StartRun(context);

            Assert.That(EnabledCameras(), Is.EqualTo(1),
                "The fallback camera is still enabled alongside the zone camera.");

            var main = Camera.main;
            Assert.That(main, Is.Not.Null);
            Assert.That(main.cullingMask, Is.Not.EqualTo(0),
                "Camera.main resolved to a camera that renders nothing -- the fallback camera must "
                + "never be tagged MainCamera.");
        }

        // --- helpers ---------------------------------------------------------------------------

        private static int EnabledCameras()
        {
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            var count = 0;
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i].enabled && cameras[i].gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static int EnabledAudioListeners()
        {
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
            var count = 0;
            for (var i = 0; i < listeners.Length; i++)
            {
                if (listeners[i].enabled && listeners[i].gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private IEnumerator StartRun(GameContext context)
        {
            var started = context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            Assert.That(started.Success, Is.True, "StartNewGameCommand was rejected: " + started.Code);

            yield return WaitFor(
                () => context.States.Current == GameStateId.InGame &&
                      context.Session.ZoneId == ContentIds.ZoneRibcage,
                LoadTimeoutSeconds);

            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Never reached InGame.");

            // The furnisher runs on the scene-loaded callback, which lands in the same frame the
            // state reaches InGame. One more frame guarantees it has finished building.
            yield return null;
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
    }
}
