using System;
using System.Globalization;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.Game.Progress;
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
        private readonly ProgressService _progress;
        private readonly InventoryService _inventory;
        private readonly RadioService _radio;
        private readonly FireService _fire;

        /// <param name="progress">Progression, wiped so a new run starts with nothing found.</param>
        public StartNewGameHandler(
            GameStateMachine states, SessionService session, ZoneRegistry zones, ProgressService progress,
            ICoreLog log, Func<int> seedSource = null, InventoryService inventory = null, RadioService radio = null,
            FireService fire = null)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _progress = progress;
            _inventory = inventory;
            _radio = radio;
            _fire = fire;
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

        /// <summary>
        /// Puts every run-scoped service back to the state a new run finds it in, and hands out
        /// what the player starts holding.
        /// </summary>
        /// <remarks>
        /// EVERY participant, not just progression. The first version reset only progress, so a
        /// new game after a finished run started with the previous run's pockets full and, later,
        /// its radio already repaired -- a save-shaped bug that no save file was involved in.
        /// Static and public so it can be pinned by a test without a zone registry or a scene.
        /// </remarks>
        public static void PrepareNewRun(ProgressService progress, InventoryService inventory, RadioService radio, FireService fire = null)
        {
            if (fire != null)
            {
                fire.ResetForNewRun();
            }

            if (progress != null)
            {
                progress.ResetForNewRun();
            }

            if (inventory != null)
            {
                inventory.ResetForNewRun();
                for (var i = 0; i < ItemIds.StartingKit.Length; i++)
                {
                    inventory.Take(ItemIds.StartingKit[i]);
                }
            }

            if (radio != null)
            {
                radio.ResetForNewRun();
            }
        }

        /// <inheritdoc />
        public void Execute(in StartNewGameCommand command)
        {
            var slot = command.Slot;
            _states.TryTransition(GameStateId.Loading);
            // A new run means nothing found. Wiped here rather than by the caller, so there is no
            // path that starts a run and leaves the previous run's discoveries standing.
            PrepareNewRun(_progress, _inventory, _radio, _fire);
            
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
            var header = SaveHeaders.Build(_session);

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
    /// Tears the run down, unloads its zones behind the loading curtain, and returns to the main menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS IS A TWO-STEP ROUTE. There is no <c>InGame -&gt; MainMenu</c> or
    /// <c>Paused -&gt; MainMenu</c> edge, and their absence is the design rather than an omission:
    /// quitting has to unload every resident zone, which takes frames, and a mode that said
    /// "MainMenu" while zone scenes were still being torn down would let the menu draw over a world
    /// that is visibly dissolving. So the quit route is
    /// <c>InGame|Paused -&gt; Loading -&gt; MainMenu</c>. <see cref="GameStateId.Loading"/> is where
    /// the curtain lives; the unload happens under it, and MainMenu is entered from the unload's
    /// completion, once there is nothing left resident.
    /// </para>
    /// <para>
    /// WHY MainMenu IS ENTERED EVEN WHEN AN UNLOAD REPORTS FAILURE: <see cref="ZoneRegistry"/>
    /// guarantees it ends up empty either way — a zone the loader gave up on is no longer the
    /// registry's to account for. Refusing to leave Loading on a failed unload would strand the
    /// player under a curtain with no way out, which is strictly worse than a logged warning and a
    /// menu.
    /// </para>
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

        /// <summary>
        /// Accepts from <see cref="GameStateId.InGame"/> and <see cref="GameStateId.Paused"/> only.
        /// </summary>
        /// <remarks>
        /// Those are the two modes that own a live run, and they are exactly the two with an edge into
        /// <see cref="GameStateId.Loading"/>, so acceptance here is a promise the route can be walked.
        /// Everything else is refused with <see cref="ResultCode.NotAllowedInState"/>: Boot and
        /// MainMenu have no run to quit, Loading is already mid-route, and
        /// <see cref="GameStateId.LoadFailed"/> is not this command's business — nothing was ever
        /// entered, so there is nothing to tear down, and its own edge straight to MainMenu is the
        /// failure screen's to take.
        /// </remarks>
        public ResultCode Validate(in QuitToMenuCommand command)
        {
            var current = _states.Current;
            if (current != GameStateId.InGame && current != GameStateId.Paused)
            {
                return ResultCode.NotAllowedInState;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in QuitToMenuCommand command)
        {
            // Step one: the curtain. This happens before anything is destroyed so that no frame is
            // ever presented showing a world mid-teardown.
            _states.TryTransition(GameStateId.Loading);

            // Then end the run, still before the scenes go, so nothing left alive in a dying zone can
            // observe a session that is half torn down, and so an in-flight autosave finds no run to
            // write rather than a partial one.
            _session.EndRun();

            // Step two: the menu, entered from the unload's completion rather than beside it. The
            // registry completes synchronously when nothing is resident, so a quit from a run whose
            // zones are already gone still lands in MainMenu within this call.
            _zones.UnloadAll(code =>
            {
                if (code != ResultCode.Ok && _log != null)
                {
                    _log.Warn(LogCode.SceneLoadSlow, "quit:" + code);
                }

                _states.TryTransition(GameStateId.MainMenu);
            });
        }
    }

    /// <summary>
    /// Loads a saved run out of a slot and takes the player back into the zone it was saved in —
    /// the main menu's "Continue".
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE CURTAIN GOES UP BEFORE THE SLOT IS READ, even though reading is the part that can
    /// fail: <see cref="GameStateId.LoadFailed"/> is reachable only from
    /// <see cref="GameStateId.Loading"/>. Reading first would mean a corrupt slot discovered while
    /// still in MainMenu had nowhere legal to go, and the handler would have to either strand the
    /// machine or widen the table. Entering Loading first gives every failure below a legal
    /// destination, and costs nothing on the happy path because Loading is where the resume was
    /// always going.
    /// </para>
    /// <para>
    /// WHY A FAILURE ENDS THE RUN: <c>SaveSlotService.Load</c> restores participants in order and
    /// stops at the first one that cannot make sense of its section, which leaves the session
    /// half-populated. Half a run is more dangerous than no run — it will autosave over the file it
    /// came from — so the failure path clears it before surfacing the failure screen.
    /// </para>
    /// </remarks>
    public sealed class ResumeSavedRunHandler : ICommandHandler<ResumeSavedRunCommand>
    {
        private readonly GameStateMachine _states;
        private readonly SaveSlotService _slots;
        private readonly SessionService _session;
        private readonly ZoneRegistry _zones;
        private readonly ICoreLog _log;

        /// <param name="states">The mode machine.</param>
        /// <param name="slots">Slot persistence, already holding the participant register.</param>
        /// <param name="session">The run being restored into.</param>
        /// <param name="zones">Zone residency.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public ResumeSavedRunHandler(
            GameStateMachine states,
            SaveSlotService slots,
            SessionService session,
            ZoneRegistry zones,
            ICoreLog log)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _log = log;
        }

        /// <summary>
        /// Accepts only from <see cref="GameStateId.MainMenu"/>, and only for a slot whose header can
        /// actually be read.
        /// </summary>
        /// <remarks>
        /// The header read is the cheapest honest test of "is there something here to continue" — it
        /// touches a bounded prefix of the file rather than the sections, and it is the same read the
        /// menu already did to label the Continue button. A slot that is empty and a slot whose header
        /// is damaged both answer <see cref="ResultCode.SlotEmpty"/>: from the player's side there is
        /// nothing to resume either way, and the corruption has already been logged once by the slot
        /// service, so reporting it a second time as a distinct code would only give the UI a second
        /// message to write for the same dead end.
        /// </remarks>
        public ResultCode Validate(in ResumeSavedRunCommand command)
        {
            if (!SaveSlotService.IsValidSlot(command.Slot))
            {
                // Not a slot at all. Distinct from SlotEmpty on purpose: an out-of-range index is a
                // caller bug, and answering "nothing saved there" would hide it behind a plausible
                // player-facing explanation.
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.MainMenu)
            {
                // Continue is a main-menu action. Resuming over a live run would restore participants
                // underneath a world that is already standing.
                return ResultCode.NotAllowedInState;
            }

            if (_zones.ResidentCount > 0)
            {
                // Menu mode with zones still resident means a previous run has not finished unloading.
                return ResultCode.NotAllowedInState;
            }

            SaveMetadata metadata;
            if (_slots.ReadMetadata(command.Slot, out metadata) != ResultCode.Ok || metadata == null)
            {
                return ResultCode.SlotEmpty;
            }

            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in ResumeSavedRunCommand command)
        {
            var slot = command.Slot;
            _states.TryTransition(GameStateId.Loading);

            var loadCode = _slots.Load(slot);
            if (loadCode != ResultCode.Ok)
            {
                Fail(LogCode.SaveCorrupt, "resume slot" + slot + ":" + loadCode);
                return;
            }

            // The zone comes from the restored session rather than from the header, because the
            // session is what the world will be built around and the two must not be allowed to
            // disagree. A save naming a zone this build no longer ships is a real possibility after a
            // content change, and it is caught here rather than inside the loader.
            var zone = _session.ZoneId;
            if (!SceneKeys.IsZone(zone))
            {
                Fail(LogCode.CatalogMissing, "resume slot" + slot + ":" + (string.IsNullOrEmpty(zone) ? "<none>" : zone));
                return;
            }

            _zones.EnterZone(zone, code =>
            {
                if (code == ResultCode.Ok)
                {
                    _session.SetZone(zone);
                    _states.TryTransition(GameStateId.InGame);
                    return;
                }

                Fail(LogCode.SceneLoadSlow, zone + ":" + code);
            });
        }

        /// <summary>
        /// Reports why the resume died, discards whatever was restored, and shows the failure screen.
        /// </summary>
        private void Fail(LogCode code, string detail)
        {
            if (_log != null)
            {
                _log.Warn(code, detail);
            }

            _session.EndRun();
            _states.TryTransition(GameStateId.LoadFailed);
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
        private readonly ProgressService _progress;

        /// <param name="progress">Progression, consulted to decide whether the destination is open.</param>
        public TravelToZoneHandler(GameStateMachine states, ZoneRegistry zones, SessionService session, SceneLoader loader, ProgressService progress, ICoreLog log)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _progress = progress;
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

            // Progression, not presentation, decides reachability. The gate object also hides its
            // TRAVEL verb while locked, but that is a courtesy -- this is the rule, and it holds for
            // a dev-overlay teleport or any future fast-travel just as much as for a gate.
            if (_progress != null && !_progress.IsZoneUnlocked(command.ZoneId))
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
                    // SetZone publishes ZoneChangedSignal, which ProgressService is subscribed to,
                    // so the objective follows the player without a second call here.
                    _session.SetZone(destination);

                    _states.TryTransition(GameStateId.InGame);
                    return;
                }

                if (_log != null)
                {
                    _log.Warn(LogCode.SceneLoadSlow, destination + ":" + code);
                }

                // The outgoing zone was hard-unloaded before this load began (ADR-0004), so a failure here
                // leaves a run whose recorded zone is no longer resident. Resuming it would place the
                // player in a scene that is not loaded, so discard the run rather than carry a pointer to
                // nothing into the failure screen.
                _session.EndRun();
                _states.TryTransition(GameStateId.LoadFailed);
            });
        }
    }

    /// <summary>
    /// Records a marker as read and narrates its line.
    /// </summary>
    /// <remarks>
    /// The narration key is derived from the content id (<c>narration.</c> + id) rather than looked
    /// up in a table. One naming rule means adding a marker is one id and one CSV row, with no third
    /// place to forget to update -- and the localization gate already fails the build on a key that
    /// has no row.
    /// <para>
    /// A marker can be read repeatedly. Only the first reading changes progression, but every
    /// reading shows the text, because a story beat the player missed while walking away should not
    /// be lost forever.
    /// </para>
    /// </remarks>
    public sealed class InspectHandler : ICommandHandler<InspectCommand>
    {
        /// <summary>Prefix that turns a content id into its narration localization key.</summary>
        public const string NarrationPrefix = "narration.";

        private readonly GameStateMachine _states;
        private readonly ProgressService _progress;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine; inspection is an in-world act only.</param>
        /// <param name="progress">Progression that records the reading.</param>
        /// <param name="signals">Bus the narration line is published on. Null tolerated.</param>
        public InspectHandler(GameStateMachine states, ProgressService progress, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in InspectCommand command)
        {
            if (string.IsNullOrEmpty(command.MarkerId))
            {
                return ResultCode.InvalidArgument;
            }

            // Only a remark may stand in for the marker's line. Anything else here is a typo that
            // would render as #narration.something# on the one occasion it fires.
            if (!string.IsNullOrEmpty(command.SaidAs) && ContentIds.KindOf(command.SaidAs) != ContentKind.Remark)
            {
                return ResultCode.InvalidArgument;
            }

            return _states.Current == GameStateId.InGame
                ? ResultCode.Ok
                : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in InspectCommand command)
        {
            _progress.Inspect(command.MarkerId);

            if (_signals != null)
            {
                var said = string.IsNullOrEmpty(command.SaidAs) ? command.MarkerId : command.SaidAs;
                _signals.Publish(new NarrationSignal(NarrationPrefix + said));
            }
        }
    }

    /// <summary>
    /// Takes a discovery: records it, narrates it, and applies whatever it unlocks.
    /// </summary>
    /// <remarks>
    /// Validation refuses an already-collected id rather than letting Execute no-op, so a double
    /// tap on the same pickup returns a reason instead of silently doing nothing -- and the
    /// interaction system logs that reason.
    /// </remarks>
    public sealed class CollectHandler : ICommandHandler<CollectCommand>
    {
        private readonly GameStateMachine _states;
        private readonly ProgressService _progress;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine; collecting is an in-world act only.</param>
        /// <param name="progress">Progression that records the pickup and its unlock.</param>
        /// <param name="signals">Bus the narration line is published on. Null tolerated.</param>
        private readonly InventoryService _inventory;

        /// <param name="inventory">Receives the item a discovery also is. Null grants nothing.</param>
        public CollectHandler(GameStateMachine states, ProgressService progress, SignalBus signals, InventoryService inventory = null)
        {
            _inventory = inventory;
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in CollectCommand command)
        {
            if (string.IsNullOrEmpty(command.DiscoveryId))
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            // Taking the same thing twice is the bug this guard exists to make impossible at the
            // command layer, not merely unlikely at the view layer.
            return _progress.HasCollected(command.DiscoveryId)
                ? ResultCode.NotAllowedInState
                : ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in CollectCommand command)
        {
            _progress.Collect(command.DiscoveryId);

            // Some discoveries are also objects. The reel is recorded AND carried; without this
            // the combination chain that starts with it could never start.
            string itemId;
            if (_inventory != null && DiscoveryItems.TryItemFor(command.DiscoveryId, out itemId))
            {
                _inventory.Take(itemId);
            }

            if (_signals != null)
            {
                _signals.Publish(new NarrationSignal(InspectHandler.NarrationPrefix + command.DiscoveryId));
            }
        }
    }

    /// <summary>Puts a found object in the player's hands.</summary>
    public sealed class TakeItemHandler : ICommandHandler<TakeItemCommand>
    {
        private readonly GameStateMachine _states;
        private readonly InventoryService _inventory;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine; taking is an in-world act only.</param>
        /// <param name="inventory">Where the item goes.</param>
        /// <param name="signals">Bus the narration line is published on. Null tolerated.</param>
        public TakeItemHandler(GameStateMachine states, InventoryService inventory, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in TakeItemCommand command)
        {
            if (string.IsNullOrEmpty(command.ItemId))
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            // Already carried. Refused with a reason rather than silently doing nothing, so a
            // double tap on a pickup reads as "you have this" instead of as a dead button.
            return _inventory.Has(command.ItemId) ? ResultCode.NotAllowedInState : ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in TakeItemCommand command)
        {
            if (!_inventory.Take(command.ItemId) || _signals == null)
            {
                return;
            }

            _signals.Publish(new NarrationSignal(InspectHandler.NarrationPrefix + command.ItemId));
        }
    }

    /// <summary>Puts two carried items together.</summary>
    public sealed class CombineItemsHandler : ICommandHandler<CombineItemsCommand>
    {
        /// <summary>Key of the line shown when two items do not go together.</summary>
        public const string NoCombinationKey = "narration.combine.nothing";

        private readonly GameStateMachine _states;
        private readonly InventoryService _inventory;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="inventory">Holds both inputs and receives the result.</param>
        /// <param name="signals">Bus the narration line is published on. Null tolerated.</param>
        public CombineItemsHandler(GameStateMachine states, InventoryService inventory, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in CombineItemsCommand command)
        {
            if (string.IsNullOrEmpty(command.First) || string.IsNullOrEmpty(command.Second)
                || command.First == command.Second)
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _inventory.Has(command.First) && _inventory.Has(command.Second)
                ? ResultCode.Ok
                : ResultCode.InvalidArgument;
        }

        /// <inheritdoc />
        public void Execute(in CombineItemsCommand command)
        {
            string result;
            var combined = _inventory.Combine(command.First, command.Second, out result);

            if (_signals == null)
            {
                return;
            }

            // A wrong pairing still answers. Silence is the one response an adventure game must
            // never give: the player cannot tell it apart from a broken control, and starts
            // distrusting every combination they have not already seen work.
            _signals.Publish(new NarrationSignal(
                combined ? InspectHandler.NarrationPrefix + result : NoCombinationKey));
        }
    }

    /// <summary>Uses a carried item on something in the world.</summary>
    /// <remarks>
    /// The handler owns no knowledge of which item fits which target — that is the target's, and it
    /// answers through <see cref="UseOutcome"/>. This class only enforces that the player is holding
    /// the thing they claim to be holding, and consumes it when the target says it was used up.
    /// </remarks>
    public sealed class UseItemHandler : ICommandHandler<UseItemCommand>
    {
        /// <summary>Key of the line shown when an item does nothing to a target.</summary>
        public const string NoEffectKey = "narration.use.nothing";

        private readonly GameStateMachine _states;
        private readonly InventoryService _inventory;
        private readonly IUseTargetResolver _targets;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="inventory">Proves the item is carried, and consumes it when spent.</param>
        /// <param name="targets">Resolves what the target does with the item. Null means nothing does.</param>
        /// <param name="signals">Bus the narration line is published on. Null tolerated.</param>
        public UseItemHandler(
            GameStateMachine states,
            InventoryService inventory,
            IUseTargetResolver targets,
            SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _targets = targets;
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in UseItemCommand command)
        {
            if (string.IsNullOrEmpty(command.ItemId) || string.IsNullOrEmpty(command.TargetId))
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _inventory.Has(command.ItemId) ? ResultCode.Ok : ResultCode.InvalidArgument;
        }

        /// <inheritdoc />
        public void Execute(in UseItemCommand command)
        {
            var outcome = _targets != null
                ? _targets.Use(command.ItemId, command.TargetId)
                : UseOutcome.Nothing;

            if (outcome.ConsumesItem)
            {
                _inventory.Consume(command.ItemId);
            }

            if (_signals == null)
            {
                return;
            }

            // A refusal always answers -- silence is indistinguishable from a broken control. A
            // success with no line means the target has already spoken for itself (the radio
            // publishes a two-line sequence when it powers up), and saying "that does nothing"
            // over the top of it would be a lie.
            if (!string.IsNullOrEmpty(outcome.NarrationKey))
            {
                _signals.Publish(new NarrationSignal(outcome.NarrationKey));
            }
            else if (!outcome.Succeeded)
            {
                _signals.Publish(new NarrationSignal(NoEffectKey));
            }
        }
    }

    /// <summary>Reaches for the radio: a diagnosis while it is broken, the dial once it works.</summary>
    public sealed class OpenRadioHandler : ICommandHandler<OpenRadioCommand>
    {
        private readonly GameStateMachine _states;
        private readonly RadioService _radio;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="radio">The set.</param>
        /// <param name="signals">Bus the diagnosis line goes out on. Null tolerated.</param>
        public OpenRadioHandler(GameStateMachine states, RadioService radio, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in OpenRadioCommand command)
        {
            return _states.Current == GameStateId.InGame ? ResultCode.Ok : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in OpenRadioCommand command)
        {
            if (_radio.IsWorking)
            {
                _radio.Open();
                return;
            }

            // Broken: the act is an inspection, and the inspection is the clue. One fault at a
            // time, in the order a person opening the set would meet them.
            var line = _radio.Inspect();
            if (_signals != null)
            {
                _signals.Publish(new NarrationSignal(line));
            }
        }
    }

    /// <summary>Puts the radio down.</summary>
    public sealed class CloseRadioHandler : ICommandHandler<CloseRadioCommand>
    {
        private readonly RadioService _radio;

        /// <param name="radio">The set.</param>
        public CloseRadioHandler(RadioService radio)
        {
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
        }

        /// <inheritdoc />
        public ResultCode Validate(in CloseRadioCommand command)
        {
            // Closing is allowed from any state: a pause with the dial open must be able to put
            // it away, and there is no state in which leaving it up is the right answer.
            return ResultCode.Ok;
        }

        /// <inheritdoc />
        public void Execute(in CloseRadioCommand command)
        {
            _radio.Close();
        }
    }

    /// <summary>Moves the needle.</summary>
    /// <remarks>
    /// Validation is by the set's state, not the requested number: any float is a legal request
    /// and the service clamps it to the band. A dial that refuses out-of-range drags would stop
    /// dead at the end stop instead of resting against it, which is not how a dial feels.
    /// </remarks>
    public sealed class TuneRadioHandler : ICommandHandler<TuneRadioCommand>
    {
        private readonly GameStateMachine _states;
        private readonly RadioService _radio;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="radio">The set.</param>
        /// <param name="signals">Bus a first hearing's line goes out on. Null tolerated.</param>
        public TuneRadioHandler(GameStateMachine states, RadioService radio, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in TuneRadioCommand command)
        {
            if (float.IsNaN(command.Mhz) || float.IsInfinity(command.Mhz))
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _radio.IsWorking && _radio.IsOpen ? ResultCode.Ok : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in TuneRadioCommand command)
        {
            var heardBefore = _radio.Heard.Count;
            _radio.Tune(command.Mhz);
            AnnounceNewHearing(_radio, _signals, heardBefore);
        }

        /// <summary>
        /// Narrates a station heard for the first time by this move, if there was one. Shared
        /// with the self-sweep so a lock found by the set alone is told exactly as a hand-found one.
        /// </summary>
        /// <param name="radio">The set, after the move.</param>
        /// <param name="signals">Bus the line goes out on. Null tolerated.</param>
        /// <param name="heardBefore">How many stations were heard before the move.</param>
        internal static void AnnounceNewHearing(RadioService radio, SignalBus signals, int heardBefore)
        {
            if (signals == null || radio.Heard.Count <= heardBefore)
            {
                return;
            }

            var station = radio.Heard[radio.Heard.Count - 1];
            if (station != Stations.TheVoice)
            {
                // A false positive is one line: the hull, or a forecast for somewhere else.
                signals.Publish(new NarrationSignal("narration." + station));
                return;
            }

            // THE TRANSMISSION. The first hearing, the forty-four seconds of her reading the list,
            // and Nadia working it out -- composed here, in the game, because a story beat is
            // content and content is not the HUD's to assemble. The HUD only paces it.
            signals.Publish(new NarrationSequenceSignal(TransmissionKeys));
        }

        /// <summary>The voice, in order: first hearing, five lines of transmission, four of deduction.</summary>
        private static readonly string[] TransmissionKeys =
        {
            "narration.radio.the_voice",
            "narration.radio.transmission.1",
            "narration.radio.transmission.2",
            "narration.radio.transmission.3",
            "narration.radio.transmission.4",
            "narration.radio.transmission.5",
            "narration.radio.after.1",
            "narration.radio.after.2",
            "narration.radio.after.3",
            "narration.radio.after.4"
        };
    }

    /// <summary>Squeezes the hand-mic.</summary>
    public sealed class SqueezeMicHandler : ICommandHandler<SqueezeMicCommand>
    {
        private readonly GameStateMachine _states;
        private readonly RadioService _radio;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="radio">The set.</param>
        /// <param name="signals">Bus the line goes out on. Null tolerated.</param>
        public SqueezeMicHandler(GameStateMachine states, RadioService radio, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in SqueezeMicCommand command)
        {
            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _radio.IsWorking && _radio.IsOpen ? ResultCode.Ok : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in SqueezeMicCommand command)
        {
            var line = _radio.SqueezeMic();
            if (_signals != null)
            {
                _signals.Publish(new NarrationSignal(line));
            }
        }
    }

    /// <summary>Holds an item up for use, or puts it down.</summary>
    public sealed class HoldItemHandler : ICommandHandler<HoldItemCommand>
    {
        private readonly GameStateMachine _states;
        private readonly InventoryService _inventory;

        /// <param name="states">Mode machine.</param>
        /// <param name="inventory">What is carried.</param>
        public HoldItemHandler(GameStateMachine states, InventoryService inventory)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        /// <inheritdoc />
        public ResultCode Validate(in HoldItemCommand command)
        {
            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return string.IsNullOrEmpty(command.ItemId) || _inventory.Has(command.ItemId)
                ? ResultCode.Ok
                : ResultCode.InvalidArgument;
        }

        /// <inheritdoc />
        public void Execute(in HoldItemCommand command)
        {
            if (string.IsNullOrEmpty(command.ItemId))
            {
                _inventory.Release();
                return;
            }

            _inventory.Hold(command.ItemId);
        }
    }

    /// <summary>
    /// Leaves the set on and sweeping: opens the dial if it is down, then starts the needle.
    /// </summary>
    /// <remarks>
    /// Refused when the set does not work or the transmission is already received. The dial is
    /// opened here rather than by a second command from the director because "the spectrogram is
    /// live" is part of what tier 4 is, not a separate act.
    /// </remarks>
    public sealed class BeginSweepHandler : ICommandHandler<BeginSweepCommand>
    {
        private readonly GameStateMachine _states;
        private readonly RadioService _radio;

        /// <param name="states">Mode machine.</param>
        /// <param name="radio">The set.</param>
        public BeginSweepHandler(GameStateMachine states, RadioService radio)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
        }

        /// <inheritdoc />
        public ResultCode Validate(in BeginSweepCommand command)
        {
            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _radio.IsWorking && !_radio.TransmissionReceived ? ResultCode.Ok : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in BeginSweepCommand command)
        {
            if (!_radio.IsOpen)
            {
                _radio.Open();
            }

            _radio.BeginSweep();
        }
    }

    /// <summary>
    /// Steps the self-sweeping needle. Refused when nothing is sweeping, so a stale tick after a
    /// hand has stopped it moves nothing.
    /// </summary>
    public sealed class SweepRadioHandler : ICommandHandler<SweepRadioCommand>
    {
        private readonly GameStateMachine _states;
        private readonly RadioService _radio;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine.</param>
        /// <param name="radio">The set.</param>
        /// <param name="signals">Bus a first hearing's line goes out on. Null tolerated.</param>
        public SweepRadioHandler(GameStateMachine states, RadioService radio, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in SweepRadioCommand command)
        {
            if (!(command.Seconds > 0f) || float.IsInfinity(command.Seconds))
            {
                return ResultCode.InvalidArgument;
            }

            if (_states.Current != GameStateId.InGame)
            {
                return ResultCode.NotAllowedInState;
            }

            return _radio.IsSweeping ? ResultCode.Ok : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in SweepRadioCommand command)
        {
            var heardBefore = _radio.Heard.Count;
            _radio.Sweep(command.Seconds);
            TuneRadioHandler.AnnounceNewHearing(_radio, _signals, heardBefore);
        }
    }

    /// <summary>
    /// Nadia carries the fire kit from the open into the lee of the hull. The last fire hint.
    /// </summary>
    /// <remarks>
    /// Refused when nothing is laid in the open or a fire already burns: the hint director asks
    /// for this on the seventh blow-out and the refusal simply means there is nothing to carry.
    /// The strike is still the player's; this only moves the kit.
    /// </remarks>
    public sealed class CarryFireKitHandler : ICommandHandler<CarryFireKitCommand>
    {
        private readonly GameStateMachine _states;
        private readonly FireService _fire;

        /// <param name="states">Mode machine.</param>
        /// <param name="fire">The fire sites.</param>
        public CarryFireKitHandler(GameStateMachine states, FireService fire)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _fire = fire ?? throw new ArgumentNullException(nameof(fire));
        }

        /// <inheritdoc />
        public ResultCode Validate(in CarryFireKitCommand command)
        {
            if (_states.Current != GameStateId.InGame || _fire.IsLit)
            {
                return ResultCode.NotAllowedInState;
            }

            for (var i = 0; i < _fire.Sites.Count; i++)
            {
                if (!_fire.Sites[i].InLee && _fire.Sites[i].HasKit)
                {
                    return ResultCode.Ok;
                }
            }

            return ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in CarryFireKitCommand command)
        {
            _fire.CarryKitToLee();
        }
    }

    /// <summary>
    /// Says a line in answer to an act, and changes nothing.
    /// </summary>
    /// <remarks>
    /// A remark is refused outside gameplay for the same reason an inspection is: Nadia does not
    /// talk over the pause screen. It touches no progression, so a remark can never credit a
    /// discovery the player has not made -- which is the whole reason it is a separate command
    /// from <see cref="InspectCommand"/> rather than a flag on it.
    /// </remarks>
    public sealed class RemarkHandler : ICommandHandler<RemarkCommand>
    {
        private readonly GameStateMachine _states;
        private readonly SignalBus _signals;

        /// <param name="states">Mode machine; a remark is an in-world act only.</param>
        /// <param name="signals">Bus the line is published on. Null tolerated.</param>
        public RemarkHandler(GameStateMachine states, SignalBus signals)
        {
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _signals = signals;
        }

        /// <inheritdoc />
        public ResultCode Validate(in RemarkCommand command)
        {
            if (string.IsNullOrEmpty(command.RemarkId) || ContentIds.KindOf(command.RemarkId) != ContentKind.Remark)
            {
                return ResultCode.InvalidArgument;
            }

            return _states.Current == GameStateId.InGame
                ? ResultCode.Ok
                : ResultCode.NotAllowedInState;
        }

        /// <inheritdoc />
        public void Execute(in RemarkCommand command)
        {
            if (_signals == null)
            {
                return;
            }

            var lines = ContentIds.RemarkLines(command.RemarkId);
            if (lines <= 1)
            {
                _signals.Publish(new NarrationSignal(InspectHandler.NarrationPrefix + command.RemarkId));
                return;
            }

            // A remark of several lines is said as a sequence, keyed ".1", ".2" ... so each line
            // gets its own time on screen rather than one long card.
            var keys = new string[lines];
            for (var i = 0; i < lines; i++)
            {
                keys[i] = InspectHandler.NarrationPrefix + command.RemarkId + "." + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            _signals.Publish(new NarrationSequenceSignal(keys));
        }
    }
}
