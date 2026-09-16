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
    /// <summary>What the objective needs to know that progression does not carry.</summary>
    /// <remarks>
    /// Plain booleans handed in from the Game-side services, as <see cref="SlateFacts"/> are, so
    /// the derivation stays engine-free and a test can state a situation as a literal.
    /// </remarks>
    public readonly struct ObjectiveFacts
    {
        public readonly bool RadioFound;
        public readonly bool RadioWorking;
        public readonly bool HeardTheVoice;

        /// <summary>Something has been laid or tried at a fire site this run.</summary>
        public readonly bool FireEngaged;
        public readonly bool FireLit;

        public ObjectiveFacts(bool radioFound, bool radioWorking, bool heardTheVoice, bool fireEngaged, bool fireLit)
        {
            RadioFound = radioFound;
            RadioWorking = radioWorking;
            HeardTheVoice = heardTheVoice;
            FireEngaged = fireEngaged;
            FireLit = fireLit;
        }

        public bool Equals(ObjectiveFacts other)
        {
            return RadioFound == other.RadioFound && RadioWorking == other.RadioWorking
                && HeardTheVoice == other.HeardTheVoice && FireEngaged == other.FireEngaged && FireLit == other.FireLit;
        }
    }

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
        private static readonly LocKey MakeFire = new LocKey("objective.make_fire");
        private static readonly LocKey FixTheSet = new LocKey("objective.fix_the_set");
        private static readonly LocKey FindTheFrequency = new LocKey("objective.find_the_frequency");

        /// <summary>The objective from progression alone: no radio, no fire.</summary>
        public static LocKey Current(WorldProgress progress, string zoneId)
        {
            return Current(progress, zoneId, default(ObjectiveFacts));
        }

        /// <summary>
        /// The one line on the HUD: what a player who has done what the record says would do next.
        /// </summary>
        /// <remarks>
        /// Derived, never stored (ADR-0015), and now from the puzzles as well as the record: a
        /// fire that has been started on and not lit, a set that has been found and not fixed, a
        /// working set that has not found her. Each is named only once the player has met it --
        /// the line describes, it never sends. Nothing here is required for progression; the
        /// tag chain underneath is what opens Fernmaw.
        /// </remarks>
        public static LocKey Current(WorldProgress progress, string zoneId, ObjectiveFacts facts)
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

            if (facts.FireEngaged && !facts.FireLit)
            {
                // Something has been laid at a site and nothing burns: the problem, stated once
                // in her words and then kept on the HUD, is "spark, tinder, shelter".
                return MakeFire;
            }

            if (!haveTag)
            {
                // Inspecting the stone is what tells the player a tag is worth looking for, so the
                // objective only names it after they have read the stone.
                return FindTheTag;
            }

            if (facts.RadioFound && !facts.RadioWorking)
            {
                return FixTheSet;
            }

            if (facts.RadioWorking && !facts.HeardTheVoice)
            {
                return FindTheFrequency;
            }

            if (!haveReel)
            {
                return EnterFernmaw;
            }

            return ActComplete;
        }
    }
}
