using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Scenes;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// CORE TIER (engine-free logic, exercised through the Game-side service).
    /// The Phase 2 progression rules: collect-once, unlock, objective advance, and save round trip.
    /// </summary>
    /// <remarks>
    /// These are the tests that would catch the failures that actually matter in this slice: a
    /// discovery that can be taken twice, a zone that unlocks when it should not, an objective that
    /// stops advancing, and progress that does not survive a save.
    /// </remarks>
    [TestFixture]
    public sealed class ProgressionTests
    {
        private ProgressService NewService()
        {
            return new ProgressService(new SignalBus(), null);
        }

        // --- collect-once ------------------------------------------------------------------

        [Test]
        public void Collect_SameDiscoveryTwice_IsRecordedOnce()
        {
            var service = NewService();

            Assert.That(service.Collect(ContentIds.DiscoveryBrassTag), Is.True, "First collect must succeed.");
            Assert.That(service.Collect(ContentIds.DiscoveryBrassTag), Is.False,
                "A second collect of the same id must report that nothing changed.");
            Assert.That(service.Progress.CollectedCount, Is.EqualTo(1),
                "Collecting twice must not produce two entries.");
        }

        [Test]
        public void Inspect_SameMarkerTwice_IsRecordedOnce()
        {
            var service = NewService();

            Assert.That(service.Inspect(ContentIds.MarkerRibStone), Is.True);
            Assert.That(service.Inspect(ContentIds.MarkerRibStone), Is.False);
        }

        [Test]
        public void Collect_EmptyId_ChangesNothing()
        {
            var service = NewService();

            Assert.That(service.Collect(string.Empty), Is.False);
            Assert.That(service.Progress.CollectedCount, Is.Zero);
        }

        // --- unlock rules ------------------------------------------------------------------

        [Test]
        public void NewRun_RibcageUnlocked_FernmawLocked()
        {
            var service = NewService();

            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneRibcage), Is.True,
                "The opening zone must always be reachable.");
            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.False,
                "Fernmaw must start locked, or the discovery that opens it means nothing.");
        }

        [Test]
        public void CollectingTheBrassTag_UnlocksFernmaw()
        {
            var service = NewService();

            service.Collect(ContentIds.DiscoveryBrassTag);

            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True,
                "Taking the tag IS the unlock; there is no separate step.");
        }

        [Test]
        public void InspectingTheMarker_DoesNotUnlockFernmaw()
        {
            var service = NewService();

            service.Inspect(ContentIds.MarkerRibStone);

            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.False,
                "Reading a marker must not open a zone; only the discovery does.");
        }

        // --- objective progression ---------------------------------------------------------

        [Test]
        public void Objective_AdvancesThroughTheRibcageChain()
        {
            var service = NewService();

            var atStart = service.ObjectiveKey;
            Assert.That(atStart, Is.EqualTo("objective.explore_ribcage"));

            service.Inspect(ContentIds.MarkerRibStone);
            Assert.That(service.ObjectiveKey, Is.EqualTo("objective.find_the_tag"),
                "Reading the stone is what tells the player a tag exists.");

            service.Collect(ContentIds.DiscoveryBrassTag);
            Assert.That(service.ObjectiveKey, Is.EqualTo("objective.enter_fernmaw"));
        }

        [Test]
        public void Objective_FollowsThePlayerIntoFernmaw()
        {
            var service = NewService();
            service.Inspect(ContentIds.MarkerRibStone);
            service.Collect(ContentIds.DiscoveryBrassTag);

            service.SetZone(ContentIds.ZoneFernmaw);

            Assert.That(service.ObjectiveKey, Is.EqualTo("objective.explore_fernmaw"),
                "Arriving somewhere new must change what the player is told to do.");
        }

        [Test]
        public void Objective_IsDerivedNotStored_SoOutOfOrderPlayStillReadsTrue()
        {
            // A player who finds the tag before reading the stone has skipped a beat. The objective
            // must still describe something true rather than an instruction they already satisfied.
            var service = NewService();

            service.Collect(ContentIds.DiscoveryBrassTag);

            Assert.That(service.ObjectiveKey, Is.EqualTo("objective.enter_fernmaw"),
                "Objectives are computed from state, so any order of play yields a valid line.");
        }

        // --- save round trip ----------------------------------------------------------------

        [Test]
        public void Progress_SurvivesACaptureAndRestore()
        {
            var source = NewService();
            source.Inspect(ContentIds.MarkerRibStone);
            source.Collect(ContentIds.DiscoveryBrassTag);

            var doc = new SaveDocument();
            source.Capture(doc);

            var target = NewService();
            target.Restore(doc);

            Assert.That(target.HasInspected(ContentIds.MarkerRibStone), Is.True);
            Assert.That(target.HasCollected(ContentIds.DiscoveryBrassTag), Is.True);
            Assert.That(target.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True,
                "An unlock earned before saving must still be in force after loading.");
            Assert.That(target.ObjectiveKey, Is.EqualTo("objective.enter_fernmaw"),
                "A restored run must show the objective its state implies, with no extra action.");
        }

        [Test]
        public void Restore_FromASaveWithNoProgressSection_StartsClean()
        {
            // A save written before Phase 2 existed. Loading it is an older run, not corruption.
            var target = NewService();
            target.Collect(ContentIds.DiscoveryBrassTag);

            target.Restore(new SaveDocument());

            Assert.That(target.Progress.CollectedCount, Is.Zero);
            Assert.That(target.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.False);
        }

        [Test]
        public void ResetForNewRun_ClearsEverythingButTheOpeningZone()
        {
            var service = NewService();
            service.Inspect(ContentIds.MarkerRibStone);
            service.Collect(ContentIds.DiscoveryBrassTag);

            service.ResetForNewRun();

            Assert.That(service.Progress.CollectedCount, Is.Zero);
            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.False);
            Assert.That(service.IsZoneUnlocked(ContentIds.ZoneRibcage), Is.True,
                "Wiping progress must never strand the player out of the zone they start in.");
        }

        [Test]
        public void RestoreFrom_ASaveThatLockedTheOpeningZone_ReopensIt()
        {
            // Defensive: no legitimate progression locks the Ribcage, so a save claiming otherwise is
            // damaged. Honouring it would strand the player somewhere they may not be.
            var progress = new WorldProgress();
            progress.RestoreFrom(null, null, new[] { ContentIds.ZoneFernmaw });

            Assert.That(progress.IsZoneUnlocked(ContentIds.ZoneRibcage), Is.True);
        }

        // --- id integrity ---------------------------------------------------------------------

        [Test]
        public void ZoneContentIds_MatchSceneKeys_Exactly()
        {
            // These are compared as strings across two files. A drift would let progression "unlock"
            // a zone whose scene cannot be loaded, which fails at travel time with no obvious cause.
            Assert.That(ContentIds.ZoneRibcage, Is.EqualTo(SceneKeys.ZoneRibcage));
            Assert.That(ContentIds.ZoneFernmaw, Is.EqualTo(SceneKeys.ZoneFernmaw));
        }
    }
}
