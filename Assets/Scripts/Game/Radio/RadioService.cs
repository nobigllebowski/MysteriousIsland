using System.Collections.Generic;
using System.Globalization;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Items;

namespace ForgottenIsle.Game.Radio
{
    /// <summary>
    /// Owns the radio: its faults, its needle, and what has been heard on it. The fifth
    /// <see cref="ISaveParticipant"/>.
    /// </summary>
    /// <remarks>
    /// The set is the prologue's spine, and a spine that forgets itself across a save is a run
    /// that has to be repaired twice. Registered in the same change that introduces it, per
    /// ADR-0011.
    /// <para>
    /// This class decides nothing about the UI. It answers questions (what is heard here, what
    /// did that item do) and announces changes; the handlers ask, and the HUD listens.
    /// </para>
    /// </remarks>
    public sealed class RadioService : ISaveParticipant
    {
        /// <summary>On-disk section id.</summary>
        public const string SectionId = SaveSections.Radio;

        /// <summary>Where the needle rests when the set first powers up: mid-band, on nothing.</summary>
        public const float RestingMhz = 9.7f;

        /// <summary>Times the mic can be squeezed for a different line. After that, the last one.</summary>
        public const int MicLines = 5;

        // ASCII unit separator between fields, comma between heard ids: neither is valid inside an
        // id, so nothing needs escaping and no id can forge a delimiter.
        private const char Separator = (char)31;
        private const char ListSeparator = ',';

        private readonly RadioRepair _repair = new RadioRepair();
        private readonly List<string> _heard = new List<string>(4);
        private readonly SignalBus _signals;
        private readonly InventoryService _inventory;
        private readonly ICoreLog _log;

        private bool _found;
        private bool _listRead;
        private bool _open;
        private float _mhz = RestingMhz;
        private int _micAttempts;

        /// <param name="signals">Bus the radio signal is published on. Null tolerated.</param>
        /// <param name="inventory">For the parts a repair yields. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public RadioService(SignalBus signals, InventoryService inventory, ICoreLog log)
        {
            _signals = signals;
            _inventory = inventory;
            _log = log;
        }

        /// <inheritdoc />
        public string ParticipantId => SectionId;

        /// <summary>The repair state.</summary>
        public RadioRepair Repair => _repair;

        /// <summary>True once the player has looked at the set.</summary>
        public bool IsFound => _found;

        /// <summary>True when every fault is cleared.</summary>
        public bool IsWorking => _repair.IsWorking;

        /// <summary>True while the dial is on screen.</summary>
        public bool IsOpen => _open;

        /// <summary>Needle position.</summary>
        public float Mhz => _mhz;

        /// <summary>Stations the needle has locked onto at least once, in the order heard.</summary>
        public IReadOnlyList<string> Heard => _heard;

        /// <summary>True once the transmission has been received. It is recorded; it is never lost.</summary>
        public bool TransmissionReceived => _heard.Contains(Stations.TheVoice);

        /// <summary>
        /// Looks at the set: the first inspection, then one fault at a time.
        /// </summary>
        /// <returns>The narration key to show.</returns>
        public string Inspect()
        {
            if (!_found)
            {
                _found = true;
                Publish(RadioChangeKind.Diagnosed, "narration.radio.found");
                return "narration.radio.found";
            }

            if (!_listRead)
            {
                // The second look finds the list taped under the handle: the clue the whole
                // puzzle turns on, found at 15:00 as designed, long before the set works. The
                // first version handed it out only from a working set, which the player could not
                // examine any more -- the clue was written and unreachable.
                _listRead = true;
                Publish(RadioChangeKind.Diagnosed, "narration.radio.list");
                return "narration.radio.list";
            }

            if (_repair.IsWorking)
            {
                Publish(RadioChangeKind.Diagnosed, "narration.radio.list");
                return "narration.radio.list";
            }

            var key = "narration.radio.fault." + FaultName(_repair.FirstOutstanding());
            Publish(RadioChangeKind.Diagnosed, key);
            return key;
        }

        /// <summary>
        /// Applies a carried item to the set.
        /// </summary>
        /// <remarks>
        /// The torch is taken apart: its cells go into the bay and its copper spring goes into the
        /// player's hands, because a person who has just opened a torch for its batteries is
        /// holding the spring. That is how most players find the fuse.
        /// </remarks>
        public UseOutcome Apply(string itemId)
        {
            RadioFault cleared;
            if (!_repair.TryApply(itemId, out cleared))
            {
                return UseOutcome.Nothing;
            }

            _found = true;

            if (itemId == ItemIds.DeadTorch && _inventory != null)
            {
                _inventory.Take(ItemIds.CopperSpring);
            }

            var line = itemId == ItemIds.FieldRecorder
                ? "narration.radio.fixed.power_recorder"
                : "narration.radio.fixed." + FaultName(cleared);

            if (_repair.IsWorking)
            {
                // Whichever fault went last, the player reads what fixing it revealed (the
                // inscription, if it was power) and THEN the click, the amber lamp, the hiss. Both
                // lines go out as one sequence from here, and the outcome carries no line of its
                // own so the use handler does not say the first one twice.
                _mhz = RestingMhz;
                Publish(RadioChangeKind.PoweredUp, "narration.radio.working");
                if (_signals != null)
                {
                    _signals.Publish(new NarrationSequenceSignal(new[] { line, "narration.radio.working" }));
                }

                return RadioRepair.ConsumesItem(itemId) ? UseOutcome.Spent(null) : UseOutcome.Worked(null);
            }

            Publish(RadioChangeKind.Repaired, line);
            return RadioRepair.ConsumesItem(itemId)
                ? UseOutcome.Spent(line)
                : UseOutcome.Worked(line);
        }

        /// <summary>Puts the dial on screen. No-op when the set does not work.</summary>
        /// <returns>False when the set is not working.</returns>
        public bool Open()
        {
            if (!_repair.IsWorking)
            {
                return false;
            }

            _open = true;
            Publish(RadioChangeKind.Opened, null);
            return true;
        }

        /// <summary>Takes the dial off screen.</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Publish(RadioChangeKind.Closed, null);
        }

        /// <summary>
        /// Moves the needle, snaps it onto a carrier inside the lock window, and records a first hearing.
        /// </summary>
        /// <returns>False when the set is not working.</returns>
        public bool Tune(float mhz)
        {
            if (!_repair.IsWorking)
            {
                return false;
            }

            _mhz = RadioTuner.Snap(RadioBand.Clamp(mhz));

            string stationId;
            var reception = RadioTuner.Receive(_mhz, out stationId);

            if (reception == Reception.Locked && stationId != null && !_heard.Contains(stationId))
            {
                // Recorded on first lock and never removed: the record is never lost in this
                // game. A player who finds the signal and wanders off mid-transmission still has it.
                _heard.Add(stationId);
                Publish(RadioChangeKind.Heard, "narration." + stationId);
                return true;
            }

            Publish(RadioChangeKind.Tuned, null);
            return true;
        }

        /// <summary>
        /// Squeezes the mic. Five different things to say, each less formal; then only the last.
        /// </summary>
        /// <returns>The narration key for what was said.</returns>
        public string SqueezeMic()
        {
            _micAttempts = _micAttempts < MicLines ? _micAttempts + 1 : MicLines;

            var key = "narration.radio.mic." + _micAttempts.ToString(CultureInfo.InvariantCulture);
            Publish(RadioChangeKind.MicSqueezed, key);
            return key;
        }

        /// <summary>Back to the state a new run finds it in.</summary>
        public void ResetForNewRun()
        {
            _repair.Reset();
            _heard.Clear();
            _found = false;
            _listRead = false;
            _open = false;
            _mhz = RestingMhz;
            _micAttempts = 0;
        }

        /// <inheritdoc />
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            // found | packed faults | mhz | heard,ids | mic attempts. The dial being open is not
            // saved: a run resumes with the set put down, whatever it was doing when it stopped.
            // First field is flags: 1 found, 2 list read.
            var flags = (_found ? 1 : 0) | (_listRead ? 2 : 0);
            var payload =
                flags.ToString(CultureInfo.InvariantCulture) + Separator +
                _repair.Capture().ToString(CultureInfo.InvariantCulture) + Separator +
                _mhz.ToString("R", CultureInfo.InvariantCulture) + Separator +
                string.Join(ListSeparator.ToString(), _heard) + Separator +
                _micAttempts.ToString(CultureInfo.InvariantCulture);

            doc.PutSection(SectionId, payload);
        }

        /// <inheritdoc />
        public void Restore(SaveDocument doc)
        {
            ResetForNewRun();

            string payload;
            if (doc == null || !doc.TryGetSection(SectionId, out payload) || string.IsNullOrEmpty(payload))
            {
                // A save written before the radio existed. An older run, not corruption.
                return;
            }

            var parts = payload.Split(Separator);
            if (parts.Length < 5)
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.SaveCorrupt, SectionId + ": " + parts.Length + " fields");
                }

                return;
            }

            int flags;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
            {
                _found = (flags & 1) != 0;
                _listRead = (flags & 2) != 0;
            }

            int packed;
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out packed))
            {
                _repair.Restore(packed);
            }

            float mhz;
            if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out mhz))
            {
                _mhz = RadioBand.Clamp(mhz);
            }

            if (!string.IsNullOrEmpty(parts[3]))
            {
                var ids = parts[3].Split(ListSeparator);
                for (var i = 0; i < ids.Length; i++)
                {
                    Station station;
                    if (Stations.TryGet(ids[i], out station) && !_heard.Contains(ids[i]))
                    {
                        _heard.Add(ids[i]);
                    }
                }
            }

            int attempts;
            if (int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out attempts))
            {
                _micAttempts = attempts < 0 ? 0 : attempts > MicLines ? MicLines : attempts;
            }
        }

        private static string FaultName(RadioFault fault)
        {
            switch (fault)
            {
                case RadioFault.Power:
                    return "power";
                case RadioFault.Contacts:
                    return "contacts";
                case RadioFault.Fuse:
                    return "fuse";
                default:
                    return "none";
            }
        }

        private void Publish(RadioChangeKind kind, string lineKey)
        {
            if (_signals == null)
            {
                return;
            }

            string stationId;
            var reception = RadioTuner.Receive(_mhz, out stationId);

            _signals.Publish(new RadioChangedSignal(
                kind,
                _mhz,
                RadioTuner.Strength(_mhz),
                reception,
                stationId,
                lineKey,
                _open));
        }
    }
}
