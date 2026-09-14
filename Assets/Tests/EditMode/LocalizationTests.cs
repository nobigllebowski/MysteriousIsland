using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ForgottenIsle.Core.Data;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="StringTableLocalization"/> resolution and the CSV dialect
    /// <see cref="CsvTableParser"/> has to survive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two behaviours here are load-bearing and easy to regress into something that looks tidier.
    /// First, a missing key must render as <c>#key#</c> and never as an empty string: the only bug
    /// report a missing translation ever produces is a screenshot, and a blank label is invisible in
    /// one. Second, that warning must be logged once per key and not once per lookup — a key that is
    /// missing is missing on every frame that draws it, and per-lookup logging turns one absent
    /// subtitle into thousands of lines a second.
    /// </para>
    /// <para>
    /// The CSV cases are drawn from what a translator's spreadsheet actually emits: a BOM, CRLF
    /// endings, blank rows, commas inside prose, and doubled quotes. Every one of them has silently
    /// eaten a string table in some project before.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationTests
    {
        private const string KnownKey = "ui.menu.continue";
        private const string KnownValue = "Continue";
        private const string MissingKey = "ui.menu.absent";

        private FakeCoreLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeCoreLog();
        }

        // ---------------------------------------------------------------- resolution

        [Test]
        public void Localization_KnownKey_ReturnsItsValue()
        {
            var loc = NewLocalization();

            Assert.AreEqual(KnownValue, loc.Get(new LocKey(KnownKey)));
            Assert.IsTrue(loc.Has(new LocKey(KnownKey)));
            Assert.AreEqual(0, _log.WarningCount, "Resolving a present key must be silent.");
        }

        /// <summary>
        /// The placeholder contract: visible, greppable, and never empty. The enclosed text is the
        /// literal key so it can be pasted straight into a search of en.csv.
        /// </summary>
        [Test]
        public void Localization_MissingKey_ReturnsHashWrappedKeyAndNeverEmpty()
        {
            var loc = NewLocalization();

            string result = null;
            Assert.DoesNotThrow(() => result = loc.Get(new LocKey(MissingKey)));

            Assert.IsNotNull(result);
            Assert.AreNotEqual(string.Empty, result);
            Assert.AreEqual("#" + MissingKey + "#", result);
            Assert.IsFalse(loc.Has(new LocKey(MissingKey)));
        }

        /// <summary>
        /// An empty key reaching a label is a caller bug. It announces itself as <c>##</c> rather than
        /// vanishing, which is the whole reason the placeholder wraps rather than substitutes.
        /// </summary>
        [Test]
        public void Localization_EmptyKey_ReturnsDoubleHashRatherThanVanishing()
        {
            var loc = NewLocalization();

            var result = loc.Get(LocKey.Empty);

            Assert.AreEqual("##", result);
            Assert.AreNotEqual(string.Empty, result);
        }

        /// <summary>
        /// The one-warning-per-key rule. Hammering the same absent key the way a per-frame label would
        /// must produce exactly one diagnostic.
        /// </summary>
        [Test]
        public void Localization_RepeatedMissingKeyLookups_WarnExactlyOnce()
        {
            var loc = NewLocalization();
            var key = new LocKey(MissingKey);

            for (var i = 0; i < 500; i++)
            {
                Assert.AreEqual("#" + MissingKey + "#", loc.Get(key));
            }

            Assert.AreEqual(1, _log.CountOf(LogCode.MissingLocKey, MissingKey), "The missing key was not reported exactly once.");
            Assert.AreEqual(1, _log.WarningCount, "Something other than the one missing-key warning was logged.");
        }

        /// <summary>
        /// De-duplication is per key, not global: a second distinct absent key still deserves its own
        /// warning, or the first missing string would hide every one after it.
        /// </summary>
        [Test]
        public void Localization_TwoDistinctMissingKeys_WarnOncePerKey()
        {
            var loc = NewLocalization();

            loc.Get(new LocKey("ui.absent.one"));
            loc.Get(new LocKey("ui.absent.one"));
            loc.Get(new LocKey("ui.absent.two"));
            loc.Get(new LocKey("ui.absent.two"));

            Assert.AreEqual(1, _log.CountOf(LogCode.MissingLocKey, "ui.absent.one"));
            Assert.AreEqual(1, _log.CountOf(LogCode.MissingLocKey, "ui.absent.two"));
            Assert.AreEqual(2, _log.CountOf(LogCode.MissingLocKey));
        }

        /// <summary>
        /// <see cref="ILocalizedText.Has"/> is the speculative probe UI uses before deciding whether to
        /// draw an optional line. It must not warn, or every optional label would report a bug.
        /// </summary>
        [Test]
        public void Localization_HasOnMissingKey_DoesNotWarn()
        {
            var loc = NewLocalization();

            Assert.IsFalse(loc.Has(new LocKey(MissingKey)));
            Assert.IsFalse(loc.Has(LocKey.Empty));
            Assert.AreEqual(0, _log.WarningCount);
        }

        [Test]
        public void Localization_NullLog_ResolvesMissingKeyWithoutThrowing()
        {
            var loc = new StringTableLocalization(null);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });

            string result = null;
            Assert.DoesNotThrow(() => result = loc.Get(new LocKey(MissingKey)));
            Assert.AreEqual("#" + MissingKey + "#", result);
        }

        // ---------------------------------------------------------------- locales

        /// <summary>
        /// A partially translated locale falls through to the authoring language rather than showing
        /// placeholders for the rows the translator has not reached yet.
        /// </summary>
        [Test]
        public void Localization_PartiallyTranslatedLocale_FallsBackToTheFallbackLocale()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue), Row("ui.menu.quit", "Quit") });
            loc.AddTable("fr", new[] { Row(KnownKey, "Continuer") });

            Assert.IsTrue(loc.SetLocale("fr"));
            Assert.AreEqual("fr", loc.CurrentLocale);

            Assert.AreEqual("Continuer", loc.Get(new LocKey(KnownKey)), "The translated row should win.");
            Assert.AreEqual("Quit", loc.Get(new LocKey("ui.menu.quit")), "The untranslated row should fall back to en.");
            Assert.AreEqual(0, _log.CountOf(LogCode.MissingLocKey), "A fallback hit is not a missing key.");
        }

        /// <summary>
        /// Absent in both the current locale and the fallback is genuinely missing — placeholder and
        /// one warning, not silence.
        /// </summary>
        [Test]
        public void Localization_KeyAbsentFromBothLocales_ReturnsPlaceholderAndWarns()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });
            loc.AddTable("fr", new[] { Row(KnownKey, "Continuer") });
            Assert.IsTrue(loc.SetLocale("fr"));

            Assert.AreEqual("#" + MissingKey + "#", loc.Get(new LocKey(MissingKey)));
            Assert.AreEqual(1, _log.CountOf(LogCode.MissingLocKey, MissingKey));
        }

        /// <summary>
        /// Switching to a language with no rows behind it would leave the player unable to find the
        /// menu that switches it back, so the request is refused and the locale is left alone.
        /// </summary>
        [Test]
        public void Localization_SetLocaleForUnknownLocale_IsRefusedAndLeavesCurrentLocale()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });
            loc.AddTable("fr", new[] { Row(KnownKey, "Continuer") });
            Assert.IsTrue(loc.SetLocale("fr"));

            Assert.IsFalse(loc.SetLocale("de"));
            Assert.AreEqual("fr", loc.CurrentLocale);
            Assert.AreEqual("Continuer", loc.Get(new LocKey(KnownKey)));
        }

        /// <summary>
        /// Locale tags arrive from the OS in whatever casing the platform prefers. A player must not
        /// lose their language to a capital letter.
        /// </summary>
        [Test]
        public void Localization_SetLocaleWithDifferentCasing_ResolvesTheSameTable()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });
            loc.AddTable("fr", new[] { Row(KnownKey, "Continuer") });

            Assert.IsTrue(loc.SetLocale("FR"));
            Assert.AreEqual("Continuer", loc.Get(new LocKey(KnownKey)));
            Assert.AreEqual(0, _log.WarningCount);
        }

        [Test]
        public void Localization_NewInstance_DefaultsToTheFallbackLocale()
        {
            var loc = new StringTableLocalization(_log);

            Assert.AreEqual(StringTableLocalization.DefaultFallbackLocale, loc.CurrentLocale);
        }

        /// <summary>
        /// Tables merge last-write-wins rather than replacing, which is what makes an override sheet
        /// loaded on top of the shipped table change only the rows it mentions.
        /// </summary>
        [Test]
        public void Localization_SecondTableForSameLocale_MergesAndOverridesPerRow()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue), Row("ui.menu.quit", "Quit") });
            loc.AddTable("en", new[] { Row(KnownKey, "Resume") });

            Assert.AreEqual("Resume", loc.Get(new LocKey(KnownKey)), "The override row did not win.");
            Assert.AreEqual("Quit", loc.Get(new LocKey("ui.menu.quit")), "A row the override did not mention was lost.");
        }

        [Test]
        public void Localization_FormattedGet_SubstitutesArguments()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row("ui.clock.display", "Day {0}, {1}:{2}") });

            Assert.AreEqual("Day 3, 19:30", loc.Get(new LocKey("ui.clock.display"), 3, 19, 30));
        }

        /// <summary>
        /// A translator who typed one placeholder too many must not crash a screen. The malformed
        /// pattern is shown instead: wrong, but legible, and it names the key.
        /// </summary>
        [Test]
        public void Localization_FormattedGetWithBadPattern_ReturnsThePatternInsteadOfThrowing()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row("ui.broken", "Day {0} of {1}") });

            string result = null;
            Assert.DoesNotThrow(() => result = loc.Get(new LocKey("ui.broken"), 3));

            Assert.AreEqual("Day {0} of {1}", result);
        }

        [Test]
        public void Localization_AvailableLocales_ListsEveryLoadedTable()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });
            loc.AddTable("fr", new[] { Row(KnownKey, "Continuer") });

            var locales = new List<string>(loc.AvailableLocales);

            Assert.AreEqual(2, locales.Count);
            CollectionAssert.Contains(locales, "en");
            CollectionAssert.Contains(locales, "fr");
        }

        // ---------------------------------------------------------------- CSV dialect

        [Test]
        public void CsvParser_LeadingByteOrderMark_IsNotAbsorbedIntoTheFirstKey()
        {
            var rows = CsvTableParser.Parse("\uFEFF" + KnownKey + "," + KnownValue + "\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(KnownKey, rows[0].Key, "The BOM became part of the first key.");
            Assert.AreEqual(KnownValue, rows[0].Value);
        }

        [Test]
        public void CsvParser_CrlfLineEndings_ProduceOneRowPerLine()
        {
            var rows = CsvTableParser.Parse("ui.a,Alpha\r\nui.b,Beta\r\nui.c,Gamma\r\n");

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("ui.a", rows[0].Key);
            Assert.AreEqual("Alpha", rows[0].Value);
            Assert.AreEqual("ui.b", rows[1].Key);
            Assert.AreEqual("Beta", rows[1].Value);
            Assert.AreEqual("ui.c", rows[2].Key);
            Assert.AreEqual("Gamma", rows[2].Value);
        }

        [Test]
        public void CsvParser_BlankLinesAndCommentLines_AreSkipped()
        {
            const string csv =
                "# Vardholm string table\r\n" +
                "\r\n" +
                "ui.a,Alpha\r\n" +
                "\r\n" +
                "    \r\n" +
                "   # an indented comment\r\n" +
                "ui.b,Beta\r\n";

            var rows = CsvTableParser.Parse(csv);

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("ui.a", rows[0].Key);
            Assert.AreEqual("ui.b", rows[1].Key);
        }

        /// <summary>
        /// A <c>#</c> is only a comment marker as the first non-whitespace character of a line. It is
        /// ordinary text everywhere else — not least because the missing-key placeholder itself is
        /// spelled <c>#key#</c>.
        /// </summary>
        [Test]
        public void CsvParser_HashInsideAValue_IsTextNotAComment()
        {
            var rows = CsvTableParser.Parse("ui.tag,Marker #7 of #9\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("Marker #7 of #9", rows[0].Value);
        }

        [Test]
        public void CsvParser_QuotedValueContainingCommas_KeepsThemInTheValue()
        {
            var rows = CsvTableParser.Parse("ui.list,\"one, two, three\"\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("ui.list", rows[0].Key);
            Assert.AreEqual("one, two, three", rows[0].Value);
        }

        [Test]
        public void CsvParser_DoubledQuotesInsideQuotedValue_BecomeOneLiteralQuote()
        {
            var rows = CsvTableParser.Parse("ui.quote,\"she said \"\"hello\"\"\"\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("she said \"hello\"", rows[0].Value);
        }

        /// <summary>
        /// Quoting is the only way an author can ask for a leading or trailing space, so a quoted
        /// field must not be trimmed the way a bare one is.
        /// </summary>
        [Test]
        public void CsvParser_QuotedValue_PreservesSurroundingWhitespace()
        {
            var rows = CsvTableParser.Parse("ui.pad,\"  spaced  \"\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("  spaced  ", rows[0].Value);
        }

        /// <summary>
        /// A bare value runs to the end of the line, commas included, so prose does not need quoting
        /// just because it contains a comma.
        /// </summary>
        [Test]
        public void CsvParser_BareValueWithCommas_RunsToTheEndOfTheLine()
        {
            var rows = CsvTableParser.Parse("ui.hint.tide,Wait for the tide, then cross\n");

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("ui.hint.tide", rows[0].Key);
            Assert.AreEqual("Wait for the tide, then cross", rows[0].Value);
        }

        [Test]
        public void CsvParser_QuotedValueWithEmbeddedNewline_KeepsTheNewline()
        {
            var rows = CsvTableParser.Parse("ui.multi,\"line one\nline two\"\nui.after,Tail\n");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("line one\nline two", rows[0].Value);
            Assert.AreEqual("ui.after", rows[1].Key);
            Assert.AreEqual("Tail", rows[1].Value);
        }

        /// <summary>
        /// One bad row costs one placeholder, not a black screen on boot. The rows around it still parse.
        /// </summary>
        [Test]
        public void CsvParser_RowWithNoSeparator_IsSkippedWithoutLosingTheRest()
        {
            var rows = CsvTableParser.Parse("ui.a,Alpha\nthis row has no comma\nui.b,Beta\n");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("ui.a", rows[0].Key);
            Assert.AreEqual("ui.b", rows[1].Key);
        }

        [Test]
        public void CsvParser_EmptyOrNullText_ReturnsAnEmptyListNeverNull()
        {
            Assert.IsNotNull(CsvTableParser.Parse(null));
            Assert.AreEqual(0, CsvTableParser.Parse(null).Count);
            Assert.AreEqual(0, CsvTableParser.Parse(string.Empty).Count);
            Assert.AreEqual(0, CsvTableParser.Parse("# only a comment\n\n").Count);
        }

        [Test]
        public void CsvParser_EmptyValue_YieldsAnEmptyStringRatherThanDroppingTheRow()
        {
            var rows = CsvTableParser.Parse("ui.blank,\nui.after,Tail\n");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("ui.blank", rows[0].Key);
            Assert.AreEqual(string.Empty, rows[0].Value);
        }

        /// <summary>
        /// Duplicates are preserved in file order and left for the consumer to resolve. That ordering
        /// is what makes an override table appended after the base table work, so it is contract.
        /// </summary>
        [Test]
        public void CsvParser_DuplicateKeys_ArePreservedInFileOrder()
        {
            var rows = CsvTableParser.Parse("ui.a,First\nui.a,Second\n");

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("First", rows[0].Value);
            Assert.AreEqual("Second", rows[1].Value);
        }

        /// <summary>
        /// The end-to-end shape the boot path actually uses: parsed CSV fed straight into the table.
        /// </summary>
        [Test]
        public void CsvParser_OutputFedToLocalization_ResolvesThroughTheStringTable()
        {
            const string csv =
                "\uFEFF# en.csv\r\n" +
                "\r\n" +
                "ui.menu.continue,Continue\r\n" +
                "ui.hint.tide,\"Wait for the tide, then cross\"\r\n";

            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", CsvTableParser.Parse(csv));

            Assert.AreEqual("Continue", loc.Get(new LocKey("ui.menu.continue")));
            Assert.AreEqual("Wait for the tide, then cross", loc.Get(new LocKey("ui.hint.tide")));
            Assert.AreEqual(0, _log.WarningCount);
        }

        // ------------------------------------------------------------------ CI gate

        /// <summary>
        /// The gate that bans hardcoded strings: every <c>new LocKey("...")</c> literal anywhere under
        /// Assets/Scripts must have a row in the shipped string table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WHY this exists as a test and not only as a code review habit: a key with no row does not
        /// throw, does not fail to compile and does not even log on the frame it is created. It renders
        /// as <c>#the.key#</c>, and only in the one screen state that draws it. A rename in a screen and
        /// a forgotten row in the table are the same edit from the compiler's point of view, so nothing
        /// but a scan of both sides catches the drift.
        /// </para>
        /// <para>
        /// WHY the Resources copy is the one parsed: Assets/Localization/en.csv is the authoring copy a
        /// translator edits, but Unity only serves <c>Resources.Load</c> from a folder literally named
        /// Resources, so Assets/Resources/Localization/en.csv is the only file a player build ever
        /// reads. A key present in the authoring copy and absent from the shipped one is still a
        /// <c>#key#</c> in the player's build. (ci/validate-structure.py separately asserts the two
        /// files are byte-identical.)
        /// </para>
        /// <para>
        /// WHY the literal scan is a regex and not a parse: the pattern is anchored on the constructor
        /// call, so a key built by concatenation -- <c>new LocKey("zone." + id + ".name")</c> -- is
        /// deliberately not matched. Those cannot be resolved without running the game, and the rows
        /// backing them (zone.*, ui.locale.*) are covered by the act/zone sections of the table
        /// instead. Everything this test does match is a literal that must exist today.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryKeyReferencedInCode_ExistsIn_en_csv()
        {
            var scriptsRoot = ScriptsRoot();
            Assert.IsTrue(Directory.Exists(scriptsRoot),
                "Expected shipped C# sources at " + scriptsRoot + ". If this path is wrong the whole " +
                "gate silently passes, so it is asserted rather than skipped.");

            var table = ShippedTableKeys();

            var sources = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories);
            System.Array.Sort(sources, System.StringComparer.Ordinal);
            Assert.Greater(sources.Length, 0, "No .cs files were found under " + scriptsRoot + ".");

            // Key -> the first "Assets/...cs:line" that references it, in key order so a failure
            // message is stable across runs and machines.
            var missing = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
            var referenced = 0;

            foreach (var source in sources)
            {
                var code = File.ReadAllText(source);
                foreach (Match match in LocKeyLiteral.Matches(code))
                {
                    var key = match.Groups["key"].Value;
                    if (key.Length == 0)
                    {
                        // new LocKey("") is a caller bug, but it is LocKey's own contract to render it
                        // as ## -- see Localization_EmptyKey_ReturnsDoubleHashRatherThanVanishing.
                        continue;
                    }

                    referenced++;
                    if (table.Contains(key) || missing.ContainsKey(key))
                    {
                        continue;
                    }

                    missing.Add(key, ProjectRelative(source) + ":" + LineOf(code, match.Index));
                }
            }

            Assert.Greater(referenced, 0,
                "Not one LocKey literal was found in " + sources.Length + " source files. The scan " +
                "pattern has stopped matching the code, which would let any missing key through.");

            if (missing.Count == 0)
            {
                return;
            }

            var message = new StringBuilder();
            message.Append(missing.Count);
            message.Append(" LocKey literal(s) under Assets/Scripts have no row in ");
            message.Append(ShippedTableRelativePath);
            message.Append(". Each renders in game as #the.key#. Add a row for every key below to ");
            message.Append("Assets/Localization/en.csv and copy it to the Resources mirror -- do not ");
            message.Append("hardcode the text in a screen:");
            foreach (var entry in missing)
            {
                message.Append("\n    ");
                message.Append(entry.Key);
                message.Append("   (first referenced at ");
                message.Append(entry.Value);
                message.Append(")");
            }

            Assert.Fail(message.ToString());
        }

        /// <summary>
        /// The mirror of the gate above: a row that exists but carries no value is a blank label, which
        /// is the one failure mode worse than <c>#key#</c> because it is invisible in a screenshot.
        /// </summary>
        /// <remarks>
        /// <see cref="CsvTableParser"/> only drops a row with an empty KEY, so <c>ui.menu.continue,</c>
        /// parses into a real entry whose value is <c>""</c>. <see cref="StringTableLocalization"/>
        /// rejects that at lookup and falls through to <c>#key#</c>, but it tests with
        /// <c>string.IsNullOrEmpty</c> -- a value of one space passes that check and reaches the label
        /// as blank text. A whitespace-only row therefore has no runtime guard at all, and this is the
        /// only place it is caught.
        /// </remarks>
        [Test]
        public void NoKeyIn_en_csv_HasAnEmptyValue()
        {
            var rows = ShippedTableRows();

            var blank = new List<string>();
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Value))
                {
                    continue;
                }

                blank.Add(row.Key);
            }

            if (blank.Count == 0)
            {
                return;
            }

            blank.Sort(System.StringComparer.Ordinal);

            var message = new StringBuilder();
            message.Append(blank.Count);
            message.Append(" row(s) in ");
            message.Append(ShippedTableRelativePath);
            message.Append(" have an empty or whitespace-only value, which draws as a blank label:");
            foreach (var key in blank)
            {
                message.Append("\n    ");
                message.Append(key);
            }

            Assert.Fail(message.ToString());
        }

        // ---------------------------------------------------------------- fixtures

        private StringTableLocalization NewLocalization()
        {
            var loc = new StringTableLocalization(_log);
            loc.AddTable("en", new[] { Row(KnownKey, KnownValue) });
            return loc;
        }

        private static KeyValuePair<string, string> Row(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        // ------------------------------------------------- fixtures for the CI gate

        /// <summary>Path of the shipped table, relative to the project root, for failure messages.</summary>
        private const string ShippedTableRelativePath = "Assets/Resources/Localization/en.csv";

        /// <summary>
        /// Matches a <c>new LocKey("literal")</c> construction. The key class excludes the backslash so
        /// an escaped sequence is skipped rather than half-read; house-style keys never contain one.
        /// </summary>
        private static readonly Regex LocKeyLiteral = new Regex(
            @"\bnew\s+LocKey\s*\(\s*""(?<key>[^""\\\r\n]*)""\s*\)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Assets/Scripts. <c>Application.dataPath</c> is the project's Assets folder in the Editor, and
        /// an EditMode test always runs in the Editor, so this needs no asset-database lookup.
        /// </summary>
        private static string ScriptsRoot()
        {
            return Path.Combine(UnityEngine.Application.dataPath, "Scripts");
        }

        /// <summary>Absolute path of the table a player build actually loads.</summary>
        private static string ShippedTablePath()
        {
            return Path.Combine(UnityEngine.Application.dataPath, "Resources", "Localization", "en.csv");
        }

        /// <summary>Parsed rows of the shipped table, asserting it exists and is not empty.</summary>
        private static IList<KeyValuePair<string, string>> ShippedTableRows()
        {
            var path = ShippedTablePath();
            Assert.IsTrue(File.Exists(path),
                "The shipped string table is missing from " + path + ". A player build loads it from " +
                "Resources, not from Assets/Localization.");

            var rows = CsvTableParser.Parse(File.ReadAllText(path));
            Assert.Greater(rows.Count, 0,
                "Parsed no rows from " + ShippedTableRelativePath + ". An empty parse would make every " +
                "key look missing, or make the blank-value check pass vacuously.");
            return rows;
        }

        /// <summary>Key set of the shipped table.</summary>
        private static HashSet<string> ShippedTableKeys()
        {
            var keys = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var row in ShippedTableRows())
            {
                keys.Add(row.Key);
            }

            return keys;
        }

        /// <summary>1-based line containing <paramref name="offset"/>.</summary>
        private static int LineOf(string text, int offset)
        {
            var line = 1;
            var limit = offset < text.Length ? offset : text.Length;
            for (var index = 0; index < limit; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        /// <summary>
        /// Rewrites an absolute source path as a project-relative one ("Assets/Scripts/..."), so a
        /// failure message is the same on CI as on a developer's machine and can be pasted into a grep.
        /// </summary>
        private static string ProjectRelative(string absolutePath)
        {
            var normalized = absolutePath.Replace('\\', '/');
            var marker = normalized.LastIndexOf("/Assets/", System.StringComparison.Ordinal);
            return marker < 0 ? normalized : normalized.Substring(marker + 1);
        }
    }
}
