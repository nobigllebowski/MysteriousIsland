using System;

namespace ForgottenIsle.Core.Radio
{
    /// <summary>What the set is receiving at a given needle position.</summary>
    public enum Reception : byte
    {
        /// <summary>Hiss. Nothing within twelve kilohertz.</summary>
        Grass = 0,

        /// <summary>A smear on the ribbon and a warble under the hiss. Something is in there.</summary>
        Smudge = 1,

        /// <summary>A voice, present and unintelligible. The classic pitch-shifted detune.</summary>
        Detuned = 2,

        /// <summary>On it. Clean carrier, full content, needle snapped to centre.</summary>
        Locked = 3
    }

    /// <summary>
    /// The arithmetic of tuning: what is heard where, and what the ribbon shows.
    /// </summary>
    /// <remarks>
    /// Pure functions, engine-free, deterministic. The panel draws from these and the handler
    /// decides from these, so the ribbon a player reads and the lock the game grants are the
    /// same function of the same number — a puzzle whose visual and its rule can never drift apart.
    /// <para>
    /// The tolerance ladder is the design's (§3.3): ±0.8 kHz lock, ±4 detuned, ±12 smudge, grass
    /// beyond. Lock is generous on purpose: this is not a dexterity test.
    /// </para>
    /// </remarks>
    public static class RadioTuner
    {
        /// <summary>
        /// Signal strength at a frequency, 0..1, from the nearest carrier.
        /// </summary>
        /// <remarks>
        /// A Lorentzian rather than a Gaussian: it has the long shoulders a real receiver's
        /// selectivity curve has, which is what puts a faint smudge on the ribbon well before the
        /// voice becomes intelligible and pulls the player in by their own eye.
        /// </remarks>
        public static float Strength(float mhz)
        {
            var best = 0f;
            var all = Stations.All;
            for (var i = 0; i < all.Length; i++)
            {
                var offsetKhz = (mhz - all[i].Mhz) * 1000f;
                var x = offsetKhz / RadioBand.DetunedHalfWidthKhz;
                var strength = 1f / (1f + x * x);
                if (strength > best)
                {
                    best = strength;
                }
            }

            return best;
        }

        /// <summary>
        /// What is being received at a frequency, and from which station.
        /// </summary>
        /// <param name="mhz">Needle position.</param>
        /// <param name="stationId">The nearest station's id, or null in the grass.</param>
        /// <returns>The reception tier.</returns>
        public static Reception Receive(float mhz, out string stationId)
        {
            stationId = null;
            var nearestKhz = float.MaxValue;

            var all = Stations.All;
            for (var i = 0; i < all.Length; i++)
            {
                var offsetKhz = Math.Abs((mhz - all[i].Mhz) * 1000f);
                if (offsetKhz < nearestKhz)
                {
                    nearestKhz = offsetKhz;
                    stationId = all[i].Id;
                }
            }

            if (nearestKhz <= RadioBand.LockHalfWidthKhz)
            {
                return Reception.Locked;
            }

            if (nearestKhz <= RadioBand.DetunedHalfWidthKhz)
            {
                return Reception.Detuned;
            }

            if (nearestKhz <= RadioBand.SmudgeHalfWidthKhz)
            {
                return Reception.Smudge;
            }

            stationId = null;
            return Reception.Grass;
        }

        /// <summary>
        /// The magnetic detent: inside the lock window the needle settles on the carrier exactly.
        /// </summary>
        /// <remarks>
        /// So the player cannot lose a lock by breathing on the screen. Outside the window the
        /// frequency is returned untouched — the detent is a feature of being on a station, not a
        /// general quantisation of the dial.
        /// </remarks>
        public static float Snap(float mhz)
        {
            string id;
            if (Receive(mhz, out id) != Reception.Locked)
            {
                return mhz;
            }

            Station station;
            return Stations.TryGet(id, out station) ? station.Mhz : mhz;
        }

        /// <summary>
        /// One column-set of the spectrogram ribbon around the needle.
        /// </summary>
        /// <remarks>
        /// Grass is deterministic hash noise keyed by column and <paramref name="seed"/>, so the
        /// ribbon scrolls with time and is identical for identical inputs. Carriers are the
        /// Lorentzian above, narrowed so a locked signal is a hard vertical line and a detuned one
        /// a visible smear — the ribbon and the lock rule share their arithmetic by construction.
        /// </remarks>
        /// <param name="centreMhz">Needle position.</param>
        /// <param name="spanKhz">Total width shown, in kHz.</param>
        /// <param name="seed">Varies the grass; pass a frame counter.</param>
        /// <param name="into">Receives one amplitude per column, 0..1. Length decides the column count.</param>
        public static void Spectrum(float centreMhz, float spanKhz, int seed, float[] into)
        {
            if (into == null || into.Length == 0)
            {
                return;
            }

            var columns = into.Length;
            var all = Stations.All;

            for (var c = 0; c < columns; c++)
            {
                var t = columns == 1 ? 0.5f : c / (float)(columns - 1);
                var khzAtColumn = (t - 0.5f) * spanKhz;
                var mhzAtColumn = centreMhz + khzAtColumn / 1000f;

                var carrier = 0f;
                for (var i = 0; i < all.Length; i++)
                {
                    var offset = (mhzAtColumn - all[i].Mhz) * 1000f / (RadioBand.LockHalfWidthKhz * 1.6f);
                    var strength = 1f / (1f + offset * offset);
                    if (strength > carrier)
                    {
                        carrier = strength;
                    }
                }

                var grass = Hash(c * 7919 + seed * 104729) * 0.28f;
                var value = carrier + grass;
                into[c] = value > 1f ? 1f : value;
            }
        }

        /// <summary>A cheap deterministic hash to 0..1. Engine-free; not cryptographic; not meant to be.</summary>
        private static float Hash(int n)
        {
            unchecked
            {
                var x = (uint)n;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }
}
