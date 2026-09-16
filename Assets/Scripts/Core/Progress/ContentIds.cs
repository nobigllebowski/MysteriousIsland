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
                case MarkerBootPrint:
                case MarkerLegBand:
                case MarkerTideMark:
                case MarkerOxySlag:
                case MarkerBroomArc:
                case MarkerCanvasSquare:
                case MarkerCutVine:
                case MarkerChalkFrequency:
                    return ContentKind.Marker;
                case DiscoveryBrassTag:
                case DiscoveryWaterloggedReel:
                    return ContentKind.Discovery;
                case MechanismSluice:
                case MechanismTapeDeck:
                case MechanismFire:
                    return ContentKind.Mechanism;
                case GateRibcageToFernmaw:
                case GateFernmawToRibcage:
                    return ContentKind.Gate;
                case RadioSet:
                    return ContentKind.Radio;
                case RemarkHullLinePartial:
                case RemarkHullLineUnprompted:
                case RemarkRadioWroteDown:
                case RemarkRadioReadsList:
                case RemarkRadioSweep:
                case RemarkFireHolds:
                case RemarkFireLee:
                case RemarkFireOpenA:
                case RemarkFireOpenB:
                case RemarkRockLook:
                case RemarkRockKnock:
                case RemarkRockRing:
                case RemarkRockNotBasalt:
                case RemarkFireSpine:
                case RemarkFireChert:
                case RemarkFireGrassTooQuick:
                case RemarkFireTearsRope:
                case RemarkFireWind:
                case RemarkFireCarriesKit:
                case RemarkBagFirst:
                case RemarkNotTheSea:
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

        // The Ribcage's optional inspectables (design §28:20 FAILURE, item 30): flavour for the
        // player who beachcombs. Slate entries only; nothing is locked behind any of them.

        /// <summary>A boot print in dried mud above the tide line. The same worn heel.</summary>
        public const string MarkerBootPrint = "marker.boot_print";

        /// <summary>A dead cormorant on the wrack, and a ring on its leg.</summary>
        public const string MarkerLegBand = "marker.leg_band";

        /// <summary>Chalk lines on the trawler's plate, dated: where the water reached.</summary>
        public const string MarkerTideMark = "marker.tide_mark";

        /// <summary>Beads of slag in the sand under the trawler's doorway. Oxy-cut, not rusted.</summary>
        public const string MarkerOxySlag = "marker.oxy_slag";

        /// <summary>An arc in the sand inside the doorway, the width of a broom.</summary>
        public const string MarkerBroomArc = "marker.broom_arc";

        /// <summary>The canvas that was folded over the set. Dry underneath.</summary>
        public const string MarkerCanvasSquare = "marker.canvas_square";

        /// <summary>
        /// The vine across the gully mouth, already cut: one clean stroke, the face pale and wet.
        /// The hook (§28:20, §2).
        /// </summary>
        public const string MarkerCutVine = "marker.cut_vine";

        /// <summary>
        /// Chalk on the trawler's plate: 5240 and a tally of five-bar gates. Readable by firelight
        /// only. The radio puzzle's second redundant source (§3.1).
        /// </summary>
        public const string MarkerChalkFrequency = "marker.chalk_frequency";

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

        /// <summary>
        /// The fire: the dry-fire problem, solved. One record however many sites were tried.
        /// </summary>
        public const string MechanismFire = "mechanism.fire";

        // --- Fire sites (places a fire can be laid; the lee is the answer) ------------------

        /// <summary>In the wind shadow of the near hull: the blown sand breaks around it.</summary>
        public const string FireSiteLee = "fire.lee";

        /// <summary>Open sand near the ribs. The wind takes everything laid here.</summary>
        public const string FireSiteOpenA = "fire.open_a";

        /// <summary>Open sand on the wrack. Same wind.</summary>
        public const string FireSiteOpenB = "fire.open_b";

        // --- The radio (one object; the prologue's spine) -------------------------------------

        /// <summary>The 1970s marine set on the crate inside the trawler hull.</summary>
        public const string RadioSet = "radio.set";

        // --- Remarks (said, never recorded) ------------------------------------------------

        /// <summary>
        /// The hull line aligned from the wrong hull: three chalk in, and "try the far end".
        /// </summary>
        public const string RemarkHullLinePartial = "remark.hull_line_partial";

        /// <summary>
        /// The hull line never aligned: at 6:00 Nadia says it anyway, and the entry writes itself.
        /// </summary>
        public const string RemarkHullLineUnprompted = "remark.hull_line_unprompted";

        /// <summary>Radio hint tier 1: she thinks aloud about the list existing.</summary>
        public const string RemarkRadioWroteDown = "remark.radio.wrote_down";

        /// <summary>Radio hint tier 3: she reads the list out, and reasons out loud, incompletely.</summary>
        public const string RemarkRadioReadsList = "remark.radio.reads_list";

        /// <summary>Radio hint tier 4: she leaves the set on and it sweeps by itself.</summary>
        public const string RemarkRadioSweep = "remark.radio.sweep";

        /// <summary>Examining a burning fire.</summary>
        public const string RemarkFireHolds = "remark.fire.holds";

        /// <summary>Examining the lee: the blown sand breaks around the hull and lies still.</summary>
        public const string RemarkFireLee = "remark.fire.lee";

        /// <summary>Examining the open sand under the ribs.</summary>
        public const string RemarkFireOpenA = "remark.fire.open_a";

        /// <summary>Examining the open sand on the wrack.</summary>
        public const string RemarkFireOpenB = "remark.fire.open_b";

        /// <summary>Examining a rock: dark, glassy, everywhere.</summary>
        public const string RemarkRockLook = "remark.rock.look";

        /// <summary>The multitool on basalt: a dull knock, no chip. The negative half of the lesson.</summary>
        public const string RemarkRockKnock = "remark.rock.knock";

        /// <summary>The multitool on chert: a bright ring and a chip. The whole clue.</summary>
        public const string RemarkRockRing = "remark.rock.ring";

        /// <summary>Within three metres of the chert: "Basalt. Basalt. That's not basalt."</summary>
        public const string RemarkRockNotBasalt = "remark.rock.not_basalt";

        /// <summary>Fire hint, no spark, tier 1: "Steel spine. So I need something harder..."</summary>
        public const string RemarkFireSpine = "remark.fire.spine";

        /// <summary>Fire hint, no spark, tier 3: she picks up the chert herself.</summary>
        public const string RemarkFireChert = "remark.fire.chert";

        /// <summary>Fire hint, spark but no tinder, tier 1: "Grass is too quick."</summary>
        public const string RemarkFireGrassTooQuick = "remark.fire.grass_too_quick";

        /// <summary>Fire hint, spark but no tinder, tier 3: she tears the rope apart herself.</summary>
        public const string RemarkFireTearsRope = "remark.fire.tears_rope";

        /// <summary>Three blow-outs: "It's the wind."</summary>
        public const string RemarkFireWind = "remark.fire.wind";

        /// <summary>Seven blow-outs: she carries the kit to the lee and sets it down.</summary>
        public const string RemarkFireCarriesKit = "remark.fire.carries_kit";

        /// <summary>Forty seconds without approaching the bag: "Bag first. Everything I own is in that bag."</summary>
        public const string RemarkBagFirst = "remark.bag_first";

        /// <summary>
        /// The hook (§2, 29:32): at the cut vine the pressure cycle rises until it cannot not be
        /// heard, and she says it out loud for the first time. "That's not the sea."
        /// </summary>
        public const string RemarkNotTheSea = "remark.not_the_sea";

        /// <summary>
        /// How many lines a remark is said in. One means the key is "narration." + id; more means
        /// "narration." + id + ".1", ".2" and so on, shown as a sequence.
        /// </summary>
        public static int RemarkLines(string id)
        {
            switch (id)
            {
                case RemarkRadioReadsList:
                    return 2;
                default:
                    return 1;
            }
        }

        // --- Gates (travel points) ----------------------------------------------------------

        /// <summary>The gully mouth in the Ribcage that leads inland.</summary>
        public const string GateRibcageToFernmaw = "gate.ribcage_to_fernmaw";

        /// <summary>The way back down the channel to the shore.</summary>
        public const string GateFernmawToRibcage = "gate.fernmaw_to_ribcage";
    }
}
