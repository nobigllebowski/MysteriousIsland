using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Hints;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.State;
using ForgottenIsle.Game.Bootstrap;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Radio;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Hints
{
    /// <summary>
    /// Runs the prologue's hint ladders against play time and says each rung through the dispatcher.
    /// </summary>
    /// <remarks>
    /// The design's rule is "never hard-block, never nag": every hint is diegetic, arrives on a
    /// timer, and the timer resets on any meaningful action. This is the timer. It counts the
    /// session's play seconds between ticks -- real seconds in the world, so a pause or a menu
    /// counts nothing -- and hands them to each <see cref="HintLadder"/>. A rung that comes due
    /// is a <see cref="RemarkCommand"/>, or an <see cref="InspectCommand"/> said as a remark when
    /// the design has the notebook entry write itself; either way it goes through the dispatcher
    /// and is refused outside gameplay like anything else Nadia says.
    /// <para>
    /// What resets the radio ladder is what the design lists: a coarse drag (accumulated travel of
    /// the needle past <see cref="HintLadders.CoarseDragMhz"/>; the signal reports positions, not
    /// gestures, so travel is the proxy), reading the taped list, taking a brass tag, and locking
    /// onto either false positive. Hearing the voice stops it. Aligning the hull line stops that
    /// ladder. The last rung of the radio's ladder leaves the set sweeping by itself; while it
    /// does, every tick steps the needle through <see cref="SweepRadioCommand"/>. Entering the world -- a new run or a load -- forgets everything, which is the
    /// design's "timers reset to zero on load"; so this holds nothing worth saving and is not a
    /// save participant. (ADR-0025)
    /// </para>
    /// </remarks>
    public sealed class HintDirector : IDisposable
    {
        /// <summary>The line the radio shows when its taped list is read.</summary>
        private const string ListLineKey = "narration.radio.list";

        private readonly SessionService _session;
        private readonly GameStateMachine _states;
        private readonly ProgressService _progress;
        private readonly RadioService _radio;
        private readonly FireService _fire;
        private readonly InventoryService _inventory;
        private readonly CommandDispatcher _commands;
        private readonly ICoreLog _log;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);

        private readonly HintLadder _hullLine = HintLadders.HullLine();
        private readonly HintLadder _radioLadder = HintLadders.Radio();
        private readonly HintLadder _fireSpark = HintLadders.FireSpark();
        private readonly HintLadder _fireTinder = HintLadders.FireTinder();
        private readonly HintLadder _bag = HintLadders.Bag();

        private double _lastPlaytime;
        private float _lastMhz;
        private bool _haveMhz;
        private float _dialTravel;

        /// <param name="session">Supplies play time and the zone.</param>
        /// <param name="states">Only a run in the world is hinted.</param>
        /// <param name="progress">Read to know what is already found.</param>
        /// <param name="radio">Read to know whether the set works and the voice is heard.</param>
        /// <param name="fire">Read to know whether sparks have flown and whether a fire burns.</param>
        /// <param name="commands">Where every rung is dispatched.</param>
        /// <param name="inventory">Read to know whether the bag has been taken. Null means it has.</param>
        /// <param name="signals">Bus the ticks and actions arrive on. Null makes this inert.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public HintDirector(
            SessionService session,
            GameStateMachine states,
            ProgressService progress,
            RadioService radio,
            FireService fire,
            CommandDispatcher commands,
            SignalBus signals,
            ICoreLog log,
            InventoryService inventory = null)
        {
            _inventory = inventory;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _states = states ?? throw new ArgumentNullException(nameof(states));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _radio = radio ?? throw new ArgumentNullException(nameof(radio));
            _fire = fire ?? throw new ArgumentNullException(nameof(fire));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _log = log;

            if (signals == null)
            {
                return;
            }

            _subscriptions.Add(signals.Subscribe<GameStateChangedSignal>(OnStateChanged));
            _subscriptions.Add(signals.Subscribe<TickCompletedSignal>(_ => OnTick()));
            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(OnRadioChanged));
            _subscriptions.Add(signals.Subscribe<ProgressChangedSignal>(OnProgressChanged));
            _subscriptions.Add(signals.Subscribe<FireChangedSignal>(OnFireChanged));
            _subscriptions.Add(signals.Subscribe<InventoryChangedSignal>(OnInventoryChanged));
        }

        /// <summary>The bag's ladder. Exposed for tests.</summary>
        public HintLadder Bag => _bag;

        /// <summary>The fire's no-spark ladder. Exposed for tests.</summary>
        public HintLadder FireSpark => _fireSpark;

        /// <summary>The fire's no-tinder ladder. Exposed for tests.</summary>
        public HintLadder FireTinder => _fireTinder;

        /// <summary>Rungs dispatched since this director was built. For tests and the overlay.</summary>
        public int Said { get; private set; }

        /// <summary>The hull line's ladder. Exposed for tests.</summary>
        public HintLadder HullLine => _hullLine;

        /// <summary>The radio's ladder. Exposed for tests.</summary>
        public HintLadder Radio => _radioLadder;

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
            if (signal.To != GameStateId.InGame || signal.From != GameStateId.Loading)
            {
                return;
            }

            // Entering the world: a new run or a load. Both forget everything -- the design's
            // "hint timers reset to zero on load" -- and start only what still applies.
            _hullLine.Forget();
            _radioLadder.Forget();
            _fireSpark.Forget();
            _fireTinder.Forget();
            _bag.Forget();
            _haveMhz = false;
            _dialTravel = 0f;
            _lastPlaytime = _session.PlaytimeSeconds;

            if (!_progress.HasInspected(ContentIds.MarkerHullLine))
            {
                _hullLine.Start();
            }

            if (_inventory != null && !_inventory.Has(ItemIds.Multitool))
            {
                // The first forty seconds of a run: the bag is the only colour on the screen.
                _bag.Start();
            }

            if (_radio.IsWorking && !_radio.TransmissionReceived)
            {
                _radioLadder.Start();
            }

            // The spark ladder waits for the player to engage with a site again; sparks having
            // flown is in the record, so the tinder ladder can pick up from that.
            if (_fire.Sparked && !_fire.IsLit && !AnyFibreLaid())
            {
                _fireTinder.Start();
            }
        }

        private void OnTick()
        {
            if (_states.Current != GameStateId.InGame)
            {
                return;
            }

            var now = _session.PlaytimeSeconds;
            var delta = now - _lastPlaytime;
            _lastPlaytime = now;
            if (!(delta > 0d))
            {
                return;
            }

            HintTier due;

            // The hull line is on the Ribcage shore. Time spent in another zone, or with the dial
            // up and the player's attention on the set, is not time spent not noticing it -- and
            // a line about hulls landing in the middle of the transmission would tread on it.
            if (_hullLine.IsRunning
                && !_radio.IsOpen
                && string.Equals(_session.ZoneId, ContentIds.ZoneRibcage, StringComparison.Ordinal)
                && _hullLine.TryAdvance(delta, out due))
            {
                Say(due);
            }

            if (_radioLadder.TryAdvance(delta, out due))
            {
                Say(due);
            }

            // The fire is on the Ribcage shore, like the hulls. So is the bag.
            var onTheShore = string.Equals(_session.ZoneId, ContentIds.ZoneRibcage, StringComparison.Ordinal);
            if (onTheShore && _bag.TryAdvance(delta, out due))
            {
                Say(due);
            }

            if (onTheShore && _fireSpark.TryAdvance(delta, out due))
            {
                Say(due);
            }

            if (onTheShore && _fireTinder.TryAdvance(delta, out due))
            {
                Say(due);
            }

            if (_radio.IsSweeping)
            {
                // The set sweeps in play time, through the dispatcher, so a lock on the way is
                // heard and narrated exactly as a hand-found one. Refused means a hand got there
                // first this tick; nothing to do.
                _commands.Dispatch(new SweepRadioCommand((float)delta));
            }
        }

        private void OnRadioChanged(RadioChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case RadioChangeKind.PoweredUp:
                    // "Timer starts the moment the radio powers up."
                    _radioLadder.Start();
                    _haveMhz = false;
                    _dialTravel = 0f;
                    break;

                case RadioChangeKind.Tuned:
                    if (_haveMhz)
                    {
                        _dialTravel += Math.Abs(signal.Mhz - _lastMhz);
                        if (_dialTravel > HintLadders.CoarseDragMhz)
                        {
                            _dialTravel = 0f;
                            _radioLadder.Reset();
                        }
                    }

                    _lastMhz = signal.Mhz;
                    _haveMhz = true;
                    break;

                case RadioChangeKind.Heard:
                    if (signal.StationId == Stations.TheVoice)
                    {
                        _radioLadder.Stop();
                    }
                    else
                    {
                        // A false positive: the player is searching, and the hint waits.
                        _radioLadder.Reset();
                    }

                    _lastMhz = signal.Mhz;
                    _haveMhz = true;
                    break;

                case RadioChangeKind.Diagnosed:
                    if (signal.LineKey == ListLineKey)
                    {
                        _radioLadder.Reset();
                    }

                    break;
            }
        }

        private void OnFireChanged(FireChangedSignal signal)
        {
            switch (signal.Kind)
            {
                case FireChangeKind.FirstSparks:
                    // Spark found. What is missing now is something for it to land in -- unless
                    // the fibre is already down somewhere, in which case nothing is.
                    _fireSpark.Stop();
                    if (!_fire.IsLit && !AnyFibreLaid())
                    {
                        _fireTinder.Start();
                    }

                    return;

                case FireChangeKind.FibreLaid:
                    // The right tinder is down; the ladder about tinder has nothing to add.
                    _fireTinder.Stop();
                    break;

                case FireChangeKind.Lit:
                    _fireSpark.Stop();
                    _fireTinder.Stop();
                    return;

                case FireChangeKind.BlewOut:
                    // Counted, not timed (§7:10 FAILURE): three and she names the wind; seven and
                    // she carries the kit into the lee herself. The player still lights it.
                    if (signal.BlowOuts == FireRules.BlowOutsBeforeTheWindLine)
                    {
                        Say(new HintTier(ContentIds.RemarkFireWind, 0d));
                    }
                    else if (signal.BlowOuts == FireRules.BlowOutsBeforeSheCarriesIt)
                    {
                        var carried = _commands.Dispatch(new CarryFireKitCommand());
                        if (carried.Success)
                        {
                            Say(new HintTier(ContentIds.RemarkFireCarriesKit, 0d));
                        }
                    }

                    return;
            }

            // Anything laid or tried at a site is engagement: the no-spark clock starts on the
            // first of it, and only if no spark has flown yet.
            if (!_fire.Sparked && !_fire.IsLit && !_fireSpark.IsRunning && !_fireSpark.IsExhausted)
            {
                _fireSpark.Start();
            }
        }

        private void OnInventoryChanged(InventoryChangedSignal signal)
        {
            if (signal.Kind == InventoryChangeKind.Added && signal.ItemId == ItemIds.Multitool)
            {
                _bag.Stop();
            }
        }

        private bool AnyFibreLaid()
        {
            for (var i = 0; i < _fire.Sites.Count; i++)
            {
                if (_fire.Sites[i].Tinder == Tinder.Fibre)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnProgressChanged(ProgressChangedSignal signal)
        {
            if (signal.Kind == ProgressChangeKind.Inspected && signal.ContentId == ContentIds.MarkerHullLine)
            {
                _hullLine.Stop();
            }
            else if (signal.Kind == ProgressChangeKind.Collected && signal.ContentId == ContentIds.DiscoveryBrassTag)
            {
                _radioLadder.Reset();
            }
        }

        private void Say(HintTier tier)
        {
            if (tier.GrantsItemId != null)
            {
                // She picks it up herself. Refused only when it is already carried, which means
                // the words still stand: "there, banded" is true of the one in the bag.
                _commands.Dispatch(new TakeItemCommand(tier.GrantsItemId));
            }

            var result = tier.RecordsMarkerId != null
                ? _commands.Dispatch(new InspectCommand(tier.RecordsMarkerId, tier.RemarkId))
                : _commands.Dispatch(new RemarkCommand(tier.RemarkId));

            if (!result.Success)
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.UnknownCommand, "hint refused: " + tier.RemarkId + " " + result.Code);
                }

                return;
            }

            Said++;

            if (tier.BeginsSweep)
            {
                // Tier 4 does something as well as says something. Refused (the set stopped
                // working is not a thing, but the voice being heard between rungs is) means the
                // words stand and the set stays put.
                var sweep = _commands.Dispatch(new BeginSweepCommand());
                if (!sweep.Success && _log != null)
                {
                    _log.Warn(LogCode.UnknownCommand, "sweep refused: " + sweep.Code);
                }
            }
        }
    }
}
