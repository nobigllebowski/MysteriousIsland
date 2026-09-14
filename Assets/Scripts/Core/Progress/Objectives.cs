using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Progress
{
    /// <summary>
    /// Works out what the player should be doing next, from progress alone.
    /// </summary>
    /// <remarks>
    /// A pure function of (progress, current zone) rather than a stored "current quest" field, and
    /// that is the whole design. Stored objective state has to be migrated, can desync from the
    /// world it describes, and gives you two sources of truth about the same thing. Deriving it
    /// means a loaded save shows the right objective automatically, and a player who does things
    /// out of order — collects before inspecting, wanders into Fernmaw first — still sees something
    /// true rather than a stale instruction.
    /// <para>
    /// The game has no objective *list* and no quest log. There is one line of text, and it is the
    /// smallest thing that answers "why am I walking this way".
    /// </para>
    /// </remarks>
    public static class Objectives
    {
        /// <summary>Shown while the player has no run at all.</summary>
        public static readonly LocKey None = LocKey.Empty;

        private static readonly LocKey ExploreRibcage = new LocKey("objective.explore_ribcage");
        private static readonly LocKey InspectRibStone = new LocKey("objective.inspect_rib_stone");
        private static readonly LocKey FindTheTag = new LocKey("objective.find_the_tag");
        private static readonly LocKey EnterFernmaw = new LocKey("objective.enter_fernmaw");
        private static readonly LocKey ExploreFernmaw = new LocKey("objective.explore_fernmaw");
        private static readonly LocKey RecoverTheReel = new LocKey("objective.recover_the_reel");
        private static readonly LocKey ReturnToShore = new LocKey("objective.return_to_shore");
        private static readonly LocKey ActComplete = new LocKey("objective.act_complete");

        /// <summary>
        /// The one line of objective text for the player's current situation.
        /// </summary>
        /// <param name="progress">What the player has found so far. Null yields <see cref="None"/>.</param>
        /// <param name="zoneId">The zone the player is standing in.</param>
        /// <returns>A localization key, or <see cref="LocKey.Empty"/> when nothing should be shown.</returns>
        public static LocKey Current(WorldProgress progress, string zoneId)
        {
            if (progress == null)
            {
                return None;
            }

            var haveTag = progress.HasCollected(ContentIds.DiscoveryBrassTag);
            var haveReel = progress.HasCollected(ContentIds.DiscoveryWaterloggedReel);

            if (zoneId == ContentIds.ZoneFernmaw)
            {
                if (!haveReel)
                {
                    // Reaching Fernmaw at all is the reward for the Ribcage chain, so the objective
                    // here names the thing to find rather than re-explaining where you are.
                    return progress.HasInspected(ContentIds.MarkerAqueductCut)
                        ? RecoverTheReel
                        : ExploreFernmaw;
                }

                return ReturnToShore;
            }

            // The Ribcage, and anywhere unexpected: the shore chain is the spine of the slice.
            if (!progress.HasInspected(ContentIds.MarkerRibStone))
            {
                return ExploreRibcage;
            }

            if (!haveTag)
            {
                // Inspecting the stone is what tells the player a tag is worth looking for, so the
                // objective only names it after they have read the stone.
                return FindTheTag;
            }

            if (!haveReel)
            {
                return EnterFernmaw;
            }

            return ActComplete;
        }
    }
}
