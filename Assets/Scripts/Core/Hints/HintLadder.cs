using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;

namespace ForgottenIsle.Core.Hints
{
    /// <summary>
    /// A rung that stages something rather than saying it: the design's tier 2s, "the object
    /// shows itself". Pure presentation; nothing in the record moves.
    /// </summary>
    public enum HintStaging : byte
    {
        /// <summary>Nothing staged. Published, it means: whatever was staged, stop.</summary>
        None = 0,

        /// <summary>She flicks the multitool's spine against a rock at her feet; the dull knock plays.</summary>
        KnockTest = 1,

        /// <summary>The rope, in the tray, visibly sheds a fibre on a six-second loop.</summary>
        RopeSheds = 2
    }

    /// <summary>One rung of a hint ladder: what is said, and how long after the timer last started.</summary>
    public readonly struct HintTier
    {
        /// <summary>A <c>ContentIds</c> remark id: the line said when this tier is due.</summary>
        public readonly string RemarkId;

        /// <summary>Seconds of play since the ladder started or was last reset.</summary>
        public readonly double AfterSeconds;

        /// <summary>
        /// A marker this tier records as it speaks, or null. The hull line's fallback is the one
        /// case: the design has the Slate entry "write itself" when Nadia says the line unprompted.
        /// </summary>
        public readonly string RecordsMarkerId;

        /// <summary>
        /// True when the tier also leaves the radio sweeping by itself: the design's tier 4
        /// safety net, the one hint that does something as well as says something.
        /// </summary>
        public readonly bool BeginsSweep;

        /// <summary>
        /// An item that goes into the player's hands as the tier speaks, or null. The fire's
        /// third rungs: she picks up the chert, she tears the rope apart. She never does the
        /// last step; the strike is still the player's.
        /// </summary>
        public readonly string GrantsItemId;

        /// <summary>What the rung stages instead of saying, when <see cref="RemarkId"/> is null.</summary>
        public readonly HintStaging Staging;

        /// <param name="remarkId">The remark said when due, or null for a rung that only stages.</param>
        /// <param name="afterSeconds">Seconds of play after the last start or reset.</param>
        /// <param name="recordsMarkerId">A marker recorded as the tier speaks, or null.</param>
        /// <param name="beginsSweep">Whether the tier starts the radio's self-sweep.</param>
        /// <param name="grantsItemId">An item handed to the player as the tier speaks, or null.</param>
        /// <param name="staging">What is staged, for a rung with no line.</param>
        public HintTier(
            string remarkId, double afterSeconds, string recordsMarkerId = null, bool beginsSweep = false, string grantsItemId = null,
            HintStaging staging = HintStaging.None)
        {
            RemarkId = remarkId;
            AfterSeconds = afterSeconds;
            RecordsMarkerId = recordsMarkerId;
            BeginsSweep = beginsSweep;
            GrantsItemId = grantsItemId;
            Staging = staging;
        }

        /// <summary>True for a rung that stages something and says nothing.</summary>
        public bool IsStaging => RemarkId == null && Staging != HintStaging.None;
    }

    /// <summary>
    /// A timed ladder of diegetic hints: each rung is said once, in order, when enough play has
    /// passed since the timer last started.
    /// </summary>
    /// <remarks>
    /// The rules are the design's (<c>design/04-first-30-minutes.md</c> §3.5): the timer starts
    /// on a named moment, resets to zero on any meaningful action, and never nags -- a rung said
    /// once is not said again after a reset, because the reset means the player is engaging and
    /// the earlier rung has done its work. Engine-free so the schedule is pinned by a test rather
    /// than by a stopwatch. The ladder counts seconds it is given; what "play" means (real seconds
    /// in the world, not menu time) is the caller's to decide.
    /// <para>
    /// Not a save participant. The design says hint timers reset to zero on load: a player must
    /// never come back to an escalated state and be told the answer they were about to get. So
    /// the ladder's state is by definition worthless across a save, and <see cref="Forget"/> is
    /// what a load does to it. (ADR-0025)
    /// </para>
    /// </remarks>
    public sealed class HintLadder
    {
        private readonly HintTier[] _tiers;
        private readonly bool[] _said;
        private double _elapsed;
        private bool _running;

        /// <param name="tiers">Rungs in ascending order of <see cref="HintTier.AfterSeconds"/>.</param>
        /// <exception cref="ArgumentException">The rungs are not ascending.</exception>
        public HintLadder(params HintTier[] tiers)
        {
            _tiers = tiers ?? Array.Empty<HintTier>();
            _said = new bool[_tiers.Length];
            for (var i = 1; i < _tiers.Length; i++)
            {
                if (_tiers[i].AfterSeconds < _tiers[i - 1].AfterSeconds)
                {
                    throw new ArgumentException("Hint tiers must be in ascending order of time.", nameof(tiers));
                }
            }
        }

        /// <summary>The rungs, in order.</summary>
        public IReadOnlyList<HintTier> Tiers => _tiers;

        /// <summary>Seconds counted since the last start or reset.</summary>
        public double Elapsed => _elapsed;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRunning => _running;

        /// <summary>True when every rung has been said.</summary>
        public bool IsExhausted
        {
            get
            {
                for (var i = 0; i < _said.Length; i++)
                {
                    if (!_said[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Starts the timer from zero. Rungs already said stay said.</summary>
        public void Start()
        {
            _elapsed = 0d;
            _running = true;
        }

        /// <summary>Puts the timer back to zero without stopping it: the player did something.</summary>
        public void Reset()
        {
            _elapsed = 0d;
        }

        /// <summary>Stops the timer: the thing being hinted at has been done.</summary>
        public void Stop()
        {
            _running = false;
        }

        /// <summary>Stops, zeroes and forgets every rung said. What a load does.</summary>
        public void Forget()
        {
            _running = false;
            _elapsed = 0d;
            for (var i = 0; i < _said.Length; i++)
            {
                _said[i] = false;
            }
        }

        /// <summary>
        /// Counts seconds and reports the one rung that has come due, if any.
        /// </summary>
        /// <remarks>
        /// At most one rung per call, lowest first, so a long stall (the app was suspended and
        /// the caller hands over a big delta) says the tiers one at a time on successive calls
        /// rather than all at once.
        /// </remarks>
        /// <param name="seconds">Seconds of play since the last call. Non-positive counts nothing.</param>
        /// <param name="due">The rung now due.</param>
        /// <returns>True when a rung came due.</returns>
        public bool TryAdvance(double seconds, out HintTier due)
        {
            due = default(HintTier);
            if (!_running)
            {
                return false;
            }

            if (seconds > 0d && !double.IsInfinity(seconds) && !double.IsNaN(seconds))
            {
                _elapsed += seconds;
            }

            for (var i = 0; i < _tiers.Length; i++)
            {
                if (_said[i])
                {
                    continue;
                }

                if (_elapsed >= _tiers[i].AfterSeconds)
                {
                    _said[i] = true;
                    due = _tiers[i];
                    return true;
                }

                // Rungs are ascending; the first unsaid one not yet due means none after it is.
                return false;
            }

            return false;
        }
    }

    /// <summary>The ladders the prologue has, with the design's timings.</summary>
    public static class HintLadders
    {
        /// <summary>A dial drag past this many MHz counts as a coarse drag and resets the radio ladder.</summary>
        public const float CoarseDragMhz = 0.1f;

        /// <summary>
        /// The hull line's fallback: if it is never aligned, at 6:00 of the run Nadia says it anyway
        /// and the Slate entry writes itself (§2:40 FAILURE).
        /// </summary>
        public static HintLadder HullLine()
        {
            return new HintLadder(
                new HintTier(ContentIds.RemarkHullLineUnprompted, 360d, ContentIds.MarkerHullLine));
        }

        /// <summary>
        /// The radio's ladder (§3.5), from the moment the set powers up. Tier 1 at 3:00, tier 3 at
        /// 10:00, and the tier 4 safety net at 16:00: she leaves the set on and it sweeps the band
        /// by itself. Tier 2 (the set turns itself over in inspect view) needs an inspect view,
        /// which does not exist; it is absent, not faked with words.
        /// </summary>
        public static HintLadder Radio()
        {
            return new HintLadder(
                new HintTier(ContentIds.RemarkRadioWroteDown, 180d),
                new HintTier(ContentIds.RemarkRadioReadsList, 600d),
                new HintTier(ContentIds.RemarkRadioSweep, 960d, null, beginsSweep: true));
        }

        /// <summary>
        /// The hook (§2): six seconds after the cut vine is read, the pressure cycle having risen
        /// under her, she says it for the first time. Not a hint; the same clock, used once.
        /// </summary>
        public static HintLadder Hook()
        {
            return new HintLadder(new HintTier(ContentIds.RemarkNotTheSea, 6d));
        }

        /// <summary>
        /// The bag (§2:00 FAILURE): forty seconds without taking it and she says so, without
        /// irritation. The surf pushing the bag closer at ninety is animation, and is not built.
        /// </summary>
        public static HintLadder Bag()
        {
            return new HintLadder(new HintTier(ContentIds.RemarkBagFirst, 40d));
        }

        /// <summary>
        /// The fire, no spark yet (§7:10 FAILURE): tier 1 at 2:30 she thinks aloud about the
        /// spine; tier 2 at 4:00 she flicks the spine against a rock at her feet and the dull
        /// knock plays -- the player now knows the test exists; tier 3 at 6:00 she picks up the
        /// chert herself. Runs from the first thing laid at a fire site.
        /// </summary>
        public static HintLadder FireSpark()
        {
            return new HintLadder(
                new HintTier(ContentIds.RemarkFireSpine, 150d),
                new HintTier(null, 240d, null, false, null, HintStaging.KnockTest),
                new HintTier(ContentIds.RemarkFireChert, 360d, null, false, ItemIds.ChertNodule));
        }

        /// <summary>
        /// The fire, sparks but nothing catching: tier 1 at 2:30 "grass is too quick"; tier 2 at
        /// 4:00 the rope, in the tray, sheds a fibre on a loop; tier 3 at 6:00 she tears the rope
        /// apart herself and holds the fibre up. Runs from the first spark.
        /// </summary>
        public static HintLadder FireTinder()
        {
            return new HintLadder(
                new HintTier(ContentIds.RemarkFireGrassTooQuick, 150d),
                new HintTier(null, 240d, null, false, null, HintStaging.RopeSheds),
                new HintTier(ContentIds.RemarkFireTearsRope, 360d, null, false, ItemIds.PolyFibre));
        }
    }
}
