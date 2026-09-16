using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// GAME TIER. The dead ends an end-to-end read found, each pinned so it cannot come back.
    /// </summary>
    /// <remarks>
    /// Every case here is a chain that was written, statically valid, and could not happen: the
    /// reel was a discovery and never an item, so the combination it starts could not start; a
    /// solved sluice re-seized on the next zone entry; a new game kept the last run's pockets.
    /// None of them needed the editor to find — they needed someone to trace the hops.
    /// </remarks>
    [TestFixture]
    public sealed class ProgressionChainTests
    {
        private static void EnterGameplay(GameStateMachine states)
        {
            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);
        }

        [Test]
        public void CollectingTheReel_RecordsTheDiscovery_AndPutsTheReelInHand()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var progress = new ProgressService(signals, null);
            var inventory = new InventoryService(signals, null);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<CollectCommand>(new CollectHandler(states, progress, signals, inventory));
            EnterGameplay(states);

            var result = dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryWaterloggedReel));

            Assert.That(result.Success, Is.True, result.Code.ToString());
            Assert.That(progress.HasCollected(ContentIds.DiscoveryWaterloggedReel), Is.True, "The story beat is recorded.");
            Assert.That(inventory.Has(ItemIds.WaterloggedReel), Is.True,
                "And the reel is carried, or the combination chain that starts with it cannot start.");
        }

        [Test]
        public void ADiscoveryThatIsNotAnObject_GrantsNothing()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var progress = new ProgressService(signals, null);
            var inventory = new InventoryService(signals, null);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<CollectCommand>(new CollectHandler(states, progress, signals, inventory));
            EnterGameplay(states);

            dispatcher.Dispatch(new CollectCommand(ContentIds.DiscoveryBrassTag));

            Assert.That(inventory.Inventory.Count, Is.Zero, "The tag is a story beat, not a tool.");
        }

        [Test]
        public void DiscoveryItems_NamesOnlyRealIds()
        {
            string itemId;
            Assert.That(DiscoveryItems.TryItemFor(ContentIds.DiscoveryWaterloggedReel, out itemId), Is.True);
            Assert.That(itemId, Is.EqualTo(ItemIds.WaterloggedReel));
            Assert.That(DiscoveryItems.TryItemFor("discovery.nothing", out itemId), Is.False);
            Assert.That(itemId, Is.Null);
        }

        [Test]
        public void ASolvedMechanism_StaysSolvedAcrossASave()
        {
            var source = new ProgressService(new SignalBus(), null);
            Assert.That(source.Solve(ContentIds.MechanismSluice), Is.True);
            Assert.That(source.Solve(ContentIds.MechanismSluice), Is.False, "Once.");

            var doc = new SaveDocument();
            source.Capture(doc);

            var target = new ProgressService(new SignalBus(), null);
            target.Restore(doc);

            Assert.That(target.HasSolved(ContentIds.MechanismSluice), Is.True, "A machine that was fixed stays fixed.");
            Assert.That(target.HasSolved(ContentIds.MechanismTapeDeck), Is.False);
        }

        [Test]
        public void AProgressSaveFromBeforeMechanisms_RestoresWithNothingSolved()
        {
            var target = new ProgressService(new SignalBus(), null);
            target.Solve(ContentIds.MechanismSluice);

            // A payload with the three original lists and no solved line: an older run.
            var doc = new SaveDocument();
            doc.PutSection(SaveSections.Progress, "inspected=\ncollected=\nunlocked=ZoneRibcage");
            target.Restore(doc);

            Assert.That(target.HasSolved(ContentIds.MechanismSluice), Is.False);
        }

        [Test]
        public void SolvingAMechanism_IsAnnounced()
        {
            var signals = new SignalBus();
            var progress = new ProgressService(signals, null);
            ProgressChangeKind? kind = null;
            string id = null;
            signals.Subscribe<ProgressChangedSignal>(s =>
            {
                kind = s.Kind;
                id = s.ContentId;
            });

            progress.Solve(ContentIds.MechanismTapeDeck);

            Assert.That(kind, Is.EqualTo(ProgressChangeKind.Solved));
            Assert.That(id, Is.EqualTo(ContentIds.MechanismTapeDeck));
        }
    }
}
