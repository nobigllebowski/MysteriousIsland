// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Time/TickScheduler.cs.
// Adapted for Vardholm: retargeted from whole simulation DAYS to sim-hours. Nation's GameSpeed enum and
// its DaysPerSecond table are gone — Vardholm passes the world-to-real time ratio in directly, so speed
// is a continuous setting owned by the session rather than four hard-coded steps. The fractional
// carry-over and the per-call tick cap are preserved unchanged.

using System;

namespace ForgottenIsle.Core.Time
{
    /// <summary>
    /// Converts elapsed real time into a whole number of simulation ticks to run.
    /// </summary>
    /// <remarks>
    /// WHY an accumulator rather than one tick per frame: the simulation must advance at the same rate on
    /// a 30 Hz phone and a 120 Hz tablet, and tick bodies are too expensive to run every frame. Fractions
    /// carry over between calls, so the long-run tick rate is exact regardless of frame rate or of frames
    /// that deliver an irregular delta.
    /// <para>
    /// WHY <see cref="MaxTicksPerAdvance"/> exists: after a hitch — a long scene load, the app returning
    /// from background — the accumulated backlog could be hundreds of ticks, and running them all in one
    /// frame would stall long enough to trip the OS watchdog. The cap trades simulation fidelity across a
    /// stall for a responsive frame, and DISCARDS the surplus rather than deferring it, so the game does
    /// not then spend the next several seconds catching up at the cap.
    /// </para>
    /// </remarks>
    public sealed class TickScheduler
    {
        /// <summary>Default ceiling on ticks produced by a single <see cref="Advance"/> call.</summary>
        public const int DefaultMaxTicksPerAdvance = 12;

        /// <summary>Real seconds in one real hour, used to convert the world-time ratio into sim hours.</summary>
        private const double SecondsPerHour = 3600.0;

        private readonly double _simHoursPerTick;

        private double _accumulatedTicks;

        /// <param name="simHoursPerTick">
        /// In-fiction hours advanced by one tick. Must be greater than zero.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="simHoursPerTick"/> is zero, negative, or not a number — each of which would
        /// make the tick count infinite or undefined rather than merely wrong.
        /// </exception>
        public TickScheduler(double simHoursPerTick)
        {
            if (!(simHoursPerTick > 0) || double.IsInfinity(simHoursPerTick))
            {
                throw new ArgumentOutOfRangeException(nameof(simHoursPerTick), "simHoursPerTick must be a finite value greater than zero.");
            }

            _simHoursPerTick = simHoursPerTick;
            MaxTicksPerAdvance = DefaultMaxTicksPerAdvance;
        }

        /// <summary>In-fiction hours one tick represents. Fixed for the scheduler's lifetime.</summary>
        public double SimHoursPerTick => _simHoursPerTick;

        /// <summary>
        /// Ceiling on ticks returned by one <see cref="Advance"/> call. Values below 1 are treated as 1 by
        /// <see cref="Advance"/>, since a scheduler that can never tick would freeze the simulation.
        /// </summary>
        public int MaxTicksPerAdvance { get; set; }

        /// <summary>
        /// Returns how many ticks are due, consuming the corresponding time from the accumulator.
        /// </summary>
        /// <param name="realDeltaSeconds">Real seconds elapsed since the previous call.</param>
        /// <param name="worldSecondsPerRealSecond">
        /// How many in-fiction seconds pass per real second. Pass 0 to represent a paused world; the call
        /// then returns 0 and leaves the accumulator untouched, so unpausing resumes mid-tick rather than
        /// losing the partial progress.
        /// </param>
        /// <returns>The number of whole ticks the caller should run now, never negative.</returns>
        public int Advance(double realDeltaSeconds, double worldSecondsPerRealSecond)
        {
            if (realDeltaSeconds <= 0 || worldSecondsPerRealSecond <= 0)
            {
                return 0;
            }

            if (double.IsNaN(realDeltaSeconds) || double.IsNaN(worldSecondsPerRealSecond))
            {
                return 0;
            }

            var simHours = realDeltaSeconds * worldSecondsPerRealSecond / SecondsPerHour;

            // Accumulate in ticks, not in hours: dividing once here keeps the floor below exact even when
            // simHoursPerTick is a value like 1/3 that no binary fraction represents cleanly.
            _accumulatedTicks += simHours / _simHoursPerTick;

            var ticks = (int)Math.Floor(_accumulatedTicks);
            if (ticks <= 0)
            {
                return 0;
            }

            var cap = MaxTicksPerAdvance > 0 ? MaxTicksPerAdvance : 1;
            if (ticks > cap)
            {
                // Drop the backlog outright. Keeping the remainder would stretch the recovery over many
                // frames, and a stall the player already felt should not also slow the next second of play.
                ticks = cap;
                _accumulatedTicks = 0;
            }
            else
            {
                _accumulatedTicks -= ticks;
            }

            return ticks;
        }

        /// <summary>
        /// Discards the fractional carry-over.
        /// </summary>
        /// <remarks>
        /// Call this whenever real time and sim time are deliberately decoupled — after a scene load, on
        /// resume from background, on loading a save — so the first frame afterwards does not cash in a
        /// stale partial tick that belonged to a different moment.
        /// </remarks>
        public void Reset()
        {
            _accumulatedTicks = 0;
        }
    }
}
