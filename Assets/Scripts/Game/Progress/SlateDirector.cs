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
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(3);

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

            _subscriptions.Add(signals.Subscribe<ProgressChangedSignal>(_ => Publish()));
            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(OnRadioChanged));

            // The "Next:" line is the objective, which the fire and the radio move without a
            // progress change; its own signal is the cheapest way to keep the page current.
            _subscriptions.Add(signals.Subscribe<ObjectiveChangedSignal>(_ => Publish()));
        }

        /// <summary>The notebook as it stands.</summary>
        public SlateContents Current()
        {
            return Slate.Build(_progress.Progress, Facts(), _progress.ObjectiveKey);
        }

        /// <summary>Announces the notebook now. Called on every change, and to prime the HUD.</summary>
        public void Publish()
        {
            if (_signals != null)
            {
                _signals.Publish(new SlateChangedSignal(Current()));
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
                    Publish();
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
