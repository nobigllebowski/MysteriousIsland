using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Hints;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Hints;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.Game.Session;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE + GAME TIER. Hints arrive on a timer, reset on action, and never nag.</summary>
    [TestFixture]
    public sealed class HintTests
    {
        // --- The ladder, as arithmetic ------------------------------------------------------

        [Test]
        public void Ladder_SaysEachRungOnce_InOrder()
        {
            var ladder = new HintLadder(new HintTier("a", 10d), new HintTier("b", 20d));
            ladder.Start();
            HintTier due;

            Assert.That(ladder.TryAdvance(9d, out due), Is.False);
            Assert.That(ladder.TryAdvance(1d, out due), Is.True);
            Assert.That(due.RemarkId, Is.EqualTo("a"));
            Assert.That(ladder.TryAdvance(5d, out due), Is.False, "a is said; b is not due.");
            Assert.That(ladder.TryAdvance(5d, out due), Is.True);
            Assert.That(due.RemarkId, Is.EqualTo("b"));
            Assert.That(ladder.IsExhausted, Is.True);
            Assert.That(ladder.TryAdvance(100d, out due), Is.False);
        }

        [Test]
        public void Ladder_ALongStall_SaysOneRungPerCall()
        {
            var ladder = new HintLadder(new HintTier("a", 10d), new HintTier("b", 20d));
            ladder.Start();
            HintTier due;

            Assert.That(ladder.TryAdvance(1000d, out due), Is.True);
            Assert.That(due.RemarkId, Is.EqualTo("a"));
            Assert.That(ladder.TryAdvance(0d, out due), Is.True);
            Assert.That(due.RemarkId, Is.EqualTo("b"));
        }

        [Test]
        public void Ladder_Reset_RestartsTheTimer_ButDoesNotRepeatARung()
        {
            var ladder = new HintLadder(new HintTier("a", 10d), new HintTier("b", 20d));
            ladder.Start();
            HintTier due;
            ladder.TryAdvance(10d, out due);

            ladder.Reset();

            Assert.That(ladder.Elapsed, Is.Zero);
            Assert.That(ladder.TryAdvance(15d, out due), Is.False, "b is 20 s after the reset, not after the start.");
            Assert.That(ladder.TryAdvance(5d, out due), Is.True);
            Assert.That(due.RemarkId, Is.EqualTo("b"));
        }

        [Test]
        public void Ladder_NotStartedOrStopped_CountsNothing()
        {
            var ladder = new HintLadder(new HintTier("a", 1d));
            HintTier due;

            Assert.That(ladder.TryAdvance(10d, out due), Is.False, "Not started.");
            ladder.Start();
            ladder.Stop();
            Assert.That(ladder.TryAdvance(10d, out due), Is.False, "Stopped.");
        }

        [Test]
        public void Ladder_Forget_IsWhatALoadDoes()
        {
            var ladder = new HintLadder(new HintTier("a", 1d));
            ladder.Start();
            HintTier due;
            ladder.TryAdvance(1d, out due);

            ladder.Forget();

            Assert.That(ladder.IsRunning, Is.False);
            Assert.That(ladder.IsExhausted, Is.False, "A rung said before a load is sayable again.");
        }

        [Test]
        public void Ladder_RefusesRungsOutOfOrder()
        {
            Assert.That(() => new HintLadder(new HintTier("b", 20d), new HintTier("a", 10d)), Throws.ArgumentException);
        }

        [Test]
        public void TheDesignsTimings()
        {
            var hull = HintLadders.HullLine();
            Assert.That(hull.Tiers[0].AfterSeconds, Is.EqualTo(360d), "6:00 (§2:40 FAILURE).");
            Assert.That(hull.Tiers[0].RecordsMarkerId, Is.EqualTo(ContentIds.MarkerHullLine), "The Slate entry writes itself.");

            var spark = HintLadders.FireSpark();
            Assert.That(spark.Tiers[1].IsStaging, Is.True, "Tier 2 shows the test, it does not say it.");
            Assert.That(spark.Tiers[1].Staging, Is.EqualTo(HintStaging.KnockTest));
            Assert.That(spark.Tiers[1].AfterSeconds, Is.EqualTo(240d));
            var tinder = HintLadders.FireTinder();
            Assert.That(tinder.Tiers[1].Staging, Is.EqualTo(HintStaging.RopeSheds));
            Assert.That(tinder.Tiers[0].IsStaging, Is.False);

            var radio = HintLadders.Radio();
            Assert.That(radio.Tiers[0].AfterSeconds, Is.EqualTo(180d), "T+3:00 tier 1.");
            Assert.That(radio.Tiers[1].AfterSeconds, Is.EqualTo(600d), "T+10:00 tier 3.");
            Assert.That(radio.Tiers[1].RecordsMarkerId, Is.Null, "Reading the list records nothing.");
            Assert.That(radio.Tiers[2].AfterSeconds, Is.EqualTo(960d), "T+16:00 tier 4, the safety net.");
            Assert.That(radio.Tiers[2].BeginsSweep, Is.True, "The one hint that does something.");
            Assert.That(radio.Tiers[0].BeginsSweep, Is.False);
            Assert.That(radio.Tiers[1].BeginsSweep, Is.False);
        }

        // --- The director, against play time --------------------------------------------------

        private SignalBus _signals;
        private GameStateMachine _states;
        private SessionService _session;
        private ProgressService _progress;
        private RadioService _radio;
        private CommandDispatcher _dispatcher;
        private HintDirector _hints;
        private FireService _fire;
        private InventoryService _inventory;
        private System.Collections.Generic.List<string> _said;

        [SetUp]
        public void SetUp()
        {
            _signals = new SignalBus();
            _states = new GameStateMachine(null, _signals);
            _session = new SessionService(new IslandClock(0d), null, _signals);
            _progress = new ProgressService(_signals, null);
            var inventory = new InventoryService(_signals, null);
            _radio = new RadioService(_signals, inventory, null);
            _dispatcher = new CommandDispatcher(null);
            _dispatcher.Register<InspectCommand>(new InspectHandler(_states, _progress, _signals));
            _dispatcher.Register<RemarkCommand>(new RemarkHandler(_states, _signals));
            _dispatcher.Register<OpenRadioCommand>(new OpenRadioHandler(_states, _radio, _signals));
            _dispatcher.Register<TuneRadioCommand>(new TuneRadioHandler(_states, _radio, _signals));
            _dispatcher.Register<BeginSweepCommand>(new BeginSweepHandler(_states, _radio));
            _dispatcher.Register<SweepRadioCommand>(new SweepRadioHandler(_states, _radio, _signals));
            _fire = new FireService(_signals, inventory, null);
            _inventory = inventory;
            _dispatcher.Register<TakeItemCommand>(new TakeItemHandler(_states, inventory, _signals));
            _dispatcher.Register<CarryFireKitCommand>(new CarryFireKitHandler(_states, _fire));
            _hints = new HintDirector(_session, _states, _progress, _radio, _fire, _dispatcher, _signals, null, inventory);
            _said = new System.Collections.Generic.List<string>();
            _signals.Subscribe<NarrationSignal>(s => _said.Add(s.LineKey));
            _signals.Subscribe<NarrationSequenceSignal>(s => _said.AddRange(s.LineKeys));
        }

        [TearDown]
        public void TearDown()
        {
            _hints.Dispose();
        }

        private void EnterTheWorld(bool withTheKit = true)
        {
            // Every ladder but the bag's is tested with the kit in hand, as it would be by then.
            if (withTheKit && !_inventory.Has(ItemIds.Multitool))
            {
                _inventory.Take(ItemIds.Multitool);
                _inventory.Take(ItemIds.FieldRecorder);
            }

            _states.TryTransition(GameStateId.MainMenu);
            _states.TryTransition(GameStateId.Loading);
            _session.BeginNewRun(0, string.Empty, ContentIds.ZoneRibcage, 1);
            _states.TryTransition(GameStateId.InGame);
            Assert.That(_states.Current, Is.EqualTo(GameStateId.InGame), "Test setup failed to reach InGame.");
        }

        private void Play(double seconds)
        {
            // Ten ticks a second, as the Ticker does; play time advances between them.
            var ticks = (int)System.Math.Round(seconds * 10d);
            for (var i = 0; i < ticks; i++)
            {
                _session.AddPlaytime(0.1d);
                _signals.Publish(new TickCompletedSignal(_session.SimHours));
            }
        }

        [Test]
        public void HullLine_NeverAligned_IsSaidAtSixMinutes_AndTheSlateEntryWritesItself()
        {
            EnterTheWorld();

            Play(359d);
            Assert.That(_said, Is.Empty);
            Assert.That(_progress.HasInspected(ContentIds.MarkerHullLine), Is.False);

            Play(2d);
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkHullLineUnprompted }),
                "Her unprompted line, not the aligned one.");
            Assert.That(_progress.HasInspected(ContentIds.MarkerHullLine), Is.True, "The Slate entry writes itself.");
        }

        [Test]
        public void HullLine_AlreadyAligned_IsNeverSaidAgain()
        {
            EnterTheWorld();
            _dispatcher.Dispatch(new InspectCommand(ContentIds.MarkerHullLine));
            _said.Clear();

            Play(400d);

            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void HullLine_TimeInAnotherZone_DoesNotCount()
        {
            EnterTheWorld();
            _session.SetZone(ContentIds.ZoneFernmaw);

            Play(400d);

            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void Radio_TimerStartsAtPowerUp_TierOneAtThree_TierThreeAtTen()
        {
            EnterTheWorld();
            Play(100d);
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, 7f, 0f, Reception.Static, null, null, false));

            Play(179d);
            Assert.That(_said, Is.Empty);
            Play(2d);
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkRadioWroteDown }));

            Play(420d);
            Assert.That(_said.Count, Is.EqualTo(3), "Tier 3 is two lines: " + string.Join(" | ", _said));
            Assert.That(_said[1], Is.EqualTo("narration." + ContentIds.RemarkRadioReadsList + ".1"));
            Assert.That(_said[2], Is.EqualTo("narration." + ContentIds.RemarkRadioReadsList + ".2"));
        }

        [Test]
        public void Radio_ACoarseDrag_ResetsTheTimer()
        {
            EnterTheWorld();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, 7f, 0f, Reception.Static, null, null, false));
            Play(170d);

            _signals.Publish(new RadioChangedSignal(RadioChangeKind.Tuned, 7f, 0f, Reception.Static, null, null, true));
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.Tuned, 7.3f, 0f, Reception.Static, null, null, true));

            Play(170d);
            Assert.That(_said, Is.Empty, "180 s from the drag, not from power-up.");
            Play(11d);
            Assert.That(_said.Count, Is.EqualTo(1));
        }

        [Test]
        public void Radio_HearingTheVoice_StopsTheLadder()
        {
            EnterTheWorld();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, 7f, 0f, Reception.Static, null, null, false));
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.Heard, 5.24f, 1f, Reception.Locked, Stations.TheVoice, null, true));

            Play(700d);

            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void Radio_AFalsePositive_ResetsRatherThanStops()
        {
            EnterTheWorld();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, 7f, 0f, Reception.Static, null, null, false));
            Play(100d);
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.Heard, 8.291f, 1f, Reception.Locked, Stations.HullThump, null, true));

            Play(100d);
            Assert.That(_said, Is.Empty);
            Play(81d);
            Assert.That(_said.Count, Is.EqualTo(1));
        }

        [Test]
        public void EnteringTheWorld_ForgetsEverything_SoALoadNeverResumesAnEscalatedHint()
        {
            EnterTheWorld();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, 7f, 0f, Reception.Static, null, null, false));
            Play(170d);

            // Pause, load, come back. The radio still does not work in this run's record, so the
            // ladder does not even start; and the 170 s are gone either way.
            _states.TryTransition(GameStateId.Paused);
            _states.TryTransition(GameStateId.Loading);
            _states.TryTransition(GameStateId.InGame);
            Assert.That(_states.Current, Is.EqualTo(GameStateId.InGame), "Test setup failed to reload.");
            Play(20d);

            Assert.That(_said, Is.Empty);
            Assert.That(_hints.Radio.IsRunning, Is.False);
            Assert.That(_hints.Radio.Elapsed, Is.Zero);
        }

        private void MakeTheSetWork()
        {
            RadioFault cleared;
            _radio.Repair.TryApply(ItemIds.DeadTorch, out cleared);
            _radio.Repair.TryApply(ItemIds.CopperSpring, out cleared);
            _radio.Repair.TryApply(ItemIds.Multitool, out cleared);
            Assert.That(_radio.IsWorking, Is.True, "Test setup: the set should work.");
        }

        [Test]
        public void Radio_TierFour_LeavesTheSetOnAndSweeping_AndTheSweepFindsHer()
        {
            EnterTheWorld();
            MakeTheSetWork();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, RadioService.RestingMhz, 0f, Reception.Static, null, null, false));

            Play(959d);
            Assert.That(_radio.IsSweeping, Is.False);
            Assert.That(_said.Count, Is.EqualTo(3), "Tiers 1 and 3 only, so far.");

            Play(2d);
            Assert.That(_said[3], Is.EqualTo("narration." + ContentIds.RemarkRadioSweep));
            Assert.That(_radio.IsOpen, Is.True, "The spectrogram is live.");
            Assert.That(_radio.IsSweeping, Is.True);

            Play(100d);
            Assert.That(_radio.TransmissionReceived, Is.True, "About ninety seconds later the carrier rises.");
            Assert.That(_radio.IsSweeping, Is.False, "And the needle stays on her.");
            Assert.That(_said, Does.Contain("narration.radio.the_voice"), "Told exactly as a hand-found lock is.");
            Assert.That(_hints.Radio.IsRunning, Is.False, "Hearing her stops the ladder.");
        }

        [Test]
        public void Radio_TierFour_AHandOnTheDial_StopsTheSweep()
        {
            EnterTheWorld();
            MakeTheSetWork();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, RadioService.RestingMhz, 0f, Reception.Static, null, null, false));
            Play(961d);
            Play(10d);
            Assert.That(_radio.IsSweeping, Is.True);

            // A tap: a tune to where the needle already is.
            _dispatcher.Dispatch(new TuneRadioCommand(_radio.Mhz));
            var where = _radio.Mhz;
            Play(60d);

            Assert.That(_radio.IsSweeping, Is.False);
            Assert.That(_radio.Mhz, Is.EqualTo(where).Within(0.0001f), "Nothing moved it afterwards.");
            Assert.That(_radio.TransmissionReceived, Is.False);
        }

        [Test]
        public void Radio_TierFour_IsRefused_OnceSheIsHeard()
        {
            EnterTheWorld();
            MakeTheSetWork();
            _signals.Publish(new RadioChangedSignal(RadioChangeKind.PoweredUp, RadioService.RestingMhz, 0f, Reception.Static, null, null, false));
            Play(700d);
            _radio.Open();
            _dispatcher.Dispatch(new TuneRadioCommand(5.24f));
            Assert.That(_radio.TransmissionReceived, Is.True, "Test setup: she is heard.");

            Play(400d);

            Assert.That(_radio.IsSweeping, Is.False);
            Assert.That(_said, Does.Not.Contain("narration." + ContentIds.RemarkRadioSweep));
        }

        // --- The fire's ladders and counts ---------------------------------------------------------

        [Test]
        public void Fire_NoSpark_TheSpineLineAtTwoThirty_AndSheFindsTheChertAtSix()
        {
            EnterTheWorld();
            _said.Clear();

            // Laying anything is engagement; the clock starts here, not at the run's start.
            Play(100d);
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.DryGrass, true);
            Assert.That(_hints.FireSpark.IsRunning, Is.True);

            Play(149d);
            Assert.That(_said, Is.Empty);
            Play(2d);
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkFireSpine }));

            Play(210d);
            Assert.That(_inventory.Has(ItemIds.ChertNodule), Is.True, "She picks it up herself.");
            Assert.That(_said[_said.Count - 1], Is.EqualTo("narration." + ContentIds.RemarkFireChert));
            Assert.That(_said, Does.Contain("narration." + ItemIds.ChertNodule), "The pickup line, as any take.");
        }

        [Test]
        public void Fire_TierTwo_IsStagedNotSaid()
        {
            EnterTheWorld();
            _said.Clear();
            var staged = new System.Collections.Generic.List<HintStaging>();
            _signals.Subscribe<HintStagingSignal>(s => staged.Add(s.Staging));
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.DryGrass, true);

            Play(241d);

            Assert.That(staged, Is.EqualTo(new[] { HintStaging.KnockTest }), "The knock, at four minutes.");
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkFireSpine }), "And no line for it.");
            Assert.That(_hints.Staged, Is.EqualTo(1));

            // Sparks fly, the tinder ladder stages the rope; the fibre laid takes the staging down.
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            Play(241d);
            Assert.That(staged[staged.Count - 1], Is.EqualTo(HintStaging.RopeSheds));
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);
            Assert.That(staged[staged.Count - 1], Is.EqualTo(HintStaging.None), "Whatever was staged, stop.");
        }

        [Test]
        public void Fire_ASpark_StopsTheSpineLadder_AndStartsTheTinderOne()
        {
            EnterTheWorld();
            _said.Clear();
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.DryGrass, true);
            Play(100d);

            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            Assert.That(_hints.FireSpark.IsRunning, Is.False);
            Assert.That(_hints.FireTinder.IsRunning, Is.True);

            Play(151d);
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkFireGrassTooQuick }));

            Play(210d);
            Assert.That(_inventory.Has(ItemIds.PolyFibre), Is.True, "She tears the rope apart herself.");
        }

        [Test]
        public void Fire_LayingTheFibre_EndsTheTinderLadder()
        {
            EnterTheWorld();
            _said.Clear();
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            Assert.That(_hints.FireTinder.IsRunning, Is.True);

            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);

            Play(400d);
            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void Fire_BlowOuts_AreCounted_ThreeIsTheWind_SevenSheCarriesIt()
        {
            EnterTheWorld();
            _said.Clear();
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.PolyFibre, true);
            _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.DriftwoodDry, true);

            for (var i = 0; i < 3; i++)
            {
                _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            }

            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkFireWind }));

            for (var i = 0; i < 4; i++)
            {
                _fire.Apply(ContentIds.FireSiteOpenA, ItemIds.ChertNodule, true);
            }

            Assert.That(_said[_said.Count - 1], Is.EqualTo("narration." + ContentIds.RemarkFireCarriesKit));
            Assert.That(_fire.Site(ContentIds.FireSiteLee).Tinder, Is.EqualTo(Tinder.Fibre));
            Assert.That(_fire.Site(ContentIds.FireSiteLee).HasWood, Is.True);
            Assert.That(_fire.IsLit, Is.False, "She never does the last step.");
        }

        [Test]
        public void Fire_Lit_StopsEveryFireLadder()
        {
            EnterTheWorld();
            _said.Clear();
            _fire.Apply(ContentIds.FireSiteLee, ItemIds.DryGrass, true);
            _fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true);
            _fire.Apply(ContentIds.FireSiteLee, ItemIds.PolyFibre, true);
            _fire.Apply(ContentIds.FireSiteLee, ItemIds.DriftwoodDry, true);
            _fire.Apply(ContentIds.FireSiteLee, ItemIds.ChertNodule, true);
            Assert.That(_fire.IsLit, Is.True);

            Play(700d);
            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void TheBag_AtForty_SheSaysSo_Once_AndNotOnceItIsTaken()
        {
            EnterTheWorld(withTheKit: false);
            Assert.That(_hints.Bag.IsRunning, Is.True, "Nothing in hand: the bag is the first thing.");

            Play(39d);
            Assert.That(_said, Is.Empty);
            Play(2d);
            Assert.That(_said, Is.EqualTo(new[] { "narration." + ContentIds.RemarkBagFirst }));

            _inventory.Take(ItemIds.Multitool);
            Assert.That(_hints.Bag.IsRunning, Is.False);
        }

        [Test]
        public void TheBag_AlreadyTaken_IsNeverAskedFor()
        {
            _inventory.Take(ItemIds.Multitool);
            EnterTheWorld();

            Assert.That(_hints.Bag.IsRunning, Is.False);
            Play(60d);
            Assert.That(_said, Is.Empty);
        }

        [Test]
        public void AHintOutsideGameplay_IsRefused_NotSaid()
        {
            EnterTheWorld();
            _states.TryTransition(GameStateId.Paused);

            // Ticks do not arrive while paused, but a stray one must still say nothing.
            _session.AddPlaytime(400d);
            _signals.Publish(new TickCompletedSignal(0d));

            Assert.That(_said, Is.Empty);
            Assert.That(_hints.Said, Is.Zero);
        }
    }
}
