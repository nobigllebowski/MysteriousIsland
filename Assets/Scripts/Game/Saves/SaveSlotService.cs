using System;
using System.Collections.Generic;
using System.Globalization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;

namespace ForgottenIsle.Game.Saves
{
    /// <summary>
    /// The save system's front door: three manual slots, a rotating autosave ring, and the register of
    /// subsystems that contribute sections to a save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layering: <c>SaveFileStore</c> knows about bytes, <c>SaveCodec</c> knows about text, and this class
    /// knows about slots and participants. It is the only one of the three that knows what a save
    /// <em>means</em> to the player.
    /// </para>
    /// <para>
    /// THE RULE THAT SHAPES THIS FILE: an unreadable slot must never be able to break the main menu. The
    /// menu enumerates slots on a cold start, on a device whose storage may be full, corrupted, or holding
    /// a save from a build that no longer exists. Every read path here therefore reports failure as a
    /// <see cref="ResultCode"/>, logs it once, and leaves the slot listed as unusable — no exception
    /// escapes, and one bad slot never prevents the other two from being drawn.
    /// </para>
    /// <para>
    /// WHY THE AUTOSAVE RING HAS NO CURSOR FILE: the ring position is derived by reading the ring's own
    /// headers and picking the oldest. A separate cursor is one more file that can disagree with reality
    /// after a crash, and the disagreement's failure mode is overwriting the newest autosave — which is
    /// precisely the one the player needs. Deriving it costs three header reads at autosave time, which is
    /// already an operation that touches the disk.
    /// </para>
    /// </remarks>
    public sealed class SaveSlotService
    {
        /// <summary>Manual save slots, addressed as 0..<see cref="SlotCount"/>-1.</summary>
        public const int SlotCount = 3;

        /// <summary>Entries in the autosave ring.</summary>
        public const int AutosaveRingLength = 3;

        /// <summary>
        /// Slot indices at or above this address the autosave ring, so the menu can pass an autosave to the
        /// same <see cref="Load"/> and <see cref="ReadMetadata"/> calls it uses for a manual slot. The gap
        /// below it is deliberate: manual slot count can grow without ever colliding with the ring.
        /// </summary>
        public const int AutosaveSlotBase = 100;

        /// <summary>
        /// Characters read from the head of a save when only the header is wanted.
        /// </summary>
        /// <remarks>
        /// Generous by an order of magnitude — the header is a couple of hundred characters — because the
        /// cost of guessing too small is an extra full read, while the cost of guessing too large is a few
        /// kilobytes of transient buffer. Sized in characters, not bytes, since the store reads decoded
        /// text.
        /// </remarks>
        public const int MetadataPrefixCharacters = 4096;

        private readonly SaveFileStore _store;
        private readonly SaveCodec _codec;
        private readonly ICoreLog _log;
        private readonly string _buildVersion;
        private readonly List<ISaveParticipant> _participants = new List<ISaveParticipant>();

        /// <param name="store">Where files land. Required.</param>
        /// <param name="codec">Document/text conversion and checksum verification. Required.</param>
        /// <param name="log">Diagnostics sink; null is tolerated.</param>
        /// <param name="buildVersion">
        /// Stamped into saves written through <see cref="Save"/>. Supplied by the caller rather than read
        /// from <c>Application.version</c> here, so the service stays testable without a player loop.
        /// Defaults to empty, which is correct for a caller that builds and stamps its own documents and
        /// writes them through <see cref="Write"/>.
        /// </param>
        public SaveSlotService(SaveFileStore store, SaveCodec codec, ICoreLog log, string buildVersion = "")
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            _store = store;
            _codec = codec;
            _log = log;
            _buildVersion = buildVersion ?? string.Empty;
        }

        /// <summary>
        /// Subsystems contributing sections, in registration order.
        /// </summary>
        /// <remarks>
        /// Order is preserved and is meaningful on restore: a participant that reads state another
        /// participant owns must be registered after it. Bootstrap registers the session before the player
        /// for that reason.
        /// </remarks>
        public IReadOnlyList<ISaveParticipant> Participants => _participants;

        /// <summary>
        /// Adds <paramref name="participant"/> to the save set.
        /// </summary>
        /// <remarks>
        /// Rejects a duplicate <see cref="ISaveParticipant.ParticipantId"/> rather than allowing it. Two
        /// participants sharing an id would take turns overwriting each other's section, producing a save
        /// that is valid, loads cleanly, and has silently lost half its contents.
        /// </remarks>
        /// <returns>False when the participant is null, has no id, or its id is already registered.</returns>
        public bool RegisterParticipant(ISaveParticipant participant)
        {
            if (participant == null || string.IsNullOrEmpty(participant.ParticipantId))
            {
                Warn(LogCode.SaveCorrupt, "rejected participant with no id");
                return false;
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                if (string.Equals(_participants[i].ParticipantId, participant.ParticipantId, StringComparison.Ordinal))
                {
                    Warn(LogCode.SaveCorrupt, "duplicate participant id " + participant.ParticipantId);
                    return false;
                }
            }

            _participants.Add(participant);
            return true;
        }

        /// <summary>
        /// Removes <paramref name="participant"/>. Its existing sections stay in saves already on disk and
        /// are carried through untouched by <see cref="SaveCodec"/>.
        /// </summary>
        public bool UnregisterParticipant(ISaveParticipant participant)
        {
            if (participant == null)
            {
                return false;
            }

            return _participants.Remove(participant);
        }

        /// <summary>True when <paramref name="slot"/> addresses the autosave ring rather than a manual slot.</summary>
        public static bool IsAutosaveSlot(int slot) => slot >= AutosaveSlotBase && slot < AutosaveSlotBase + AutosaveRingLength;

        /// <summary>Converts a ring index (0..<see cref="AutosaveRingLength"/>-1) into a slot index.</summary>
        public static int AutosaveSlot(int ringIndex) => AutosaveSlotBase + ringIndex;

        /// <summary>True when <paramref name="slot"/> addresses a real manual slot or ring entry.</summary>
        public static bool IsValidSlot(int slot) => (slot >= 0 && slot < SlotCount) || IsAutosaveSlot(slot);

        /// <summary>
        /// Captures every participant and writes the result to <paramref name="slot"/>.
        /// </summary>
        /// <param name="slot">Manual slot, or a ring slot from <see cref="AutosaveSlot"/>.</param>
        /// <param name="header">
        /// The run's facts — act, zone, zone display key, playtime, percent. Copied, not retained; the
        /// service stamps slot, timestamp, build and schema over the copy, so a caller may pass the same
        /// object to consecutive saves.
        /// </param>
        /// <returns>
        /// <see cref="ResultCode.Ok"/>, <see cref="ResultCode.InvalidArgument"/> for a bad slot, or
        /// <see cref="ResultCode.SaveWriteFailed"/> when a participant or the filesystem refused.
        /// </returns>
        public ResultCode Save(int slot, SaveMetadata header)
        {
            if (!IsValidSlot(slot))
            {
                Warn(LogCode.SaveCorrupt, "save to invalid slot " + slot.ToString(CultureInfo.InvariantCulture));
                return ResultCode.InvalidArgument;
            }

            var savedAtIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            var document = new SaveDocument
            {
                SchemaVersion = SaveDocument.CurrentSchemaVersion,
                BuildVersion = _buildVersion,
                SavedAtIso = savedAtIso,
                Metadata = StampHeader(header, slot, savedAtIso)
            };

            for (var i = 0; i < _participants.Count; i++)
            {
                var participant = _participants[i];
                try
                {
                    participant.Capture(document);
                }
                catch (Exception exception)
                {
                    // The whole save fails rather than shipping without this section. A document missing
                    // the session is not a partial save, it is a save that will load into an empty world —
                    // and the player would have no way to tell until they pressed Continue.
                    Warn(
                        LogCode.SaveCorrupt,
                        "capture failed for " + participant.ParticipantId + ": " + exception.GetType().Name);
                    return ResultCode.SaveWriteFailed;
                }
            }

            return Write(slot, document);
        }

        /// <summary>
        /// Encodes an already-captured document and writes it to <paramref name="slot"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The low half of the two-level API. <see cref="Save"/> is for a caller that wants the service to
        /// do everything — stamp the header, walk the participant register, write. This is for a caller
        /// that has already built the document itself, which is what a command handler does when it owns
        /// its own capture order and wants the validate/execute split to bound exactly what the write
        /// touches.
        /// </para>
        /// <para>
        /// Nothing is stamped here. The document is written as given, checksum included, because a caller
        /// at this level has taken responsibility for the header and silently overwriting its timestamp or
        /// slot number would make the two levels disagree about what was just saved.
        /// </para>
        /// </remarks>
        public ResultCode Write(int slot, SaveDocument document)
        {
            if (!IsValidSlot(slot) || document == null)
            {
                return ResultCode.InvalidArgument;
            }

            var payload = _codec.Encode(document);
            var code = _store.Write(FileKey(slot), payload);
            if (code != ResultCode.Ok)
            {
                Warn(LogCode.SaveCorrupt, "write failed for slot " + slot.ToString(CultureInfo.InvariantCulture));
            }

            return code;
        }

        /// <summary>
        /// Reads, verifies and migrates a slot's document without restoring anything.
        /// </summary>
        /// <remarks>
        /// The read counterpart of <see cref="Write"/>, for a caller that owns its own participant list.
        /// The <c>.bak</c> fallback and the migration both happen here, so a caller at this level gets the
        /// same recovery behaviour as <see cref="Load"/> and only takes over the restore step.
        /// </remarks>
        public ResultCode Read(int slot, out SaveDocument document)
        {
            document = null;

            if (!IsValidSlot(slot))
            {
                return ResultCode.InvalidArgument;
            }

            var key = FileKey(slot);
            var code = ReadAndDecode(key, false, out document);

            if (code == ResultCode.SaveCorrupt && _store.BackupExists(key))
            {
                Warn(LogCode.SaveCorrupt, "slot " + slot.ToString(CultureInfo.InvariantCulture) + " falling back to backup");
                code = ReadAndDecode(key, true, out document);
            }

            if (code != ResultCode.Ok)
            {
                document = null;
                return code;
            }

            var migration = SaveMigrator.Migrate(document, _log);
            if (migration != ResultCode.Ok)
            {
                document = null;
                return migration;
            }

            return ResultCode.Ok;
        }

        /// <summary>
        /// Writes an autosave into the oldest entry of the ring.
        /// </summary>
        /// <param name="header">Run facts, as for <see cref="Save"/>.</param>
        /// <param name="slot">The ring slot that was written, so a caller can report or reload it.</param>
        public ResultCode SaveAutosave(SaveMetadata header, out int slot)
        {
            slot = AutosaveSlot(NextAutosaveRingIndex());
            return Save(slot, header);
        }

        /// <summary>
        /// Loads <paramref name="slot"/> and hands each registered participant its section.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On a decode failure the rotated backup is tried before giving up. That is the entire reason the
        /// backup exists: the realistic corruption is a write interrupted by the OS killing the app, which
        /// damages the live file and leaves the previous one — one save older, and intact — sitting beside
        /// it. Falling back costs the player a few minutes of progress and saves the run.
        /// </para>
        /// <para>
        /// Participants are restored only after the document has been verified AND migrated, so no
        /// participant ever sees a section from a version it does not understand.
        /// </para>
        /// </remarks>
        public ResultCode Load(int slot)
        {
            SaveDocument document;
            var code = Read(slot, out document);
            if (code != ResultCode.Ok)
            {
                return code;
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                var participant = _participants[i];
                try
                {
                    participant.Restore(document);
                }
                catch (Exception exception)
                {
                    // A participant that throws on restore has been handed a section it cannot make sense
                    // of. Reporting corruption is honest: the game state is now half-loaded and must not be
                    // entered, and the caller's job is to return to the menu rather than to the world.
                    Warn(
                        LogCode.SaveCorrupt,
                        "restore failed for " + participant.ParticipantId + ": " + exception.GetType().Name);
                    return ResultCode.SaveCorrupt;
                }
            }

            return ResultCode.Ok;
        }

        /// <summary>
        /// Reads a slot's header without deserializing its body.
        /// </summary>
        /// <remarks>
        /// Reads a bounded prefix of the file first and only falls back to a full read if the header is not
        /// complete within it — so the common case never touches the sections at all. A slot that will not
        /// yield a header is reported, not thrown: the menu lists it as unusable and moves on.
        /// </remarks>
        /// <returns>
        /// <see cref="ResultCode.Ok"/>; <see cref="ResultCode.SlotEmpty"/> when nothing is saved there;
        /// <see cref="ResultCode.SaveCorrupt"/> when the file exists but has no readable header.
        /// </returns>
        public ResultCode ReadMetadata(int slot, out SaveMetadata metadata)
        {
            metadata = null;

            if (!IsValidSlot(slot))
            {
                return ResultCode.InvalidArgument;
            }

            var key = FileKey(slot);

            string prefix;
            var readCode = _store.ReadPrefix(key, MetadataPrefixCharacters, out prefix);
            if (readCode == ResultCode.SlotEmpty)
            {
                return ResultCode.SlotEmpty;
            }

            if (readCode == ResultCode.Ok && _codec.DecodeMetadata(prefix, out metadata) == ResultCode.Ok)
            {
                return ResultCode.Ok;
            }

            // Either the prefix was too short to contain a complete header, or the head of the file is
            // damaged. One full read settles which.
            string whole;
            if (_store.Read(key, out whole) == ResultCode.Ok
                && _codec.DecodeMetadata(whole, out metadata) == ResultCode.Ok)
            {
                return ResultCode.Ok;
            }

            metadata = null;
            Warn(LogCode.SaveCorrupt, "unreadable header in slot " + slot.ToString(CultureInfo.InvariantCulture));
            return ResultCode.SaveCorrupt;
        }

        /// <summary>
        /// Fills <paramref name="destination"/> with one entry per manual slot, in slot order.
        /// </summary>
        /// <remarks>
        /// Corrupt and empty slots are represented by a null entry rather than omitted, so the caller's
        /// index into the list is still the slot number — a list that silently shortens is how a player
        /// ends up overwriting slot 2 while looking at slot 3.
        /// </remarks>
        public void ReadAllMetadata(IList<SaveMetadata> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                SaveMetadata metadata;
                destination.Add(ReadMetadata(slot, out metadata) == ResultCode.Ok ? metadata : null);
            }
        }

        /// <summary>
        /// Finds the most recently written save across the manual slots and the autosave ring — what the
        /// menu's Continue button resumes.
        /// </summary>
        /// <remarks>
        /// Compares <see cref="SaveMetadata.SavedAtIso"/> ordinally. That is correct, not a shortcut: the
        /// timestamps are written as round-trip UTC ISO-8601 with fixed-width fields, a form whose
        /// lexicographic order is its chronological order. Parsing to <see cref="DateTime"/> would add a
        /// culture and a failure mode for no gain.
        /// </remarks>
        /// <returns>False when every slot is empty or unreadable.</returns>
        public bool TryFindMostRecent(out int slot, out SaveMetadata metadata)
        {
            slot = -1;
            metadata = null;

            for (var i = 0; i < SlotCount; i++)
            {
                ConsiderNewer(i, ref slot, ref metadata);
            }

            for (var ring = 0; ring < AutosaveRingLength; ring++)
            {
                ConsiderNewer(AutosaveSlot(ring), ref slot, ref metadata);
            }

            return slot >= 0;
        }

        /// <summary>Deletes a slot, including its backup. Reports success for an already-empty slot.</summary>
        public ResultCode Delete(int slot)
        {
            if (!IsValidSlot(slot))
            {
                return ResultCode.InvalidArgument;
            }

            return _store.Delete(FileKey(slot));
        }

        /// <summary>
        /// Picks the ring entry to overwrite: the first empty one, otherwise the oldest.
        /// </summary>
        /// <remarks>
        /// Empty entries are filled before anything is overwritten, so a fresh install builds up a full
        /// ring of distinct restore points instead of cycling one entry. An unreadable entry is treated as
        /// the best candidate to overwrite — it holds nothing recoverable anyway, and reusing it quietly
        /// repairs the ring.
        /// </remarks>
        private int NextAutosaveRingIndex()
        {
            var oldestIndex = 0;
            string oldestStamp = null;

            for (var ring = 0; ring < AutosaveRingLength; ring++)
            {
                SaveMetadata metadata;
                var code = ReadMetadata(AutosaveSlot(ring), out metadata);

                if (code != ResultCode.Ok || metadata == null)
                {
                    return ring;
                }

                var stamp = metadata.SavedAtIso ?? string.Empty;
                if (oldestStamp == null || string.CompareOrdinal(stamp, oldestStamp) < 0)
                {
                    oldestStamp = stamp;
                    oldestIndex = ring;
                }
            }

            return oldestIndex;
        }

        private void ConsiderNewer(int candidateSlot, ref int bestSlot, ref SaveMetadata bestMetadata)
        {
            SaveMetadata metadata;
            if (ReadMetadata(candidateSlot, out metadata) != ResultCode.Ok || metadata == null)
            {
                return;
            }

            var candidateStamp = metadata.SavedAtIso ?? string.Empty;
            if (bestMetadata == null
                || string.CompareOrdinal(candidateStamp, bestMetadata.SavedAtIso ?? string.Empty) > 0)
            {
                bestSlot = candidateSlot;
                bestMetadata = metadata;
            }
        }

        private ResultCode ReadAndDecode(string key, bool fromBackup, out SaveDocument document)
        {
            document = null;

            string text;
            var readCode = fromBackup ? _store.ReadBackup(key, out text) : _store.Read(key, out text);
            if (readCode != ResultCode.Ok)
            {
                return readCode;
            }

            var decodeCode = _codec.Decode(text, out document);
            if (decodeCode != ResultCode.Ok)
            {
                Warn(LogCode.SaveCorrupt, key + (fromBackup ? ".bak" : string.Empty) + " decode: " + decodeCode);
            }

            return decodeCode;
        }

        /// <summary>
        /// Copies the caller's header and stamps the fields the service owns.
        /// </summary>
        /// <remarks>
        /// Copied rather than mutated in place so a caller can keep one header object for the session and
        /// pass it to every save without watching it acquire another slot's number.
        /// </remarks>
        private SaveMetadata StampHeader(SaveMetadata source, int slot, string savedAtIso)
        {
            var stamped = new SaveMetadata
            {
                Slot = slot,
                SavedAtIso = savedAtIso,
                BuildVersion = _buildVersion,
                SchemaVersion = SaveDocument.CurrentSchemaVersion
            };

            if (source != null)
            {
                stamped.ActId = source.ActId ?? string.Empty;
                stamped.ZoneId = source.ZoneId ?? string.Empty;
                stamped.ZoneDisplayKey = source.ZoneDisplayKey ?? string.Empty;
                stamped.PlaytimeSeconds = source.PlaytimeSeconds;
                stamped.RecordedPercent = source.RecordedPercent;
            }

            return stamped;
        }

        /// <summary>
        /// Maps a slot index to its file key. Manual slots are <c>slot0</c>..<c>slot2</c>; ring entries are
        /// <c>auto0</c>..<c>auto2</c>. These names appear on players' devices, so they are stable.
        /// </summary>
        private static string FileKey(int slot)
        {
            if (IsAutosaveSlot(slot))
            {
                return "auto" + (slot - AutosaveSlotBase).ToString(CultureInfo.InvariantCulture);
            }

            return "slot" + slot.ToString(CultureInfo.InvariantCulture);
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log == null)
            {
                return;
            }

            _log.Warn(code, detail);
        }
    }
}
