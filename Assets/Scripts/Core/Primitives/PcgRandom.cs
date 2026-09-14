// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Utilities/DeterministicRandom.cs.
// Adapted for Vardholm: renamed to PcgRandom and moved to Core.Primitives, plus State/SetState so the
// generator's position round-trips through GameState.RngState and a reloaded save keeps drawing the
// same sequence it would have drawn had the player never quit.

using System;

namespace ForgottenIsle.Core.Primitives
{
    /// <summary>
    /// Seeded xorshift64* generator driving every random decision in the simulation.
    /// </summary>
    /// <remarks>
    /// WHY not <see cref="Random"/>: .NET makes no guarantee that a given seed yields the same sequence
    /// across runtimes or framework versions, and this game ships on Mono in the editor and IL2CPP on
    /// device. A hand-rolled generator is the only way a seed means the same island everywhere.
    /// <para>
    /// WHY it is stateful and mutable rather than a struct: the single live instance is owned by the
    /// session and its position is part of the save. A value type would make it far too easy to advance a
    /// copy, silently desyncing the saved state from the state that produced the last draw.
    /// </para>
    /// <para>
    /// NOT thread-safe. One instance per logical stream; use <see cref="Derive"/> to split streams.
    /// </para>
    /// </remarks>
    public sealed class PcgRandom
    {
        /// <summary>
        /// Substituted for a zero seed. WHY: xorshift is a linear map with zero as a fixed point, so a
        /// zero state emits zeros forever. This is the golden-ratio constant used by SplitMix64.
        /// </summary>
        private const ulong NonZeroSeedSubstitute = 0x9E3779B97F4A7C15UL;

        private ulong _state;

        /// <param name="seed">
        /// Any 64-bit value. It is run through SplitMix64 before use, which both avoids the zero fixed
        /// point and decorrelates seeds that differ only in their low bits (world seeds 1, 2, 3...).
        /// </param>
        public PcgRandom(ulong seed)
        {
            _state = SplitMix64(seed == 0 ? NonZeroSeedSubstitute : seed);
        }

        /// <summary>Convenience overload for the signed seeds that authored content and UI produce.</summary>
        public PcgRandom(int seed)
            : this(unchecked((ulong)(uint)seed))
        {
        }

        /// <summary>
        /// The generator's current position.
        /// </summary>
        /// <remarks>
        /// WHY exposed (this is the Vardholm addition to the Nation original): saving a seed alone is not
        /// enough to resume a game, because reproducing the stream would require replaying every draw made
        /// since the game began. Persisting the raw state instead makes a load O(1) and exact. The value is
        /// opaque — never derive gameplay from it, only hand it back to <see cref="SetState"/>.
        /// </remarks>
        public ulong State => _state;

        /// <summary>
        /// Restores a position previously read from <see cref="State"/>.
        /// </summary>
        /// <param name="state">
        /// A previously captured state. Zero is replaced with the same substitute the constructor uses,
        /// so a truncated or absent field in an old save degrades to a valid stream rather than to a
        /// generator that returns nothing but zero.
        /// </param>
        /// <remarks>
        /// SplitMix64 is deliberately NOT applied here: the constructor mixes a user-chosen seed, this
        /// method restores an already-mixed internal position. Mixing again would land somewhere else.
        /// </remarks>
        public void SetState(ulong state)
        {
            _state = state == 0 ? SplitMix64(NonZeroSeedSubstitute) : state;
        }

        /// <summary>
        /// Derives an independent stream seed for a subsystem or tick, so one system's draw count never
        /// shifts another system's results.
        /// </summary>
        /// <param name="worldSeed">The seed identifying this playthrough's world.</param>
        /// <param name="streamIndex">A stable index for the consumer — a tick number, a subsystem id.</param>
        public static ulong Derive(int worldSeed, long streamIndex)
        {
            return SplitMix64(unchecked((ulong)(uint)worldSeed * 0xBF58476D1CE4E5B9UL + (ulong)streamIndex));
        }

        /// <summary>Advances the generator and returns the next raw 64-bit draw.</summary>
        public ulong NextUInt64()
        {
            var x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return unchecked(x * 0x2545F4914F6CDD1DUL);
        }

        /// <summary>
        /// Uniform double in [0, 1).
        /// </summary>
        /// <remarks>
        /// Takes the top 53 bits, which is exactly the mantissa width of a double, so every representable
        /// value in the range is reachable and none is favoured by rounding.
        /// </remarks>
        public double NextDouble()
        {
            return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        /// <exception cref="ArgumentOutOfRangeException">The range is empty or inverted.</exception>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            // The long cast matters: int.MaxValue - int.MinValue overflows an int.
            var range = (ulong)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)(NextUInt64() % range));
        }

        /// <summary>Uniform double in [minInclusive, maxInclusive].</summary>
        public double NextRange(double minInclusive, double maxInclusive)
        {
            return minInclusive + ((maxInclusive - minInclusive) * NextDouble());
        }

        /// <summary>
        /// True with the given probability in [0, 1].
        /// </summary>
        /// <remarks>
        /// The 0 and 1 shortcuts consume no draw. WHY that is correct rather than a subtle desync: a
        /// certainty is not a random decision, and letting a disabled feature (probability 0) silently
        /// advance the shared stream would change every subsequent unrelated roll.
        /// </remarks>
        public bool Chance(double probability)
        {
            if (probability <= 0)
            {
                return false;
            }

            if (probability >= 1)
            {
                return true;
            }

            return NextDouble() < probability;
        }

        /// <summary>Avalanche mix used for seeding; spreads clustered inputs across all 64 bits.</summary>
        private static ulong SplitMix64(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
