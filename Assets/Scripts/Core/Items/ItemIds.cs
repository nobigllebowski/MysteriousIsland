namespace ForgottenIsle.Core.Items
{
    /// <summary>
    /// Every item the player can carry, as a stable string id.
    /// </summary>
    /// <remarks>
    /// Same contract as <c>ContentIds</c>, and for the same reason: these strings go into save
    /// files. Renaming one after a save exists reads as "never picked up", and the player silently
    /// loses an item — possibly one a puzzle needs, which turns a rename into an unwinnable run.
    /// <para>
    /// WHAT AN ITEM IS IN THIS GAME. Every one of these is a real object with a job: a spindle
    /// winds tape, a tin holds grease, a reel carries a recording. Nothing here is a crafting
    /// reagent and nothing stacks — the island is a place someone maintained, and its contents are
    /// the tools and spares they maintained it with. An item that is only useful as an ingredient
    /// for another item does not belong.
    /// </para>
    /// </remarks>
    public static class ItemIds
    {
        // --- Carried from the wreck ---------------------------------------------------------

        /// <summary>The multitool. Never consumed; a tool.</summary>
        public const string Multitool = "item.multitool";

        /// <summary>
        /// The field recorder, dried out, warm, at nine per cent. Its cells fit a radio.
        /// </summary>
        /// <remarks>
        /// Spending them is the prologue's one real decision, and it is never flagged as one.
        /// </remarks>
        public const string FieldRecorder = "item.field_recorder";

        /// <summary>
        /// The orange dry bag, ten metres up the beach, half-buried: the only colour in the first
        /// frame. Taken, it becomes the worn inventory and is not an item any more.
        /// </summary>
        public const string DryBag = "item.dry_bag";

        /// <summary>What comes out of the bag (§2:20). A run holds nothing until the bag is taken.</summary>
        public static readonly string[] BagContents = { Multitool, FieldRecorder };

        /// <summary>What an item contains, to be taken with it; empty for everything but the bag.</summary>
        public static string[] ContentsOf(string itemId)
        {
            return itemId == DryBag ? BagContents : System.Array.Empty<string>();
        }

        /// <summary>True for a container that becomes the inventory itself rather than a chip in it.</summary>
        public static bool IsContainer(string itemId)
        {
            return itemId == DryBag;
        }

        // --- Found in the trawler hull --------------------------------------------------------

        /// <summary>A dead hand-torch off the nail row. Two D-cells and a copper spring inside.</summary>
        public const string DeadTorch = "item.dead_torch";

        /// <summary>The torch's spring. Not a fuse: a decision to trust the wiring.</summary>
        public const string CopperSpring = "item.copper_spring";

        // --- Found on the shore -------------------------------------------------------------

        /// <summary>The brass maintenance tag, stamped 11.04.97 and two initials.</summary>
        public const string BrassTag = "item.brass_tag";

        /// <summary>A dry hardwood spindle off the net drum. Takes a reel's core exactly.</summary>
        public const string DrySpindle = "item.dry_spindle";

        // --- Found in the channel -----------------------------------------------------------

        /// <summary>Quarter-inch tape, waterlogged and spooled loose. Unplayable as found.</summary>
        public const string WaterloggedReel = "item.waterlogged_reel";

        /// <summary>A flat steel key for the sluice housing, left in its bracket.</summary>
        public const string SluiceKey = "item.sluice_key";

        // --- The wrack: what the beach gives for a fire (design §5:00, §4.1) ----------------
        /// <summary>An armful of bleached driftwood from above the tide mark. It clicks. Fuel.</summary>
        public const string DriftwoodDry = "item.driftwood_dry";

        /// <summary>An armful from below the mark. Dark, heavy, thuds. Will not light; dries by a fire.</summary>
        public const string DriftwoodWet = "item.driftwood_wet";

        /// <summary>A nest of grass from a crevice the sun got at. Flares and dies: an authored failure.</summary>
        public const string DryGrass = "item.dry_grass";

        /// <summary>Blue polypropylene rope, sun-rotted to felt. Teased with the blade, it is tinder.</summary>
        public const string PolyRope = "item.poly_rope";

        /// <summary>A pale, banded nodule from the strand line. Struck on the spine, it throws sparks.</summary>
        public const string ChertNodule = "item.chert_nodule";

        /// <summary>A safety-orange hull panel. A windbreak, and a stencil that pays off in Act 3.</summary>
        public const string FibreglassPanel = "item.fibreglass_panel";

        /// <summary>Black kelp, wet through. An honest dead end: not food, not yet.</summary>
        public const string Kelp = "item.kelp";

        // --- Made, not found -----------------------------------------------------------------
        /// <summary>The rope teased apart into a bird's nest of fibre. Holds an ember. Stinks.</summary>
        public const string PolyFibre = "item.poly_fibre";

        /// <summary>True for the things that are used and never used up.</summary>
        /// <remarks>
        /// A combination consumes its ingredients; a tool is not an ingredient. The multitool
        /// teases the rope and is still a multitool afterwards.
        /// </remarks>
        public static bool IsTool(string itemId)
        {
            return itemId == Multitool;
        }


        /// <summary>
        /// The reel wound onto the dry spindle. This is the only form the deck will accept.
        /// </summary>
        /// <remarks>
        /// The combination is not a recipe in the crafting sense. Tape that has been in water is
        /// unusable until it is off its swollen core and onto something that will not bind, which
        /// is a real thing a person does to a real tape, and is the whole reason the spindle is on
        /// the shore rather than in a box of parts.
        /// </remarks>
        public const string ReboundReel = "item.rebound_reel";
    }
}
