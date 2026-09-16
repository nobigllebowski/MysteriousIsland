using System;
using ForgottenIsle.Core.Radio;

namespace ForgottenIsle.Core.Audio
{
    /// <summary>What the radio's two sounds do at a needle position: hiss level, carrier level and pitch, warble.</summary>
    public readonly struct RadioLevels
    {
        /// <summary>Hiss, 0..1. Loud in the grass, ducked on a lock.</summary>
        public readonly float Hiss;

        /// <summary>The carrier, 0..1. Nothing in the grass, full on a lock.</summary>
        public readonly float Carrier;

        /// <summary>Playback pitch of the carrier: the detune, 1 on a lock and higher off it.</summary>
        public readonly float Pitch;

        /// <summary>True in the smudge: the carrier warbles, "there's a person in there somewhere".</summary>
        public readonly bool Warble;

        public RadioLevels(float hiss, float carrier, float pitch, bool warble)
        {
            Hiss = hiss;
            Carrier = carrier;
            Pitch = pitch;
            Warble = warble;
        }
    }

    /// <summary>
    /// The tolerance ladder (design §3.3), as sound.
    /// </summary>
    /// <remarks>
    /// The voice cannot be synthesised and is not pretended: the carrier is a tone. What the
    /// ladder promises is still kept -- grass is hiss; a smudge is a warble under heavy hiss; a
    /// detune is the carrier present, pitched off, under less hiss; a lock ducks the hiss and
    /// brings the carrier up clean at pitch one, with the snap the tuner already does. The
    /// spectrogram and this share <see cref="RadioTuner.Strength"/>, so what is seen and what is
    /// heard cannot disagree.
    /// </remarks>
    public static class RadioMix
    {
        /// <summary>Hiss when nothing is near.</summary>
        public const float HissInGrass = 1f;

        /// <summary>Hiss under a lock: ducked, not gone. The set is still a 1970s set.</summary>
        public const float HissOnLock = 0.22f;

        /// <summary>How much a kilohertz of detune raises the carrier's pitch. The donald-duck.</summary>
        public const float PitchPerKhz = 0.3f;

        /// <summary>Levels for a needle position.</summary>
        /// <param name="mhz">The needle.</param>
        public static RadioLevels At(float mhz)
        {
            string stationId;
            var reception = RadioTuner.Receive(mhz, out stationId);
            var strength = RadioTuner.Strength(mhz);

            var offsetKhz = 0f;
            Station station;
            if (stationId != null && Stations.TryGet(stationId, out station))
            {
                offsetKhz = Math.Abs(mhz - station.Mhz) * 1000f;
            }

            return For(reception, strength, offsetKhz);
        }

        /// <summary>Levels for a reception tier, a strength and an offset. Pure; pinned by tests.</summary>
        public static RadioLevels For(Reception reception, float strength, float offsetKhz)
        {
            strength = strength < 0f ? 0f : strength > 1f ? 1f : strength;
            switch (reception)
            {
                case Reception.Locked:
                    return new RadioLevels(HissOnLock, 1f, 1f, false);

                case Reception.Detuned:
                    return new RadioLevels(
                        HissInGrass - (HissInGrass - HissOnLock) * strength * 0.8f,
                        0.35f + 0.5f * strength,
                        1f + PitchPerKhz * offsetKhz,
                        false);

                case Reception.Smudge:
                    return new RadioLevels(HissInGrass - 0.1f * strength, 0.12f + 0.2f * strength, 1f, true);

                default:
                    return new RadioLevels(HissInGrass, 0f, 1f, false);
            }
        }
    }
}
