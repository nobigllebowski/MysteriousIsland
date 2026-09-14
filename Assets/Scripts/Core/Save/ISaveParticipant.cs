namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// A subsystem that owns a slice of the save file and can write it out and read it back.
    /// </summary>
    /// <remarks>
    /// Saving is distributed rather than centralised: no single serializer knows the shape of
    /// every subsystem, each one writes its own section under its own id, and the document is just
    /// the envelope that carries them. That is what lets a new system be added in a later phase
    /// without touching the save code, and it is what lets an unknown section in an old save be
    /// skipped rather than treated as corruption.
    /// </remarks>
    public interface ISaveParticipant
    {
        /// <summary>
        /// Stable on-disk id of this participant's section, from <see cref="SaveSections"/>.
        /// Must never change for a shipped participant.
        /// </summary>
        string ParticipantId { get; }

        /// <summary>
        /// Writes this subsystem's current state into <paramref name="doc"/> via
        /// <see cref="SaveDocument.PutSection"/>.
        /// </summary>
        void Capture(SaveDocument doc);

        /// <summary>
        /// Reads this subsystem's state back out of <paramref name="doc"/>.
        /// </summary>
        /// <remarks>
        /// Implementations must treat a missing section as "this save predates me" and fall back
        /// to defaults rather than failing the load. Migration has already run by this point, so
        /// the section — if present — is at the current schema version.
        /// </remarks>
        void Restore(SaveDocument doc);
    }
}
