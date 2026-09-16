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
    /// <summary>What kind of thing a content id names. Decides how it is recorded.</summary>
    public enum ContentKind : byte
    {
        Unknown = 0,
        Zone = 1,
        Marker = 2,
        Discovery = 3,
        Mechanism = 4,
        Gate = 5,
        Radio = 6,

        /// <summary>Something Nadia says in answer to an act. Never recorded, never in the Slate.</summary>
        Remark = 7
    }

    public static class ContentIds
    {
        /// <summary>
        /// The kind of a content id, by table rather than by prefix.
        /// </summary>
        /// <remarks>
        /// A prefix test would credit a mechanism written without its prefix as a marker -- the
        /// bug this replaces, in a different coat. Every id this project records is listed here,
        /// and an id that is not is Unknown, which records as nothing.
        /// </remarks>
        public static ContentKind KindOf(string id)
        {
            switch (id)
            {
                case ZoneRibcage:
                case ZoneFernmaw:
                    return ContentKind.Zone;
                case MarkerRibStone:
                case MarkerHullLine:
                case MarkerAqueductCut:
                    return ContentKind.Marker;
                case DiscoveryBrassTag:
                case DiscoveryWaterloggedReel:
                    return ContentKind.Discovery;
                case MechanismSluice:
                case MechanismTapeDeck:
                    return ContentKind.Mechanism;
                case GateRibcageToFernmaw:
                case GateFernmawToRibcage:
                    return ContentKind.Gate;
                case RadioSet:
                    return ContentKind.Radio;
                case RemarkHullLinePartial:
                    return ContentKind.Remark;
                default:
                    return ContentKind.Unknown;
            }
        }

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

        /// <summary>
        /// Six wrecked hulls on one line, seen by standing at the bow of the nearest and looking
        /// down the beach. Recorded by looking, not by a prompt.
        /// </summary>
        public const string MarkerHullLine = "marker.hull_line";

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

        // --- Remarks (said, never recorded) ------------------------------------------------

        /// <summary>
        /// The hull line aligned from the wrong hull: three chalk in, and "try the far end".
        /// </summary>
        public const string RemarkHullLinePartial = "remark.hull_line_partial";

        // --- Gates (travel points) ----------------------------------------------------------

        /// <summary>The gully mouth in the Ribcage that leads inland.</summary>
        public const string GateRibcageToFernmaw = "gate.ribcage_to_fernmaw";

        /// <summary>The way back down the channel to the shore.</summary>
        public const string GateFernmawToRibcage = "gate.fernmaw_to_ribcage";
    }
}
