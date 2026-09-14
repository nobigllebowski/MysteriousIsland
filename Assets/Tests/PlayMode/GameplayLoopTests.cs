using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Interaction;
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
    /// The Phase 2 loop, end to end, against a real zone with real interactables in it.
    /// </summary>
    /// <remarks>
    /// WHY THIS CANNOT BE AN EDITMODE TEST: it needs a zone scene to actually load, the furnisher to
    /// actually build it, and the interactables it creates to actually exist in a hierarchy. The
    /// rules about what is legal are covered in EditMode; this covers whether the pieces meet.
    /// <para>
    /// Requires the scene assets. Run <c>Vardholm &gt; Setup Project</c> first; these self-skip with
    /// a clear reason otherwise, rather than failing with an inscrutable timeout.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class GameplayLoopTests
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

        /// <summary>The Ribcage builds its marker, its discovery and its gate.</summary>
        [UnityTest]
        public IEnumerator Ribcage_IsFurnishedWithItsInteractables()
        {
            var context = RequireContext();
            yield return StartRun(context);

            Assert.That(context.Interactions.RegisteredCount, Is.GreaterThanOrEqualTo(3),
                "The Ribcage must register at least a marker, a discovery and a gate.");

            Assert.That(FindInteractable<AncientMarker>(), Is.Not.Null, "No marker in the Ribcage.");
            Assert.That(FindInteractable<DiscoveryPickup>(), Is.Not.Null, "No discovery in the Ribcage.");
            Assert.That(FindInteractable<ZoneGate>(), Is.Not.Null, "No gate in the Ribcage.");
        }

        /// <summary>
        /// The whole slice: inspect, collect, unlock, travel.
        /// </summary>
        /// <remarks>
        /// Drives it through commands rather than by simulating touches, because what is under test
        /// is that gameplay state actually advances — not that a finger lands on a button. The touch
        /// path is a view over these same commands.
        /// </remarks>
        [UnityTest]
        public IEnumerator InspectThenCollect_UnlocksFernmaw_AndTravelSucceeds()
        {
            var context = RequireContext();
            yield return StartRun(context);

            Assert.That(context.Progress.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.False,
                "Fernmaw must be locked at the start of a new run.");

            var blocked = context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw));
            Assert.That(blocked.Success, Is.False,
                "Travel to a locked zone must be refused by the command layer, not merely hidden.");

            Assert.That(context.Commands.Dispatch(new InspectCommand(ContentIds.MarkerRibStone)).Success, Is.True);
            Assert.That(context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag)).Success, Is.True);
            Assert.That(context.Progress.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True,
                "Taking the brass tag must open Fernmaw.");

            var travel = context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw));
            Assert.That(travel.Success, Is.True, "Travel was refused after unlocking: " + travel.Code);

            yield return WaitFor(
                () => context.States.Current == GameStateId.InGame &&
                      context.Session.ZoneId == ContentIds.ZoneFernmaw,
                LoadTimeoutSeconds);

            Assert.That(context.Session.ZoneId, Is.EqualTo(ContentIds.ZoneFernmaw), "Never arrived in Fernmaw.");
            Assert.That(context.Zones.ResidentCount, Is.LessThanOrEqualTo(context.Zones.MaxResident),
                "Travel exceeded the resident zone cap.");
            Assert.That(IsSceneLoaded(ContentIds.ZoneRibcage), Is.False,
                "The Ribcage must be unloaded after travelling away from it.");
        }

        /// <summary>A collected discovery stays collected when its zone is rebuilt.</summary>
        /// <remarks>
        /// This is the test for the persistence-by-rebuild design: zones are reconstructed on every
        /// entry, so the pickup exists again as an object and must deactivate itself from progress.
        /// </remarks>
        [UnityTest]
        public IEnumerator CollectedDiscovery_StaysGoneAfterLeavingAndReturning()
        {
            var context = RequireContext();
            yield return StartRun(context);

            context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));
            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneFernmaw, LoadTimeoutSeconds);

            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneRibcage)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneRibcage, LoadTimeoutSeconds);

            var pickup = FindInteractable<DiscoveryPickup>();
            if (pickup != null)
            {
                Assert.That(pickup.gameObject.activeInHierarchy, Is.False,
                    "A rebuilt zone must hide a discovery the player already took.");
            }

            Assert.That(context.Progress.HasCollected(ContentIds.DiscoveryBrassTag), Is.True);
        }

        /// <summary>Save, quit, continue: progression and zone both come back.</summary>
        [UnityTest]
        public IEnumerator Save_Quit_Continue_RestoresProgressionAndZone()
        {
            var context = RequireContext();
            yield return StartRun(context);

            context.Commands.Dispatch(new InspectCommand(ContentIds.MarkerRibStone));
            context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneFernmaw, LoadTimeoutSeconds);

            Assert.That(context.Commands.Dispatch(new SaveGameCommand(TestSlot)).Success, Is.True);
            Assert.That(context.Commands.Dispatch(new QuitToMenuCommand()).Success, Is.True);
            yield return WaitFor(
                () => context.States.Current == GameStateId.MainMenu && context.Zones.ResidentCount == 0,
                UnloadTimeoutSeconds);

            Assert.That(context.Commands.Dispatch(new ResumeSavedRunCommand(TestSlot)).Success, Is.True);
            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);

            Assert.That(context.Session.ZoneId, Is.EqualTo(ContentIds.ZoneFernmaw),
                "Continue must return the player to the zone they saved in.");
            Assert.That(context.Progress.HasInspected(ContentIds.MarkerRibStone), Is.True,
                "A restored run must remember what was read.");
            Assert.That(context.Progress.HasCollected(ContentIds.DiscoveryBrassTag), Is.True,
                "A restored run must remember what was taken.");
            Assert.That(context.Progress.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True,
                "An unlock earned before saving must still hold after loading.");
        }

        /// <summary>A furnished zone produces a player with a body and a camera.</summary>
        [UnityTest]
        public IEnumerator FurnishedZone_HasPlayerWithControllerAndCamera()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(rig, Is.Not.Null, "No PlayerRig after entering a zone.");
            Assert.That(rig.CameraPivot, Is.Not.Null, "The rig has no camera pivot; nothing would render.");
            Assert.That(rig.GetComponent<CharacterController>(), Is.Not.Null,
                "Phase 2 movement needs a CharacterController for ground following.");
            Assert.That(rig.transform.position.y, Is.GreaterThan(-5f),
                "The player spawned below the world.");
        }

        // --- helpers ---------------------------------------------------------------------------

        private IEnumerator StartRun(GameContext context)
        {
            var started = context.Commands.Dispatch(new StartNewGameCommand(TestSlot));
            Assert.That(started.Success, Is.True, "StartNewGameCommand was rejected: " + started.Code);

            yield return WaitFor(() => context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);
            Assert.That(context.States.Current, Is.EqualTo(GameStateId.InGame), "Never reached InGame.");
        }

        private static T FindInteractable<T>() where T : Interactable
        {
            return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
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
