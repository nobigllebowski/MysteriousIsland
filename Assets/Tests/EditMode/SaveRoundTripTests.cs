using System;
using System.Collections.Generic;
using System.IO;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.State;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Covers the save pipeline end to end: capture, encode, verify, decode, restore — plus the two
    /// refusals (corrupt body, too-new schema) and the deep-copy property everything else rests on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the tests worth having. A save bug does not present as a crash; it presents weeks
    /// later as a player whose position reset, whose clock jumped, or whose run diverged from the one
    /// they saved. None of that is visible at the call site, and none of it is caught by the compiler,
    /// so it has to be pinned down field by field here.
    /// </para>
    /// <para>
    /// The deep-copy test in particular exists because <see cref="GameState"/> is a graph of mutable
    /// POCOs with public fields. A <c>Clone</c> that assigned <c>Session</c> and <c>Player</c> by
    /// reference instead of cloning them would compile, would pass every equality assertion written
    /// against a fresh clone, and would silently make every snapshot a live view of the running game.
    /// The only way to catch it is to mutate the clone and look at the original.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class SaveRoundTripTests
    {
        private const string BuildVersion = "1.2.3-editmode";
        private const string SavedAtIso = "2026-09-14T08:30:00Z";
        private const int WorldSeed = 31337;
        private const int Slot = 2;

        /// <summary>
        /// The on-disk name <see cref="SaveSlotService"/> gives manual slot <see cref="Slot"/>. Spelled out
        /// rather than derived because the mapping is deliberately stable — these names appear on players'
        /// devices — and a test that reaches for the file directly should break if it ever changes.
        /// </summary>
        private const string SlotFileKey = "slot2";

        /// <summary>A locale with a table but a deliberately blank row, for the never-blank guarantee.</summary>
        private const string BlankValueLocale = "en";

        /// <summary>A second locale, so the fallback branch of the lookup can be exercised separately.</summary>
        private const string TranslatedLocale = "fr";

        private FakeCoreLog _log;
        private SaveCodec _codec;
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeCoreLog();
            _codec = new SaveCodec();
            _tempRoot = null;
        }

        /// <summary>
        /// Removes the scratch save directory, if a test made one. Failures to clean up are swallowed: a
        /// leftover temp folder is untidy, whereas a test that reports red because the OS held a handle a
        /// moment longer is actively misleading.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (string.IsNullOrEmpty(_tempRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            _tempRoot = null;
        }

        // ---------------------------------------------------------------- capture / restore

        /// <summary>
        /// A run written out and read back into a brand new service must be the same run, field for
        /// field, all the way down through the nested session and player state.
        /// </summary>
        [Test]
        public void SaveRoundTrip_CaptureThenRestore_ReproducesEveryGameStateField()
        {
            var source = NewPopulatedSession();

            // A clean save and a clean load must be completely silent. Failing inside the log puts the
            // failure stack on the call that warned, instead of on an assertion long after it.
            _log.FailOnWarn = true;

            var doc = new SaveDocument();
            source.Capture(doc);
            source.PlayerParticipant.Capture(doc);

            // Taken AFTER capture: Capture refreshes GameState.RngState from the live generator, so a
            // snapshot from before it would be comparing against a stale word.
            var expected = source.Snapshot();

            var target = new SessionService(new IslandClock(0d), _log, null);
            target.Restore(doc);
            target.PlayerParticipant.Restore(doc);
            var actual = target.Snapshot();

            Assert.AreEqual(expected.Session.ActId, actual.Session.ActId, "Session.ActId");
            Assert.AreEqual(expected.Session.ZoneId, actual.Session.ZoneId, "Session.ZoneId");
            Assert.AreEqual(expected.Session.SimHours, actual.Session.SimHours, "Session.SimHours");
            Assert.AreEqual(expected.Session.PlaytimeSeconds, actual.Session.PlaytimeSeconds, "Session.PlaytimeSeconds");
            Assert.AreEqual(expected.Session.RecordedPercent, actual.Session.RecordedPercent, "Session.RecordedPercent");

            Assert.AreEqual(expected.Player.Position.X, actual.Player.Position.X, "Player.Position.X");
            Assert.AreEqual(expected.Player.Position.Y, actual.Player.Position.Y, "Player.Position.Y");
            Assert.AreEqual(expected.Player.Position.Z, actual.Player.Position.Z, "Player.Position.Z");
            Assert.IsTrue(expected.Player.Position == actual.Player.Position, "Player.Position exact equality");
            Assert.AreEqual(expected.Player.YawDegrees, actual.Player.YawDegrees, "Player.YawDegrees");
            Assert.AreEqual(expected.Player.EquippedToolInstanceId, actual.Player.EquippedToolInstanceId, "Player.EquippedToolInstanceId");

            Assert.AreEqual(expected.RngState, actual.RngState, "RngState");

            Assert.AreEqual(Slot, target.BoundSlot, "BoundSlot did not survive the round trip.");
            Assert.IsTrue(target.HasRun, "A restored session must report that it has a run.");
            Assert.AreEqual(expected.Session.SimHours, target.Clock.SimHours, "The clock was not re-synced to the restored sim hours.");
            Assert.AreEqual(0, _log.CountOf(LogCode.SaveCorrupt), "A clean round trip reported corruption.");
        }

        /// <summary>
        /// The point of persisting the generator's position rather than its seed: the restored run must
        /// draw the numbers the original would have drawn next, not a fresh stream.
        /// </summary>
        [Test]
        public void SaveRoundTrip_RestoredSession_ContinuesTheSameRandomStream()
        {
            var source = NewPopulatedSession();

            var doc = new SaveDocument();
            source.Capture(doc);

            var target = new SessionService(new IslandClock(0d), _log, null);
            target.Restore(doc);

            Assert.AreEqual(source.Random.State, target.Random.State, "Restored generator is at a different position.");

            for (var i = 0; i < 16; i++)
            {
                Assert.AreEqual(source.Random.NextUInt64(), target.Random.NextUInt64(), "Streams diverged at draw " + i);
            }
        }

        /// <summary>
        /// A section a build has never heard of is carried across untouched. That is what keeps a save
        /// survivable through a build with a feature switched off, instead of quietly deleting it.
        /// </summary>
        [Test]
        public void SaveRoundTrip_UnknownSection_SurvivesEncodeAndDecode()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"actId\":\"act1\"}");
            doc.PutSection("weather.phase2", "{\"front\":\"squall\"}");

            var text = _codec.Encode(doc);

            SaveDocument decoded;
            Assert.AreEqual(ResultCode.Ok, _codec.Decode(text, out decoded));

            string payload;
            Assert.IsTrue(decoded.TryGetSection("weather.phase2", out payload), "Unknown section was dropped.");
            Assert.AreEqual("{\"front\":\"squall\"}", payload);
        }

        /// <summary>
        /// A save written before a participant existed is not an error: that participant keeps its
        /// defaults and the load proceeds.
        /// </summary>
        [Test]
        public void SaveRoundTrip_MissingSection_LeavesParticipantAtDefaults()
        {
            var doc = NewEnvelope();

            var target = new SessionService(new IslandClock(0d), _log, null);
            target.PlayerParticipant.Restore(doc);

            Assert.AreEqual(Vec3.Zero, target.PlayerPosition);
            Assert.AreEqual(0f, target.PlayerYawDegrees);
            Assert.AreEqual(string.Empty, target.EquippedToolInstanceId);
            Assert.AreEqual(0, _log.CountOf(LogCode.SaveCorrupt), "An absent section must not be reported as corruption.");
        }

        // ---------------------------------------------------------------- envelope

        [Test]
        public void SaveCodec_Encode_StampsSchemaVersionBuildVersionAndChecksum()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"actId\":\"act1\"}");

            var text = _codec.Encode(doc);

            Assert.IsNotNull(text);
            Assert.AreNotEqual(0u, doc.Checksum, "Encode left the checksum unset.");
            StringAssert.Contains("\"schemaVersion\":" + SaveDocument.CurrentSchemaVersion, text);
            StringAssert.Contains("\"buildVersion\":\"" + BuildVersion + "\"", text);
            StringAssert.Contains("\"checksum\":" + doc.Checksum.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
            StringAssert.Contains("\"savedAtIso\":\"" + SavedAtIso + "\"", text);

            SaveDocument decoded;
            Assert.AreEqual(ResultCode.Ok, _codec.Decode(text, out decoded));
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, decoded.SchemaVersion);
            Assert.AreEqual(BuildVersion, decoded.BuildVersion);
            Assert.AreEqual(SavedAtIso, decoded.SavedAtIso);
            Assert.AreEqual(doc.Checksum, decoded.Checksum);
        }

        /// <summary>
        /// The two-pass checksum protocol only works if the writer is deterministic. Encoding the same
        /// document twice must produce byte-identical text, or verification is comparing against a
        /// moving target and every save eventually fails its own integrity check.
        /// </summary>
        [Test]
        public void SaveCodec_EncodeTwice_ProducesIdenticalText()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"actId\":\"act1\",\"zoneId\":\"ZoneRibcage\"}");
            doc.PutSection("player", "{\"x\":1.5,\"y\":0,\"z\":-2.25}");

            var first = _codec.Encode(doc);
            var second = _codec.Encode(doc);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void SaveCodec_EmptyText_ReportsSlotEmpty()
        {
            SaveDocument decoded;

            Assert.AreEqual(ResultCode.SlotEmpty, _codec.Decode(string.Empty, out decoded));
            Assert.IsNull(decoded);
            Assert.AreEqual(ResultCode.SlotEmpty, _codec.Decode(null, out decoded));
            Assert.IsNull(decoded);
        }

        // ---------------------------------------------------------------- corruption

        /// <summary>
        /// Flipping bytes inside the body must be caught, and must yield nothing at all — a partially
        /// populated document is more dangerous than none, because the game will happily run on it.
        /// </summary>
        [Test]
        public void SaveCodec_TamperedBody_FailsChecksumWithSaveCorrupt()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"marker\":\"AAAA\"}");

            var text = _codec.Encode(doc);

            Assert.AreEqual(
                text.IndexOf("AAAA", StringComparison.Ordinal),
                text.LastIndexOf("AAAA", StringComparison.Ordinal),
                "Fixture is ambiguous: the tamper marker appears more than once.");
            Assert.Greater(text.IndexOf("AAAA", StringComparison.Ordinal), 0, "Fixture is broken: the tamper marker is absent.");

            var tampered = text.Replace("AAAA", "BBBB");
            Assert.AreNotEqual(text, tampered);

            SaveDocument decoded;
            var code = _codec.Decode(tampered, out decoded);

            Assert.AreEqual(ResultCode.SaveCorrupt, code);
            Assert.IsNull(decoded, "A failed decode must not hand back a partial document.");
        }

        [Test]
        public void SaveCodec_TamperedChecksumField_ReportsSaveCorrupt()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"actId\":\"act1\"}");
            var text = _codec.Encode(doc);

            var original = "\"checksum\":" + doc.Checksum.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tampered = text.Replace(original, "\"checksum\":1");

            Assert.AreNotEqual(text, tampered, "Fixture is broken: the checksum member was not found.");

            SaveDocument decoded;
            Assert.AreEqual(ResultCode.SaveCorrupt, _codec.Decode(tampered, out decoded));
            Assert.IsNull(decoded);
        }

        [Test]
        public void SaveCodec_UnparseableText_ReportsSaveCorruptWithoutThrowing()
        {
            SaveDocument decoded = null;
            ResultCode code = ResultCode.Ok;

            Assert.DoesNotThrow(() => code = _codec.Decode("{ this is not json", out decoded));

            Assert.AreEqual(ResultCode.SaveCorrupt, code);
            Assert.IsNull(decoded);
        }

        // ---------------------------------------------------------------- version guard

        /// <summary>
        /// A save from a newer build is not corrupt; it is beyond us. The distinction is load-bearing:
        /// a player who downgraded must be told their save is too new, not told it is damaged and
        /// offered a fresh start.
        /// </summary>
        [Test]
        public void SaveCodec_SchemaVersionNewerThanCurrent_ReportsSaveVersionTooNew()
        {
            var doc = NewEnvelope();
            doc.SchemaVersion = SaveDocument.CurrentSchemaVersion + 1;
            doc.Metadata.SchemaVersion = doc.SchemaVersion;
            doc.PutSection("session", "{\"actId\":\"act1\"}");

            var text = _codec.Encode(doc);

            SaveDocument decoded;
            var code = _codec.Decode(text, out decoded);

            Assert.AreEqual(ResultCode.SaveVersionTooNew, code);
            Assert.IsNull(decoded);
        }

        [Test]
        public void SaveMigrator_SchemaVersionNewerThanCurrent_ReportsSaveVersionTooNew()
        {
            var doc = new SaveDocument();
            doc.SchemaVersion = SaveDocument.CurrentSchemaVersion + 1;

            var code = SaveMigrator.Migrate(doc, _log);

            Assert.AreEqual(ResultCode.SaveVersionTooNew, code);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion + 1, doc.SchemaVersion, "A refused migration must not rewrite the version.");
            Assert.IsTrue(_log.Contains(LogCode.SaveCorrupt), "The refusal was not reported anywhere.");
        }

        [Test]
        public void SaveMigrator_DocumentAtCurrentVersion_SucceedsAndSyncsMetadata()
        {
            var doc = new SaveDocument();
            doc.Metadata.SchemaVersion = 0;

            var code = SaveMigrator.Migrate(doc, _log);

            Assert.AreEqual(ResultCode.Ok, code);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, doc.SchemaVersion);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, doc.Metadata.SchemaVersion, "The header's version copy was left stale.");
            Assert.AreEqual(0, _log.CountOf(LogCode.SaveMigrationApplied), "Nothing should have been migrated.");
        }

        /// <summary>
        /// Below the current version with no migration registered, the document is unreachable rather
        /// than merely old — the honest answer is corrupt, not a silent best-effort load.
        /// </summary>
        [Test]
        public void SaveMigrator_UnreachableOlderVersion_ReportsSaveCorrupt()
        {
            var doc = new SaveDocument();
            doc.SchemaVersion = SaveDocument.CurrentSchemaVersion - 1;

            var code = SaveMigrator.Migrate(doc, _log);

            Assert.AreEqual(ResultCode.SaveCorrupt, code);
            Assert.IsTrue(_log.Contains(LogCode.SaveCorrupt));
        }

        [Test]
        public void SaveMigrator_NullDocument_ReportsSaveCorruptWithoutThrowing()
        {
            ResultCode code = ResultCode.Ok;

            Assert.DoesNotThrow(() => code = SaveMigrator.Migrate(null, _log));

            Assert.AreEqual(ResultCode.SaveCorrupt, code);
        }

        // ---------------------------------------------------------------- header-only read

        /// <summary>
        /// Drawing the slot list must not cost a full parse per slot. Proven by handing
        /// <see cref="SaveCodec.DecodeMetadata"/> a PREFIX of the file that stops before the sections
        /// begin: it succeeds, while a full decode of the same prefix cannot.
        /// </summary>
        [Test]
        public void SaveCodec_DecodeMetadata_ReadsHeaderFromPrefixWithoutTheBody()
        {
            var doc = NewEnvelope();
            doc.Metadata.Slot = Slot;
            doc.Metadata.ActId = "act1";
            doc.Metadata.ZoneId = SceneKeys.ZoneRibcage;
            doc.Metadata.ZoneDisplayKey = SceneKeys.ZoneDisplayKey(SceneKeys.ZoneRibcage);
            doc.Metadata.PlaytimeSeconds = 1234.5d;
            doc.Metadata.RecordedPercent = 37;
            doc.Metadata.SavedAtIso = SavedAtIso;
            doc.Metadata.BuildVersion = BuildVersion;
            doc.PutSection("session", "{\"actId\":\"act1\",\"zoneId\":\"ZoneRibcage\"}");

            var text = _codec.Encode(doc);

            var sectionsAt = text.IndexOf(",\"sections\"", StringComparison.Ordinal);
            Assert.Greater(sectionsAt, 0, "Fixture is broken: the envelope has no sections member.");

            var prefix = text.Substring(0, sectionsAt);

            SaveMetadata metadata;
            var code = _codec.DecodeMetadata(prefix, out metadata);

            Assert.AreEqual(ResultCode.Ok, code, "The header could not be read from a body-free prefix.");
            Assert.IsNotNull(metadata);
            Assert.AreEqual(Slot, metadata.Slot);
            Assert.AreEqual("act1", metadata.ActId);
            Assert.AreEqual(SceneKeys.ZoneRibcage, metadata.ZoneId);
            Assert.AreEqual(SceneKeys.ZoneDisplayKey(SceneKeys.ZoneRibcage), metadata.ZoneDisplayKey);
            Assert.AreEqual(1234.5d, metadata.PlaytimeSeconds);
            Assert.AreEqual(37, metadata.RecordedPercent);
            Assert.AreEqual(SavedAtIso, metadata.SavedAtIso);
            Assert.AreEqual(BuildVersion, metadata.BuildVersion);
            Assert.AreEqual(SaveDocument.CurrentSchemaVersion, metadata.SchemaVersion);

            // The same prefix is NOT a loadable save — which is exactly why the header read had to
            // avoid the body rather than decode and take Metadata.
            SaveDocument whole;
            Assert.AreNotEqual(ResultCode.Ok, _codec.Decode(prefix, out whole));
        }

        /// <summary>
        /// Section payloads are serialized blobs containing braces and quotes of their own. The header
        /// scanner walks raw text, so a section that merely mentions <c>metadata</c> must not be
        /// mistaken for the header.
        /// </summary>
        [Test]
        public void SaveCodec_DecodeMetadata_IgnoresBracesAndKeywordsInsideSectionPayloads()
        {
            var doc = NewEnvelope();
            doc.Metadata.ActId = "act1";
            doc.Metadata.RecordedPercent = 84;
            doc.PutSection("session", "{\"metadata\":\"{not the real header}\",\"recordedPercent\":9}");

            var text = _codec.Encode(doc);

            SaveMetadata metadata;
            Assert.AreEqual(ResultCode.Ok, _codec.DecodeMetadata(text, out metadata));
            Assert.AreEqual(84, metadata.RecordedPercent, "The scanner latched onto a decoy inside a section payload.");
            Assert.AreEqual("act1", metadata.ActId);
        }

        [Test]
        public void SaveCodec_DecodeMetadata_EmptyTextReportsSlotEmpty()
        {
            SaveMetadata metadata;

            Assert.AreEqual(ResultCode.SlotEmpty, _codec.DecodeMetadata(string.Empty, out metadata));
            Assert.IsNull(metadata);
        }

        [Test]
        public void SaveCodec_DecodeMetadata_TooShortPrefixReportsSaveCorrupt()
        {
            var doc = NewEnvelope();
            doc.PutSection("session", "{\"actId\":\"act1\"}");
            var text = _codec.Encode(doc);

            SaveMetadata metadata;
            var code = _codec.DecodeMetadata(text.Substring(0, 12), out metadata);

            Assert.AreEqual(ResultCode.SaveCorrupt, code);
            Assert.IsNull(metadata);
        }

        // ---------------------------------------------------------------- backup recovery

        /// <summary>
        /// The disaster the <c>.bak</c> exists for, reproduced exactly: the process is killed mid-write,
        /// the rename has landed but the bytes have not, and the live file is left at zero length.
        /// </summary>
        /// <remarks>
        /// The point of truncating rather than scribbling over the file is that a zero-length save is NOT
        /// <see cref="ResultCode.SaveCorrupt"/>. It reads back cleanly as empty text and decodes as
        /// <see cref="ResultCode.SlotEmpty"/> — so a fallback that triggers only on corruption is
        /// unreachable in precisely the scenario it was written for, and the half-written slot is then
        /// overwritten by the next save with the last good copy still sitting beside it.
        /// </remarks>
        [Test]
        public void SaveSlotService_LiveFileTruncatedByAKillMidWrite_RecoversFromTheBackup()
        {
            var store = NewTempStore();
            var slots = new SaveSlotService(store, _codec, _log, BuildVersion);

            // Two writes: the first is rotated into the backup by the second. One write leaves no backup
            // at all, which is the state a genuinely fresh slot is in.
            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act1", 11)));
            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act2", 22)));

            var livePath = store.GetSavePath(SlotFileKey);
            Assert.IsTrue(
                File.Exists(store.GetBackupPath(SlotFileKey)),
                "Fixture is broken: the second write did not rotate a backup in.");

            using (var truncate = new FileStream(livePath, FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                truncate.SetLength(0L);
            }

            Assert.AreEqual(0L, new FileInfo(livePath).Length, "Fixture is broken: the live file was not truncated.");

            SaveDocument recovered;
            var code = slots.Read(Slot, out recovered);

            Assert.AreEqual(ResultCode.Ok, code, "A zero-length live file did not fall back to the backup.");
            Assert.IsNotNull(recovered, "A successful recovery handed back no document.");
            Assert.AreEqual("act1", recovered.Metadata.ActId, "The recovered save is not the rotated previous one.");
            Assert.AreEqual(11, recovered.Metadata.RecordedPercent);
            Assert.IsTrue(_log.Contains(LogCode.SaveCorrupt), "The recovery took a path nobody logged.");
        }

        /// <summary>
        /// The same recovery, seen through the header read the main menu actually uses.
        /// </summary>
        /// <remarks>
        /// <c>TryFindMostRecent</c> and <c>ReadAllMetadata</c> are both built on <c>ReadMetadata</c>, so a
        /// header read that gives up without consulting the backup makes a recoverable slot show as
        /// unusable and makes CONTINUE refuse a run the loader would have opened without complaint. The
        /// menu and the loader have to agree about what is loadable.
        /// </remarks>
        [Test]
        public void SaveSlotService_ReadMetadataWithACorruptLiveFile_RecoversTheHeaderFromTheBackup()
        {
            var store = NewTempStore();
            var slots = new SaveSlotService(store, _codec, _log, BuildVersion);

            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act1", 11)));
            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act2", 22)));

            // Not truncation this time: a file that is present, non-empty, and has no header in it.
            File.WriteAllText(store.GetSavePath(SlotFileKey), "@@@ this is not a save @@@");

            SaveMetadata metadata;
            var code = slots.ReadMetadata(Slot, out metadata);

            Assert.AreEqual(ResultCode.Ok, code, "A corrupt live file reported no header despite a good backup.");
            Assert.IsNotNull(metadata);
            Assert.AreEqual("act1", metadata.ActId, "The header did not come from the backup.");
            Assert.AreEqual(11, metadata.RecordedPercent);

            int recentSlot;
            SaveMetadata recent;
            Assert.IsTrue(
                slots.TryFindMostRecent(out recentSlot, out recent),
                "CONTINUE would have refused a save that is sitting recoverable on disk.");
            Assert.AreEqual(Slot, recentSlot);
            Assert.AreEqual("act1", recent.ActId);
        }

        /// <summary>
        /// A write that fails must leave the previous good state intact — both copies of it.
        /// </summary>
        /// <remarks>
        /// The failure is arranged by planting a DIRECTORY where the temp file has to go, which makes the
        /// very first step of the write throw on every platform without depending on advisory file locks
        /// or on a full disk. What is asserted is the guarantee that matters: the live save still loads and
        /// the backup is still there behind it. A failure path that tidied either of them away would turn
        /// a save that did not happen into a save that was destroyed.
        /// </remarks>
        [Test]
        public void SaveFileStore_FailedWrite_LeavesBothTheLiveSaveAndTheBackupRecoverable()
        {
            var store = NewTempStore();
            var slots = new SaveSlotService(store, _codec, _log, BuildVersion);

            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act1", 11)));
            Assert.AreEqual(ResultCode.Ok, slots.Write(Slot, NewSlotDocument("act2", 22)));

            Directory.CreateDirectory(store.GetTempPath(SlotFileKey));

            Assert.AreEqual(
                ResultCode.SaveWriteFailed,
                slots.Write(Slot, NewSlotDocument("act3", 33)),
                "Fixture is broken: the write was expected to fail.");

            SaveDocument live;
            Assert.AreEqual(ResultCode.Ok, slots.Read(Slot, out live), "A failed write left the live save unreadable.");
            Assert.AreEqual("act2", live.Metadata.ActId, "A failed write disturbed the live save.");

            string backupText;
            Assert.AreEqual(
                ResultCode.Ok,
                store.ReadBackup(SlotFileKey, out backupText),
                "A failed write destroyed the backup.");

            SaveDocument backup;
            Assert.AreEqual(ResultCode.Ok, _codec.Decode(backupText, out backup), "The surviving backup no longer decodes.");
            Assert.AreEqual("act1", backup.Metadata.ActId, "The backup is no longer the previous good save.");
        }

        // ---------------------------------------------------------------- never blank

        /// <summary>
        /// The never-blank guarantee, at the one hole that defeats it: a key that is PRESENT and EMPTY.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This case is not hypothetical and it is not malformed input. <c>CsvTableParser</c> drops a row
        /// only when its KEY is empty, so the line <c>ui.menu.continue,</c> — a translator who tabbed past
        /// a cell, a merge that emptied a column — parses into a real entry whose value is <c>""</c>. A
        /// lookup that treats presence as a hit returns that empty string, and the Continue button renders
        /// with no text on it: invisible in a screenshot, which is the only artefact a missing-translation
        /// bug ever arrives as, and reported nowhere because nothing looked like a miss.
        /// </para>
        /// <para>
        /// It lives in this fixture rather than beside the other localization tests only because of file
        /// ownership; behaviourally it belongs with them.
        /// </para>
        /// </remarks>
        [Test]
        public void Localization_KeyPresentWithAnEmptyValue_RendersHashKeyAndIsReportedMissing()
        {
            const string key = "ui.menu.continue";

            var loc = new StringTableLocalization(_log, BlankValueLocale);
            loc.AddTable(BlankValueLocale, new[] { new KeyValuePair<string, string>(key, string.Empty) });

            var result = loc.Get(new LocKey(key));

            Assert.AreEqual("#" + key + "#", result, "A present-but-empty row was accepted as a translation.");
            Assert.AreNotEqual(string.Empty, result, "The never-blank guarantee was defeated by an empty value.");
            Assert.IsFalse(loc.Has(new LocKey(key)), "A row with no text must not count as having the key.");
            Assert.AreEqual(
                1,
                _log.CountOf(LogCode.MissingLocKey, key),
                "An empty value must be reported exactly like an absent one.");
        }

        /// <summary>
        /// The other half of the same fix: an empty row in the CURRENT locale must fall THROUGH to the
        /// fallback locale, not short-circuit there. Stopping at the empty row would hide a perfectly good
        /// English string behind a blank French one.
        /// </summary>
        [Test]
        public void Localization_EmptyValueInCurrentLocale_FallsThroughToTheFallbackLocale()
        {
            const string key = "ui.menu.continue";

            var loc = new StringTableLocalization(_log, BlankValueLocale);
            loc.AddTable(BlankValueLocale, new[] { new KeyValuePair<string, string>(key, "Continue") });
            loc.AddTable(TranslatedLocale, new[] { new KeyValuePair<string, string>(key, string.Empty) });

            Assert.IsTrue(loc.SetLocale(TranslatedLocale), "Fixture is broken: the second locale has no table.");

            Assert.AreEqual("Continue", loc.Get(new LocKey(key)), "The blank current-locale row blocked the fallback.");
            Assert.IsTrue(loc.Has(new LocKey(key)));
            Assert.AreEqual(0, _log.CountOf(LogCode.MissingLocKey, key), "A key resolved from the fallback is not missing.");
        }

        // ---------------------------------------------------------------- deep copy

        /// <summary>
        /// The aliasing bug no compiler catches: a shallow <c>Clone</c> compiles, type-checks, and
        /// passes every assertion made against a freshly taken copy. Mutating the clone and inspecting
        /// the ORIGINAL is the only thing that distinguishes a snapshot from a live view.
        /// </summary>
        [Test]
        public void GameStateClone_MutatingTheClone_LeavesTheOriginalUntouched()
        {
            var original = new GameState();
            original.Session.ActId = "act1";
            original.Session.ZoneId = SceneKeys.ZoneRibcage;
            original.Session.SimHours = 5.25d;
            original.Session.PlaytimeSeconds = 1234.5d;
            original.Session.RecordedPercent = 37;
            original.Player.Position = new Vec3(1.5f, 2.25f, -3.75f);
            original.Player.YawDegrees = 91.5f;
            original.Player.EquippedToolInstanceId = "tool-instance-7";
            original.RngState = 0xDEADBEEFCAFEF00DUL;

            var clone = original.Clone();

            Assert.AreNotSame(original, clone, "Clone returned the same instance.");
            Assert.AreNotSame(original.Session, clone.Session, "Session is shared between original and clone.");
            Assert.AreNotSame(original.Player, clone.Player, "Player is shared between original and clone.");

            clone.Session.ActId = "act9";
            clone.Session.ZoneId = SceneKeys.ZoneFernmaw;
            clone.Session.SimHours = 999d;
            clone.Session.PlaytimeSeconds = 1d;
            clone.Session.RecordedPercent = 100;
            clone.Player.Position = new Vec3(-1f, -1f, -1f);
            clone.Player.YawDegrees = 0f;
            clone.Player.EquippedToolInstanceId = "tool-instance-other";
            clone.RngState = 1UL;

            Assert.AreEqual("act1", original.Session.ActId);
            Assert.AreEqual(SceneKeys.ZoneRibcage, original.Session.ZoneId);
            Assert.AreEqual(5.25d, original.Session.SimHours);
            Assert.AreEqual(1234.5d, original.Session.PlaytimeSeconds);
            Assert.AreEqual(37, original.Session.RecordedPercent);
            Assert.AreEqual(new Vec3(1.5f, 2.25f, -3.75f), original.Player.Position);
            Assert.AreEqual(91.5f, original.Player.YawDegrees);
            Assert.AreEqual("tool-instance-7", original.Player.EquippedToolInstanceId);
            Assert.AreEqual(0xDEADBEEFCAFEF00DUL, original.RngState);
        }

        /// <summary>
        /// The reverse direction: the clone must not see later writes to the original either. Sole
        /// ownership of <see cref="GameState"/> means a snapshot handed to a reader has to stay frozen
        /// while the simulation keeps running.
        /// </summary>
        [Test]
        public void GameStateClone_MutatingTheOriginal_LeavesTheCloneUntouched()
        {
            var original = new GameState();
            original.Session.ZoneId = SceneKeys.ZoneRibcage;
            original.Player.YawDegrees = 45f;

            var clone = original.Clone();

            original.Session.ZoneId = SceneKeys.ZoneFernmaw;
            original.Player.YawDegrees = 180f;
            original.Session = new SessionState();

            Assert.AreEqual(SceneKeys.ZoneRibcage, clone.Session.ZoneId);
            Assert.AreEqual(45f, clone.Player.YawDegrees);
        }

        /// <summary>
        /// A clone is always a valid state, even from a source whose children were nulled out. That is
        /// what lets every consumer skip null checks all the way down the tree.
        /// </summary>
        [Test]
        public void GameStateClone_NullChildren_ProducesValidChildrenRatherThanNulls()
        {
            var original = new GameState();
            original.Session = null;
            original.Player = null;

            var clone = original.Clone();

            Assert.IsNotNull(clone.Session);
            Assert.IsNotNull(clone.Player);
            Assert.AreEqual(string.Empty, clone.Session.ActId);
            Assert.AreEqual(Vec3.Zero, clone.Player.Position);
        }

        /// <summary>
        /// <see cref="SessionService.Snapshot"/> is the public face of the deep copy: mutating what a
        /// reader was handed must not reach the live run.
        /// </summary>
        [Test]
        public void SessionServiceSnapshot_MutatedByCaller_DoesNotAffectTheLiveRun()
        {
            var service = NewPopulatedSession();
            var snapshot = service.Snapshot();

            snapshot.Session.ZoneId = "tampered";
            snapshot.Session.RecordedPercent = 100;
            snapshot.Player.EquippedToolInstanceId = "tampered";

            Assert.AreEqual(SceneKeys.ZoneRibcage, service.ZoneId);
            Assert.AreEqual(37, service.RecordedPercent);
            Assert.AreEqual("tool-instance-7", service.EquippedToolInstanceId);
        }

        // ---------------------------------------------------------------- document primitives

        [Test]
        public void SaveDocument_TryGetSectionForUnknownId_ReturnsFalseAndEmptyNeverNull()
        {
            var doc = new SaveDocument();

            string payload;
            var found = doc.TryGetSection("nobody", out payload);

            Assert.IsFalse(found);
            Assert.IsNotNull(payload);
            Assert.AreEqual(string.Empty, payload);
        }

        [Test]
        public void SaveDocument_PutSectionWithNullPayload_StoresEmptyString()
        {
            var doc = new SaveDocument();
            doc.PutSection("session", null);

            string payload;
            Assert.IsTrue(doc.TryGetSection("session", out payload));
            Assert.AreEqual(string.Empty, payload);
        }

        [Test]
        public void SaveDocument_PutSectionTwice_ReplacesThePreviousPayload()
        {
            var doc = new SaveDocument();
            doc.PutSection("session", "first");
            doc.PutSection("session", "second");

            string payload;
            Assert.IsTrue(doc.TryGetSection("session", out payload));
            Assert.AreEqual("second", payload);
            Assert.AreEqual(1, doc.Sections.Count);
        }

        [Test]
        public void SaveDocument_PutSectionWithEmptyId_IsDroppedRatherThanThrowing()
        {
            var doc = new SaveDocument();

            Assert.DoesNotThrow(() => doc.PutSection(string.Empty, "payload"));
            Assert.DoesNotThrow(() => doc.PutSection(null, "payload"));
            Assert.AreEqual(0, doc.Sections.Count);
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary>
        /// A session carrying a value in every field that is supposed to survive a save, with values
        /// chosen to be exactly representable so a round-trip failure means a lost field rather than
        /// floating-point noise.
        /// </summary>
        private SessionService NewPopulatedSession()
        {
            var service = new SessionService(new IslandClock(0d), _log, null);
            service.BeginNewRun(Slot, "act1", SceneKeys.ZoneRibcage, WorldSeed);

            service.AdvanceTick(5.25d);
            service.AddPlaytime(1234.5d);
            service.SetRecordedPercent(37);
            service.SetPlayerPose(new Vec3(1.5f, 2.25f, -3.75f), 91.5f);
            service.SetEquippedTool("tool-instance-7");

            // Move the generator off its starting position so the round trip has to carry a real state
            // word rather than one that happens to match a freshly derived stream.
            for (var i = 0; i < 7; i++)
            {
                service.Random.NextUInt64();
            }

            return service;
        }

        /// <summary>
        /// A store rooted at a fresh scratch directory, remembered so <c>TearDown</c> can remove it.
        /// </summary>
        /// <remarks>
        /// The explicit-directory constructor is the reason these tests can exist at all: the default one
        /// resolves <c>Application.persistentDataPath</c>, and a suite that wrote there would be
        /// interfering with the editor's own saves and with every other run of itself.
        /// </remarks>
        private SaveFileStore NewTempStore()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "vardholm-saveroundtrip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return new SaveFileStore(_tempRoot, _log);
        }

        /// <summary>
        /// A complete, writable document for <see cref="Slot"/>, identifiable by its act id and percent so
        /// a recovery test can say WHICH of two saves it got back.
        /// </summary>
        private static SaveDocument NewSlotDocument(string actId, int recordedPercent)
        {
            var doc = NewEnvelope();
            doc.Metadata.Slot = Slot;
            doc.Metadata.ActId = actId;
            doc.Metadata.ZoneId = SceneKeys.ZoneRibcage;
            doc.Metadata.ZoneDisplayKey = SceneKeys.ZoneDisplayKey(SceneKeys.ZoneRibcage);
            doc.Metadata.RecordedPercent = recordedPercent;
            doc.PutSection("session", "{\"actId\":\"" + actId + "\"}");
            return doc;
        }

        private static SaveDocument NewEnvelope()
        {
            var doc = new SaveDocument();
            doc.BuildVersion = BuildVersion;
            doc.SavedAtIso = SavedAtIso;
            doc.Metadata.BuildVersion = BuildVersion;
            doc.Metadata.SavedAtIso = SavedAtIso;
            return doc;
        }
    }
}
