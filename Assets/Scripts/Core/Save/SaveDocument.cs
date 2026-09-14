using System;
using System.Collections.Generic;

namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// The on-disk envelope for a single save: a header, a checksum, and one opaque blob per
    /// participating subsystem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document deliberately knows nothing about what is inside its sections. Sections are
    /// strings, keyed by participant id, and only the participant that wrote one can interpret it.
    /// That is what keeps the save format stable while subsystems churn: adding a system adds a
    /// key, and an old build reading a new save skips the key it does not recognise instead of
    /// failing to parse.
    /// </para>
    /// <para>
    /// <see cref="Checksum"/> is not present for security — a determined player can recompute it.
    /// It is there to distinguish a truncated write (power loss mid-flush) from a valid save, so
    /// the store can fall back to the <c>.bak</c> rotation instead of loading half a game.
    /// </para>
    /// </remarks>
    public sealed class SaveDocument
    {
        /// <summary>
        /// Schema version this build writes and understands. Bump it in the same commit that adds
        /// the migration from the previous version — never on its own.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Schema version this particular document was written at.</summary>
        public int SchemaVersion;

        /// <summary>Application version that wrote it, for triage.</summary>
        public string BuildVersion;

        /// <summary>UTC write time, ISO-8601.</summary>
        public string SavedAtIso;

        /// <summary>
        /// CRC32 over the serialized payload, computed with the checksum field itself zeroed.
        /// Zero means "not yet computed"; the codec sets it as the last step of writing.
        /// </summary>
        public uint Checksum;

        /// <summary>Cheap header for the slot list. Never null.</summary>
        public SaveMetadata Metadata;

        /// <summary>
        /// Participant id to serialized blob. Ordinal-compared: these are on-disk identifiers, and
        /// a culture-sensitive comparison could match differently on a Turkish-locale device.
        /// </summary>
        public Dictionary<string, string> Sections;

        /// <summary>
        /// Creates an empty document already stamped at the current schema version, so the common
        /// path (write a new save) needs no initialisation ceremony.
        /// </summary>
        public SaveDocument()
        {
            SchemaVersion = CurrentSchemaVersion;
            BuildVersion = string.Empty;
            SavedAtIso = string.Empty;
            Checksum = 0u;
            Metadata = new SaveMetadata();
            Sections = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Stores <paramref name="payload"/> under <paramref name="participantId"/>, replacing any
        /// previous blob for that participant.
        /// </summary>
        /// <remarks>
        /// Bad input is dropped rather than thrown on. This runs inside the save path, often from
        /// an autosave the player did not ask for; a participant with a broken id should cost that
        /// participant its section, not cost the player the whole save. A null payload is stored as
        /// an empty string so that <see cref="TryGetSection"/> never hands a caller null.
        /// </remarks>
        public void PutSection(string participantId, string payload)
        {
            if (string.IsNullOrEmpty(participantId))
            {
                return;
            }

            if (Sections == null)
            {
                Sections = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            Sections[participantId] = payload ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the blob written by <paramref name="participantId"/>.
        /// </summary>
        /// <returns>
        /// True when the section exists. False — with <paramref name="payload"/> set to empty,
        /// never null — when it does not, which is the normal outcome for a save written before
        /// that participant existed.
        /// </returns>
        public bool TryGetSection(string participantId, out string payload)
        {
            if (string.IsNullOrEmpty(participantId) || Sections == null)
            {
                payload = string.Empty;
                return false;
            }

            string stored;
            if (Sections.TryGetValue(participantId, out stored))
            {
                payload = stored ?? string.Empty;
                return true;
            }

            payload = string.Empty;
            return false;
        }
    }
}
