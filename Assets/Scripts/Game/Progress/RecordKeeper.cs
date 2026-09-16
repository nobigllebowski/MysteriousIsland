using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Progress
{
    /// <summary>
    /// Keeps the session's recorded-percent figure in step with progression.
    /// </summary>
    /// <remarks>
    /// Progression does not know the session and the session does not know progression; this is
    /// the one small thing that knows both, and it does one arithmetic step whenever progress
    /// changes. Held by the context so the subscription lives as long as the run does.
    /// </remarks>
    public sealed class RecordKeeper : IDisposable
    {
        private readonly ProgressService _progress;
        private readonly SessionService _session;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(1);

        /// <param name="progress">What has been recorded.</param>
        /// <param name="session">Where the figure is kept.</param>
        /// <param name="signals">Bus progress changes arrive on. Null makes this inert.</param>
        public RecordKeeper(ProgressService progress, SessionService session, SignalBus signals)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _session = session ?? throw new ArgumentNullException(nameof(session));

            if (signals != null)
            {
                _subscriptions.Add(signals.Subscribe<ProgressChangedSignal>(_ => Refresh()));
            }
        }

        /// <summary>Recomputes the figure now. Called on every progress change, and on demand.</summary>
        public void Refresh()
        {
            _session.SetRecordedPercent(Recorded.Percent(_progress.Progress));
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
    }
}
