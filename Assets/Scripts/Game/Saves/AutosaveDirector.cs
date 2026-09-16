using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Saves
{
    /// <summary>
    /// Writes the autosave ring at the moments the record must not be lost.
    /// </summary>
    /// <remarks>
    /// The ring existed since Phase 1 and nothing ever wrote it: CONTINUE read a slot that was
    /// never filled. This is the writer. It fires on the beats the design names as unlosable --
    /// arriving in a zone, hearing the voice, making a machine work -- and only while a run is in
    /// the world. It is not on a timer: a save every N seconds is a save mid-fall or mid-menu, and
    /// this project has already learned what a save taken at a bad moment costs. The write itself
    /// happens on the next tick, after the command that raised the beat has completed.
    /// <para>
    /// Not a save participant itself; it asks the slot service to capture the participants that
    /// are, exactly as the manual save does, and builds the same header.
    /// </para>
    /// </remarks>
    public sealed class AutosaveDirector : IDisposable
    {
        private readonly SaveSlotService _slots;
        private readonly SessionService _session;
        private readonly GameStateMachine _states;
        private readonly SignalBus _signals;
        private readonly ICoreLog _log;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(3);

        /// <param name="slots">Writes the ring.</param>
        /// <param name="session">Supplies the header.</param>
        /// <param name="states">Only a run in the world is saved.</param>
        /// <param name="signals">Bus the beats arrive on. Null makes this inert.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public AutosaveDirector(
            SaveSlotService slots, SessionService session, GameStateMachine states, SignalBus signals, ICoreLog log)
        {
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _signals = signals;
            _log = log;

            if (signals == null)
            {
                return;
            }

            // "Arriving in a zone" is the transition INTO the world, not ZoneChangedSignal: that
            // signal is published by SetZone while the machine is still in Loading, so a guard on
            // InGame would refuse every one of them and the beat would never fire.
            _subscriptions.Add(signals.Subscribe<GameStateChangedSignal>(OnStateChanged));
            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(OnRadioChanged));
            _subscriptions.Add(signals.Subscribe<ProgressChangedSignal>(OnProgressChanged));
            _subscriptions.Add(signals.Subscribe<TickCompletedSignal>(_ => Flush()));
        }

        private string _pending;

        /// <summary>Slots written by this director since it was built. For tests and the overlay.</summary>
        public int Writes { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        private void OnStateChanged(GameStateChangedSignal signal)
        {
            if (signal.To == GameStateId.InGame && signal.From == GameStateId.Loading)
            {
                Request("arrived");
            }
        }

        private void OnRadioChanged(RadioChangedSignal signal)
        {
            if (signal.Kind == RadioChangeKind.Heard && signal.StationId == Stations.TheVoice)
            {
                Request("transmission");
            }
        }

        private void OnProgressChanged(ProgressChangedSignal signal)
        {
            if (signal.Kind == ProgressChangeKind.Solved || signal.Kind == ProgressChangeKind.ZoneUnlocked)
            {
                Request("progress");
            }
        }

        /// <summary>
        /// Asks for a write on the next tick rather than writing now.
        /// </summary>
        /// <remarks>
        /// The beats arrive as signals published from INSIDE a command handler -- a solve is
        /// announced from Mechanism.Use, before UseItemHandler has consumed the part. A save taken
        /// at that instant captures a machine that works and a part still in the pockets, and
        /// restoring it hands the player a duplicate. Deferring to the tick means the command has
        /// completed and every participant is consistent when the capture runs.
        /// </remarks>
        private void Request(string reason)
        {
            _pending = reason;
        }

        private void Flush()
        {
            if (_pending == null)
            {
                return;
            }

            var reason = _pending;
            _pending = null;
            Autosave(reason);
        }

        private void Autosave(string reason)
        {
            if (_states.Current != GameStateId.InGame)
            {
                // A zone change during a load, or a continue restoring progress in the menu, is
                // not a moment in a run. Saving there would write the previous run's header over
                // whatever the player is about to start.
                return;
            }

            var header = SaveHeaders.Build(_session);

            int slot;
            var code = _slots.SaveAutosave(header, out slot);
            if (code != Core.Primitives.ResultCode.Ok)
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.SaveCorrupt, "autosave (" + reason + "): " + code);
                }

                return;
            }

            Writes++;
            if (_signals != null)
            {
                _signals.Publish(new GameSavedSignal(slot));
            }
        }
    }
}
