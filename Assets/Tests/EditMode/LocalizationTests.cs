using System.Collections.Generic;
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
    }
}
