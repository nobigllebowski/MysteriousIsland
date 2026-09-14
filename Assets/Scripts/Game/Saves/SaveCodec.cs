using System;
using ForgottenIsle.Core.Data;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Save;

namespace ForgottenIsle.Game.Saves
{
    /// <summary>
    /// Turns a <see cref="SaveDocument"/> into the exact text that is written to disk, and turns that text
    /// back into a document — refusing, rather than repairing, anything whose CRC32 does not match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the codec lives in the Game layer while the document lives in Core: nothing here touches
    /// UnityEngine, but the choice of encoding is a shipping decision, not a domain rule. Core defines what
    /// a save <em>is</em>; this file defines what a save <em>looks like</em> on this platform, through
    /// Core's own JSON reader and writer rather than <c>JsonUtility</c> (ADR-0013).
    /// </para>
    /// <para>
    /// THE CHECKSUM PROTOCOL, and why it is two passes. The checksum field lives inside the very document
    /// it protects, so it cannot be computed over the final text — the act of storing it would change what
    /// was measured. Writing therefore serializes once with the field zeroed, CRC32s that, stores the
    /// result, and serializes again. Verification repeats the first half: parse, zero the field,
    /// re-serialize, CRC32, compare. This works only because <see cref="JsonWriter"/> is deterministic
    /// (ordinal member order, round-trippable numbers, one canonical escaping), which is precisely the
    /// property that file was written to guarantee.
    /// </para>
    /// <para>
    /// CONSEQUENCE worth stating plainly: verification reproduces the bytes from the parsed document, so it
    /// checks the document's <em>envelope shape</em> as well as its contents. A save carrying members this
    /// build does not know about will not re-serialize identically and is reported corrupt. That is the
    /// right answer for a truncated or hand-edited file, and the wrong answer for a save from a newer
    /// build — which is why the schema-version guard below runs BEFORE the checksum is ever compared. If
    /// the envelope itself ever gains a field, the writer must gain a versioned mode in the same change,
    /// or every existing save fails its own integrity check on the next launch.
    /// </para>
    /// <para>
    /// Nothing here throws. <see cref="JsonParser"/> signals a malformed document by exception; that
    /// exception is caught at this boundary and becomes <see cref="ResultCode.SaveCorrupt"/>, because the
    /// caller above is a loading screen and a loading screen has no useful response to a stack trace.
    /// </para>
    /// <para>
    /// WHY AN INSTANCE, given that it holds no state: so it is an injectable seam. The single most
    /// important behaviour in the save layer is the fallback from a damaged file to its <c>.bak</c>, and
    /// the only way to exercise that in a test is to make the codec produce or reject something it
    /// otherwise would not. A static class cannot be substituted; an instance costs one reference on the
    /// composition root's line and makes that test possible. The instance is stateless and therefore safe
    /// to share, which is why one is constructed at boot and handed to everything that needs it.
    /// </para>
    /// </remarks>
    public sealed class SaveCodec
    {
        /// <summary>
        /// Serializes <paramref name="document"/> with a freshly computed checksum.
        /// </summary>
        /// <remarks>
        /// Mutates <see cref="SaveDocument.Checksum"/> to the value that was written. That is deliberate:
        /// after a successful encode the in-memory document and the text on disk agree, so a caller that
        /// re-encodes the same document (an autosave following a manual save, say) produces identical
        /// bytes rather than silently disagreeing with what it just wrote.
        /// </remarks>
        /// <param name="document">The document. Null yields an empty JSON object, which will not verify.</param>
        /// <returns>The complete on-disk text. Never null.</returns>
        public string Encode(SaveDocument document)
        {
            if (document == null)
            {
                return JsonWriter.WriteSaveDocument(null);
            }

            document.Checksum = 0u;
            var zeroed = JsonWriter.WriteSaveDocument(document);
            document.Checksum = Crc32.Compute(zeroed);
            return JsonWriter.WriteSaveDocument(document);
        }

        /// <summary>
        /// Parses and verifies a save.
        /// </summary>
        /// <param name="text">Text exactly as read from disk.</param>
        /// <param name="document">
        /// The parsed document on success; null on every failure. Never a partially populated document —
        /// half a save is more dangerous than no save, because the game will happily run on it.
        /// </param>
        /// <returns>
        /// <see cref="ResultCode.Ok"/>; <see cref="ResultCode.SlotEmpty"/> for empty text;
        /// <see cref="ResultCode.SaveVersionTooNew"/> when the document was written by a later build;
        /// <see cref="ResultCode.SaveCorrupt"/> when it will not parse or fails its checksum.
        /// </returns>
        /// <remarks>
        /// Migration is NOT run here. The checksum describes the bytes on disk, so it must be verified
        /// against the document exactly as it was read; migrating first would rewrite the sections the
        /// checksum was computed over. <c>SaveSlotService</c> calls <see cref="SaveMigrator"/> on the
        /// document this method returns.
        /// </remarks>
        public ResultCode Decode(string text, out SaveDocument document)
        {
            document = null;

            if (string.IsNullOrEmpty(text))
            {
                return ResultCode.SlotEmpty;
            }

            JsonValue root;
            try
            {
                if (!JsonParser.TryParse(text, out root))
                {
                    return ResultCode.SaveCorrupt;
                }
            }
            catch (Exception)
            {
                // TryParse already swallows JsonParseException; this catch exists for the pathological
                // input (an enormous nesting depth) that surfaces as something else entirely. A save file
                // must never be able to take the process down.
                return ResultCode.SaveCorrupt;
            }

            if (root == null || !root.IsObject)
            {
                return ResultCode.SaveCorrupt;
            }

            var parsed = new SaveDocument
            {
                SchemaVersion = root["schemaVersion"].AsInt(0),
                BuildVersion = root["buildVersion"].AsString(string.Empty),
                SavedAtIso = root["savedAtIso"].AsString(string.Empty)
            };

            // Ordered before verification on purpose — see the class remarks. A newer build's save cannot
            // be re-serialized by this build, so its checksum would "fail" for a reason that has nothing to
            // do with integrity, and the player would be told their intact save was corrupt.
            if (parsed.SchemaVersion > SaveDocument.CurrentSchemaVersion)
            {
                return ResultCode.SaveVersionTooNew;
            }

            var storedChecksum = root["checksum"].AsUInt32(0u);
            parsed.Metadata = ReadMetadata(root["metadata"]);
            ReadSections(root["sections"], parsed);

            parsed.Checksum = 0u;
            var recomputed = Crc32.Compute(JsonWriter.WriteSaveDocument(parsed));
            if (recomputed != storedChecksum)
            {
                return ResultCode.SaveCorrupt;
            }

            parsed.Checksum = storedChecksum;
            document = parsed;
            return ResultCode.Ok;
        }

        /// <summary>
        /// Reads only the slot header out of save text, without materializing the body.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WHY this exists rather than calling <see cref="Decode"/> and taking
        /// <see cref="SaveDocument.Metadata"/>: drawing the main menu must not cost three full save parses
        /// on a cold start. This walks the raw text to the <c>metadata</c> member, parses that object
        /// alone, and never looks at the sections — which is also why <c>SaveFileStore</c> can hand it a
        /// truncated prefix of the file instead of the whole thing.
        /// </para>
        /// <para>
        /// THE HEADER IS NOT VERIFIED, and cannot be: the checksum covers the whole document, which this
        /// deliberately has not read. A header therefore means "the slot claims to contain this"; it is
        /// enough to draw a slot card, and it is not enough to start a game on. The full
        /// <see cref="Decode"/> runs when the player actually picks the slot, and that is where a corrupt
        /// body is caught.
        /// </para>
        /// </remarks>
        /// <param name="text">Save text, or a prefix of it that is long enough to contain the header.</param>
        /// <param name="metadata">The header on success; null otherwise.</param>
        /// <returns>
        /// <see cref="ResultCode.Ok"/>; <see cref="ResultCode.SlotEmpty"/> for empty text;
        /// <see cref="ResultCode.SaveCorrupt"/> when no complete <c>metadata</c> object can be found —
        /// which, for a prefix, may simply mean the prefix was too short.
        /// </returns>
        public ResultCode DecodeMetadata(string text, out SaveMetadata metadata)
        {
            metadata = null;

            if (string.IsNullOrEmpty(text))
            {
                return ResultCode.SlotEmpty;
            }

            int start;
            int length;
            if (!TryLocateTopLevelMember(text, "metadata", out start, out length))
            {
                return ResultCode.SaveCorrupt;
            }

            JsonValue node;
            try
            {
                if (!JsonParser.TryParse(text.Substring(start, length), out node))
                {
                    return ResultCode.SaveCorrupt;
                }
            }
            catch (Exception)
            {
                return ResultCode.SaveCorrupt;
            }

            if (node == null || !node.IsObject)
            {
                return ResultCode.SaveCorrupt;
            }

            metadata = ReadMetadata(node);
            return ResultCode.Ok;
        }

        /// <summary>
        /// Rebuilds a <see cref="SaveMetadata"/> from its JSON node, member by member.
        /// </summary>
        /// <remarks>
        /// Every read supplies an explicit fallback rather than relying on the field initialiser, so a
        /// header missing a member produces the same object as a header carrying that member's default.
        /// The two must be indistinguishable, because <see cref="Decode"/> re-serializes this object and
        /// compares the bytes: any asymmetry between reading and writing would show up as a checksum
        /// failure on a perfectly good save.
        /// </remarks>
        private static SaveMetadata ReadMetadata(JsonValue node)
        {
            var metadata = new SaveMetadata();
            if (node == null || !node.IsObject)
            {
                return metadata;
            }

            metadata.Slot = node["slot"].AsInt(0);
            metadata.ActId = node["actId"].AsString(string.Empty);
            metadata.ZoneId = node["zoneId"].AsString(string.Empty);
            metadata.ZoneDisplayKey = node["zoneDisplayKey"].AsString(string.Empty);
            metadata.PlaytimeSeconds = node["playtimeSeconds"].AsDouble(0d);
            metadata.RecordedPercent = node["recordedPercent"].AsInt(0);
            metadata.SavedAtIso = node["savedAtIso"].AsString(string.Empty);
            metadata.BuildVersion = node["buildVersion"].AsString(string.Empty);
            metadata.SchemaVersion = node["schemaVersion"].AsInt(SaveDocument.CurrentSchemaVersion);
            return metadata;
        }

        /// <summary>
        /// Copies every member of the <c>sections</c> object into the document, unread and uninterpreted.
        /// </summary>
        /// <remarks>
        /// Sections belonging to participants this build has never heard of are carried across verbatim.
        /// That is what lets a save survive a round trip through a build with a feature disabled instead of
        /// having that feature's progress quietly deleted.
        /// </remarks>
        private static void ReadSections(JsonValue node, SaveDocument document)
        {
            if (node == null || !node.IsObject)
            {
                return;
            }

            foreach (var member in node.Members)
            {
                document.PutSection(member.Key, member.Value.AsString(string.Empty));
            }
        }

        /// <summary>
        /// Finds the text span of a named member of the root object, without parsing anything.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A hand-rolled scan rather than a parse, because the whole point is not to build the object graph
        /// the parse would build. It tracks nesting depth and string boundaries so that a brace, a bracket
        /// or the word <c>metadata</c> occurring inside a section's payload cannot be mistaken for
        /// structure — section payloads are themselves serialized blobs, so that is not a hypothetical.
        /// </para>
        /// <para>
        /// Member names are compared in their raw on-disk form. Every name this project writes is a plain
        /// identifier with nothing to escape; a name that did contain an escape would simply fail to match
        /// and the caller would fall back to a full decode, which is the safe direction to fail in.
        /// </para>
        /// </remarks>
        /// <returns>True when the member was found and its value is complete within <paramref name="text"/>.</returns>
        private static bool TryLocateTopLevelMember(string text, string memberName, out int start, out int length)
        {
            start = 0;
            length = 0;

            var depth = 0;
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (c == '"')
                {
                    var nameStart = i + 1;
                    if (!TrySkipString(text, ref i))
                    {
                        return false;
                    }

                    // i now sits just past the closing quote.
                    var nameLength = i - 1 - nameStart;
                    if (depth == 1 && nameLength == memberName.Length
                        && string.CompareOrdinal(text, nameStart, memberName, 0, memberName.Length) == 0)
                    {
                        var colon = SkipWhitespace(text, i);
                        if (colon < text.Length && text[colon] == ':')
                        {
                            var valueStart = SkipWhitespace(text, colon + 1);
                            return TryMeasureValue(text, valueStart, out start, out length);
                        }
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth <= 0)
                    {
                        // The root value closed without the member appearing.
                        return false;
                    }
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Measures one complete JSON value starting at <paramref name="valueStart"/>, balancing braces and
        /// brackets and ignoring anything inside strings.
        /// </summary>
        /// <returns>False when the value runs past the end of the text, which is how a too-short prefix is detected.</returns>
        private static bool TryMeasureValue(string text, int valueStart, out int start, out int length)
        {
            start = valueStart;
            length = 0;

            if (valueStart >= text.Length)
            {
                return false;
            }

            var opener = text[valueStart];
            if (opener != '{' && opener != '[')
            {
                // A scalar: run to the first structural character that cannot be part of it.
                var scan = valueStart;
                if (opener == '"')
                {
                    if (!TrySkipString(text, ref scan))
                    {
                        return false;
                    }

                    length = scan - valueStart;
                    return true;
                }

                while (scan < text.Length && text[scan] != ',' && text[scan] != '}' && text[scan] != ']')
                {
                    scan++;
                }

                if (scan >= text.Length)
                {
                    return false;
                }

                length = scan - valueStart;
                return length > 0;
            }

            var depth = 0;
            var i = valueStart;
            while (i < text.Length)
            {
                var c = text[i];

                if (c == '"')
                {
                    if (!TrySkipString(text, ref i))
                    {
                        return false;
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        length = i - valueStart + 1;
                        return true;
                    }
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Advances <paramref name="i"/> from an opening quote to just past the matching closing quote,
        /// honouring backslash escapes so an escaped quote does not end the string early.
        /// </summary>
        /// <returns>False for an unterminated string — a truncated file, or a prefix cut mid-value.</returns>
        private static bool TrySkipString(string text, ref int i)
        {
            i++; // consume the opening quote
            while (i < text.Length)
            {
                var c = text[i++];
                if (c == '\\')
                {
                    if (i >= text.Length)
                    {
                        return false;
                    }

                    i++;
                    continue;
                }

                if (c == '"')
                {
                    return true;
                }
            }

            return false;
        }

        private static int SkipWhitespace(string text, int position)
        {
            while (position < text.Length)
            {
                var c = text[position];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    break;
                }

                position++;
            }

            return position;
        }
    }
}
