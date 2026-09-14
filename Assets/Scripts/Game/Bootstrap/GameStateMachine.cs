using System;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// The application's coarse mode — menu, loading, playing, paused — guarded by an explicit
    /// table of the transitions that are allowed to happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY a table rather than a switch or a set of booleans: every illegal transition in a game
    /// like this one is a bug that presents as something else entirely — input accepted during a
    /// load, a save written against a half-torn-down run, a pause menu over the main menu. A
    /// declarative table makes the legal set reviewable in one glance and makes the illegal set
    /// impossible to reach by accident, rather than impossible to reach by discipline.
    /// </para>
    /// <para>
    /// WHY a rejected transition does not throw: transition requests arrive from UI buttons and
    /// from async load completions, both of which can race with a state the player changed a frame
    /// earlier. That race is routine, not exceptional. It is reported as a
    /// <see cref="ResultCode.IllegalStateTransition"/> and logged as
    /// <see cref="LogCode.IllegalTransition"/>, and — this is the load-bearing part — the machine
    /// is left exactly as it was, so a caller that ignores the result cannot corrupt the mode.
    /// </para>
    /// </remarks>
    public sealed class GameStateMachine
    {
        /// <summary>
        /// The complete legal transition set. Nothing outside this array is reachable.
        /// </summary>
        /// <remarks>
        /// Deliberately absent, and worth naming because their absence looks like an oversight:
        /// there is no self-transition (re-entering the state you are already in is a caller bug,
        /// not a no-op), and there is no edge back to <see cref="GameStateId.Boot"/> (boot happens
        /// once per process; a second boot is a new process).
        /// </remarks>
        private static readonly (GameStateId From, GameStateId To)[] LegalTransitions =
        {
            (GameStateId.Boot, GameStateId.MainMenu),
            (GameStateId.MainMenu, GameStateId.Loading),
            (GameStateId.Loading, GameStateId.InGame),
            (GameStateId.Loading, GameStateId.LoadFailed),
            (GameStateId.LoadFailed, GameStateId.MainMenu),
            (GameStateId.InGame, GameStateId.Paused),
            (GameStateId.Paused, GameStateId.InGame),
            (GameStateId.Paused, GameStateId.Loading),
            (GameStateId.InGame, GameStateId.Loading)
        };

        private readonly ICoreLog _log;
        private readonly SignalBus _signals;

        private GameStateId _current;

        /// <summary>
        /// Creates a machine sitting in <see cref="GameStateId.Boot"/>.
        /// </summary>
        /// <param name="log">
        /// Diagnostics sink for rejected transitions. Null is tolerated: a missing warning is a
        /// smaller loss than a composition root that fails to build.
        /// </param>
        /// <param name="signals">
        /// Bus on which <see cref="GameStateChangedSignal"/> is published. Null is tolerated so the
        /// machine can be exercised in isolation by an EditMode test with no bus around it.
        /// </param>
        public GameStateMachine(ICoreLog log, SignalBus signals)
        {
            _log = log;
            _signals = signals;
            _current = GameStateId.Boot;
        }

        /// <summary>The mode the application is in right now.</summary>
        public GameStateId Current => _current;

        /// <summary>
        /// Raised after the state has already changed, with (from, to).
        /// </summary>
        /// <remarks>
        /// This exists alongside <see cref="GameStateChangedSignal"/> on purpose. The event is for
        /// the few objects that are wired to the machine directly and must react synchronously and
        /// in a known order — the ticker, the overlay. The signal is for everything that merely
        /// wants to know. Handlers here run inside the transition, so they must not transition
        /// again; re-entrancy is rejected by the table (the machine has already moved) rather than
        /// by a guard flag.
        /// </remarks>
        public event Action<GameStateId, GameStateId> Changed;

        /// <summary>
        /// True when moving from <see cref="Current"/> to <paramref name="to"/> is in the table.
        /// </summary>
        /// <remarks>
        /// Command handlers call this in <c>Validate</c> so that a command which cannot possibly
        /// succeed is rejected before it executes any of its other side effects. Asking is always
        /// cheaper than repairing.
        /// </remarks>
        public bool CanTransition(GameStateId to)
        {
            return IsLegal(_current, to);
        }

        /// <summary>
        /// Moves to <paramref name="to"/> if the table allows it.
        /// </summary>
        /// <returns>
        /// <see cref="CommandResult.Ok"/> on success; a failure carrying
        /// <see cref="ResultCode.IllegalStateTransition"/> otherwise, with the state unchanged.
        /// </returns>
        public CommandResult TryTransition(GameStateId to)
        {
            if (!IsLegal(_current, to))
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.IllegalTransition, _current + "->" + to);
                }

                return CommandResult.Fail(ResultCode.IllegalStateTransition);
            }

            var from = _current;
            _current = to;

            var changed = Changed;
            if (changed != null)
            {
                changed(from, to);
            }

            if (_signals != null)
            {
                _signals.Publish(new GameStateChangedSignal(from, to));
            }

            return CommandResult.Ok;
        }

        private static bool IsLegal(GameStateId from, GameStateId to)
        {
            for (var i = 0; i < LegalTransitions.Length; i++)
            {
                if (LegalTransitions[i].From == from && LegalTransitions[i].To == to)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
