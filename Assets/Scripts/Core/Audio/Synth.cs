using System;

namespace ForgottenIsle.Core.Audio
{
    /// <summary>
    /// Sample generators for the sounds the game makes until sounds are authored.
    /// </summary>
    /// <remarks>
    /// Engine-free: these fill a float buffer in -1..1 and know nothing about clips or sources.
    /// The project ships no audio files and fabricates none (see <c>AudioDirector</c>); what it
    /// can do honestly is compute a hiss, a carrier, a knock, a ring and the surf from arithmetic,
    /// at runtime, deterministically, and say so. A clip that lands in <c>Resources/Audio</c>
    /// replaces the arithmetic with no code change. (ADR-0027)
    /// <para>
    /// Deterministic by seed so a test can pin a buffer and a bug can be reproduced. Nothing here
    /// is tuned by ear yet -- there is no ear in this environment -- and every constant is a
    /// starting point for the day there is one.
    /// </para>
    /// </remarks>
    public static class Synth
    {
        /// <summary>Sample rate every generator assumes. Mobile hardware's native rate.</summary>
        public const int SampleRate = 44100;

        /// <summary>White noise, uniform in -gain..gain.</summary>
        public static void Noise(float[] into, int seed, float gain)
        {
            if (into == null)
            {
                return;
            }

            var state = (uint)(seed * 2654435761u + 0x9E3779B9u) | 1u;
            for (var i = 0; i < into.Length; i++)
            {
                into[i] = Next(ref state) * gain;
            }
        }

        /// <summary>
        /// One-pole low-pass in place: <c>y += k (x - y)</c>. <paramref name="cutoffHz"/> is the
        /// -3 dB point for the sample rate.
        /// </summary>
        public static void LowPass(float[] buffer, float cutoffHz)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return;
            }

            var k = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / SampleRate));
            var y = 0f;
            for (var i = 0; i < buffer.Length; i++)
            {
                y += k * (buffer[i] - y);
                buffer[i] = y;
            }
        }

        /// <summary>A sine at <paramref name="hz"/>, whole cycles so the buffer loops cleanly.</summary>
        public static void Sine(float[] into, float hz, float gain)
        {
            if (into == null || into.Length == 0)
            {
                return;
            }

            // Snap the frequency so an integer number of cycles fits the buffer: a loop point
            // mid-cycle is a click every time round.
            var cycles = Math.Max(1.0, Math.Round(hz * into.Length / (double)SampleRate));
            var step = 2.0 * Math.PI * cycles / into.Length;
            for (var i = 0; i < into.Length; i++)
            {
                into[i] = (float)Math.Sin(i * step) * gain;
            }
        }

        /// <summary>Multiplies the buffer by a slow sine tremolo, 1-depth..1, whole cycles.</summary>
        public static void Tremolo(float[] buffer, float rateHz, float depth)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return;
            }

            var cycles = Math.Max(1.0, Math.Round(rateHz * buffer.Length / (double)SampleRate));
            var step = 2.0 * Math.PI * cycles / buffer.Length;
            for (var i = 0; i < buffer.Length; i++)
            {
                var m = 1.0 - depth * 0.5 * (1.0 - Math.Cos(i * step));
                buffer[i] = (float)(buffer[i] * m);
            }
        }

        /// <summary>
        /// A struck object: a sine at <paramref name="hz"/> under an exponential decay, with a
        /// burst of noise at the front for the contact. Basalt is low, short and mostly noise;
        /// chert is high, long and mostly tone -- the whole clue of the spark problem, as arithmetic.
        /// </summary>
        public static void Strike(float[] into, int seed, float hz, float decaySeconds, float noiseMix, float gain)
        {
            if (into == null || into.Length == 0)
            {
                return;
            }

            var state = (uint)(seed * 2654435761u + 0x9E3779B9u) | 1u;
            var step = 2.0 * Math.PI * hz / SampleRate;
            var tau = Math.Max(0.001, decaySeconds) * SampleRate;
            var contact = SampleRate * 0.004;
            for (var i = 0; i < into.Length; i++)
            {
                var env = Math.Exp(-i / tau);
                var tone = Math.Sin(i * step) * (1.0 - noiseMix);
                var noise = i < contact ? Next(ref state) * noiseMix : Next(ref state) * noiseMix * Math.Exp(-i / (tau * 0.2));
                into[i] = (float)((tone + noise) * env * gain);
            }
        }

        /// <summary>
        /// Fire: sparse impulses through a low-pass, each decaying, over a soft noise floor. Whole
        /// buffer loops; the impulses are seeded so the crackle is the same every time round.
        /// </summary>
        public static void Crackle(float[] into, int seed, float density, float gain)
        {
            if (into == null || into.Length == 0)
            {
                return;
            }

            var state = (uint)(seed * 2654435761u + 0x9E3779B9u) | 1u;
            var floor = 0.08f;
            var y = 0f;
            var pop = 0f;
            var perSample = density / SampleRate;
            for (var i = 0; i < into.Length; i++)
            {
                var r = (Next(ref state) + 1f) * 0.5f;
                if (r < perSample)
                {
                    pop = 0.6f + r / perSample * 0.4f;
                }

                var x = Next(ref state) * (floor + pop);
                pop *= 0.9992f;
                y += 0.12f * (x - y);
                into[i] = y * gain;
            }
        }

        /// <summary>Clamps every sample to -1..1. Generators stay inside on their own; sums may not.</summary>
        public static void Clamp(float[] buffer)
        {
            if (buffer == null)
            {
                return;
            }

            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] > 1f)
                {
                    buffer[i] = 1f;
                }
                else if (buffer[i] < -1f)
                {
                    buffer[i] = -1f;
                }
            }
        }

        /// <summary>The gain for a level in dBFS: -38 dBFS is the pressure cycle's mix.</summary>
        public static float GainFor(float dbfs)
        {
            return (float)Math.Pow(10.0, dbfs / 20.0);
        }

        /// <summary>Volume of the eleven-minute pressure cycle at a moment, 0..1 of its peak gain.</summary>
        /// <remarks>
        /// The sound under everything from the first frame (§2:00): sub-bass breathing on an
        /// eleven-minute cycle. Nobody mentions it for two hours. A clip cannot be eleven minutes;
        /// the breath is a short loop and this is its envelope, driven from the tick.
        /// </remarks>
        public static float PressureCycle(double seconds)
        {
            const double period = 660.0;
            var phase = 2.0 * Math.PI * (seconds / period);
            return (float)(0.5 - 0.5 * Math.Cos(phase));
        }

        /// <summary>Seconds the hook's rise takes to reach its peak (§2, 29:32: "over four seconds").</summary>
        public const double HookRiseSeconds = 4.0;

        /// <summary>Seconds the peak holds before it lets go. The design cuts to black here; we cannot.</summary>
        public const double HookHoldSeconds = 4.0;

        /// <summary>Seconds the rise takes to fall back into the cycle.</summary>
        public const double HookFallSeconds = 12.0;

        /// <summary>
        /// How far the pressure cycle is lifted toward the hook's peak at a moment after the
        /// trigger, 0..1: a four-second rise, a hold, and a slow fall. Zero before and after.
        /// </summary>
        public static float HookRise(double secondsSinceTrigger)
        {
            if (secondsSinceTrigger < 0d)
            {
                return 0f;
            }

            if (secondsSinceTrigger < HookRiseSeconds)
            {
                return (float)(secondsSinceTrigger / HookRiseSeconds);
            }

            var afterHold = secondsSinceTrigger - HookRiseSeconds - HookHoldSeconds;
            if (afterHold <= 0d)
            {
                return 1f;
            }

            return afterHold >= HookFallSeconds ? 0f : (float)(1.0 - afterHold / HookFallSeconds);
        }

        private static float Next(ref uint state)
        {
            // xorshift32: cheap, deterministic, and good enough for noise nobody listens to twice.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 8388607.5f - 1f;
        }
    }
}
