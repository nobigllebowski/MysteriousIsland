using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="GameStateMachine"/>'s transition table, its rejection behaviour and its
    /// change notification.
    /// </summary>
    /// <remarks>
    /// The property under test is not "the happy path works". It is that a REJECTED transition is
    /// inert: it returns <see cref="ResultCode.IllegalStateTransition"/>, it does not move
    /// <see cref="GameStateMachine.Current"/>, and it does not raise
    /// <see cref="GameStateMachine.Changed"/>. Callers are allowed to ignore the returned result —
    /// UI buttons and async load completions both do — so every one of those three has to hold
    /// independently, and a test that checked only the return value would pass against a machine
    /// that had already corrupted its own mode.
    /// </remarks>
    [TestFixture]
    public sealed class StateMachineTests
    {
        private FakeCoreLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeCoreLog();
        }

        /// <summary>
        /// Every edge in the machine's legal table, spelled out here independently of the production
        /// array. A test that iterated the machine's own table would agree with any table, including a
        /// wrong one; this list is the specification, restated so the two can disagree.
        /// </summary>
        [TestCase(GameStateId.Boot, GameStateId.MainMenu)]
        [TestCase(GameStateId.MainMenu, GameStateId.Loading)]
        [TestCase(GameStateId.Loading, GameStateId.InGame)]
        [TestCase(GameStateId.Loading, GameStateId.LoadFailed)]
        [TestCase(GameStateId.LoadFailed, GameStateId.MainMenu)]
        [TestCase(GameStateId.InGame, GameStateId.Paused)]
        [TestCase(GameStateId.Paused, GameStateId.InGame)]
        [TestCase(GameStateId.Paused, GameStateId.Loading)]
        [TestCase(GameStateId.InGame, GameStateId.Loading)]
        public void Machine_LegalTransition_MovesAndReportsSuccess(GameStateId from, GameStateId to)
        {
            var machine = MachineAt(from);
            _log.Clear();

            Assert.IsTrue(machine.CanTransition(to), "CanTransition denied a legal edge " + from + "->" + to);

            var result = machine.TryTransition(to);

            Assert.IsTrue(result.Success, "TryTransition failed on a legal edge " + from + "->" + to);
            Assert.AreEqual(ResultCode.Ok, result.Code);
            Assert.AreEqual(to, machine.Current);
            Assert.AreEqual(0, _log.CountOf(LogCode.IllegalTransition), "A legal transition logged an illegal-transition warning.");
        }

        /// <summary>
        /// Representative illegal edges. Self-transitions and the edges back to
        /// <see cref="GameStateId.Boot"/> are included because their absence from the table looks like
        /// an oversight and is not: re-entering the current state is a caller bug, and boot happens
        /// once per process.
        /// </summary>
        [TestCase(GameStateId.Boot, GameStateId.Loading)]
        [TestCase(GameStateId.Boot, GameStateId.InGame)]
        [TestCase(GameStateId.Boot, GameStateId.Paused)]
        [TestCase(GameStateId.Boot, GameStateId.Boot)]
        [TestCase(GameStateId.MainMenu, GameStateId.Paused)]
        [TestCase(GameStateId.MainMenu, GameStateId.MainMenu)]
        [TestCase(GameStateId.MainMenu, GameStateId.Boot)]
        [TestCase(GameStateId.Loading, GameStateId.MainMenu)]
        [TestCase(GameStateId.Loading, GameStateId.Paused)]
        [TestCase(GameStateId.InGame, GameStateId.MainMenu)]
        [TestCase(GameStateId.InGame, GameStateId.InGame)]
        [TestCase(GameStateId.InGame, GameStateId.LoadFailed)]
        [TestCase(GameStateId.Paused, GameStateId.MainMenu)]
        [TestCase(GameStateId.Paused, GameStateId.Paused)]
        [TestCase(GameStateId.LoadFailed, GameStateId.InGame)]
        [TestCase(GameStateId.LoadFailed, GameStateId.Loading)]
        public void Machine_IllegalTransition_RejectsAndLeavesStateUnchanged(GameStateId from, GameStateId to)
        {
            var machine = MachineAt(from);
            _log.Clear();

            var changeCount = 0;
            machine.Changed += (_, __) => changeCount++;

            Assert.IsFalse(machine.CanTransition(to), "CanTransition allowed an illegal edge " + from + "->" + to);

            var result = machine.TryTransition(to);

            Assert.IsFalse(result.Success, "TryTransition succeeded on an illegal edge " + from + "->" + to);
            Assert.AreEqual(ResultCode.IllegalStateTransition, result.Code);
            Assert.AreEqual(from, machine.Current, "A rejected transition moved the machine.");
            Assert.AreEqual(0, changeCount, "A rejected transition raised Changed.");
            Assert.AreEqual(1, _log.CountOf(LogCode.IllegalTransition), "A rejected transition did not log exactly one warning.");
        }

        /// <summary>
        /// The single most important illegal edge in the project: you cannot walk from the menu
        /// straight into play. <see cref="GameStateId.Loading"/> is mandatory because that is where the
        /// curtain is raised and the zone is brought in (ADR-0004); an accepted MainMenu-&gt;InGame would
        /// present the player with a live session standing in an empty world.
        /// </summary>
        [Test]
        public void Machine_MainMenuToInGame_IsRejectedBecauseLoadingIsMandatory()
        {
            var machine = MachineAt(GameStateId.MainMenu);
            _log.Clear();

            var result = machine.TryTransition(GameStateId.InGame);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ResultCode.IllegalStateTransition, result.Code);
            Assert.AreEqual(GameStateId.MainMenu, machine.Current);

            // And the legal route is available from exactly the same state, so the rejection above is
            // about the edge and not about the machine being wedged.
            Assert.IsTrue(machine.TryTransition(GameStateId.Loading).Success);
            Assert.IsTrue(machine.TryTransition(GameStateId.InGame).Success);
            Assert.AreEqual(GameStateId.InGame, machine.Current);
        }

        [Test]
        public void Machine_NewInstance_StartsInBoot()
        {
            var machine = new GameStateMachine(_log, null);

            Assert.AreEqual(GameStateId.Boot, machine.Current);
            Assert.AreEqual(0, _log.WarningCount);
        }

        [Test]
        public void Machine_LegalTransition_RaisesChangedExactlyOnceWithFromAndTo()
        {
            var machine = MachineAt(GameStateId.MainMenu);

            var observed = new List<KeyValuePair<GameStateId, GameStateId>>();
            machine.Changed += (from, to) => observed.Add(new KeyValuePair<GameStateId, GameStateId>(from, to));

            var result = machine.TryTransition(GameStateId.Loading);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, observed.Count, "Changed did not fire exactly once.");
            Assert.AreEqual(GameStateId.MainMenu, observed[0].Key, "Changed reported the wrong 'from'.");
            Assert.AreEqual(GameStateId.Loading, observed[0].Value, "Changed reported the wrong 'to'.");
        }

        /// <summary>
        /// The event is documented as firing AFTER the state has already moved. Handlers act on that —
        /// the ticker and the overlay both read <see cref="GameStateMachine.Current"/> from inside the
        /// callback — so the ordering is part of the contract, not an implementation detail.
        /// </summary>
        [Test]
        public void Machine_ChangedHandler_ObservesCurrentAlreadyUpdated()
        {
            var machine = MachineAt(GameStateId.MainMenu);

            var currentInsideHandler = GameStateId.Boot;
            machine.Changed += (_, __) => currentInsideHandler = machine.Current;

            machine.TryTransition(GameStateId.Loading);

            Assert.AreEqual(GameStateId.Loading, currentInsideHandler);
        }

        [Test]
        public void Machine_ChainedLegalTransitions_RaiseChangedOncePerStep()
        {
            var machine = new GameStateMachine(_log, null);

            var transitions = new List<string>();
            machine.Changed += (from, to) => transitions.Add(from + "->" + to);

            Assert.IsTrue(machine.TryTransition(GameStateId.MainMenu).Success);
            Assert.IsTrue(machine.TryTransition(GameStateId.Loading).Success);
            Assert.IsTrue(machine.TryTransition(GameStateId.InGame).Success);
            Assert.IsTrue(machine.TryTransition(GameStateId.Paused).Success);
            Assert.IsTrue(machine.TryTransition(GameStateId.InGame).Success);

            Assert.AreEqual(5, transitions.Count);
            Assert.AreEqual("Boot->MainMenu", transitions[0]);
            Assert.AreEqual("MainMenu->Loading", transitions[1]);
            Assert.AreEqual("Loading->InGame", transitions[2]);
            Assert.AreEqual("InGame->Paused", transitions[3]);
            Assert.AreEqual("Paused->InGame", transitions[4]);
            Assert.AreEqual(GameStateId.InGame, machine.Current);
        }

        /// <summary>
        /// A failed load is terminal for the attempt: the only way out is back to the menu. If
        /// LoadFailed-&gt;Loading or LoadFailed-&gt;InGame were reachable, a retry would run against the
        /// half-torn-down session the failure left behind.
        /// </summary>
        [Test]
        public void Machine_LoadFailed_OnlyEscapesToMainMenu()
        {
            var machine = MachineAt(GameStateId.LoadFailed);

            Assert.IsFalse(machine.CanTransition(GameStateId.Loading));
            Assert.IsFalse(machine.CanTransition(GameStateId.InGame));
            Assert.IsFalse(machine.CanTransition(GameStateId.Paused));
            Assert.IsFalse(machine.CanTransition(GameStateId.Boot));
            Assert.IsTrue(machine.CanTransition(GameStateId.MainMenu));

            Assert.IsTrue(machine.TryTransition(GameStateId.MainMenu).Success);
            Assert.AreEqual(GameStateId.MainMenu, machine.Current);
        }

        /// <summary>
        /// Rejection must not be a one-shot poison: after an illegal request the machine still honours
        /// legal ones. This is the "a caller that ignores the result cannot corrupt the mode" guarantee.
        /// </summary>
        [Test]
        public void Machine_AfterRejectedTransition_StillAcceptsLegalOne()
        {
            var machine = MachineAt(GameStateId.InGame);
            _log.Clear();

            Assert.IsFalse(machine.TryTransition(GameStateId.MainMenu).Success);
            Assert.AreEqual(GameStateId.InGame, machine.Current);

            var result = machine.TryTransition(GameStateId.Paused);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(GameStateId.Paused, machine.Current);
            Assert.AreEqual(1, _log.CountOf(LogCode.IllegalTransition), "Only the rejected attempt should have warned.");
        }

        /// <summary>
        /// A null log is documented as tolerated so the machine can be built before diagnostics exist.
        /// Exercise the path that would dereference it — the rejection path — rather than only construction.
        /// </summary>
        [Test]
        public void Machine_NullLog_RejectsWithoutThrowing()
        {
            var machine = new GameStateMachine(null, null);

            CommandResultHolder holder = null;
            Assert.DoesNotThrow(() => holder = new CommandResultHolder(machine.TryTransition(GameStateId.InGame)));

            Assert.IsNotNull(holder);
            Assert.IsFalse(holder.Value.Success);
            Assert.AreEqual(ResultCode.IllegalStateTransition, holder.Value.Code);
            Assert.AreEqual(GameStateId.Boot, machine.Current);
        }

        /// <summary>
        /// Drives a fresh machine to <paramref name="target"/> along a legal route.
        /// </summary>
        /// <remarks>
        /// Every step is asserted, so a fixture that cannot be arranged fails loudly here instead of
        /// producing a green test that ran against the wrong starting state.
        /// </remarks>
        private GameStateMachine MachineAt(GameStateId target)
        {
            var machine = new GameStateMachine(_log, null);

            if (target == GameStateId.Boot)
            {
                return machine;
            }

            Step(machine, GameStateId.MainMenu);
            if (target == GameStateId.MainMenu)
            {
                return machine;
            }

            Step(machine, GameStateId.Loading);
            if (target == GameStateId.Loading)
            {
                return machine;
            }

            if (target == GameStateId.LoadFailed)
            {
                Step(machine, GameStateId.LoadFailed);
                return machine;
            }

            Step(machine, GameStateId.InGame);
            if (target == GameStateId.InGame)
            {
                return machine;
            }

            Step(machine, GameStateId.Paused);
            Assert.AreEqual(GameStateId.Paused, machine.Current);
            return machine;
        }

        private static void Step(GameStateMachine machine, GameStateId to)
        {
            var result = machine.TryTransition(to);
            Assert.IsTrue(result.Success, "Fixture setup could not reach " + to + " from " + machine.Current);
        }

        /// <summary>
        /// Boxes a <c>CommandResult</c> so it can escape a lambda. <c>ref struct</c>-like capture rules
        /// do not apply here, but a readonly struct assigned inside <see cref="Assert.DoesNotThrow"/>
        /// still needs a reference cell to survive the call.
        /// </summary>
        private sealed class CommandResultHolder
        {
            public CommandResultHolder(ForgottenIsle.Core.Commands.CommandResult value)
            {
                Value = value;
            }

            public ForgottenIsle.Core.Commands.CommandResult Value { get; }
        }
    }
}
