using System.Collections.Generic;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Time;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>
    /// An <see cref="ICoreLog"/> that remembers everything it was told instead of printing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY the suite needs this at all: most of Core's failure paths are deliberately silent to the
    /// caller — a rejected transition returns a code, a missing localization key returns a placeholder,
    /// a corrupt save section falls back to defaults. The <em>only</em> observable difference between
    /// "handled the problem correctly" and "never noticed the problem" is the diagnostic that was
    /// emitted. Asserting on the recorded codes is therefore not incidental bookkeeping; it is how
    /// several of these tests test anything at all.
    /// </para>
    /// <para>
    /// WHY <see cref="FailOnWarn"/> exists as a flag rather than as a separate strict double: many
    /// assertions are of the form "this path must be completely clean". Counting after the fact proves
    /// that too, but it reports the failure at the assertion rather than at the call that caused it,
    /// and by then the stack that produced the warning is gone. Failing inside <see cref="Warn(LogCode)"/>
    /// puts the test's failure stack exactly where the unexpected diagnostic came from.
    /// </para>
    /// <para>
    /// The real <see cref="ICoreLog"/> contract forbids throwing. This double breaks that contract on
    /// purpose and only when <see cref="FailOnWarn"/> is set by a test that wants the throw; with the
    /// flag clear it is as inert as the contract requires.
    /// </para>
    /// </remarks>
    public sealed class FakeCoreLog : ICoreLog
    {
        private readonly List<LogCode> _warnings = new List<LogCode>();
        private readonly List<string> _details = new List<string>();

        /// <summary>
        /// When true, any warning aborts the current test at the point of the call.
        /// The warning is still recorded first, so the transcript is complete either way.
        /// </summary>
        public bool FailOnWarn { get; set; }

        /// <summary>Every code passed to this log, in call order.</summary>
        public IReadOnlyList<LogCode> Warnings => _warnings;

        /// <summary>
        /// Every detail string passed to this log, index-aligned with <see cref="Warnings"/>.
        /// The code-only overload records an empty string so the two lists never drift apart.
        /// </summary>
        public IReadOnlyList<string> Details => _details;

        /// <summary>Total warnings recorded, of any code.</summary>
        public int WarningCount => _warnings.Count;

        /// <inheritdoc />
        public void Warn(LogCode code)
        {
            Record(code, string.Empty);
        }

        /// <inheritdoc />
        public void Warn(LogCode code, string detail)
        {
            Record(code, detail ?? string.Empty);
        }

        /// <summary>How many times <paramref name="code"/> was reported, ignoring detail.</summary>
        public int CountOf(LogCode code)
        {
            var count = 0;
            for (var i = 0; i < _warnings.Count; i++)
            {
                if (_warnings[i] == code)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// How many times <paramref name="code"/> was reported with exactly <paramref name="detail"/>.
        /// This is the overload the "missing key is logged once and only once" assertion needs: the
        /// count must be per key, not per code, or a second distinct missing key would mask a duplicate.
        /// </summary>
        public int CountOf(LogCode code, string detail)
        {
            var wanted = detail ?? string.Empty;
            var count = 0;
            for (var i = 0; i < _warnings.Count; i++)
            {
                if (_warnings[i] == code && string.Equals(_details[i], wanted, System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>True when <paramref name="code"/> was reported at least once.</summary>
        public bool Contains(LogCode code)
        {
            return CountOf(code) > 0;
        }

        /// <summary>
        /// Drops everything recorded so far, leaving <see cref="FailOnWarn"/> alone.
        /// Lets a test tolerate the noise of arranging a fixture and then assert cleanliness over the act.
        /// </summary>
        public void Clear()
        {
            _warnings.Clear();
            _details.Clear();
        }

        private void Record(LogCode code, string detail)
        {
            _warnings.Add(code);
            _details.Add(detail);

            if (FailOnWarn)
            {
                Assert.Fail("Unexpected ICoreLog warning: " + code + " (detail: '" + detail + "')");
            }
        }
    }

    /// <summary>
    /// An <see cref="IClock"/> whose time only moves when a test moves it.
    /// </summary>
    /// <remarks>
    /// WHY a settable clock rather than a real <c>IslandClock</c> in tests that merely observe time:
    /// <c>IslandClock.Advance</c> refuses non-positive input and clamps, which is correct behaviour and
    /// exactly wrong for a fixture that wants to pin time at an arbitrary value — including a value a
    /// real clock would never produce — to prove that a collaborator read the clock at all. Assigning
    /// <see cref="SimHours"/> directly makes "what time did this code see" a controlled input instead of
    /// something the test has to steer a real clock into.
    /// </remarks>
    public sealed class FakeClock : IClock
    {
        /// <param name="simHours">Initial reading. Any value, including ones a real clock would clamp.</param>
        public FakeClock(double simHours = 0d)
        {
            SimHours = simHours;
        }

        /// <inheritdoc cref="IClock.SimHours" />
        /// <remarks>Freely settable, and never advances on its own.</remarks>
        public double SimHours { get; set; }
    }
}
