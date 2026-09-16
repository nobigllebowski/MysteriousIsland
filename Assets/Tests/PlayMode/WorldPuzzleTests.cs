using System;
using System.Collections;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
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
    /// The world half of the puzzles: the real fire sites, rock nodes, proximity remark and
    /// sightline, built by the furnisher and driven through the command layer.
    /// </summary>
    /// <remarks>
    /// The rules are pinned in EditMode. What this covers is that the MonoBehaviours the zone
    /// builds meet them: that a use routed by the interaction system reaches the site, that the
    /// flame child comes on, that a chert rings and remembers it, that a passive fires from a
    /// tick with the rig's position and heading. Self-skips without the scenes, as the loop
    /// tests do.
    /// </remarks>
    [TestFixture]
    public sealed class WorldPuzzleTests
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

        [UnityTest]
        public IEnumerator Ribcage_BuildsTheWrackTheRocksAndThreeFireSites()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var sites = UnityEngine.Object.FindObjectsByType<FireSite>(FindObjectsInactive.Include);
            Assert.That(sites.Length, Is.EqualTo(3), "The lee and two patches of open sand.");

            var rocks = UnityEngine.Object.FindObjectsByType<RockNode>(FindObjectsInactive.Include);
            var chert = 0;
            for (var i = 0; i < rocks.Length; i++)
            {
                if (rocks[i].IsChert)
                {
                    chert++;
                }
            }

            Assert.That(rocks.Length, Is.EqualTo(15), "Twelve basalt and three chert.");
            Assert.That(chert, Is.EqualTo(3));
            Assert.That(FindInteractable<ProximityRemark>(), Is.Not.Null, "The 'that's not basalt' remark.");
            Assert.That(FindInteractable<Sightline>(), Is.Not.Null, "The hull line.");

            var lee = FindSite(ContentIds.FireSiteLee);
            Assert.That(lee, Is.Not.Null);
            Assert.That(lee.transform.Find("Flame"), Is.Not.Null, "A site has a flame child, off.");
            Assert.That(lee.transform.Find("Flame").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator TheFire_LitInTheLeeThroughTheCommandLayer_ShowsItsFlame_AndSurvivesALeaveAndReturn()
        {
            var context = RequireContext();
            yield return StartRun(context);

            context.Commands.Dispatch(new TakeItemCommand(ItemIds.PolyFibre));
            context.Commands.Dispatch(new TakeItemCommand(ItemIds.DriftwoodDry));
            context.Commands.Dispatch(new TakeItemCommand(ItemIds.ChertNodule));

            Assert.That(context.Commands.Dispatch(new UseItemCommand(ItemIds.PolyFibre, ContentIds.FireSiteLee)).Success, Is.True);
            Assert.That(context.Inventory.Has(ItemIds.PolyFibre), Is.False, "Laid, so consumed.");
            Assert.That(context.Commands.Dispatch(new UseItemCommand(ItemIds.DriftwoodDry, ContentIds.FireSiteLee)).Success, Is.True);

            var lee = FindSite(ContentIds.FireSiteLee);
            Assert.That(lee.transform.Find("Kit").gameObject.activeSelf, Is.True, "Something is laid.");

            Assert.That(context.Commands.Dispatch(new UseItemCommand(ItemIds.ChertNodule, ContentIds.FireSiteLee)).Success, Is.True);
            Assert.That(context.Fire.IsLit, Is.True, "The real FireSite reached the service.");
            Assert.That(context.Progress.HasSolved(ContentIds.MechanismFire), Is.True, "And marked the record.");
            Assert.That(lee.transform.Find("Flame").gameObject.activeSelf, Is.True);
            Assert.That(lee.transform.Find("Kit").gameObject.activeSelf, Is.False);
            Assert.That(context.Inventory.Has(ItemIds.ChertNodule), Is.True, "Not consumed by striking.");

            // Leave and come back: the zone is rebuilt, and the fire must still burn.
            context.Commands.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));
            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneFernmaw)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneFernmaw && context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);
            Assert.That(context.Commands.Dispatch(new TravelToZoneCommand(ContentIds.ZoneRibcage)).Success, Is.True);
            yield return WaitFor(() => context.Session.ZoneId == ContentIds.ZoneRibcage && context.States.Current == GameStateId.InGame, LoadTimeoutSeconds);

            var rebuilt = FindSite(ContentIds.FireSiteLee);
            Assert.That(rebuilt, Is.Not.Null);
            Assert.That(rebuilt.transform.Find("Flame").gameObject.activeSelf, Is.True, "A rebuilt zone shows the fire its state implies.");

            var fibre = FindPickup(ItemIds.PolyFibre);
            Assert.That(fibre == null || !fibre.gameObject.activeInHierarchy, Is.True, "What was taken and used up does not grow back.");
        }

        [UnityTest]
        public IEnumerator TheChert_RingsUnderTheMultitool_OffersTake_AndRemembersItRang()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var rocks = UnityEngine.Object.FindObjectsByType<RockNode>(FindObjectsInactive.Include);
            RockNode chert = null;
            RockNode basalt = null;
            for (var i = 0; i < rocks.Length; i++)
            {
                if (rocks[i].IsChert && chert == null)
                {
                    chert = rocks[i];
                }
                else if (!rocks[i].IsChert && basalt == null)
                {
                    basalt = rocks[i];
                }
            }

            Assert.That(chert, Is.Not.Null);
            Assert.That(basalt, Is.Not.Null);

            var lines = new System.Collections.Generic.List<string>();
            using (context.Signals.Subscribe<NarrationSignal>(n => lines.Add(n.LineKey)))
            {
                Assert.That(context.Commands.Dispatch(new UseItemCommand(ItemIds.Multitool, basalt.ContentId)).Success, Is.True);
                Assert.That(lines[lines.Count - 1], Is.EqualTo("narration." + ContentIds.RemarkRockKnock));
                Assert.That(basalt.CanInteract(context.Interactions), Is.True);
                Assert.That(basalt.PromptKey, Is.EqualTo("interact.examine"), "Basalt is never takeable.");

                Assert.That(context.Commands.Dispatch(new UseItemCommand(ItemIds.Multitool, chert.ContentId)).Success, Is.True);
                Assert.That(lines[lines.Count - 1], Is.EqualTo("narration." + ContentIds.RemarkRockRing));
            }

            Assert.That(chert.HasRung, Is.True);
            Assert.That(context.Fire.HasRung(chert.ContentId), Is.True, "Remembered by the service, across rebuilds.");
            Assert.That(chert.CanInteract(context.Interactions), Is.True);
            Assert.That(chert.PromptKey, Is.EqualTo("interact.take"));
        }

        [UnityTest]
        public IEnumerator WalkingUpToTheChert_SaysItIsNotBasalt_Once()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var remark = FindInteractable<ProximityRemark>();
            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(rig, Is.Not.Null);

            var lines = new System.Collections.Generic.List<string>();
            using (context.Signals.Subscribe<NarrationSignal>(n => lines.Add(n.LineKey)))
            {
                var at = remark.transform.position + new Vector3(1f, 0.5f, 0f);
                rig.Teleport(at);
                context.Interactions.Tick(at, 0f);
                context.Interactions.Tick(at, 0f);
                context.Interactions.Tick(at, 0f);
            }

            var count = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i] == "narration." + ContentIds.RemarkRockNotBasalt)
                {
                    count++;
                }
            }

            Assert.That(count, Is.EqualTo(1), "Said once, not once per tick.");
        }

        [UnityTest]
        public IEnumerator StandingAtTheBow_AndLookingDownTheBeach_RecordsTheLine()
        {
            var context = RequireContext();
            yield return StartRun(context);

            var sightline = FindInteractable<Sightline>();
            var rig = UnityEngine.Object.FindAnyObjectByType<PlayerRig>(FindObjectsInactive.Exclude);
            Assert.That(sightline, Is.Not.Null);
            Assert.That(context.Progress.HasInspected(ContentIds.MarkerHullLine), Is.False);

            var at = sightline.StandAt + new Vector3(0f, 0.5f, 0f);
            rig.Teleport(at);

            // Looking the wrong way first: nothing.
            context.Interactions.Tick(at, sightline.AxisYaw + 90f);
            Assert.That(sightline.IsAligned, Is.False);
            Assert.That(context.Progress.HasInspected(ContentIds.MarkerHullLine), Is.False);

            context.Interactions.Tick(at, sightline.AxisYaw + 2f);
            Assert.That(sightline.IsAligned, Is.True, "Within the design's four degrees.");
            Assert.That(context.Progress.HasInspected(ContentIds.MarkerHullLine), Is.True, "SIX HULLS, ONE LINE.");
            Assert.That(sightline.transform.Find("Chalk").GetComponent<LineRenderer>().enabled, Is.True, "The chalk is in.");

            context.Interactions.Tick(at, sightline.AxisYaw + 30f);
            Assert.That(sightline.transform.Find("Chalk").GetComponent<LineRenderer>().enabled, Is.False, "And goes when the angle goes.");
        }

        // --- helpers ---------------------------------------------------------------------------

        private static FireSite FindSite(string siteId)
        {
            var sites = UnityEngine.Object.FindObjectsByType<FireSite>(FindObjectsInactive.Include);
            for (var i = 0; i < sites.Length; i++)
            {
                if (sites[i].ContentId == siteId)
                {
                    return sites[i];
                }
            }

            return null;
        }

        private static ItemPickup FindPickup(string itemId)
        {
            var pickups = UnityEngine.Object.FindObjectsByType<ItemPickup>(FindObjectsInactive.Include);
            for (var i = 0; i < pickups.Length; i++)
            {
                if (pickups[i].ContentId == itemId)
                {
                    return pickups[i];
                }
            }

            return null;
        }

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
    }
}
