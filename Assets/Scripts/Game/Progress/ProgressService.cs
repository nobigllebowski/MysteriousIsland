using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;

namespace ForgottenIsle.Game.Progress
{
    /// <summary>
    /// Owns the run's <see cref="WorldProgress"/>: the only object allowed to change it, and the
    /// save participant that persists it.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>SessionService</c> deliberately — one owner, read-only views out,
    /// mutation only through methods that publish a signal. ADR-0011 requires a new system to ship
    /// its <see cref="ISaveParticipant"/> in the same change that introduces it, so this is the
    /// third participant and the save envelope gains a <c>progress</c> section.
    /// <para>
    /// Objective text is derived rather than stored (see <see cref="Objectives"/>), so this service
    /// recomputes it after every change and publishes only when the line actually differs. A HUD
    /// that re-renders on every pickup is noise; one that re-renders when the instruction changes
    /// is information.
    /// </para>
    /// </remarks>
    public sealed class ProgressService : ISaveParticipant
    {
        /// <summary>True once the mechanism has been made to work.</summary>
        public bool HasSolved(string mechanismId)
        {
            return _progress.HasSolved(mechanismId);
        }

        /// <summary>
        /// Records a mechanism as working. A machine that was fixed stays fixed across a save,
        /// because the whole premise is that somebody maintaining things is what keeps them going.
        /// </summary>
        /// <returns>False when it already was.</returns>
        public bool Solve(string mechanismId)
        {
            if (!_progress.Solve(mechanismId))
            {
                return false;
            }

            Publish(new ProgressChangedSignal(mechanismId, ProgressChangeKind.Solved));
            PublishObjectiveIfChanged();
            return true;
        }

        /// <summary>On-disk section id, owned by <see cref="SaveSections"/> like every other one.</summary>
        public const string SectionId = SaveSections.Progress;

        private const string KeyInspected = "inspected";
        private const string KeyCollected = "collected";
        private const string KeyUnlocked = "unlocked";
        private const string KeySolved = "solved";
        private const char Separator = '\u001F';  // ASCII unit separator: never valid inside an id

        private readonly WorldProgress _progress = new WorldProgress();
        private readonly SignalBus _signals;
        private readonly ICoreLog _log;

        private string _zoneId = ContentIds.ZoneRibcage;
        private string _objectiveKey = string.Empty;

        /// <param name="signals">Bus the progression signals are published on. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public ProgressService(SignalBus signals, ICoreLog log)
        {
            _signals = signals;
            _log = log;
            _objectiveKey = Objectives.Current(_progress, _zoneId).Value;

            // Subscribed rather than told. SessionService already publishes ZoneChangedSignal from
            // its one SetZone, so every path that moves the player -- new game, travel, a restored
            // save, a dev-overlay teleport -- updates the objective without each of them having to
            // remember to. One subscription replaces a call site in four handlers and every future one.
            _zoneSubscription = signals?.Subscribe<ZoneChangedSignal>(OnZoneChanged);
        }

        private readonly System.IDisposable _zoneSubscription;

        private void OnZoneChanged(ZoneChangedSignal signal)
        {
            SetZone(signal.ZoneId);
        }

        /// <inheritdoc />
        public string ParticipantId => SectionId;

        /// <summary>Read-only view of what the player has found.</summary>
        public WorldProgress Progress => _progress;

        /// <summary>Localization key of the current objective. Empty means show nothing.</summary>
        public string ObjectiveKey => _objectiveKey;

        /// <summary>True when the marker has already been read.</summary>
        /// <param name="markerId">A <see cref="ContentIds"/> marker id.</param>
        public bool HasInspected(string markerId)
        {
            return _progress.HasInspected(markerId);
        }

        /// <summary>True when the discovery has already been taken.</summary>
        /// <param name="discoveryId">A <see cref="ContentIds"/> discovery id.</param>
        public bool HasCollected(string discoveryId)
        {
            return _progress.HasCollected(discoveryId);
        }

        /// <summary>True when the player may travel to this zone.</summary>
        /// <param name="zoneId">A scene key.</param>
        public bool IsZoneUnlocked(string zoneId)
        {
            return _progress.IsZoneUnlocked(zoneId);
        }

        /// <summary>Tells the service which zone the player is in, so objectives stay right.</summary>
        /// <param name="zoneId">A scene key.</param>
        public void SetZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || _zoneId == zoneId)
            {
                return;
            }

            _zoneId = zoneId;
            PublishObjectiveIfChanged();
        }

        /// <summary>Records a marker as read.</summary>
        /// <param name="markerId">A <see cref="ContentIds"/> marker id.</param>
        /// <returns>True when this was the first reading.</returns>
        public bool Inspect(string markerId)
        {
            if (!_progress.Inspect(markerId))
            {
                return false;
            }

            Publish(new ProgressChangedSignal(markerId, ProgressChangeKind.Inspected));
            PublishObjectiveIfChanged();
            return true;
        }

        /// <summary>
        /// Records a discovery as taken, and applies whatever it unlocks.
        /// </summary>
        /// <remarks>
        /// The unlock rule lives here rather than in the pickup object, so it is decided by the
        /// service that owns the state and is covered by engine-free tests. A pickup in a zone
        /// scene knows its own id and nothing else.
        /// </remarks>
        /// <param name="discoveryId">A <see cref="ContentIds"/> discovery id.</param>
        /// <returns>True when this was the first time it was taken.</returns>
        public bool Collect(string discoveryId)
        {
            if (!_progress.Collect(discoveryId))
            {
                return false;
            }

            Publish(new ProgressChangedSignal(discoveryId, ProgressChangeKind.Collected));

            // The brass tag IS the key to Fernmaw. Taking it is the unlock; there is no separate
            // "quest completed" step, because a second step the player cannot see is not content.
            if (discoveryId == ContentIds.DiscoveryBrassTag && _progress.UnlockZone(ContentIds.ZoneFernmaw))
            {
                Publish(new ProgressChangedSignal(ContentIds.ZoneFernmaw, ProgressChangeKind.ZoneUnlocked));
            }

            PublishObjectiveIfChanged();
            return true;
        }

        /// <summary>Wipes progress for a new run.</summary>
        public void ResetForNewRun()
        {
            _progress.Reset();
            _zoneId = ContentIds.ZoneRibcage;
            Publish(new ProgressChangedSignal(string.Empty, ProgressChangeKind.Replaced));
            PublishObjectiveIfChanged();
        }

        /// <inheritdoc />
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            // Three separator-joined lists rather than nested JSON: the section payload is a single
            // string by contract, the ids contain no whitespace or separators by construction, and a
            // flat encoding is one thing to migrate instead of a shape.
            var payload = string.Concat(
                KeyInspected, "=", Join(_progress.Inspected), "\n",
                KeyCollected, "=", Join(_progress.Collected), "\n",
                KeyUnlocked, "=", Join(_progress.UnlockedZones), "\n",
                KeySolved, "=", Join(_progress.Solved));

            doc.PutSection(SectionId, payload);
        }

        /// <inheritdoc />
        public void Restore(SaveDocument doc)
        {
            if (doc == null || !doc.TryGetSection(SectionId, out var payload) || string.IsNullOrEmpty(payload))
            {
                // A save written before this system existed has no progress section. That is not
                // corruption -- it is an older run, and it starts the slice from the beginning
                // rather than refusing to load.
                _progress.Reset();
                PublishReplaced();
                return;
            }

            List<string> inspected = null;
            List<string> collected = null;
            List<string> unlocked = null;
            List<string> solved = null;

            var lines = payload.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, split);
                var values = Split(line.Substring(split + 1));

                if (key == KeyInspected)
                {
                    inspected = values;
                }
                else if (key == KeyCollected)
                {
                    collected = values;
                }
                else if (key == KeyUnlocked)
                {
                    unlocked = values;
                }
                else if (key == KeySolved)
                {
                    solved = values;
                }
                else if (_log != null)
                {
                    _log.Warn(LogCode.SaveCorrupt, "progress: unknown key '" + key + "'");
                }
            }

            // A save from before mechanisms were recorded has no solved list: an older run with
            // nothing fixed yet, not corruption.
            _progress.RestoreFrom(inspected, collected, unlocked, solved);
            PublishReplaced();
        }

        private void PublishReplaced()
        {
            Publish(new ProgressChangedSignal(string.Empty, ProgressChangeKind.Replaced));
            PublishObjectiveIfChanged();
        }

        private void PublishObjectiveIfChanged()
        {
            var next = Objectives.Current(_progress, _zoneId).Value ?? string.Empty;
            if (next == _objectiveKey)
            {
                return;
            }

            _objectiveKey = next;
            Publish(new ObjectiveChangedSignal(next));
        }

        private void Publish<TSignal>(TSignal signal) where TSignal : struct, ISignal
        {
            if (_signals != null)
            {
                _signals.Publish(signal);
            }
        }

        private static string Join(IReadOnlyCollection<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var buffer = new string[values.Count];
            var index = 0;
            foreach (var value in values)
            {
                buffer[index++] = value;
            }

            return string.Join(Separator.ToString(), buffer);
        }

        private static List<string> Split(string packed)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(packed))
            {
                return result;
            }

            var parts = packed.Split(Separator);
            for (var i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    result.Add(parts[i]);
                }
            }

            return result;
        }
    }
}
