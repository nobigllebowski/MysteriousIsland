using System;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// Begins a new run in a chosen save slot and takes the player through the curtain into the
    /// opening zone.
    /// </summary>
    /// <remarks>
    /// WHY the zone load is not awaited: <see cref="ICommandHandler{TCommand}.Execute"/> is void and
    /// synchronous by design, because a command that could block would make every caller — including
    /// UI button handlers — reentrancy-sensitive. The handler instead moves the machine into
    /// <see cref="GameStateId.Loading"/> synchronously, which is the visible, testable effect, and
    /// lets the load's completion callback decide between <see cref="GameStateId.InGame"/> and
    /// <see cref="GameStateId.LoadFailed"/>. Both destinations are legal from Loading, so there is no
    /// outcome that strands the machine.
    /// </remarks>
    public sealed class StartNewGameHandler : ICommandHandler<StartNewGameCommand>
    {
        private readonly GameStateMachine _states;
        private readonly SessionService _session;
        private readonly ZoneRegistry _zones;
        private readonly ICoreLog _log;
        private readonly Func<int> _seedSource;

        /// <param name="states">The mode machine.</param>
        /// <param name="session">The run owner.</param>
        /// <param name="zones">Zone residency.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        /// <param name="seedSource">
        /// Supplies the world seed. Null falls back to <see cref="Environment.TickCount"/>. Injected so
        /// a determinism test can pin the seed without the handler knowing it is under test.
        /// </param>
        public StartNewGameHandler(GameStateMachine states, SessionService session, ZoneRegistry zones, ICoreLog log, Func<int> seedSource = null)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _log = log;
            _seedSource = seedSource;
        }

        /// <summary>The zone every new run opens in.</summary>
        public const string StartZone = SceneKeys.ZoneRibcage;

        /// <inheritdoc />
        public ResultCode Validate(in StartNewGameCommand command)
        {
            if (command.Slot < 0 || command.Slot >= SaveSlotService.SlotCount)
            {
                // A new run must bind to a manual slot. Autosave ring slots are written by the autosave
                // path, never chosen by the player, so they are not valid here even though the slot
                // service would accept them.
                return ResultCode.InvalidArgument;
            }

            if (!_states.CanTransition(GameStateId.Loading))
            {
                return ResultCode.NotAllowedInState;
            }

            if (_zones.ResidentCount > 0)
            {
                // A run is still resident. Starting a second one over the top would leave the first
                // one's scenes loaded with nothing tracking them.
                return ResultCode.NotAllowedInState;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in StartNewGameCommand command)
        {
            var slot = command.Slot;
            _states.TryTransition(GameStateId.Loading);
            _session.BeginNewRun(slot, SessionService.DefaultActId, StartZone, _seedSource != null ? _seedSource() : Environment.TickCount);

            _zones.EnterZone(StartZone, code =>
            {
                if (code == ResultCode.Ok)
                {
                    _session.SetZone(StartZone);
                    _states.TryTransition(GameStateId.InGame);
                    return;
                }

                if (_log != null)
                {
                    _log.Warn(LogCode.SceneLoadSlow, StartZone + ":" + code);
                }

                _states.TryTransition(GameStateId.LoadFailed);
            });
        }
    }

    /// <summary>
    /// Stamps a header for the current run and asks the slot service to write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the participants are not walked here: <c>SaveSlotService</c> keeps the participant register
    /// (they are added to it in <see cref="AppCompositionRoot"/>) and its <c>Save</c> overload stamps
    /// the header, captures every participant in registration order, and fails the whole save if any
    /// capture throws. Duplicating that here would give the project two capture orders that could
    /// silently diverge — and a save missing a section is not a partial save, it is a save that loads
    /// into an empty world.
    /// </para>
    /// <para>
    /// WHY the write failure is reported by log rather than by result code: the validate/execute split
    /// guarantees that a rejected command changes nothing, which means validation can only check facts
    /// that are knowable before any work happens. Whether a file write will succeed is not one of
    /// them — the disk fills between the check and the write. The command therefore reports what it
    /// could know, and the save layer's own <c>.bak</c> rotation is what makes a failed write
    /// survivable rather than fatal.
    /// </para>
    /// </remarks>
    public sealed class SaveGameHandler : ICommandHandler<SaveGameCommand>
    {
        private readonly SaveSlotService _slots;
        private readonly SessionService _session;
        private readonly GameStateMachine _states;
        private readonly SignalBus _signals;
        private readonly ICoreLog _log;

        /// <param name="slots">Slot persistence, already holding the participant register.</param>
        /// <param name="session">The run being written.</param>
        /// <param name="states">The mode machine, consulted for whether saving is allowed now.</param>
        /// <param name="signals">Bus for <see cref="GameSavedSignal"/>. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public SaveGameHandler(
            SaveSlotService slots,
            SessionService session,
            GameStateMachine states,
            SignalBus signals,
            ICoreLog log)
        {
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _signals = signals;
            _log = log;
        }

        /// <inheritdoc />
        public ResultCode Validate(in SaveGameCommand command)
        {
            if (!SaveSlotService.IsValidSlot(command.Slot))
            {
                return ResultCode.InvalidArgument;
            }

            // InGame and Paused, and nothing else. Paused is included deliberately and is not a
            // loophole: the save button lives in the pause menu, so rejecting Paused would reject every
            // save the player can actually reach. Every other mode is refused — there is no run to
            // write in Boot, MainMenu or LoadFailed, and a save taken mid-Loading would capture a
            // session whose zone is half-swapped.
            var current = _states.Current;
            if (current != GameStateId.InGame && current != GameStateId.Paused)
            {
                return ResultCode.NotAllowedInState;
            }

            if (!_session.HasRun)
            {
                return ResultCode.NotAllowedInState;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in SaveGameCommand command)
        {
            var slot = command.Slot;

            // Only the run's own facts are filled in. Slot, timestamp, build version and schema version
            // are stamped by the slot service over a copy of this header, so setting them here would
            // create a second opinion about what was just saved.
            var header = new SaveMetadata();
            header.ActId = _session.ActId;
            header.ZoneId = _session.ZoneId;
            header.ZoneDisplayKey = SceneKeys.ZoneDisplayKey(_session.ZoneId);
            header.PlaytimeSeconds = _session.PlaytimeSeconds;
            header.RecordedPercent = _session.RecordedPercent;

            var code = _slots.Save(slot, header);
            if (code != ResultCode.Ok)
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.SaveCorrupt, "slot" + slot + ":" + code);
                }

                return;
            }

            if (_signals != null)
            {
                _signals.Publish(new GameSavedSignal(slot));
            }
        }
    }

    /// <summary>
    /// Tears the run down, unloads its zones and returns to the main menu.
    /// </summary>
    /// <remarks>
    /// ⚠ VERIFY — CONTRACT DEFECT, NOT AN IMPLEMENTATION GAP. The legal transition table fixed by the
    /// architecture contract contains no edge from <see cref="GameStateId.InGame"/> or
    /// <see cref="GameStateId.Paused"/> to <see cref="GameStateId.MainMenu"/>; the only edge into
    /// MainMenu is from <see cref="GameStateId.LoadFailed"/>. As specified, therefore, this command
    /// can never succeed from a live run, and <see cref="Validate"/> below correctly refuses it with
    /// <see cref="ResultCode.IllegalStateTransition"/>. The table was not widened here because it is
    /// binding and a silent extra edge is exactly the kind of drift the table exists to prevent. The
    /// owner of <see cref="GameStateMachine"/> must add <c>Paused -&gt; MainMenu</c> (and, if quitting
    /// without pausing is wanted, <c>InGame -&gt; MainMenu</c>) before the pause menu ships. The
    /// moment that edge exists this handler works unchanged.
    /// </remarks>
    public sealed class QuitToMenuHandler : ICommandHandler<QuitToMenuCommand>
    {
        private readonly GameStateMachine _states;
        private readonly ZoneRegistry _zones;
        private readonly SessionService _session;
        private readonly ICoreLog _log;

        /// <param name="states">The mode machine.</param>
        /// <param name="zones">Zone residency, emptied on the way out.</param>
        /// <param name="session">The run to end.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public QuitToMenuHandler(GameStateMachine states, ZoneRegistry zones, SessionService session, ICoreLog log)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log;
        }

        /// <inheritdoc />
        public ResultCode Validate(in QuitToMenuCommand command)
        {
            var current = _states.Current;
            if (current != GameStateId.InGame && current != GameStateId.Paused && current != GameStateId.LoadFailed)
            {
                return ResultCode.NotAllowedInState;
            }

            if (!_states.CanTransition(GameStateId.MainMenu))
            {
                return ResultCode.IllegalStateTransition;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in QuitToMenuCommand command)
        {
            // Order matters: end the run before the scenes go, so nothing left in a dying zone can
            // observe a session that is half torn down, and so an in-flight autosave finds no run to
            // write rather than a partial one.
            _session.EndRun();
            _states.TryTransition(GameStateId.MainMenu);

            _zones.UnloadAll(code =>
            {
                if (code != ResultCode.Ok && _log != null)
                {
                    _log.Warn(LogCode.SceneLoadSlow, "quit:" + code);
                }
            });
        }
    }

    /// <summary>
    /// Moves the player to another zone through the loading curtain.
    /// </summary>
    public sealed class TravelToZoneHandler : ICommandHandler<TravelToZoneCommand>
    {
        private readonly GameStateMachine _states;
        private readonly ZoneRegistry _zones;
        private readonly SessionService _session;
        private readonly SceneLoader _loader;
        private readonly ICoreLog _log;

        /// <param name="states">The mode machine.</param>
        /// <param name="zones">Zone residency and the two-zone cap.</param>
        /// <param name="session">The run whose zone is being changed.</param>
        /// <param name="loader">Consulted only to reject travel while another load is in flight.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public TravelToZoneHandler(GameStateMachine states, ZoneRegistry zones, SessionService session, SceneLoader loader, ICoreLog log)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _log = log;
        }

        /// <inheritdoc />
        public ResultCode Validate(in TravelToZoneCommand command)
        {
            if (string.IsNullOrEmpty(command.ZoneId))
            {
                return ResultCode.InvalidArgument;
            }

            if (!SceneKeys.IsZone(command.ZoneId))
            {
                // Names a scene that is not a zone, or nothing at all. Both would otherwise reach the
                // loader, and loading the menu scene additively on top of a run is not recoverable.
                return ResultCode.SceneNotFound;
            }

            if (!_session.HasRun)
            {
                return ResultCode.NotAllowedInState;
            }

            if (string.Equals(_session.ZoneId, command.ZoneId, StringComparison.Ordinal))
            {
                // Already there. Refused rather than silently succeeding, because the caller asking to
                // travel to the zone it is standing in is a bug in the caller, and a curtain that goes
                // down and back up for no reason reads as a stutter.
                return ResultCode.InvalidArgument;
            }

            if (_loader.IsLoading)
            {
                return ResultCode.AlreadyLoading;
            }

            if (!_states.CanTransition(GameStateId.Loading))
            {
                return ResultCode.NotAllowedInState;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in TravelToZoneCommand command)
        {
            var destination = command.ZoneId;
            _states.TryTransition(GameStateId.Loading);

            _zones.EnterZone(destination, code =>
            {
                if (code == ResultCode.Ok)
                {
                    _session.SetZone(destination);
                    _states.TryTransition(GameStateId.InGame);
                    return;
                }

                if (_log != null)
                {
                    _log.Warn(LogCode.SceneLoadSlow, destination + ":" + code);
                }

                _states.TryTransition(GameStateId.LoadFailed);
            });
        }
    }
}
