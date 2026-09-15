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

        /// <summary>What a new run starts holding.</summary>
        public static readonly string[] StartingKit = { Multitool, FieldRecorder };

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

        // --- Made, not found -----------------------------------------------------------------

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
