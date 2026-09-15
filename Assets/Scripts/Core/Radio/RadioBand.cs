namespace ForgottenIsle.Core.Radio
{
    /// <summary>
    /// The dial: what the set can tune, and how far a thumb moves it.
    /// </summary>
    /// <remarks>
    /// Numbers from <c>design/04-first-30-minutes.md</c> §3.2 and §3.3. The band covers the whole
    /// hand-written frequency list with room either side, so nothing on the list sits against a
    /// stop. The coarse rate is the design's, and it is slow on purpose: crossing the band is
    /// meant to be a sweep the player feels, not a flick — inertia in the panel carries a flick a
    /// long way, and the number to tune is the friction there, not this.
    /// <para>
    /// UNCONFIRMED (design §5 says so itself): the six frequencies are fictional set-dressing and
    /// some coincide with real allocations. To be cleared before shipped text.
    /// </para>
    /// </remarks>
    public static class RadioBand
    {
        /// <summary>Bottom of the dial, in MHz.</summary>
        public const float MinMhz = 2.000f;

        /// <summary>Top of the dial, in MHz.</summary>
        public const float MaxMhz = 14.000f;

        /// <summary>Coarse tuning: kHz moved by one screen-width of drag on the strip.</summary>
        public const float CoarseKhzPerScreen = 180f;

        /// <summary>Fine tuning multiplies the coarse rate by this. The right fifth of the band.</summary>
        public const float FineRatio = 0.1f;

        /// <summary>Fraction of the band's width, from the right, that is the fine-tune knob.</summary>
        public const float FineZoneFraction = 0.2f;

        /// <summary>Mechanical detent spacing. A tick every one of these.</summary>
        public const float DetentKhz = 25f;

        /// <summary>Within this of a carrier the needle is on it: clean, intelligible, snapped.</summary>
        public const float LockHalfWidthKhz = 0.8f;

        /// <summary>Within this a voice is present and unintelligible.</summary>
        public const float DetunedHalfWidthKhz = 4f;

        /// <summary>Within this there is a smudge on the ribbon and a warble under the hiss.</summary>
        public const float SmudgeHalfWidthKhz = 12f;

        /// <summary>Clamps a frequency to the dial.</summary>
        public static float Clamp(float mhz)
        {
            if (mhz < MinMhz)
            {
                return MinMhz;
            }

            return mhz > MaxMhz ? MaxMhz : mhz;
        }
    }

    /// <summary>Something on the band that is not grass.</summary>
    public readonly struct Station
    {
        /// <summary>Stable id; also the root of its localization keys.</summary>
        public readonly string Id;

        /// <summary>Carrier frequency in MHz.</summary>
        public readonly float Mhz;

        /// <summary>True for the one signal the puzzle is about.</summary>
        public readonly bool IsTheSignal;

        /// <param name="id">Stable id.</param>
        /// <param name="mhz">Carrier frequency.</param>
        /// <param name="isTheSignal">Whether this is the transmission the prologue turns on.</param>
        public Station(string id, float mhz, bool isTheSignal)
        {
            Id = id;
            Mhz = mhz;
            IsTheSignal = isTheSignal;
        }
    }

    /// <summary>
    /// Everything that can be heard on the set, authored.
    /// </summary>
    /// <remarks>
    /// Three carriers, two of them honest false positives: the hull's own pressure cycle coming
    /// through a loose gland at 8.291 (the game's thesis hidden in a red herring), and a Portuguese
    /// weather bulletin at 12.510 for a sea area nine hundred kilometres away (the set works;
    /// nobody is looking). The signal is at 5.240 — the one line on the list written without a
    /// unit, in a list whose every other line is in kHz. That is the whole puzzle, and it is a
    /// reading puzzle, not a trivia puzzle.
    /// </remarks>
    public static class Stations
    {
        /// <summary>The hull. Not a transmission.</summary>
        public const string HullThump = "radio.hull_thump";

        /// <summary>A distant, uninterested maritime weather bulletin.</summary>
        public const string Bulletin = "radio.bulletin";

        /// <summary>A woman, live, close, reading a maintenance log. Does not answer.</summary>
        public const string TheVoice = "radio.the_voice";

        private static readonly Station[] AllStations =
        {
            new Station(HullThump, 8.291f, false),
            new Station(Bulletin, 12.510f, false),
            new Station(TheVoice, 5.240f, true)
        };

        /// <summary>Every station, in no particular order.</summary>
        public static Station[] All => AllStations;

        /// <summary>Number of authored stations. For a test that guards against an emptied table.</summary>
        public static int Count => AllStations.Length;

        /// <summary>Looks a station up by id.</summary>
        /// <returns>False when no station has that id.</returns>
        public static bool TryGet(string id, out Station station)
        {
            for (var i = 0; i < AllStations.Length; i++)
            {
                if (AllStations[i].Id == id)
                {
                    station = AllStations[i];
                    return true;
                }
            }

            station = default(Station);
            return false;
        }
    }
}
