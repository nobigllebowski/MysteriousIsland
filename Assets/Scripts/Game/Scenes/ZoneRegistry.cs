using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Game.Scenes
{
    /// <summary>
    /// Decides which zone scenes are allowed to be resident, and enforces ADR-0004's hard cap of two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the cap is enforced here rather than in <see cref="SceneLoader"/>: the loader is a
    /// mechanism and knows nothing about zones — it will happily hold four scenes open if asked. The
    /// memory budget that makes two the right number is a design decision about zones specifically,
    /// so the rule lives with the type that understands what a zone is. The loader stays reusable for
    /// non-zone scenes (the menu, a future cutscene) that the cap must not apply to.
    /// </para>
    /// <para>
    /// WHY the eviction is "unload the oldest, then load" rather than "load, then unload the oldest":
    /// the peak matters more than the order. Loading first would put three zones in memory
    /// simultaneously for the duration of the load — which is precisely the state the cap exists to
    /// prevent, and precisely the moment memory pressure is highest. Entering a zone therefore always
    /// passes through a frame with a hole in the world, which is fine, because ADR-0004 puts a curtain
    /// over that frame.
    /// </para>
    /// </remarks>
    public sealed class ZoneRegistry
    {
        /// <summary>ADR-0004's ceiling on simultaneously resident zone scenes.</summary>
        public const int DefaultMaxResident = 2;

        private readonly SceneLoader _loader;
        private readonly ICoreLog _log;
        private readonly int _maxResident;
        private readonly List<string> _residentZones;

        private string _activeZoneKey;

        /// <summary>
        /// Creates a registry over <paramref name="loader"/>.
        /// </summary>
        /// <param name="loader">The loader that performs the actual scene work. Required.</param>
        /// <param name="log">Diagnostics sink. Null is tolerated.</param>
        /// <param name="maxResident">
        /// Resident-zone ceiling. Values below one are raised to one, because a registry that can hold
        /// no zones would make the game unplayable rather than merely frugal.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="loader"/> is null.</exception>
        public ZoneRegistry(SceneLoader loader, ICoreLog log, int maxResident = DefaultMaxResident)
        {
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            _loader = loader;
            _log = log;
            _maxResident = maxResident > 0 ? maxResident : 1;
            _residentZones = new List<string>(_maxResident + 1);
        }

        /// <summary>The resident-zone ceiling this registry was built with.</summary>
        public int MaxResident => _maxResident;

        /// <summary>How many zone scenes are resident right now.</summary>
        public int ResidentCount => _residentZones.Count;

        /// <summary>Zone keys currently resident, oldest first. The eviction order is this order.</summary>
        public IReadOnlyList<string> Resident => _residentZones;

        /// <summary>
        /// The zone the player is in, or an empty string when no run is live.
        /// </summary>
        /// <remarks>
        /// Distinct from "resident": the previous zone can still be resident for a moment after travel
        /// while the curtain is down, and it is not the one the player is standing in.
        /// </remarks>
        public string ActiveZoneKey => _activeZoneKey ?? string.Empty;

        /// <summary>True when <paramref name="zoneKey"/> is resident.</summary>
        public bool IsResident(string zoneKey)
        {
            return IndexOf(zoneKey) >= 0;
        }

        /// <summary>
        /// Records a zone scene that is already open — the editor's play-a-zone-directly path — as
        /// resident and active, without loading anything.
        /// </summary>
        /// <remarks>
        /// See <see cref="SceneLoader.AdoptResident"/> for why this exists. Kept separate from
        /// <see cref="EnterZone"/> so the normal path has no branch for a condition that only occurs in
        /// the editor.
        /// </remarks>
        public void AdoptResident(string zoneKey)
        {
            if (!SceneKeys.IsZone(zoneKey))
            {
                return;
            }

            _loader.AdoptResident(zoneKey);
            if (!IsResident(zoneKey))
            {
                _residentZones.Add(zoneKey);
            }

            _activeZoneKey = zoneKey;
        }

        /// <summary>
        /// Makes <paramref name="zoneKey"/> resident and active, evicting the oldest zone first if the
        /// cap would otherwise be exceeded.
        /// </summary>
        /// <param name="zoneKey">A zone key from <see cref="SceneKeys"/>.</param>
        /// <param name="onComplete">
        /// Invoked exactly once with the outcome. On anything other than <see cref="ResultCode.Ok"/> the
        /// active zone is left unchanged, so a failed travel leaves the player where they were rather
        /// than nowhere.
        /// </param>
        public void EnterZone(string zoneKey, Action<ResultCode> onComplete = null)
        {
            if (!SceneKeys.IsZone(zoneKey))
            {
                Warn(LogCode.CatalogMissing, zoneKey);
                Complete(onComplete, ResultCode.SceneNotFound);
                return;
            }

            if (_loader.IsLoading)
            {
                Complete(onComplete, ResultCode.AlreadyLoading);
                return;
            }

            if (IsResident(zoneKey))
            {
                // Already in memory — travelling back to the zone we just came from. Promote it to
                // active and to newest, so the zone the player is standing in is never the eviction
                // candidate on the next travel.
                Touch(zoneKey);
                _activeZoneKey = zoneKey;
                Complete(onComplete, ResultCode.Ok);
                return;
            }

            if (_residentZones.Count >= _maxResident)
            {
                var evicted = _residentZones[0];
                _loader.UnloadAsync(evicted, unloadCode =>
                {
                    // Whatever the unload reported, the zone is no longer ours to account for: the loader
                    // has either freed it or given up on it, and in both cases holding a slot for it would
                    // deadlock every future travel at the cap.
                    Remove(evicted);
                    if (unloadCode != ResultCode.Ok)
                    {
                        Warn(LogCode.SceneLoadSlow, evicted);
                    }

                    LoadZone(zoneKey, onComplete);
                });
                return;
            }

            LoadZone(zoneKey, onComplete);
        }

        /// <summary>
        /// Unloads every resident zone, one after another, and clears the active zone.
        /// </summary>
        /// <remarks>
        /// Used when a run ends. Serialised rather than parallel because <see cref="SceneLoader"/>
        /// permits one operation at a time; the completion of each unload starts the next.
        /// </remarks>
        /// <param name="onComplete">
        /// Invoked once when the last unload settles, carrying <see cref="ResultCode.Ok"/> only if every
        /// unload succeeded. Even on failure the registry ends up empty — see <see cref="EnterZone"/>.
        /// </param>
        public void UnloadAll(Action<ResultCode> onComplete = null)
        {
            _activeZoneKey = null;
            UnloadNext(ResultCode.Ok, onComplete);
        }

        private void UnloadNext(ResultCode worstSoFar, Action<ResultCode> onComplete)
        {
            if (_residentZones.Count == 0)
            {
                Complete(onComplete, worstSoFar);
                return;
            }

            var next = _residentZones[0];
            _loader.UnloadAsync(next, code =>
            {
                Remove(next);
                var worst = code != ResultCode.Ok ? code : worstSoFar;
                UnloadNext(worst, onComplete);
            });
        }

        private void LoadZone(string zoneKey, Action<ResultCode> onComplete)
        {
            _loader.LoadAdditive(zoneKey, code =>
            {
                if (code == ResultCode.Ok)
                {
                    Touch(zoneKey);
                    _activeZoneKey = zoneKey;
                }

                Complete(onComplete, code);
            });
        }

        /// <summary>Moves <paramref name="zoneKey"/> to the newest position, adding it if absent.</summary>
        private void Touch(string zoneKey)
        {
            Remove(zoneKey);
            _residentZones.Add(zoneKey);
        }

        private void Remove(string zoneKey)
        {
            var index = IndexOf(zoneKey);
            if (index >= 0)
            {
                _residentZones.RemoveAt(index);
            }
        }

        private int IndexOf(string zoneKey)
        {
            if (string.IsNullOrEmpty(zoneKey))
            {
                return -1;
            }

            for (var i = 0; i < _residentZones.Count; i++)
            {
                if (string.Equals(_residentZones[i], zoneKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Complete(Action<ResultCode> onComplete, ResultCode code)
        {
            if (onComplete != null)
            {
                onComplete(code);
            }
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log != null)
            {
                _log.Warn(code, detail ?? string.Empty);
            }
        }
    }
}
