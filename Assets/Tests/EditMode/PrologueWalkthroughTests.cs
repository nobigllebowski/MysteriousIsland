using System;
using System.Collections.Generic;
using System.IO;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Hints;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Session;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// GAME TIER. The prologue, walked end to end through the command layer: the stone, the
    /// fire, the tag, the set, the voice, and a save in the middle of it.
    /// </summary>
    /// <remarks>
    /// Everything the composition root builds except what needs a scene: no zone registry, no
    /// interactables. World targets are stood in for by a resolver that routes a use to the fire
    /// or the radio the way <c>FireSite</c> and <c>RadioSet</c> do. What this pins is the chain:
    /// that each step's command is legal after the previous one, that the objective, the Slate,
    /// the hints and the autosave beats follow, and that a run saved mid-way resumes as itself.
    /// The two chain suites before this one each found dead ends nobody had traced by hand.
    /// </remarks>
    [TestFixture]
    public sealed class PrologueWalkthroughTests
    {
        private sealed class World : IDisposable, IUseTargetResolver
        {
            public readonly SignalBus Signals = new SignalBus();
            public readonly GameStateMachine States;
            public readonly SessionService Session;
            public readonly ProgressService Progress;
            public readonly InventoryService Inventory;
            public readonly RadioService Radio;
            public readonly FireService Fire;
            public readonly CommandDispatcher Commands;
            public readonly SaveSlotService Slots;
            public readonly AutosaveDirector Autosave;
            public readonly HintDirector Hints;
            public readonly SlateDirector Slate;
            public readonly ObjectiveKeeper Objectives;
            public readonly RecordKeeper Records;
            public readonly List<string> Said = new List<string>();
            public readonly List<string> Sequences = new List<string>();
            public int LastSavedSlot = -1;

            public World(string saveRoot)
            {
                States = new GameStateMachine(null, Signals);
                Session = new SessionService(new IslandClock(0d), null, Signals);
                Progress = new ProgressService(Signals, null);
                Inventory = new InventoryService(Signals, null);
                Radio = new RadioService(Signals, Inventory, null);
                Fire = new FireService(Signals, Inventory, null);
                Commands = new CommandDispatcher(null);
                Slots = new SaveSlotService(new SaveFileStore(saveRoot, null), new SaveCodec(), null, "test");
                foreach (var participant in new ISaveParticipant[] { Session, Session.PlayerParticipant, Progress, Inventory, Radio, Fire })
                {
                    Slots.RegisterParticipant(participant);
                }

                Records = new RecordKeeper(Progress, Session, Signals);
                Autosave = new AutosaveDirector(Slots, Session, States, Signals, null);
                Slate = new SlateDirector(Progress, Radio, Signals);
                Objectives = new ObjectiveKeeper(Progress, Radio, Fire, Signals);
                Hints = new HintDirector(Session, States, Progress, Radio, Fire, Commands, Signals, null);

                Commands.Register<InspectCommand>(new InspectHandler(States, Progress, Signals));
                Commands.Register<CollectCommand>(new CollectHandler(States, Progress, Signals, Inventory));
                Commands.Register<TakeItemCommand>(new TakeItemHandler(States, Inventory, Signals));
                Commands.Register<CombineItemsCommand>(new CombineItemsHandler(States, Inventory, Signals));
                Commands.Register<UseItemCommand>(new UseItemHandler(States, Inventory, this, Signals));
                Commands.Register<HoldItemCommand>(new HoldItemHandler(States, Inventory));
                Commands.Register<RemarkCommand>(new RemarkHandler(States, Signals));
                Commands.Register<OpenRadioCommand>(new OpenRadioHandler(States, Radio, Signals));
                Commands.Register<CloseRadioCommand>(new CloseRadioHandler(Radio));
                Commands.Register<TuneRadioCommand>(new TuneRadioHandler(States, Radio, Signals));
                Commands.Register<SqueezeMicCommand>(new SqueezeMicHandler(States, Radio, Signals));
                Commands.Register<BeginSweepCommand>(new BeginSweepHandler(States, Radio));
                Commands.Register<SweepRadioCommand>(new SweepRadioHandler(States, Radio, Signals));
                Commands.Register<CarryFireKitCommand>(new CarryFireKitHandler(States, Fire));

                Signals.Subscribe<NarrationSignal>(s => Said.Add(s.LineKey));
                Signals.Subscribe<NarrationSequenceSignal>(s => Sequences.AddRange(s.LineKeys));
                Signals.Subscribe<GameSavedSignal>(s => LastSavedSlot = s.Slot);
            }

            /// <summary>What StartNewGameHandler does, minus the scene.</summary>
            public void NewGame()
            {
                States.TryTransition(GameStateId.MainMenu);
                States.TryTransition(GameStateId.Loading);
                StartNewGameHandler.PrepareNewRun(Progress, Inventory, Radio, Fire);
                Session.BeginNewRun(0, string.Empty, ContentIds.ZoneRibcage, 7);
                Session.SetZone(ContentIds.ZoneRibcage);
                States.TryTransition(GameStateId.InGame);
                Assert.That(States.Current, Is.EqualTo(GameStateId.InGame));
            }

            /// <summary>What ResumeSavedRunHandler does, minus the scene.</summary>
            public void Resume(int slot)
            {
                States.TryTransition(GameStateId.MainMenu);
                States.TryTransition(GameStateId.Loading);
                Assert.That(Slots.Load(slot), Is.EqualTo(ForgottenIsle.Core.Primitives.ResultCode.Ok));
                States.TryTransition(GameStateId.InGame);
                Assert.That(States.Current, Is.EqualTo(GameStateId.InGame));
            }

            public void Tick(double seconds)
            {
                var ticks = (int)Math.Round(seconds * Ticker.TargetTicksPerRealSecond);
                for (var i = 0; i < ticks; i++)
                {
                    Session.AddPlaytime(1d / Ticker.TargetTicksPerRealSecond);
                    Signals.Publish(new TickCompletedSignal(Session.SimHours));
                }
            }

            public CommandResult Do<T>(T command) where T : struct, ICommand
            {
                var result = Commands.Dispatch(command);
                Assert.That(result.Success, Is.True, typeof(T).Name + " refused: " + result.Code);
                return result;
            }

            public string Objective => Progress.ObjectiveKey;

            /// <summary>Routes a use the way the world's interactables would.</summary>
            public UseOutcome Use(string itemId, string targetId)
            {
                if (targetId == ContentIds.RadioSet)
                {
                    return Radio.Apply(itemId);
                }

                if (Fire.Site(targetId) != null)
                {
                    var act = Fire.Apply(targetId, itemId, Inventory.Has(ItemIds.Multitool));
                    if (act == FireAct.Nothing)
                    {
                        return UseOutcome.Nothing;
                    }

                    if (act == FireAct.Lit)
                    {
                        Progress.Solve(ContentIds.MechanismFire);
                    }

                    var key = FireRules.NarrationKey(act);
                    return FireRules.Consumes(act) ? UseOutcome.Spent(key) : FireRules.IsRefusal(act) ? UseOutcome.Refused(key) : UseOutcome.Worked(key);
                }

                return UseOutcome.Nothing;
            }

            public void Dispose()
            {
                Hints.Dispose();
                Autosave.Dispose();
                Slate.Dispose();
                Objectives.Dispose();
            }
        }

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "vardholm-walkthrough-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void ThePrologue_FromTheStoneToTheVoice_WithASaveInTheMiddle()
        {
            int savedAfterTheFire;
            using (var w = new World(_root))
            {
                w.NewGame();
                Assert.That(w.Inventory.Has(ItemIds.Multitool), Is.True, "The starting kit.");
                Assert.That(w.Objective, Is.EqualTo("objective.explore_ribcage"));
                Assert.That(w.Autosave.Writes, Is.Zero, "The arrival beat waits for the tick.");
                w.Tick(0.1d);
                Assert.That(w.Autosave.Writes, Is.EqualTo(1), "Arrived.");

                // 2:40 -- the stone, and the line.
                w.Do(new InspectCommand(ContentIds.MarkerRibStone));
                Assert.That(w.Objective, Is.EqualTo("objective.find_the_tag"));
                w.Do(new InspectCommand(ContentIds.MarkerHullLine));
                Assert.That(w.Slate.Current().OpenQuestions, Is.EqualTo(1), "SIX HULLS, ONE LINE.");

                // 5:00 -- the wrack.
                w.Do(new TakeItemCommand(ItemIds.PolyRope));
                w.Do(new TakeItemCommand(ItemIds.DryGrass));
                w.Do(new TakeItemCommand(ItemIds.DriftwoodDry));
                w.Do(new TakeItemCommand(ItemIds.DriftwoodWet));
                w.Do(new TakeItemCommand(ItemIds.FibreglassPanel));
                w.Do(new TakeItemCommand(ItemIds.ChertNodule));
                w.Do(new CombineItemsCommand(ItemIds.PolyRope, ItemIds.Multitool));
                Assert.That(w.Inventory.Has(ItemIds.PolyFibre), Is.True);
                Assert.That(w.Inventory.Has(ItemIds.Multitool), Is.True, "A tool survives combining.");
                Assert.That(w.Inventory.HasEverTaken(ItemIds.PolyRope), Is.True, "And the rope will not grow back.");

                // 7:10 -- the dry-fire problem, the wrong way first.
                w.Do(new UseItemCommand(ItemIds.DryGrass, ContentIds.FireSiteOpenA));
                Assert.That(w.Objective, Is.EqualTo("objective.make_fire"));
                Assert.That(w.Hints.FireSpark.IsRunning, Is.True);
                w.Do(new UseItemCommand(ItemIds.ChertNodule, ContentIds.FireSiteOpenA));
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.fire.grass_flared"));
                Assert.That(w.Inventory.Has(ItemIds.ChertNodule), Is.True, "Not consumed by striking.");
                Assert.That(w.Hints.FireSpark.IsRunning, Is.False);
                Assert.That(w.Hints.FireTinder.IsRunning, Is.True);

                w.Do(new UseItemCommand(ItemIds.PolyFibre, ContentIds.FireSiteOpenA));
                Assert.That(w.Hints.FireTinder.IsRunning, Is.False, "The right tinder is down.");
                w.Do(new UseItemCommand(ItemIds.DriftwoodDry, ContentIds.FireSiteOpenA));
                for (var i = 0; i < 3; i++)
                {
                    w.Do(new UseItemCommand(ItemIds.ChertNodule, ContentIds.FireSiteOpenA));
                }

                Assert.That(w.Said, Does.Contain("narration.fire.blew_out"));
                // The hint fires inside the use, before the handler narrates the blow-out itself, so
                // her line lands one before the last. Order is not the point; that it was said is.
                Assert.That(w.Said, Does.Contain("narration." + ContentIds.RemarkFireWind), "Three blow-outs: it's the wind.");
                Assert.That(w.Said.IndexOf("narration." + ContentIds.RemarkFireWind), Is.GreaterThan(w.Said.IndexOf("narration.fire.blew_out")));

                // The panel: the other authored solution.
                w.Do(new UseItemCommand(ItemIds.FibreglassPanel, ContentIds.FireSiteOpenA));
                w.Do(new UseItemCommand(ItemIds.ChertNodule, ContentIds.FireSiteOpenA));
                Assert.That(w.Fire.IsLit, Is.True);
                Assert.That(w.Progress.HasSolved(ContentIds.MechanismFire), Is.True);
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.fire.lit"));
                Assert.That(w.Objective, Is.EqualTo("objective.find_the_tag"), "Lit, the line goes back to the chain.");
                Assert.That(HasTitle(w.Slate.Current(), "slate.observed." + ContentIds.MechanismFire), Is.True, "FIRE is in the notebook.");

                w.Tick(0.1d);
                Assert.That(w.Autosave.Writes, Is.EqualTo(2), "A solved mechanism is an autosave beat.");
                savedAfterTheFire = w.LastSavedSlot;

                // 8:00 -- the warm zone.
                w.Do(new UseItemCommand(ItemIds.DriftwoodWet, ContentIds.FireSiteOpenA));
                Assert.That(w.Inventory.Has(ItemIds.DriftwoodWet), Is.False);
                w.Tick(91d);
                Assert.That(w.Inventory.Has(ItemIds.DriftwoodDry), Is.True, "Dry again, in the bag.");

                // 12:00 -- the tag; 15:00 -- the set.
                w.Do(new CollectCommand(ContentIds.DiscoveryBrassTag));
                Assert.That(w.Progress.IsZoneUnlocked(ContentIds.ZoneFernmaw), Is.True);
                Assert.That(w.Objective, Is.EqualTo("objective.enter_fernmaw"));
                w.Do(new OpenRadioCommand());
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.radio.found"));
                Assert.That(w.Objective, Is.EqualTo("objective.fix_the_set"));
                w.Do(new OpenRadioCommand());
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.radio.list"));

                // 17:00 -> 20:00 -- the repair, without the spring: the cord.
                w.Do(new TakeItemCommand(ItemIds.DeadTorch));
                w.Do(new UseItemCommand(ItemIds.DeadTorch, ContentIds.RadioSet));
                Assert.That(w.Inventory.Has(ItemIds.CopperSpring), Is.True, "The spring is in hand.");
                w.Do(new UseItemCommand(ItemIds.Multitool, ContentIds.RadioSet));
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.radio.fixed.contacts"));
                w.Do(new UseItemCommand(ItemIds.Multitool, ContentIds.RadioSet));
                Assert.That(w.Radio.IsWorking, Is.True, "The cord, not the spring.");
                Assert.That(w.Radio.Repair.FuseFromCord, Is.True);
                Assert.That(w.Sequences, Does.Contain("narration.radio.fixed.fuse_cord").And.Contain("narration.radio.working"));
                Assert.That(w.Objective, Is.EqualTo("objective.find_the_frequency"));
                Assert.That(w.Hints.Radio.IsRunning, Is.True, "The radio ladder starts at power-up.");

                // 22:00 -- the puzzle; 25:20 -- the voice.
                w.Do(new OpenRadioCommand());
                Assert.That(w.Radio.IsOpen, Is.True);
                w.Do(new TuneRadioCommand(8.291f));
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration." + Stations.HullThump), "A false positive is one line.");
                w.Sequences.Clear();
                w.Do(new TuneRadioCommand(5.24f));
                Assert.That(w.Radio.TransmissionReceived, Is.True);
                Assert.That(w.Sequences[0], Is.EqualTo("narration.radio.the_voice"));
                Assert.That(w.Sequences[w.Sequences.Count - 1], Is.EqualTo(TuneRadioHandler.DecisionRecordedKey), "The torch's cells: she records it properly.");
                Assert.That(w.Hints.Radio.IsRunning, Is.False);
                Assert.That(w.Objective, Is.EqualTo("objective.enter_fernmaw"));

                var slate = w.Slate.Current();
                Assert.That(HasTitle(slate, "slate.observed.radio.working_cord"), Is.True, "Which cells and which conductor.");
                Assert.That(HasTitle(slate, "slate.people.the_voice"), Is.True);
                Assert.That(slate.OpenQuestions, Is.EqualTo(4), "The line, the lantern, the gates, why won't she answer.");

                w.Tick(0.1d);
                Assert.That(w.Autosave.Writes, Is.EqualTo(3), "Hearing her is an autosave beat.");
                w.Do(new SqueezeMicCommand());
                Assert.That(w.Said[w.Said.Count - 1], Is.EqualTo("narration.radio.mic.1"));
                w.Do(new CloseRadioCommand());
                Assert.That(w.Session.RecordedPercent, Is.GreaterThan(0), "The record moved.");
            }

            // CONTINUE, from the save taken after the fire: the run resumes as itself.
            using (var w = new World(_root))
            {
                w.Resume(savedAfterTheFire);

                Assert.That(w.Fire.IsLit, Is.True, "The fire is still burning.");
                Assert.That(w.Fire.Site(ContentIds.FireSiteOpenA).PanelPlaced, Is.True);
                Assert.That(w.Progress.HasSolved(ContentIds.MechanismFire), Is.True);
                Assert.That(w.Inventory.Has(ItemIds.ChertNodule), Is.True);
                Assert.That(w.Inventory.Has(ItemIds.DriftwoodWet), Is.True, "Saved before it went by the fire.");
                Assert.That(w.Inventory.HasEverTaken(ItemIds.PolyRope), Is.True);
                Assert.That(w.Radio.IsWorking, Is.False, "The set was not yet fixed.");
                Assert.That(w.Objective, Is.EqualTo("objective.find_the_tag"), "The line its state implies.");
                Assert.That(w.Hints.Radio.IsRunning, Is.False);
                Assert.That(w.Hints.FireSpark.IsRunning, Is.False, "Forgotten on load.");
                Assert.That(HasTitle(w.Slate.Current(), "slate.observed." + ContentIds.MechanismFire), Is.True);

                // And the chain continues from here exactly as before.
                w.Do(new CollectCommand(ContentIds.DiscoveryBrassTag));
                Assert.That(w.Objective, Is.EqualTo("objective.enter_fernmaw"));
            }
        }

        [Test]
        public void LeftAlone_TheSetFindsHerByItself()
        {
            using (var w = new World(_root))
            {
                w.NewGame();
                w.Do(new TakeItemCommand(ItemIds.DeadTorch));
                w.Do(new UseItemCommand(ItemIds.DeadTorch, ContentIds.RadioSet));
                w.Do(new UseItemCommand(ItemIds.CopperSpring, ContentIds.RadioSet));
                w.Do(new UseItemCommand(ItemIds.Multitool, ContentIds.RadioSet));
                Assert.That(w.Radio.IsWorking, Is.True);

                w.Tick(961d);
                Assert.That(w.Radio.IsSweeping, Is.True, "Tier 4: left on and sweeping.");
                w.Tick(100d);

                Assert.That(w.Radio.TransmissionReceived, Is.True);
                Assert.That(w.Sequences, Does.Contain("narration.radio.the_voice"));
                Assert.That(w.Sequences[w.Sequences.Count - 1], Is.EqualTo(TuneRadioHandler.DecisionRecordedKey));
            }
        }

        private static bool HasTitle(SlateContents contents, string key)
        {
            foreach (var list in new[] { contents.Observed, contents.People, contents.Unresolved })
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].TitleKey == key)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
