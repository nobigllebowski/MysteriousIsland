namespace ForgottenIsle.Core.Progress
{
    /// <summary>
    /// Every piece of content the player can interact with, as a stable string id.
    /// </summary>
    /// <remarks>
    /// WHY STRINGS AND NOT OBJECT REFERENCES: these ids go into save files. A save written today
    /// must still name the same marker after the zone is rebuilt, re-authored, or moved from a
    /// runtime-furnished placeholder to hand-made art. A Unity object reference cannot survive any
    /// of that; a string can.
    /// <para>
    /// WHY THEY LIVE IN CORE: the objective resolver and the save participant both need them, and
    /// both are engine-free. Nothing here may be renamed once a save exists in the wild — a renamed
    /// id reads as "never collected", so the player silently loses progress.
    /// </para>
    /// </remarks>
    public static class ContentIds
    {
        // --- Zones -------------------------------------------------------------------------
        // These match SceneKeys values exactly; a test asserts it, because a drift here would let
        // the player "unlock" a zone that cannot be loaded.

        /// <summary>The shore zone. Always unlocked — it is where a new run begins.</summary>
        public const string ZoneRibcage = "ZoneRibcage";

        /// <summary>The fern gully. Locked until the Ribcage discovery is collected.</summary>
        public const string ZoneFernmaw = "ZoneFernmaw";

        // --- Markers (inspected, never consumed) -------------------------------------------

        /// <summary>The standing stone at the centre of the hull line.</summary>
        public const string MarkerRibStone = "marker.rib_stone";

        /// <summary>The graded channel wall in Fernmaw, cut too regularly to be natural.</summary>
        public const string MarkerAqueductCut = "marker.aqueduct_cut";

        // --- Discoveries (collected once, then gone) ---------------------------------------

        /// <summary>The brass maintenance tag wired to a wreck. The key to Fernmaw.</summary>
        public const string DiscoveryBrassTag = "discovery.brass_tag";

        /// <summary>A waterlogged reel in the Fernmaw sill. The Act 2 hook.</summary>
        public const string DiscoveryWaterloggedReel = "discovery.waterlogged_reel";

        // --- Mechanisms (built things that stopped working) ---------------------------------

        /// <summary>
        /// The sluice in the Fernmaw channel wall. Seized, and the reason the channel runs dry.
        /// </summary>
        public const string MechanismSluice = "mechanism.channel_sluice";

        /// <summary>
        /// The tape deck in the sluice housing, left wired to the island's mains.
        /// </summary>
        public const string MechanismTapeDeck = "mechanism.tape_deck";

        // --- The radio (one object; the prologue's spine) -------------------------------------

        /// <summary>The 1970s marine set on the crate inside the trawler hull.</summary>
        public const string RadioSet = "radio.set";

        // --- Gates (travel points) ----------------------------------------------------------

        /// <summary>The gully mouth in the Ribcage that leads inland.</summary>
        public const string GateRibcageToFernmaw = "gate.ribcage_to_fernmaw";

        /// <summary>The way back down the channel to the shore.</summary>
        public const string GateFernmawToRibcage = "gate.fernmaw_to_ribcage";
    }
}
