namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// The stable participant ids under which each subsystem stores its blob in a
    /// <see cref="SaveDocument"/>.
    /// </summary>
    /// <remarks>
    /// These strings are on-disk identifiers. Renaming one orphans that section in every existing
    /// save, so they live here as constants rather than as literals scattered through the
    /// participants — a rename then either updates every site or fails to compile, instead of
    /// silently losing a player's progress. Keep them lowercase and free of separators.
    /// </remarks>
    public static class SaveSections
    {
        /// <summary>Section owned by the session service: act, zone, clock, playtime.</summary>
        public const string Session = "session";

        /// <summary>Section owned by the player service: position, facing, equipped tool.</summary>
        public const string Player = "player";

        /// <summary>Section owned by the progress service: markers read, discoveries taken, zones open.</summary>
        public const string Progress = "progress";

        /// <summary>Section owned by the inventory service: what the player is carrying.</summary>
        public const string Inventory = "inventory";

        /// <summary>The radio: its faults, its needle, and what has been heard on it.</summary>
        public const string Radio = "radio";

        /// <summary>The fire sites: what is laid where, what burns, what is drying.</summary>
        public const string Fire = "fire";
    }
}
