using System;
using System.Collections.Generic;
using System.Text;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Time;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Covers the engine-free primitives the rest of the game is built on: the deterministic generator,
    /// the save checksum, vector maths, and the island clock.
    /// </summary>
    /// <remarks>
    /// Everything here is small enough to look obviously correct and consequential enough that being
    /// wrong is catastrophic and silent. A generator whose state does not round-trip produces a loaded
    /// game that diverges from the saved one; a checksum that disagrees with the standard makes every
    /// save written by one build unreadable by another; a normalise that divides by zero seeds a NaN
    /// that propagates into a saved position; a clock that mishandles the day boundary puts the sun in
    /// the wrong place. None of those announce themselves at the call site.
    /// </remarks>
    [TestFixture]
    public sealed class PrimitiveTests
    {
        // ---------------------------------------------------------------- PcgRandom

        /// <summary>
        /// The premise of the whole seeding scheme: a seed names a sequence, not a starting point that
        /// the runtime is free to interpret.
        /// </summary>
        [Test]
        public void PcgRandom_SameSeed_ProducesTheSameSequence()
        {
            var a = new PcgRandom(777UL);
            var b = new PcgRandom(777UL);

            for (var i = 0; i < 256; i++)
            {
                Assert.AreEqual(a.NextUInt64(), b.NextUInt64(), "Sequences diverged at draw " + i);
            }
        }

        [Test]
        public void PcgRandom_DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new PcgRandom(1UL);
            var b = new PcgRandom(2UL);

            var identical = 0;
            for (var i = 0; i < 64; i++)
            {
                if (a.NextUInt64() == b.NextUInt64())
                {
                    identical++;
                }
            }

            Assert.Less(identical, 4, "Two different seeds produced near-identical streams.");
        }

        /// <summary>
        /// Seeds that differ only in their low bits must not produce correlated streams — world seeds
        /// 1, 2, 3 are exactly what a player or a test will hand this thing.
        /// </summary>
        [Test]
        public void PcgRandom_AdjacentIntSeeds_AreDecorrelated()
        {
            var first = new PcgRandom(1).NextUInt64();
            var second = new PcgRandom(2).NextUInt64();
            var third = new PcgRandom(3).NextUInt64();

            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(second, third);
            Assert.AreNotEqual(first, third);
        }

        /// <summary>
        /// A zero seed would sit on xorshift's fixed point and emit zeros forever. The substitution is
        /// what stops "seed 0" from being a broken game rather than a valid one.
        /// </summary>
        [Test]
        public void PcgRandom_ZeroSeed_StillProducesVaryingOutput()
        {
            var random = new PcgRandom(0UL);
            var seen = new HashSet<ulong>();

            for (var i = 0; i < 32; i++)
            {
                seen.Add(random.NextUInt64());
            }

            Assert.Greater(seen.Count, 16, "A zero seed collapsed the generator.");
            Assert.IsFalse(seen.Contains(0UL) && seen.Count == 1);
        }

        /// <summary>
        /// THE save-critical property. Capturing <see cref="PcgRandom.State"/> mid-run and pushing it
        /// back must resume the identical stream — not a stream derived from the same seed, the same
        /// stream at the same position. This is what makes a load O(1) instead of a replay of every
        /// draw the run ever made.
        /// </summary>
        [Test]
        public void PcgRandom_StateRoundTripMidSequence_ResumesTheIdenticalStream()
        {
            var source = new PcgRandom(4242UL);

            for (var i = 0; i < 37; i++)
            {
                source.NextUInt64();
            }

            var captured = source.State;

            var expected = new ulong[64];
            for (var i = 0; i < expected.Length; i++)
            {
                expected[i] = source.NextUInt64();
            }

            // Deliberately seeded differently, so only SetState can account for a match.
            var restored = new PcgRandom(999999UL);
            Assert.AreNotEqual(captured, restored.State);

            restored.SetState(captured);
            Assert.AreEqual(captured, restored.State, "SetState did not land on the captured position.");

            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], restored.NextUInt64(), "Restored stream diverged at draw " + i);
            }
        }

        /// <summary>
        /// A truncated or absent state field in an old save must degrade to a valid stream, not to a
        /// generator that returns zero forever.
        /// </summary>
        [Test]
        public void PcgRandom_SetStateToZero_FallsBackToAValidStream()
        {
            var random = new PcgRandom(5UL);
            random.SetState(0UL);

            Assert.AreNotEqual(0UL, random.State);

            var seen = new HashSet<ulong>();
            for (var i = 0; i < 32; i++)
            {
                seen.Add(random.NextUInt64());
            }

            Assert.Greater(seen.Count, 16);
        }

        /// <summary>
        /// SetState restores an already-mixed internal position and must NOT re-mix it — mixing again
        /// would land somewhere else entirely, which is the subtle way this class breaks.
        /// </summary>
        [Test]
        public void PcgRandom_SetState_DoesNotReapplySeedMixing()
        {
            var random = new PcgRandom(11UL);
            var position = random.State;

            random.NextUInt64();
            random.SetState(position);

            Assert.AreEqual(position, random.State);
        }

        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(-5, 7)]
        [TestCase(1, 100)]
        [TestCase(int.MaxValue - 1, int.MaxValue)]
        public void PcgRandom_NextInt_StaysWithinBoundsOverManyDraws(int minInclusive, int maxExclusive)
        {
            var random = new PcgRandom(20260914UL);

            for (var i = 0; i < 20000; i++)
            {
                var value = random.NextInt(minInclusive, maxExclusive);
                Assert.GreaterOrEqual(value, minInclusive, "Draw " + i + " fell below the minimum.");
                Assert.Less(value, maxExclusive, "Draw " + i + " reached or passed the exclusive maximum.");
            }
        }

        /// <summary>
        /// Bounds alone are not enough: a generator returning the minimum every time would pass them.
        /// A wide range must actually be covered.
        /// </summary>
        [Test]
        public void PcgRandom_NextInt_CoversItsWholeRange()
        {
            var random = new PcgRandom(8675309UL);
            var seen = new HashSet<int>();

            for (var i = 0; i < 20000; i++)
            {
                seen.Add(random.NextInt(-5, 7));
            }

            Assert.AreEqual(12, seen.Count, "Not every value in [-5, 7) was produced.");
        }

        [Test]
        public void PcgRandom_NextIntWithEmptyOrInvertedRange_Throws()
        {
            var random = new PcgRandom(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(3, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(9, 4));
        }

        [Test]
        public void PcgRandom_NextDouble_StaysInTheUnitInterval()
        {
            var random = new PcgRandom(31337UL);
            var sawBelowHalf = false;
            var sawAtOrAboveHalf = false;

            for (var i = 0; i < 20000; i++)
            {
                var value = random.NextDouble();
                Assert.GreaterOrEqual(value, 0d);
                Assert.Less(value, 1d, "NextDouble returned 1.0, which the half-open range forbids.");

                if (value < 0.5d)
                {
                    sawBelowHalf = true;
                }
                else
                {
                    sawAtOrAboveHalf = true;
                }
            }

            Assert.IsTrue(sawBelowHalf && sawAtOrAboveHalf, "NextDouble is not spread across the interval.");
        }

        /// <summary>
        /// A certainty is not a random decision. If probability 0 or 1 consumed a draw, switching a
        /// feature off would shift every subsequent unrelated roll in the shared stream.
        /// </summary>
        [Test]
        public void PcgRandom_ChanceAtCertainty_ConsumesNoDraw()
        {
            var random = new PcgRandom(64UL);
            var before = random.State;

            Assert.IsFalse(random.Chance(0d));
            Assert.IsFalse(random.Chance(-1d));
            Assert.IsTrue(random.Chance(1d));
            Assert.IsTrue(random.Chance(2d));

            Assert.AreEqual(before, random.State, "A certain outcome advanced the shared stream.");
        }

        [Test]
        public void PcgRandom_Chance_ProducesBothOutcomesAndAdvancesTheStream()
        {
            var random = new PcgRandom(97UL);
            var before = random.State;
            var trueCount = 0;

            for (var i = 0; i < 4000; i++)
            {
                if (random.Chance(0.5d))
                {
                    trueCount++;
                }
            }

            Assert.AreNotEqual(before, random.State, "An uncertain roll did not advance the stream.");
            Assert.Greater(trueCount, 1600);
            Assert.Less(trueCount, 2400);
        }

        /// <summary>
        /// Derived streams exist so one system's draw count cannot shift another system's results.
        /// Different indices must therefore give different starting positions, and the derivation must
        /// be reproducible across calls.
        /// </summary>
        [Test]
        public void PcgRandomDerive_DifferentStreamIndices_ProduceDifferentSeeds()
        {
            var zero = PcgRandom.Derive(42, 0);
            var one = PcgRandom.Derive(42, 1);
            var two = PcgRandom.Derive(42, 2);
            var far = PcgRandom.Derive(42, 1_000_000);

            Assert.AreNotEqual(zero, one);
            Assert.AreNotEqual(one, two);
            Assert.AreNotEqual(zero, two);
            Assert.AreNotEqual(zero, far);
        }

        [Test]
        public void PcgRandomDerive_DifferentWorldSeeds_ProduceDifferentSeeds()
        {
            Assert.AreNotEqual(PcgRandom.Derive(1, 0), PcgRandom.Derive(2, 0));
            Assert.AreNotEqual(PcgRandom.Derive(-1, 0), PcgRandom.Derive(1, 0));
        }

        [Test]
        public void PcgRandomDerive_SameInputs_AreReproducible()
        {
            Assert.AreEqual(PcgRandom.Derive(42, 7), PcgRandom.Derive(42, 7));
        }

        /// <summary>
        /// Two subsystems on derived streams must not shadow each other's draws.
        /// </summary>
        [Test]
        public void PcgRandomDerive_DerivedStreams_AreIndependent()
        {
            var weather = new PcgRandom(PcgRandom.Derive(42, 1));
            var loot = new PcgRandom(PcgRandom.Derive(42, 2));

            var collisions = 0;
            for (var i = 0; i < 64; i++)
            {
                if (weather.NextUInt64() == loot.NextUInt64())
                {
                    collisions++;
                }
            }

            Assert.Less(collisions, 4);
        }

        // ---------------------------------------------------------------- Crc32

        /// <summary>
        /// Checked against the published CRC-32/ISO-HDLC vectors rather than against this
        /// implementation's own output. A self-consistent-but-nonstandard CRC would pass a round-trip
        /// test and still make saves unreadable across a format change or an external tool.
        /// </summary>
        [TestCase("", 0x00000000u)]
        [TestCase("a", 0xE8B7BE43u)]
        [TestCase("abc", 0x352441C2u)]
        [TestCase("123456789", 0xCBF43926u)]
        [TestCase("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
        public void Crc32_KnownVectors_MatchTheStandard(string input, uint expected)
        {
            Assert.AreEqual(expected, Crc32.Compute(input));
        }

        [Test]
        public void Crc32_NullInput_YieldsTheEmptyChecksum()
        {
            Assert.AreEqual(0u, Crc32.Compute((string)null));
            Assert.AreEqual(0u, Crc32.Compute((byte[])null));
            Assert.AreEqual(0u, Crc32.Compute(new byte[0]));
        }

        /// <summary>
        /// The encoding is pinned to UTF-8 so a save written on one device verifies on another. Proven
        /// by feeding the byte overload UTF-8 bytes and getting the string overload's answer.
        /// </summary>
        [Test]
        public void Crc32_StringAndUtf8Bytes_AgreeIncludingNonAscii()
        {
            const string text = "Vardholm — Day 1, 19:30";
            var bytes = Encoding.UTF8.GetBytes(text);

            Assert.AreEqual(Crc32.Compute(bytes), Crc32.Compute(text));
        }

        /// <summary>
        /// The realistic mobile failure is a truncated or partially flushed file, so the checksum must
        /// change when a byte changes and when the payload is cut short.
        /// </summary>
        [Test]
        public void Crc32_SingleCharacterChange_ChangesTheChecksum()
        {
            var intact = Crc32.Compute("{\"zoneId\":\"ZoneRibcage\"}");
            var altered = Crc32.Compute("{\"zoneId\":\"ZoneRibcase\"}");
            var truncated = Crc32.Compute("{\"zoneId\":\"ZoneRibcage\"");

            Assert.AreNotEqual(intact, altered);
            Assert.AreNotEqual(intact, truncated);
        }

        /// <summary>
        /// The final inversion is what makes trailing zero bytes matter. Without it, a file padded with
        /// NULs by a failed flush would check out as intact.
        /// </summary>
        [Test]
        public void Crc32_TrailingZeroBytes_ChangeTheChecksum()
        {
            var payload = new byte[] { 1, 2, 3 };
            var padded = new byte[] { 1, 2, 3, 0, 0 };

            Assert.AreNotEqual(Crc32.Compute(payload), Crc32.Compute(padded));
        }

        [Test]
        public void Crc32_RangeOverload_ClampsInsteadOfReadingPastTheBuffer()
        {
            var buffer = Encoding.UTF8.GetBytes("123456789padding");

            Assert.AreEqual(0xCBF43926u, Crc32.Compute(buffer, 0, 9), "A pooled-buffer range did not match the vector.");
            Assert.DoesNotThrow(() => Crc32.Compute(buffer, 0, buffer.Length + 100));
            Assert.DoesNotThrow(() => Crc32.Compute(buffer, -5, 4));
            Assert.AreEqual(0u, Crc32.Compute(buffer, buffer.Length + 10, 4), "An out-of-range start did not degrade to the empty checksum.");
        }

        // ---------------------------------------------------------------- Vec3 / Vec3Math

        /// <summary>
        /// A zero vector has no direction, so normalising it is undefined. Returning
        /// <see cref="Vec3.Zero"/> rather than dividing is what keeps a NaN out of a saved position —
        /// where it would serialize as 0 and silently teleport the player to the origin.
        /// </summary>
        [Test]
        public void Vec3MathNormalize_ZeroVector_ReturnsZeroWithoutDividingByZero()
        {
            var result = Vec3Math.Normalize(Vec3.Zero);

            Assert.AreEqual(Vec3.Zero, result);
            Assert.IsFalse(float.IsNaN(result.X) || float.IsNaN(result.Y) || float.IsNaN(result.Z), "Normalize produced a NaN.");
            Assert.IsFalse(float.IsInfinity(result.X) || float.IsInfinity(result.Y) || float.IsInfinity(result.Z), "Normalize produced an infinity.");
        }

        [Test]
        public void Vec3MathNormalize_VectorBelowEpsilon_ReturnsZeroWithoutNaN()
        {
            var result = Vec3Math.Normalize(new Vec3(1e-9f, -1e-9f, 1e-9f));

            Assert.AreEqual(Vec3.Zero, result);
            Assert.IsFalse(float.IsNaN(result.X) || float.IsNaN(result.Y) || float.IsNaN(result.Z));
        }

        [Test]
        public void Vec3MathNormalize_OrdinaryVector_ReturnsUnitLength()
        {
            var result = Vec3Math.Normalize(new Vec3(3f, 0f, 4f));

            Assert.That(result.X, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(result.Y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(result.Z, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(Vec3Math.Length(result), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Vec3Math_DistanceAndLength_AgreeWithTheirSquaredForms()
        {
            var a = new Vec3(1f, 2f, 3f);
            var b = new Vec3(4f, 6f, 3f);

            Assert.That(Vec3Math.DistanceSquared(a, b), Is.EqualTo(25f).Within(1e-4f));
            Assert.That(Vec3Math.Distance(a, b), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(Vec3Math.LengthSquared(new Vec3(0f, 3f, 4f)), Is.EqualTo(25f).Within(1e-4f));
            Assert.That(Vec3Math.Length(new Vec3(0f, 3f, 4f)), Is.EqualTo(5f).Within(1e-5f));
        }

        /// <summary>
        /// Callers feed <c>t</c> from elapsed/duration ratios that overshoot on a long frame. Clamping
        /// is what stops that from flinging the player past the target.
        /// </summary>
        [Test]
        public void Vec3MathLerp_OutOfRangeFactors_AreClampedToTheEndpoints()
        {
            var a = new Vec3(0f, 0f, 0f);
            var b = new Vec3(10f, 20f, -30f);

            Assert.AreEqual(a, Vec3Math.Lerp(a, b, -3f));
            Assert.AreEqual(b, Vec3Math.Lerp(a, b, 4f));
            Assert.AreEqual(a, Vec3Math.Lerp(a, b, 0f));
            Assert.AreEqual(b, Vec3Math.Lerp(a, b, 1f));

            var midpoint = Vec3Math.Lerp(a, b, 0.5f);
            Assert.That(midpoint.X, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(midpoint.Y, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(midpoint.Z, Is.EqualTo(-15f).Within(1e-5f));
        }

        [Test]
        public void Vec3_Equality_IsExactAndConsistentWithHashing()
        {
            var a = new Vec3(1.5f, -2.25f, 3.75f);
            var b = new Vec3(1.5f, -2.25f, 3.75f);
            var c = new Vec3(1.5f, -2.25f, 3.75001f);

            // Guard the fixture itself: a drift value that rounds back onto 3.75f would make the
            // inequality assertion below vacuous rather than failing.
            Assert.AreNotEqual(3.75f, 3.75001f, "Fixture is broken: the drifted component is the same float.");

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsTrue(a != c, "Exact comparison must not tolerate drift.");
            Assert.AreEqual(Vec3.Zero, default(Vec3));
        }

        // ---------------------------------------------------------------- LocKey

        [Test]
        public void LocKey_NullValue_IsNormalisedToEmptyAndNeverReturnsNull()
        {
            var key = new LocKey(null);

            Assert.AreEqual(string.Empty, key.Value);
            Assert.IsTrue(key.IsEmpty);
            Assert.AreEqual(string.Empty, key.ToString());
            Assert.AreEqual(LocKey.Empty, key);
        }

        [Test]
        public void LocKey_Comparison_IsOrdinalAndCaseSensitive()
        {
            Assert.AreEqual(new LocKey("ui.menu.continue"), new LocKey("ui.menu.continue"));
            Assert.IsTrue(new LocKey("ui.a") != new LocKey("ui.A"), "Keys are identifiers; casing is significant.");
            Assert.AreEqual("ui.a", new LocKey("ui.a").ToString());
        }

        // ---------------------------------------------------------------- IslandClock

        [Test]
        public void IslandClock_NewClock_StartsOnDayOneAtMidnight()
        {
            var clock = new IslandClock();

            Assert.AreEqual(0d, clock.SimHours);
            Assert.AreEqual(1, clock.Day, "The shipwreck happens on Day 1, not Day 0.");
            Assert.AreEqual(0, clock.Hour);
            Assert.AreEqual(0, clock.Minute);
        }

        [TestCase(0d, 1, 0, 0)]
        [TestCase(5.25d, 1, 5, 15)]
        [TestCase(19.5d, 1, 19, 30)]
        [TestCase(23.5d, 1, 23, 30)]
        [TestCase(24.0d, 2, 0, 0)]
        [TestCase(24.5d, 2, 0, 30)]
        [TestCase(47.75d, 2, 23, 45)]
        [TestCase(48.0d, 3, 0, 0)]
        [TestCase(49.75d, 3, 1, 45)]
        [TestCase(240.25d, 11, 0, 15)]
        public void IslandClock_SetSimHours_DerivesDayHourAndMinute(double simHours, int day, int hour, int minute)
        {
            var clock = new IslandClock();
            clock.SetSimHours(simHours);

            Assert.AreEqual(simHours, clock.SimHours);
            Assert.AreEqual(day, clock.Day, "Day");
            Assert.AreEqual(hour, clock.Hour, "Hour");
            Assert.AreEqual(minute, clock.Minute, "Minute");
        }

        /// <summary>
        /// The boundary that actually breaks: exactly 24.0 must be the first moment of Day 2, not the
        /// twenty-fifth hour of Day 1.
        /// </summary>
        [Test]
        public void IslandClock_ExactlyTwentyFourHours_RollsOverToDayTwoMidnight()
        {
            var clock = new IslandClock();
            clock.Advance(24.0d);

            Assert.AreEqual(24.0d, clock.SimHours);
            Assert.AreEqual(2, clock.Day);
            Assert.AreEqual(0, clock.Hour);
            Assert.AreEqual(0, clock.Minute);
        }

        [Test]
        public void IslandClock_AdvanceAcrossManyDays_AccumulatesAndRollsOverEachTime()
        {
            var clock = new IslandClock();

            for (var day = 1; day <= 10; day++)
            {
                Assert.AreEqual(day, clock.Day, "Day count drifted on day " + day);
                Assert.AreEqual(0, clock.Hour);
                clock.Advance(24.0d);
            }

            Assert.AreEqual(11, clock.Day);
            Assert.AreEqual(240.0d, clock.SimHours);
        }

        /// <summary>
        /// <see cref="IslandClock.Hour"/> and <see cref="IslandClock.Minute"/> must never report 24 or
        /// 60. Rounding up would let a displayed minute run ahead of the sim time that gates an event.
        /// </summary>
        [Test]
        public void IslandClock_NearDayBoundary_NeverReportsHour24OrMinute60()
        {
            var clock = new IslandClock();

            for (var i = 0; i < 2000; i++)
            {
                clock.SetSimHours(23.0d + (i * 0.001d));
                Assert.GreaterOrEqual(clock.Hour, 0);
                Assert.LessOrEqual(clock.Hour, 23, "Hour reached 24 at simHours " + clock.SimHours);
                Assert.GreaterOrEqual(clock.Minute, 0);
                Assert.LessOrEqual(clock.Minute, 59, "Minute reached 60 at simHours " + clock.SimHours);
            }
        }

        /// <summary>
        /// The clock must never run backwards: callers derive the delta from a real frame time, which
        /// is legitimately zero on a paused or stalled frame.
        /// </summary>
        [Test]
        public void IslandClock_AdvanceWithNonPositiveOrNaN_IsIgnored()
        {
            var clock = new IslandClock();
            clock.Advance(6.0d);

            clock.Advance(0d);
            clock.Advance(-5d);
            clock.Advance(double.NaN);

            Assert.AreEqual(6.0d, clock.SimHours);
        }

        /// <summary>
        /// A corrupt save must not be able to poison every later division with a negative or NaN time.
        /// </summary>
        [Test]
        public void IslandClock_SetSimHoursWithNegativeOrNaN_ClampsToZero()
        {
            var clock = new IslandClock(10d);

            clock.SetSimHours(-1d);
            Assert.AreEqual(0d, clock.SimHours);
            Assert.AreEqual(1, clock.Day);

            clock.SetSimHours(7d);
            clock.SetSimHours(double.NaN);
            Assert.AreEqual(0d, clock.SimHours);
        }

        [Test]
        public void IslandClock_NegativeStartSimHours_ClampsToZero()
        {
            var clock = new IslandClock(-100d);

            Assert.AreEqual(0d, clock.SimHours);
            Assert.AreEqual(1, clock.Day);
        }

        /// <summary>
        /// Developer-facing only, and pinned because it goes into logs and save-file inspection where a
        /// changed shape breaks whatever is grepping it. Invariant culture, zero-padded.
        /// </summary>
        [Test]
        public void IslandClock_ToDisplayString_RendersDayAndZeroPaddedTime()
        {
            var clock = new IslandClock();

            clock.SetSimHours(19.5d);
            Assert.AreEqual("Day 1, 19:30", clock.ToDisplayString());
            Assert.AreEqual(clock.ToDisplayString(), clock.ToString());

            clock.SetSimHours(24.0d);
            Assert.AreEqual("Day 2, 00:00", clock.ToDisplayString());

            clock.SetSimHours(49.1d);
            StringAssert.StartsWith("Day 3, 01:0", clock.ToDisplayString());
        }

        /// <summary>
        /// <see cref="IClock"/> deliberately exposes no way to move time, so the "only the session
        /// mutates state" rule is enforced by the type system rather than by convention.
        /// </summary>
        [Test]
        public void IslandClock_ViewedAsIClock_ReportsTheSameSimHours()
        {
            var clock = new IslandClock(3.5d);
            IClock view = clock;

            Assert.AreEqual(3.5d, view.SimHours);
            clock.Advance(1.5d);
            Assert.AreEqual(5.0d, view.SimHours);
        }
    }
}
