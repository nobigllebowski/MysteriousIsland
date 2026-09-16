namespace ForgottenIsle.Core.Progress
{
    /// <summary>
    /// How much of the record the player has made: markers read, discoveries taken, machines fixed.
    /// </summary>
    /// <remarks>
    /// The figure the pause summary and the save header show. It was rendered from the day the
    /// header existed and computed by nothing (O-12): every save said 0%. It is a plain fraction of
    /// the recordable content ids, which is honest for a slice this size and will need weighting
    /// when there are 340 documents -- but a fraction that moves beats a constant that lies.
    /// </remarks>
    public static class Recorded
    {
        /// <summary>Every content id that counts toward the record. Add here when content is added.</summary>
        public static readonly string[] Recordable =
        {
            ContentIds.MarkerHullLine,
            ContentIds.MarkerRibStone,
            ContentIds.MarkerAqueductCut,
            ContentIds.MarkerBootPrint,
            ContentIds.MarkerLegBand,
            ContentIds.MarkerTideMark,
            ContentIds.MarkerOxySlag,
            ContentIds.MarkerBroomArc,
            ContentIds.MarkerCanvasSquare,
            ContentIds.MarkerCutVine,
            ContentIds.MarkerChalkFrequency,
            ContentIds.DiscoveryBrassTag,
            ContentIds.DiscoveryWaterloggedReel,
            ContentIds.MechanismSluice,
            ContentIds.MechanismTapeDeck,
            ContentIds.MechanismFire
        };

        /// <summary>Percentage of the recordable content the run has recorded, 0..100.</summary>
        public static int Percent(WorldProgress progress)
        {
            if (progress == null || Recordable.Length == 0)
            {
                return 0;
            }

            var done = 0;
            for (var i = 0; i < Recordable.Length; i++)
            {
                // By kind: a mechanism is recorded when it is fixed, not when it is looked at.
                if (Slate.IsRecorded(progress, Recordable[i]))
                {
                    done++;
                }
            }

            // Rounded to nearest rather than truncated, so one of six reads 17 and six of six 100.
            return (done * 100 + Recordable.Length / 2) / Recordable.Length;
        }
    }
}
