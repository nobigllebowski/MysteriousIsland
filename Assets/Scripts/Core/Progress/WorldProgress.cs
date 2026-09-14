using System;
using System.Collections.Generic;

namespace ForgottenIsle.Core.Progress
{
    /// <summary>
    /// Everything the player has found, taken, or opened. The whole of Phase 2's progression.
    /// </summary>
    /// <remarks>
    /// Deliberately three sets of strings and nothing else. A progression system large enough to
    /// need a framework is a progression system that outran its content, and this game currently
    /// has two markers, two discoveries and two zones. When that stops being true, this grows —
    /// not before.
    /// <para>
    /// Every mutator is idempotent and returns whether it changed anything, which is what makes
    /// "collect the same thing twice" impossible rather than merely discouraged, and what lets
    /// callers avoid publishing a signal for a no-op.
    /// </para>
    /// </remarks>
    public sealed class WorldProgress
    {
        private readonly HashSet<string> _inspected;
        private readonly HashSet<string> _collected;
        private readonly HashSet<string> _unlockedZones;

        /// <summary>A fresh run: nothing found, and only the opening zone reachable.</summary>
        public WorldProgress()
        {
            // Ordinal, not the culture-aware default: these are identifiers, not words. A Turkish
            // locale lowercasing 'I' differently must never change whether a save matches.
            _inspected = new HashSet<string>(StringComparer.Ordinal);
            _collected = new HashSet<string>(StringComparer.Ordinal);
            _unlockedZones = new HashSet<string>(StringComparer.Ordinal) { ContentIds.ZoneRibcage };
        }

        /// <summary>Markers the player has read.</summary>
        public IReadOnlyCollection<string> Inspected => _inspected;

        /// <summary>Discoveries the player has taken. These no longer exist in the world.</summary>
        public IReadOnlyCollection<string> Collected => _collected;

        /// <summary>Zones the player may travel to.</summary>
        public IReadOnlyCollection<string> UnlockedZones => _unlockedZones;

        /// <summary>How many discoveries have been taken. Shown on the dev overlay.</summary>
        public int CollectedCount => _collected.Count;

        /// <summary>True when <paramref name="markerId"/> has been read.</summary>
        /// <param name="markerId">A <see cref="ContentIds"/> marker id.</param>
        public bool HasInspected(string markerId)
        {
            return !string.IsNullOrEmpty(markerId) && _inspected.Contains(markerId);
        }

        /// <summary>True when <paramref name="discoveryId"/> has been taken.</summary>
        /// <param name="discoveryId">A <see cref="ContentIds"/> discovery id.</param>
        public bool HasCollected(string discoveryId)
        {
            return !string.IsNullOrEmpty(discoveryId) && _collected.Contains(discoveryId);
        }

        /// <summary>True when <paramref name="zoneId"/> may be travelled to.</summary>
        /// <param name="zoneId">A scene key.</param>
        public bool IsZoneUnlocked(string zoneId)
        {
            return !string.IsNullOrEmpty(zoneId) && _unlockedZones.Contains(zoneId);
        }

        /// <summary>Records a marker as read.</summary>
        /// <param name="markerId">A <see cref="ContentIds"/> marker id.</param>
        /// <returns>True when this was the first time; false when already known or the id was empty.</returns>
        public bool Inspect(string markerId)
        {
            return !string.IsNullOrEmpty(markerId) && _inspected.Add(markerId);
        }

        /// <summary>Records a discovery as taken.</summary>
        /// <param name="discoveryId">A <see cref="ContentIds"/> discovery id.</param>
        /// <returns>True when this was the first time; false when already held or the id was empty.</returns>
        public bool Collect(string discoveryId)
        {
            return !string.IsNullOrEmpty(discoveryId) && _collected.Add(discoveryId);
        }

        /// <summary>Opens a zone for travel.</summary>
        /// <param name="zoneId">A scene key.</param>
        /// <returns>True when it was not already open.</returns>
        public bool UnlockZone(string zoneId)
        {
            return !string.IsNullOrEmpty(zoneId) && _unlockedZones.Add(zoneId);
        }

        /// <summary>Wipes everything back to a new run's state.</summary>
        public void Reset()
        {
            _inspected.Clear();
            _collected.Clear();
            _unlockedZones.Clear();
            _unlockedZones.Add(ContentIds.ZoneRibcage);
        }

        /// <summary>Replaces the contents from a loaded save.</summary>
        /// <remarks>
        /// The Ribcage is re-added unconditionally afterwards. A save that somehow recorded it as
        /// locked would otherwise strand the player in a zone they are not allowed to be in, and no
        /// possible progression makes locking the opening zone correct.
        /// </remarks>
        /// <param name="inspected">Marker ids. Null is treated as empty.</param>
        /// <param name="collected">Discovery ids. Null is treated as empty.</param>
        /// <param name="unlockedZones">Zone ids. Null is treated as empty.</param>
        public void RestoreFrom(
            IEnumerable<string> inspected,
            IEnumerable<string> collected,
            IEnumerable<string> unlockedZones)
        {
            Reset();
            AddAll(_inspected, inspected);
            AddAll(_collected, collected);
            AddAll(_unlockedZones, unlockedZones);
            _unlockedZones.Add(ContentIds.ZoneRibcage);
        }

        /// <summary>An independent copy, so a caller can hold a stable view across a mutation.</summary>
        public WorldProgress Clone()
        {
            var copy = new WorldProgress();
            copy.RestoreFrom(_inspected, _collected, _unlockedZones);
            return copy;
        }

        private static void AddAll(HashSet<string> target, IEnumerable<string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var value in source)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    target.Add(value);
                }
            }
        }
    }
}
