using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Radio;

namespace ForgottenIsle.Game.Progress
{
    /// <summary>
    /// Rebuilds the Field Slate whenever the record changes and announces it.
    /// </summary>
    /// <remarks>
    /// The one place that knows both progression and the radio. It computes nothing itself:
    /// <see cref="Slate.Build"/> does, in Core, from facts this class collects. Held by the context
    /// so the subscriptions live as long as the run does.
    /// </remarks>
    public sealed class SlateDirector : IDisposable
    {
        private readonly ProgressService _progress;
        private readonly RadioService _radio;
        private readonly SignalBus _signals;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);

        /// <param name="progress">The record.</param>
        /// <param name="radio">The set. Null reads as a radio never found.</param>
        /// <param name="signals">Bus changes arrive on and the notebook goes out on. Null makes this inert.</param>
        public SlateDirector(ProgressService progress, RadioService radio, SignalBus signals)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _radio = radio;
            _signals = signals;

            if (signals == null)
            {
                return;
            }

            // Every change marks the notebook dirty; the rebuild happens once, on the next tick.
            // A single inspection publishes a progress change AND an objective change, and the
            // radio a change and its narration; rebuilding three lists per signal was a cost
            // with no reader when the tick that follows would show the same page.
            _subscriptions.Add(signals.Subscribe<ProgressChangedSignal>(_ => _dirty = true));
            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(OnRadioChanged));
            _subscriptions.Add(signals.Subscribe<ObjectiveChangedSignal>(_ => _dirty = true));
            _subscriptions.Add(signals.Subscribe<TickCompletedSignal>(_ => Flush()));
        }

        /// <summary>The notebook as it stands.</summary>
        public SlateContents Current()
        {
            return Slate.Build(_progress.Progress, Facts(), _progress.ObjectiveKey);
        }

        private bool _dirty;

        /// <summary>Rebuilds and announces the notebook now. Priming uses this; changes wait for the tick.</summary>
        public void Publish()
        {
            _dirty = false;
            if (_signals != null)
            {
                _signals.Publish(new SlateChangedSignal(Current()));
            }
        }

        private void Flush()
        {
            if (_dirty)
            {
                Publish();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        private void OnRadioChanged(RadioChangedSignal signal)
        {
            // Not on every needle move. Tuned is raised per pointer-move on the dial, and Opened
            // and Closed change nothing the notebook records; rebuilding three lists and thirty
            // lookups per thumb movement is a cost with no reader.
            switch (signal.Kind)
            {
                case RadioChangeKind.Diagnosed:
                case RadioChangeKind.Repaired:
                case RadioChangeKind.PoweredUp:
                case RadioChangeKind.Heard:
                    _dirty = true;
                    break;
            }
        }

        private SlateFacts Facts()
        {
            if (_radio == null)
            {
                return new SlateFacts(false, false, false, false, false, false, false);
            }

            var powerFixed = (_radio.Repair.Outstanding & RadioFault.Power) == 0;
            return new SlateFacts(
                _radio.IsFound,
                powerFixed,
                _radio.IsWorking,
                _radio.HasHeard(Stations.HullThump),
                _radio.HasHeard(Stations.Bulletin),
                _radio.TransmissionReceived,
                _radio.Repair.UsedRecorderCells,
                _radio.Repair.FuseFromCord);
        }
    }
}
