using System;
using System.Collections.Generic;
using System.Globalization;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Items;

namespace ForgottenIsle.Game.Fire
{
    /// <summary>
    /// Owns the fire sites: what is laid where, what burns, what is drying. The sixth
    /// <see cref="ISaveParticipant"/>.
    /// </summary>
    /// <remarks>
    /// Three places a fire can be laid -- the lee of the near hull and two patches of open sand
    /// -- and one set of rules (<see cref="FireSiteState"/>) applied to whichever the player
    /// stands at. The service adds what a single site cannot know: whether sparks have ever
    /// been thrown this run (the hint ladders turn on it), the run's blow-out count, the warm
    /// zone's clock, and the carry-to-the-lee the last hint does. It announces every change on
    /// the bus; the sites' visuals and the hint director read it from there.
    /// <para>
    /// Time is play seconds, counted from ticks: ten a second (<see cref="Ticker"/>). The fire
    /// does not burn down. Fuel life is the camp system's (Phase 5), authored in world hours,
    /// and that clock's scale is CONFLICT-6; nothing here depends on it. (ADR-0026)
    /// </para>
    /// </remarks>
    public sealed class FireService : ISaveParticipant, IDisposable
    {
        /// <summary>On-disk section id.</summary>
        public const string SectionId = SaveSections.Fire;

        private const char Separator = (char)31;
        private const char SiteSeparator = ';';
        private const char KeySeparator = ':';

        private readonly FireSiteState[] _sites =
        {
            new FireSiteState(ContentIds.FireSiteLee, true),
            new FireSiteState(ContentIds.FireSiteOpenA, false),
            new FireSiteState(ContentIds.FireSiteOpenB, false)
        };

        private readonly SignalBus _signals;
        private readonly InventoryService _inventory;
        private readonly ICoreLog _log;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(1);

        private readonly HashSet<string> _rung = new HashSet<string>(StringComparer.Ordinal);
        private bool _sparked;
        private int _blowOuts;
        private double _dryingRemaining;
        private string _dryingSiteId;

        /// <param name="signals">Bus changes go out on, and ticks come in on. Null tolerated.</param>
        /// <param name="inventory">Where dried wood goes. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public FireService(SignalBus signals, InventoryService inventory, ICoreLog log)
        {
            _signals = signals;
            _inventory = inventory;
            _log = log;

            if (signals != null)
            {
                _subscriptions.Add(signals.Subscribe<TickCompletedSignal>(_ => Advance(1d / Ticker.TargetTicksPerRealSecond)));
            }
        }

        /// <inheritdoc />
        public string ParticipantId => SectionId;

        /// <summary>The sites, lee first.</summary>
        public IReadOnlyList<FireSiteState> Sites => _sites;

        /// <summary>True once sparks have been thrown this run, whatever they landed in.</summary>
        public bool Sparked => _sparked;

        /// <summary>Blow-outs so far this run, at every site.</summary>
        public int BlowOuts => _blowOuts;

        /// <summary>Seconds of play until the wet wood by the fire is dry, or zero.</summary>
        public double DryingRemaining => _dryingRemaining;

        /// <summary>True when a fire is burning anywhere.</summary>
        public bool IsLit
        {
            get
            {
                for (var i = 0; i < _sites.Length; i++)
                {
                    if (_sites[i].IsLit)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Remembers that this chert has rung under the multitool.</summary>
        public void MarkRung(string rockId)
        {
            if (!string.IsNullOrEmpty(rockId))
            {
                _rung.Add(rockId);
            }
        }

        /// <summary>True once this chert has rung. Survives zone rebuilds and saves.</summary>
        public bool HasRung(string rockId)
        {
            return !string.IsNullOrEmpty(rockId) && _rung.Contains(rockId);
        }

        /// <summary>The site with this id, or null.</summary>
        public FireSiteState Site(string siteId)
        {
            for (var i = 0; i < _sites.Length; i++)
            {
                if (_sites[i].Id == siteId)
                {
                    return _sites[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Applies a carried item to a site and announces what happened.
        /// </summary>
        /// <param name="siteId">A <c>ContentIds</c> fire site id.</param>
        /// <param name="itemId">The carried item.</param>
        /// <param name="hasMultitool">Whether the multitool is carried.</param>
        public FireAct Apply(string siteId, string itemId, bool hasMultitool)
        {
            var site = Site(siteId);
            if (site == null)
            {
                return FireAct.Nothing;
            }

            var act = site.Apply(itemId, hasMultitool);

            if (FireRules.ThrewSparks(act) && !_sparked)
            {
                _sparked = true;
                Publish(FireChangeKind.FirstSparks, siteId);
            }

            switch (act)
            {
                case FireAct.GrassLaid:
                    Publish(FireChangeKind.GrassLaid, siteId);
                    break;
                case FireAct.FibreLaid:
                    Publish(FireChangeKind.FibreLaid, siteId);
                    break;
                case FireAct.WoodStacked:
                    Publish(FireChangeKind.WoodStacked, siteId);
                    break;
                case FireAct.PanelPlaced:
                    Publish(FireChangeKind.PanelPlaced, siteId);
                    break;
                case FireAct.SparksOnSand:
                case FireAct.GrassFlared:
                case FireAct.EmberStarved:
                    Publish(FireChangeKind.SparksFailed, siteId);
                    break;
                case FireAct.BlewOut:
                    _blowOuts++;
                    Publish(FireChangeKind.BlewOut, siteId);
                    break;
                case FireAct.Lit:
                    Publish(FireChangeKind.Lit, siteId);
                    break;
                case FireAct.WoodDrying:
                    _dryingRemaining = FireRules.WoodDryingSeconds;
                    _dryingSiteId = siteId;
                    Publish(FireChangeKind.WoodDrying, siteId);
                    break;
            }

            return act;
        }

        /// <summary>
        /// Counts play seconds for the warm zone. Wet wood that has had its ninety seconds comes
        /// back to the player's hands dry.
        /// </summary>
        public void Advance(double seconds)
        {
            if (_dryingRemaining <= 0d || !(seconds > 0d))
            {
                return;
            }

            _dryingRemaining -= seconds;
            if (_dryingRemaining > 0d)
            {
                return;
            }

            _dryingRemaining = 0d;
            var site = _dryingSiteId;
            _dryingSiteId = null;

            if (_inventory != null)
            {
                // Take rather than Consume/Take: the wet armful was spent when it was laid, and
                // an armful already carried (dry wood taken since) is simply not doubled.
                _inventory.Take(ItemIds.DriftwoodDry);
            }

            Publish(FireChangeKind.WoodDried, site);
        }

        /// <summary>
        /// Carries whatever kit is laid in the open into the lee and lays it there.
        /// </summary>
        /// <returns>False when nothing was laid anywhere in the open, or the lee already burns.</returns>
        public bool CarryKitToLee()
        {
            var lee = Site(ContentIds.FireSiteLee);
            if (lee == null || lee.IsLit)
            {
                return false;
            }

            var moved = false;
            for (var i = 0; i < _sites.Length; i++)
            {
                var site = _sites[i];
                if (site.InLee || site.IsLit || !site.HasKit)
                {
                    continue;
                }

                Tinder tinder;
                bool wood;
                site.TakeKit(out tinder, out wood);
                lee.ReceiveKit(tinder, wood);
                moved = true;
            }

            if (moved)
            {
                Publish(FireChangeKind.Carried, ContentIds.FireSiteLee);
            }

            return moved;
        }

        /// <summary>Back to bare sand everywhere. A new run.</summary>
        public void ResetForNewRun()
        {
            for (var i = 0; i < _sites.Length; i++)
            {
                _sites[i].Reset();
            }

            _sparked = false;
            _blowOuts = 0;
            _dryingRemaining = 0d;
            _dryingSiteId = null;
            _rung.Clear();
        }

        /// <inheritdoc />
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            // sites (id:packed;...) | sparked | blow-outs | drying seconds | drying site | rung rocks
            var builder = new System.Text.StringBuilder(96);
            for (var i = 0; i < _sites.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(SiteSeparator);
                }

                builder.Append(_sites[i].Id).Append(KeySeparator)
                    .Append(_sites[i].Capture().ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(Separator).Append(_sparked ? '1' : '0')
                .Append(Separator).Append(_blowOuts.ToString(CultureInfo.InvariantCulture))
                .Append(Separator).Append(_dryingRemaining.ToString("R", CultureInfo.InvariantCulture))
                .Append(Separator).Append(_dryingSiteId ?? string.Empty);

            var rung = new List<string>(_rung);
            rung.Sort(StringComparer.Ordinal);
            builder.Append(Separator).Append(string.Join(SiteSeparator.ToString(), rung));

            doc.PutSection(SectionId, builder.ToString());
        }

        /// <inheritdoc />
        public void Restore(SaveDocument doc)
        {
            ResetForNewRun();

            string payload;
            if (doc == null || !doc.TryGetSection(SectionId, out payload) || string.IsNullOrEmpty(payload))
            {
                // A save written before fire existed. An older run, not corruption.
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

            var sites = parts[0].Split(SiteSeparator);
            for (var i = 0; i < sites.Length; i++)
            {
                var pair = sites[i].Split(KeySeparator);
                int packed;
                var site = pair.Length == 2 ? Site(pair[0]) : null;
                if (site != null && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out packed))
                {
                    site.Restore(packed);
                }
            }

            _sparked = parts[1] == "1";

            int blowOuts;
            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out blowOuts))
            {
                _blowOuts = blowOuts < 0 ? 0 : blowOuts;
            }

            double drying;
            if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out drying) && drying > 0d && !double.IsInfinity(drying))
            {
                _dryingRemaining = drying;
                _dryingSiteId = string.IsNullOrEmpty(parts[4]) ? null : parts[4];
            }

            if (parts.Length > 5 && !string.IsNullOrEmpty(parts[5]))
            {
                var rung = parts[5].Split(SiteSeparator);
                for (var r = 0; r < rung.Length; r++)
                {
                    _rung.Add(rung[r]);
                }
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

        private void Publish(FireChangeKind kind, string siteId)
        {
            if (_signals != null)
            {
                _signals.Publish(new FireChangedSignal(kind, siteId, _blowOuts));
            }
        }
    }
}
