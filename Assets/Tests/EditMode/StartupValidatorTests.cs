using System.Collections.Generic;
using ForgottenIsle.Game.Diagnostics;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// UNITY EDITMODE TIER.
    /// Covers the startup validator's reporting contract — the part that can be exercised without a
    /// live scene. The scene-dependent checks belong to the PlayMode tier.
    /// </summary>
    /// <remarks>
    /// The validator is the project's substitute for "we ran it and it worked", so its own failure
    /// modes matter: a validator that reports PASS when handed nothing, or that throws instead of
    /// reporting, is worse than no validator at all.
    /// </remarks>
    [TestFixture]
    public sealed class StartupValidatorTests
    {
        [Test]
        public void Run_NullContext_ReportsFailureInsteadOfThrowing()
        {
            var lines = VardholmStartupValidator.Run(null);

            Assert.That(lines, Is.Not.Empty, "A null context must produce a report, not an empty list.");
            Assert.That(
                HasSeverity(lines, VardholmStartupValidator.Severity.Fail),
                Is.True,
                "A null context is the most severe startup failure there is; it must report FAIL.");
        }

        [Test]
        public void Run_NullContext_DoesNotThrow()
        {
            // The validator exists to diagnose a broken boot. One that throws during a broken boot
            // replaces the real error with its own, which is precisely the failure it is meant to
            // prevent.
            Assert.DoesNotThrow(() => VardholmStartupValidator.Run(null));
        }

        [Test]
        public void Format_EmptyReport_StillCarriesTheHeader()
        {
            var text = VardholmStartupValidator.Format(new List<VardholmStartupValidator.Line>());

            Assert.That(text, Does.Contain("VARDHOLM STARTUP CHECK"));
        }

        [Test]
        public void Format_GroupsLinesUnderSeverityHeadings()
        {
            var lines = new List<VardholmStartupValidator.Line>
            {
                new VardholmStartupValidator.Line(VardholmStartupValidator.Severity.Pass, "alpha"),
                new VardholmStartupValidator.Line(VardholmStartupValidator.Severity.Fail, "bravo"),
                new VardholmStartupValidator.Line(VardholmStartupValidator.Severity.Warn, "charlie")
            };

            var text = VardholmStartupValidator.Format(lines);

            Assert.That(text, Does.Contain("PASS:"));
            Assert.That(text, Does.Contain("WARN:"));
            Assert.That(text, Does.Contain("FAIL:"));
            Assert.That(text, Does.Contain("alpha"));
            Assert.That(text, Does.Contain("bravo"));
            Assert.That(text, Does.Contain("charlie"));

            // Order matters for readability: a developer scans for FAIL last because it is the part
            // they must act on, and burying it between passes is how a report gets ignored.
            Assert.That(text.IndexOf("PASS:"), Is.LessThan(text.IndexOf("WARN:")));
            Assert.That(text.IndexOf("WARN:"), Is.LessThan(text.IndexOf("FAIL:")));
        }

        [Test]
        public void Format_OmitsHeadingsForSeveritiesWithNoLines()
        {
            var lines = new List<VardholmStartupValidator.Line>
            {
                new VardholmStartupValidator.Line(VardholmStartupValidator.Severity.Pass, "only a pass")
            };

            var text = VardholmStartupValidator.Format(lines);

            Assert.That(text, Does.Contain("PASS:"));
            Assert.That(text, Does.Not.Contain("FAIL:"),
                "An all-clear report must not print an empty FAIL heading; it reads as a failure at a glance.");
        }

        private static bool HasSeverity(
            List<VardholmStartupValidator.Line> lines,
            VardholmStartupValidator.Severity severity)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
