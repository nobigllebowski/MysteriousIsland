namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// The small, cheap-to-read header describing a save, duplicated out of the sections so the
    /// slot list can be drawn without deserializing a whole game.
    /// </summary>
    /// <remarks>
    /// This is intentional denormalisation. The alternative — deriving the slot card from the
    /// session section — means parsing three full saves to draw the menu, on a cold start, on a
    /// phone, which is precisely the moment the game can least afford it. The cost is that these
    /// fields must be rewritten on every save; <c>SaveSlotService</c> owns that duty.
    /// </remarks>
    public sealed class SaveMetadata
    {
        /// <summary>Zero-based slot this document was written to.</summary>
        public int Slot;

        /// <summary>Act the run is in, mirrored from the session.</summary>
        public string ActId = string.Empty;

        /// <summary>Zone the run is in, mirrored from the session.</summary>
        public string ZoneId = string.Empty;

        /// <summary>
        /// Localization key for the zone's display name. The key, not the resolved name: a save
        /// written in one language must show correctly after the player switches to another.
        /// </summary>
        public string ZoneDisplayKey = string.Empty;

        /// <summary>Real seconds played in this run, mirrored from the session.</summary>
        public double PlaytimeSeconds;

        /// <summary>Completion percentage last recorded for this run, 0..100.</summary>
        public int RecordedPercent;

        /// <summary>
        /// UTC timestamp of the write, ISO-8601. Stored as a string rather than a DateTime so the
        /// on-disk form is unambiguous and does not depend on the deserializer's culture or
        /// kind-handling.
        /// </summary>
        public string SavedAtIso = string.Empty;

        /// <summary>Application version that wrote the save, for triaging player bug reports.</summary>
        public string BuildVersion = string.Empty;

        /// <summary>
        /// Schema version of the document this header describes. Duplicated from the document so
        /// a too-new save can be detected and greyed out from the header read alone.
        /// </summary>
        public int SchemaVersion = SaveDocument.CurrentSchemaVersion;
    }
}
