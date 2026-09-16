using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.UI.Hud;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// CORE and GAME tiers. The radio: what is heard where, what fixes what, and what survives a save.
    /// </summary>
    /// <remarks>
    /// The tolerance ladder is pinned by number because the design pins it by number (§3.3), and
    /// a puzzle whose lock window drifts is a puzzle whose difficulty drifts. The reading-comprehension
    /// clue — 5 2 4 0 with no unit, in a list otherwise in kHz — is pinned as a station at 5.240.
    /// </remarks>
    [TestFixture]
    public sealed class RadioTests
    {
        // --- the band -----------------------------------------------------------------------

        [Test]
        public void TheSignal_IsAt5240_AndInsideTheBand()
        {
            Station voice;
            Assert.That(Stations.TryGet(Stations.TheVoice, out voice), Is.True);
            Assert.That(voice.Mhz, Is.EqualTo(5.240f).Within(0.0001f), "5 2 4 0, read in kHz.");
            Assert.That(voice.IsTheSignal, Is.True);
            Assert.That(voice.Mhz, Is.GreaterThan(RadioBand.MinMhz).And.LessThan(RadioBand.MaxMhz));
        }

        [Test]
        public void EveryStation_IsInsideTheBand_AndTheTableIsNotEmpty()
        {
            Assert.That(Stations.Count, Is.GreaterThanOrEqualTo(3));
            for (var i = 0; i < Stations.All.Length; i++)
            {
                Assert.That(Stations.All[i].Mhz, Is.GreaterThan(RadioBand.MinMhz).And.LessThan(RadioBand.MaxMhz),
                    Stations.All[i].Id + " sits against a stop.");
            }
        }

        // --- tolerance ----------------------------------------------------------------------

        [TestCase(0.0f, Reception.Locked)]
        [TestCase(0.0007f, Reception.Locked)]
        [TestCase(0.0009f, Reception.Detuned)]
        [TestCase(0.0039f, Reception.Detuned)]
        [TestCase(0.0041f, Reception.Smudge)]
        [TestCase(0.0119f, Reception.Smudge)]
        [TestCase(0.0121f, Reception.Grass)]
        [TestCase(0.5f, Reception.Grass)]
        public void Reception_FollowsTheDesignsLadder(float offsetMhz, Reception expected)
        {
            string id;
            Assert.That(RadioTuner.Receive(5.240f + offsetMhz, out id), Is.EqualTo(expected));
            Assert.That(RadioTuner.Receive(5.240f - offsetMhz, out id), Is.EqualTo(expected), "The ladder is symmetric.");
        }

        [Test]
        public void InTheGrass_ThereIsNoStation()
        {
            string id;
            RadioTuner.Receive(3.000f, out id);
            Assert.That(id, Is.Null);
        }

        [Test]
        public void Snap_SettlesInsideTheLockWindow_AndNowhereElse()
        {
            Assert.That(RadioTuner.Snap(5.2405f), Is.EqualTo(5.240f).Within(0.00001f), "Inside the window: on the carrier.");
            Assert.That(RadioTuner.Snap(5.2430f), Is.EqualTo(5.2430f).Within(0.00001f), "Outside it: untouched.");
        }

        [Test]
        public void Strength_FallsAwayFromTheCarrier_AndNeverExceedsOne()
        {
            var on = RadioTuner.Strength(5.240f);
            var near = RadioTuner.Strength(5.244f);
            var far = RadioTuner.Strength(5.300f);
            Assert.That(on, Is.EqualTo(1f).Within(0.001f));
            Assert.That(near, Is.LessThan(on).And.GreaterThan(far));
            Assert.That(far, Is.LessThan(0.02f));
        }

        [Test]
        public void Spectrum_ShowsACarrierAsALine_AndGrassAsGrass()
        {
            var columns = new float[61];
            RadioTuner.Spectrum(5.240f, 120f, 7, columns);

            // Column 30 is the needle, on the carrier. The edges are 60 kHz out: grass.
            Assert.That(columns[30], Is.GreaterThan(0.9f));
            Assert.That(columns[0], Is.LessThan(0.35f));
            Assert.That(columns[60], Is.LessThan(0.35f));

            // Deterministic for a seed, different for another: that is what makes it scroll.
            var again = new float[61];
            RadioTuner.Spectrum(5.240f, 120f, 7, again);
            Assert.That(again[5], Is.EqualTo(columns[5]));
            var other = new float[61];
            RadioTuner.Spectrum(5.240f, 120f, 8, other);
            Assert.That(other[5], Is.Not.EqualTo(columns[5]));
        }

        // --- repair -------------------------------------------------------------------------

        [Test]
        public void ThreeFaults_ClearedInAnyOrder_MakeTheSetWork()
        {
            var repair = new RadioRepair();
            RadioFault cleared;

            Assert.That(repair.IsWorking, Is.False);
            Assert.That(repair.TryApply(ItemIds.CopperSpring, out cleared), Is.True);
            Assert.That(cleared, Is.EqualTo(RadioFault.Fuse));
            Assert.That(repair.TryApply(ItemIds.Multitool, out cleared), Is.True);
            Assert.That(repair.IsWorking, Is.False, "Two of three.");
            Assert.That(repair.TryApply(ItemIds.DeadTorch, out cleared), Is.True);
            Assert.That(repair.IsWorking, Is.True);
        }

        [Test]
        public void TheWrongItem_DoesNothing_AndAFixedFaultCannotBeFixedAgain()
        {
            var repair = new RadioRepair();
            RadioFault cleared;

            Assert.That(repair.TryApply(ItemIds.DrySpindle, out cleared), Is.False);
            Assert.That(repair.TryApply(ItemIds.Multitool, out cleared), Is.True);
            Assert.That(repair.TryApply(ItemIds.CopperSpring, out cleared), Is.True);
            Assert.That(repair.TryApply(ItemIds.Multitool, out cleared), Is.False, "Already clean, and the fuse is done.");
            Assert.That(repair.TryApply(ItemIds.CopperSpring, out cleared), Is.False, "One spring is enough.");
        }

        [Test]
        public void TheMultitool_StripsTheMicCord_ForTheFuse_OnceTheContactsAreClean()
        {
            // The design's other valid fuse fix (§4.2): a player who never found the spring.
            var repair = new RadioRepair();
            RadioFault cleared;

            Assert.That(repair.FaultFor(ItemIds.Multitool), Is.EqualTo(RadioFault.Contacts), "First the scraper.");
            Assert.That(repair.TryApply(ItemIds.Multitool, out cleared), Is.True);
            Assert.That(cleared, Is.EqualTo(RadioFault.Contacts));

            Assert.That(repair.FaultFor(ItemIds.Multitool), Is.EqualTo(RadioFault.Fuse), "Then the blade, on the cord.");
            Assert.That(repair.TryApply(ItemIds.Multitool, out cleared), Is.True);
            Assert.That(cleared, Is.EqualTo(RadioFault.Fuse));
            Assert.That(repair.FuseFromCord, Is.True);
            Assert.That(repair.TryApply(ItemIds.CopperSpring, out cleared), Is.False, "The holder is already bridged.");

            var restored = new RadioRepair();
            restored.Restore(repair.Capture());
            Assert.That(restored.FuseFromCord, Is.True, "The cord survives a save.");
            Assert.That(restored.Outstanding, Is.EqualTo(RadioFault.Power));
        }

        [Test]
        public void TheSpringFirst_LeavesTheCordAlone()
        {
            var repair = new RadioRepair();
            RadioFault cleared;
            repair.TryApply(ItemIds.CopperSpring, out cleared);
            repair.TryApply(ItemIds.Multitool, out cleared);

            Assert.That(cleared, Is.EqualTo(RadioFault.Contacts));
            Assert.That(repair.FuseFromCord, Is.False);
            Assert.That(repair.FaultFor(ItemIds.Multitool), Is.EqualTo(RadioFault.None));
        }

        [Test]
        public void TheRecordersCells_PowerTheSet_AndAreRemembered()
        {
            // The prologue's one real decision, never flagged as one.
            var repair = new RadioRepair();
            RadioFault cleared;
            repair.TryApply(ItemIds.FieldRecorder, out cleared);

            Assert.That(cleared, Is.EqualTo(RadioFault.Power));
            Assert.That(repair.UsedRecorderCells, Is.True);
            Assert.That(RadioRepair.ConsumesItem(ItemIds.FieldRecorder), Is.False, "The recorder stays carried, dead.");
            Assert.That(RadioRepair.ConsumesItem(ItemIds.DeadTorch), Is.True, "The torch is taken apart.");
            Assert.That(RadioRepair.ConsumesItem(ItemIds.CopperSpring), Is.True, "The spring stays in the holder.");
            Assert.That(RadioRepair.ConsumesItem(ItemIds.Multitool), Is.False, "A tool.");
        }

        [Test]
        public void Repair_SurvivesACaptureAndRestore()
        {
            var source = new RadioRepair();
            RadioFault cleared;
            source.TryApply(ItemIds.FieldRecorder, out cleared);
            source.TryApply(ItemIds.CopperSpring, out cleared);

            var target = new RadioRepair();
            target.Restore(source.Capture());

            Assert.That(target.Outstanding, Is.EqualTo(RadioFault.Contacts));
            Assert.That(target.UsedRecorderCells, Is.True);
        }

        [Test]
        public void TakingTheTorchApart_IsRemembered_SoTheNailRowDoesNotGrowANewOne()
        {
            var source = new RadioRepair();
            RadioFault cleared;
            source.TryApply(ItemIds.DeadTorch, out cleared);
            Assert.That(source.TorchTakenApart, Is.True);

            var target = new RadioRepair();
            target.Restore(source.Capture());
            Assert.That(target.TorchTakenApart, Is.True);
        }

        // --- the service --------------------------------------------------------------------

        private static RadioService NewService(out InventoryService inventory)
        {
            var signals = new SignalBus();
            inventory = new InventoryService(signals, null);
            return new RadioService(signals, inventory, null);
        }

        [Test]
        public void TakingTheTorchApart_PutsItsSpringInThePlayersHands()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);
            inventory.Take(ItemIds.DeadTorch);

            var outcome = radio.Apply(ItemIds.DeadTorch);

            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(outcome.ConsumesItem, Is.True);
            Assert.That(inventory.Has(ItemIds.CopperSpring), Is.True, "The spring is how most players find the fuse.");
        }

        [Test]
        public void Inspecting_GivesTheFirstLook_ThenOneFaultAtATime()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);

            Assert.That(radio.Inspect(), Is.EqualTo("narration.radio.found"));
            Assert.That(radio.Inspect(), Is.EqualTo("narration.radio.list"),
                "The clue the puzzle turns on is found on the second look, long before the set works.");
            Assert.That(radio.Inspect(), Is.EqualTo("narration.radio.fault.power"));
            radio.Apply(ItemIds.DeadTorch);
            Assert.That(radio.Inspect(), Is.EqualTo("narration.radio.fault.contacts"));
        }

        [Test]
        public void AWorkingSet_OpensTunesAndRecordsAFirstHearing()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);
            radio.Apply(ItemIds.DeadTorch);
            radio.Apply(ItemIds.Multitool);
            radio.Apply(ItemIds.CopperSpring);

            Assert.That(radio.IsWorking, Is.True);
            Assert.That(radio.Open(), Is.True);
            radio.Tune(5.2404f);

            Assert.That(radio.Mhz, Is.EqualTo(5.240f).Within(0.00001f), "Snapped.");
            Assert.That(radio.TransmissionReceived, Is.True);
            Assert.That(radio.Heard.Count, Is.EqualTo(1));

            radio.Tune(5.2401f);
            Assert.That(radio.Heard.Count, Is.EqualTo(1), "Heard once; recorded once.");
        }

        [Test]
        public void PoweringUp_SpeaksOnceAsASequence_WhicheverFaultWentLast()
        {
            // The fix line and then the click/lamp/hiss line, as one sequence from the game, and
            // an outcome with no line of its own so the use handler does not say the fix twice.
            var signals = new SignalBus();
            var inventory = new InventoryService(signals, null);
            var radio = new RadioService(signals, inventory, null);
            System.Collections.Generic.IReadOnlyList<string> sequence = null;
            signals.Subscribe<NarrationSequenceSignal>(s => sequence = s.LineKeys);

            radio.Apply(ItemIds.DeadTorch);
            radio.Apply(ItemIds.CopperSpring);
            var last = radio.Apply(ItemIds.Multitool);

            Assert.That(last.Succeeded, Is.True);
            Assert.That(last.NarrationKey, Is.Null, "The set has already spoken.");
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence[0], Is.EqualTo("narration.radio.fixed.contacts"));
            Assert.That(sequence[1], Is.EqualTo("narration.radio.working"));
        }

        [Test]
        public void ABrokenSet_CannotBeOpenedOrTuned()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);

            Assert.That(radio.Open(), Is.False);
            Assert.That(radio.Tune(5.240f), Is.False);
            Assert.That(radio.IsOpen, Is.False);
        }

        [Test]
        public void TheRadio_SurvivesACaptureAndRestore()
        {
            InventoryService inventory;
            var source = NewService(out inventory);
            source.Inspect();
            source.Apply(ItemIds.FieldRecorder);
            source.Apply(ItemIds.Multitool);
            source.Apply(ItemIds.CopperSpring);
            source.Open();
            source.Tune(8.291f);
            source.SqueezeMic();

            var doc = new SaveDocument();
            source.Capture(doc);

            InventoryService other;
            var target = NewService(out other);
            target.Restore(doc);

            Assert.That(target.IsFound, Is.True);
            Assert.That(target.IsWorking, Is.True);
            Assert.That(target.Repair.UsedRecorderCells, Is.True);
            Assert.That(target.Mhz, Is.EqualTo(8.291f).Within(0.0001f));
            Assert.That(target.Heard, Does.Contain(Stations.HullThump));
            Assert.That(target.IsOpen, Is.False, "A run resumes with the set put down.");
        }

        [Test]
        public void Restore_FromASaveWithNoRadioSection_IsAnOlderRun()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);
            radio.Apply(ItemIds.DeadTorch);

            radio.Restore(new SaveDocument());

            Assert.That(radio.IsWorking, Is.False);
            Assert.That(radio.Repair.Outstanding, Is.EqualTo(RadioFault.All));
        }

        // --- the command layer --------------------------------------------------------------

        [Test]
        public void ANewGame_StartsWithTheKit_AndABrokenRadio()
        {
            var signals = new SignalBus();
            var inventory = new InventoryService(signals, null);
            var radio = new RadioService(signals, inventory, null);
            inventory.Take(ItemIds.SluiceKey);
            radio.Apply(ItemIds.DeadTorch);

            // The reset is a static step of the handler, tested directly: the handler itself
            // needs a zone registry and a scene, which is a PlayMode concern.
            StartNewGameHandler.PrepareNewRun(null, inventory, radio);

            Assert.That(inventory.Has(ItemIds.SluiceKey), Is.False, "The previous run's pockets are emptied.");
            Assert.That(inventory.Has(ItemIds.Multitool), Is.True);
            Assert.That(inventory.Has(ItemIds.FieldRecorder), Is.True);
            Assert.That(radio.Repair.Outstanding, Is.EqualTo(RadioFault.All));
        }

        [Test]
        public void Tuning_IsRefused_UntilTheDialIsOpen()
        {
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var inventory = new InventoryService(signals, null);
            var radio = new RadioService(signals, inventory, null);
            radio.Apply(ItemIds.DeadTorch);
            radio.Apply(ItemIds.Multitool);
            radio.Apply(ItemIds.CopperSpring);

            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<TuneRadioCommand>(new TuneRadioHandler(states, radio, signals));
            dispatcher.Register<OpenRadioCommand>(new OpenRadioHandler(states, radio, signals));

            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);

            System.Collections.Generic.IReadOnlyList<string> sequence = null;
            signals.Subscribe<NarrationSequenceSignal>(s => sequence = s.LineKeys);

            Assert.That(dispatcher.Dispatch(new TuneRadioCommand(5.24f)).Code, Is.EqualTo(ResultCode.NotAllowedInState));
            Assert.That(dispatcher.Dispatch(new OpenRadioCommand()).Success, Is.True);
            Assert.That(dispatcher.Dispatch(new TuneRadioCommand(5.24f)).Success, Is.True);
            Assert.That(radio.TransmissionReceived, Is.True);

            // The transmission is composed by the game, first hearing first, deduction last.
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence[0], Is.EqualTo("narration.radio.the_voice"));
            Assert.That(sequence[sequence.Count - 1], Is.EqualTo("narration.radio.after.4"));
        }

        [Test]
        public void AUseThatSucceedsWithoutALine_IsNotCalledNothing()
        {
            // The use handler's rule: a refusal always answers; a success may have already spoken.
            var signals = new SignalBus();
            var states = new GameStateMachine(null, signals);
            var inventory = new InventoryService(signals, null);
            var radio = new RadioService(signals, inventory, null);
            radio.Apply(ItemIds.DeadTorch);
            radio.Apply(ItemIds.CopperSpring);
            inventory.Take(ItemIds.Multitool);

            var lines = new System.Collections.Generic.List<string>();
            signals.Subscribe<NarrationSignal>(n => lines.Add(n.LineKey));

            var resolver = new StubResolver(radio);
            var dispatcher = new CommandDispatcher(null);
            dispatcher.Register<UseItemCommand>(new UseItemHandler(states, inventory, resolver, signals));
            states.TryTransition(GameStateId.MainMenu);
            states.TryTransition(GameStateId.Loading);
            states.TryTransition(GameStateId.InGame);

            Assert.That(dispatcher.Dispatch(new UseItemCommand(ItemIds.Multitool, "radio.set")).Success, Is.True);
            Assert.That(lines, Does.Not.Contain(UseItemHandler.NoEffectKey));

            Assert.That(dispatcher.Dispatch(new UseItemCommand(ItemIds.Multitool, "radio.set")).Success, Is.True,
                "Using it again is legal and does nothing.");
            Assert.That(lines, Does.Contain(UseItemHandler.NoEffectKey), "And a refusal always answers.");
        }

        private sealed class StubResolver : IUseTargetResolver
        {
            private readonly RadioService _radio;

            public StubResolver(RadioService radio)
            {
                _radio = radio;
            }

            public UseOutcome Use(string itemId, string targetId)
            {
                return targetId == "radio.set" ? _radio.Apply(itemId) : UseOutcome.Nothing;
            }
        }

        // --- the panel's one piece of arithmetic --------------------------------------------

        [Test]
        public void DraggingTheStripLeft_MovesTheNeedleUpTheBand_AtTheDesignsRate()
        {
            // A full screen-width of coarse drag is 180 kHz, per design §3.2; fine is a tenth.
            var coarse = RadioPanel.MhzForDrag(-400f, 400f, false);
            var fine = RadioPanel.MhzForDrag(-400f, 400f, true);

            Assert.That(coarse, Is.EqualTo(0.180f).Within(0.00001f));
            Assert.That(fine, Is.EqualTo(0.018f).Within(0.00001f));
            Assert.That(RadioPanel.MhzForDrag(10f, 0f, false), Is.EqualTo(0f), "No width, no movement, no divide.");
        }

        // --- the self-sweep (hint tier 4) ---------------------------------------------------

        private static RadioService WorkingService(out InventoryService inventory)
        {
            var radio = NewService(out inventory);
            RadioFault cleared;
            radio.Repair.TryApply(ItemIds.DeadTorch, out cleared);
            radio.Repair.TryApply(ItemIds.CopperSpring, out cleared);
            radio.Repair.TryApply(ItemIds.Multitool, out cleared);
            Assert.That(radio.IsWorking, Is.True, "Test setup: the set should work.");
            return radio;
        }

        [Test]
        public void Sweep_FromRest_PassesTheSignal_InAboutNinetySeconds()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            Assert.That(radio.BeginSweep(), Is.True);
            Assert.That(radio.IsSweeping, Is.True);

            var seconds = 0f;
            while (radio.IsSweeping && seconds < 300f)
            {
                radio.Sweep(0.1f);
                seconds += 0.1f;
            }

            Assert.That(radio.TransmissionReceived, Is.True, "The sweep finds her.");
            Assert.That(radio.IsSweeping, Is.False, "And stops on her.");
            Assert.That(seconds, Is.EqualTo(89f).Within(3f), "About ninety seconds later (§3.5, tier 4).");
            Assert.That(radio.HasHeard(Stations.HullThump), Is.True, "It locked onto the hull on the way, as a thumb would.");
        }

        [Test]
        public void Sweep_DoesNotStickOnTheHull()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            radio.Tune(8.6f);
            radio.BeginSweep();

            for (var i = 0; i < 400; i++)
            {
                radio.Sweep(0.1f);
            }

            Assert.That(radio.Mhz, Is.LessThan(8.291f - RadioBand.LockHalfWidthKhz / 1000f),
                "The snapped needle must not pull the sweep back onto the carrier every tick.");
        }

        [Test]
        public void AHandOnTheDial_StopsTheSweep_EvenWithoutMovingIt()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            radio.BeginSweep();
            radio.Sweep(1f);
            var where = radio.Mhz;

            radio.Tune(where);

            Assert.That(radio.IsSweeping, Is.False);
            Assert.That(radio.Sweep(1f), Is.False);
            Assert.That(radio.Mhz, Is.EqualTo(where).Within(0.0001f));
        }

        [Test]
        public void PuttingTheSetDown_StopsTheSweep()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            radio.Open();
            radio.BeginSweep();

            radio.Close();

            Assert.That(radio.IsSweeping, Is.False);
        }

        [Test]
        public void Sweep_BouncesAtTheStops()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);

            // She is already heard, so passing her does not end the sweep and it can reach a stop.
            radio.Tune(5.24f);
            Assert.That(radio.TransmissionReceived, Is.True, "Test setup.");
            radio.Tune(RadioBand.MinMhz + 0.3f);
            radio.BeginSweep();

            // Direction is toward the signal (up): 30 MHz of travel crosses the top stop and comes back.
            var highest = 0f;
            var cameBack = false;
            for (var i = 0; i < 6000; i++)
            {
                Assert.That(radio.Sweep(0.1f), Is.True, "Still sweeping at step " + i);
                Assert.That(radio.Mhz, Is.GreaterThanOrEqualTo(RadioBand.MinMhz).And.LessThanOrEqualTo(RadioBand.MaxMhz));
                if (radio.Mhz > highest)
                {
                    highest = radio.Mhz;
                }
                else if (highest >= RadioBand.MaxMhz - 0.001f && radio.Mhz < highest - 1f)
                {
                    cameBack = true;
                }
            }

            Assert.That(highest, Is.EqualTo(RadioBand.MaxMhz).Within(0.001f), "Reached the top stop.");
            Assert.That(cameBack, Is.True, "And turned round.");
        }

        [Test]
        public void Sweep_CannotJumpOverACarrier()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            radio.Tune(5.30f);
            radio.BeginSweep();

            // One long step of a whole second: 50 kHz, thirty lock windows wide.
            radio.Sweep(1f);

            Assert.That(radio.Mhz, Is.EqualTo(5.24f).Within(0.0001f), "The step lands on the carrier it crosses.");
            Assert.That(radio.TransmissionReceived, Is.True);
        }

        [Test]
        public void ABrokenSet_CannotSweep()
        {
            InventoryService inventory;
            var radio = NewService(out inventory);

            Assert.That(radio.BeginSweep(), Is.False);
            Assert.That(radio.IsSweeping, Is.False);
        }

        [Test]
        public void TheSweep_IsNotSaved()
        {
            InventoryService inventory;
            var radio = WorkingService(out inventory);
            radio.BeginSweep();
            var doc = new SaveDocument();
            radio.Capture(doc);

            InventoryService other;
            var restored = WorkingService(out other);
            restored.Restore(doc);

            Assert.That(restored.IsSweeping, Is.False, "A run resumes with the set put down (ADR-0025).");
            Assert.That(restored.IsWorking, Is.True);
        }
    }
}
