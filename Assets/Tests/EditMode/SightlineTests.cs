using ForgottenIsle.Core.Progress;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE TIER. Looking down a line: the game's foundational observation verb, as arithmetic.</summary>
    [TestFixture]
    public sealed class SightlineTests
    {
        [TestCase(0f, 0f, 4f, true)]
        [TestCase(3.9f, 0f, 4f, true)]
        [TestCase(4.1f, 0f, 4f, false)]
        [TestCase(356.5f, 0f, 4f, true, TestName = "Wraps below zero")]
        [TestCase(180f, 0f, 4f, false, TestName = "Looking back down the line does not count")]
        [TestCase(92f, 90f, 4f, true)]
        [TestCase(359f, 2f, 4f, true, TestName = "Wraps across 360")]
        public void Alignment_IsWithinToleranceOfTheAxis_EitherWayRoundTheCompass(
            float heading, float axis, float tolerance, bool expected)
        {
            Assert.That(SightlineMath.IsAligned(heading, axis, tolerance), Is.EqualTo(expected));
        }

        [Test]
        public void YawOf_FollowsTheRigsConvention_ZeroAlongZ_NinetyAlongX()
        {
            Assert.That(SightlineMath.YawOf(0f, 1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(SightlineMath.YawOf(1f, 0f), Is.EqualTo(90f).Within(0.001f));
            Assert.That(SightlineMath.YawOf(0f, -1f), Is.EqualTo(180f).Within(0.001f));
            Assert.That(SightlineMath.YawOf(-1f, 0f), Is.EqualTo(270f).Within(0.001f));
        }

        [Test]
        public void Difference_IsSignedAndSmallest()
        {
            Assert.That(SightlineMath.Difference(10f, 350f), Is.EqualTo(20f).Within(0.001f));
            Assert.That(SightlineMath.Difference(350f, 10f), Is.EqualTo(-20f).Within(0.001f));
        }

        [Test]
        public void TheLine_IsTheNotebooksFirstEntry_AndItsFirstQuestion()
        {
            var progress = new WorldProgress();
            progress.Inspect(ContentIds.MarkerHullLine);

            var contents = Slate.Build(progress, new SlateFacts(false, false, false, false, false, false, false));

            Assert.That(contents.Observed[0].TitleKey, Is.EqualTo("slate.observed." + ContentIds.MarkerHullLine));
            Assert.That(contents.Unresolved[0].TitleKey, Is.EqualTo("slate.unresolved.six_hulls_one_line"));
            Assert.That(contents.OpenQuestions, Is.EqualTo(1), "UNRESOLVED has a 1 on it.");
        }
    }
}
