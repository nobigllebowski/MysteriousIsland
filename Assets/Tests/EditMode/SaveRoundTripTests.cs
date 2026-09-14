using System;
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

        private FakeCoreLog _log;
        private SaveCodec _codec;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeCoreLog();
            _codec = new SaveCodec();
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
