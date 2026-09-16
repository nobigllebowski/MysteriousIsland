using ForgottenIsle.Core.Audio;
using ForgottenIsle.Core.Radio;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE TIER. The sounds the game makes from arithmetic, and the radio's mix.</summary>
    [TestFixture]
    public sealed class AudioTests
    {
        private static void AssertInRange(float[] data, string what)
        {
            for (var i = 0; i < data.Length; i++)
            {
                Assert.That(data[i], Is.InRange(-1f, 1f), what + " sample " + i);
            }
        }

        [Test]
        public void Noise_IsBounded_AndDeterministicBySeed()
        {
            var a = new float[4096];
            var b = new float[4096];
            var c = new float[4096];
            Synth.Noise(a, 7, 1f);
            Synth.Noise(b, 7, 1f);
            Synth.Noise(c, 8, 1f);

            AssertInRange(a, "noise");
            Assert.That(b, Is.EqualTo(a), "Same seed, same buffer: a bug can be reproduced.");
            Assert.That(c, Is.Not.EqualTo(a));

            var sum = 0f;
            for (var i = 0; i < a.Length; i++)
            {
                sum += a[i];
            }

            Assert.That(sum / a.Length, Is.EqualTo(0f).Within(0.05f), "Centred.");
        }

        [Test]
        public void LowPass_TakesTheTopOff()
        {
            var data = new float[4096];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = i % 2 == 0 ? 1f : -1f;
            }

            Synth.LowPass(data, 200f);

            var peak = 0f;
            for (var i = 100; i < data.Length; i++)
            {
                peak = System.Math.Max(peak, System.Math.Abs(data[i]));
            }

            Assert.That(peak, Is.LessThan(0.05f), "A 22 kHz square through a 200 Hz pole is nearly nothing.");
        }

        [Test]
        public void Sine_LoopsCleanly_WholeCycles()
        {
            var data = new float[Synth.SampleRate];
            Synth.Sine(data, 620f, 1f);

            AssertInRange(data, "sine");
            Assert.That(data[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(data[data.Length - 1], Is.EqualTo(0f).Within(0.1f), "The last sample is near the loop point.");
        }

        [Test]
        public void Strike_Decays_AndChertOutlastsBasalt()
        {
            var basalt = new float[Synth.SampleRate / 5];
            var chert = new float[Synth.SampleRate];
            Synth.Strike(basalt, 1, 180f, 0.05f, 0.8f, 0.9f);
            Synth.Strike(chert, 1, 2400f, 0.35f, 0.15f, 0.7f);
            AssertInRange(basalt, "knock");
            AssertInRange(chert, "ring");

            var at = Synth.SampleRate / 4;
            Assert.That(Peak(basalt, basalt.Length - 2000, basalt.Length), Is.LessThan(0.05f), "The knock is gone in a fifth of a second.");
            Assert.That(Peak(chert, at, at + 2000), Is.GreaterThan(0.2f), "The ring is still there at a quarter second.");
        }

        [Test]
        public void Crackle_IsBounded_AndNotSilent()
        {
            var data = new float[Synth.SampleRate * 2];
            Synth.Crackle(data, 19, 14f, 1f);
            AssertInRange(data, "crackle");
            Assert.That(Peak(data, 0, data.Length), Is.GreaterThan(0.2f));
        }

        [Test]
        public void ThePressureCycle_IsElevenMinutes_AndMinusThirtyEight()
        {
            Assert.That(Synth.PressureCycle(0d), Is.EqualTo(0f).Within(0.001f));
            Assert.That(Synth.PressureCycle(330d), Is.EqualTo(1f).Within(0.001f), "Peak at half the cycle.");
            Assert.That(Synth.PressureCycle(660d), Is.EqualTo(0f).Within(0.001f), "Back at eleven minutes.");
            Assert.That(Synth.GainFor(-38f), Is.EqualTo(0.0126f).Within(0.0005f));
            Assert.That(Synth.GainFor(0f), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TheHooksRise_TakesFourSeconds_Holds_AndLetsGo()
        {
            Assert.That(Synth.HookRise(-1d), Is.Zero, "Before the vine: nothing.");
            Assert.That(Synth.HookRise(0d), Is.Zero);
            Assert.That(Synth.HookRise(2d), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(Synth.HookRise(4d), Is.EqualTo(1f).Within(0.001f), "The peak, at four seconds.");
            Assert.That(Synth.HookRise(6d), Is.EqualTo(1f), "Held.");
            Assert.That(Synth.HookRise(8d + 6d), Is.EqualTo(0.5f).Within(0.001f), "Halfway back.");
            Assert.That(Synth.HookRise(8d + 12d), Is.Zero, "Gone into the cycle again.");
            Assert.That(Synth.HookRise(1000d), Is.Zero);
        }

        // --- the mix ---------------------------------------------------------------------------

        [Test]
        public void TheMix_FollowsTheToleranceLadder()
        {
            var grass = RadioMix.For(Reception.Grass, 0f, 100f);
            var smudge = RadioMix.For(Reception.Smudge, 0.1f, 8f);
            var detuned = RadioMix.For(Reception.Detuned, 0.5f, 2f);
            var locked = RadioMix.For(Reception.Locked, 1f, 0f);

            Assert.That(grass.Carrier, Is.Zero, "Grass is hiss.");
            Assert.That(grass.Hiss, Is.EqualTo(RadioMix.HissInGrass));
            Assert.That(smudge.Warble, Is.True, "There's a person in there somewhere.");
            Assert.That(smudge.Carrier, Is.GreaterThan(0f).And.LessThan(detuned.Carrier));
            Assert.That(detuned.Pitch, Is.GreaterThan(1f), "The donald-duck.");
            Assert.That(detuned.Warble, Is.False);
            Assert.That(locked.Pitch, Is.EqualTo(1f));
            Assert.That(locked.Carrier, Is.EqualTo(1f));
            Assert.That(locked.Hiss, Is.EqualTo(RadioMix.HissOnLock), "Ducked, not gone.");
            Assert.That(locked.Hiss, Is.LessThan(detuned.Hiss).And.LessThan(smudge.Hiss).And.LessThan(grass.Hiss));
        }

        [Test]
        public void TheMix_AtTheNeedle_AgreesWithTheTuner()
        {
            Station voice;
            Stations.TryGet(Stations.TheVoice, out voice);

            Assert.That(RadioMix.At(voice.Mhz).Carrier, Is.EqualTo(1f), "On her.");
            Assert.That(RadioMix.At(voice.Mhz + 0.002f).Pitch, Is.GreaterThan(1f), "Two kilohertz off: detuned.");
            Assert.That(RadioMix.At(voice.Mhz + 0.008f).Warble, Is.True, "Eight off: the smudge.");
            Assert.That(RadioMix.At(voice.Mhz + 0.5f).Carrier, Is.Zero, "Half a megahertz off: grass.");
        }

        private static float Peak(float[] data, int from, int to)
        {
            var peak = 0f;
            for (var i = from; i < to && i < data.Length; i++)
            {
                peak = System.Math.Max(peak, System.Math.Abs(data[i]));
            }

            return peak;
        }
    }
}
