using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Session;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE and GAME tiers. The recorded-percent figure, which said 0% for two phases.</summary>
    [TestFixture]
    public sealed class RecordedTests
    {
        [Test]
        public void Nothing_IsZero_AndEverything_IsAHundred()
        {
            var progress = new WorldProgress();
            Assert.That(Recorded.Percent(progress), Is.Zero);

            for (var i = 0; i < Recorded.Recordable.Length; i++)
            {
                var id = Recorded.Recordable[i];
                progress.Inspect(id);
                progress.Collect(id);
                progress.Solve(id);
            }

            Assert.That(Recorded.Percent(progress), Is.EqualTo(100));
        }

        [Test]
        public void FourOfFourteen_ReadsTwentyNine_NotTwentyEight()
        {
            var progress = new WorldProgress();
            progress.Inspect(ContentIds.MarkerRibStone);
            progress.Inspect(ContentIds.MarkerBootPrint);
            progress.Inspect(ContentIds.MarkerLegBand);
            progress.Inspect(ContentIds.MarkerCutVine);

            Assert.That(Recorded.Recordable.Length, Is.EqualTo(14), "The slice's recordable content.");
            Assert.That(Recorded.Percent(progress), Is.EqualTo(29), "Rounded, not truncated.");
        }

        [Test]
        public void EveryRecordableId_IsARealContentId()
        {
            for (var i = 0; i < Recorded.Recordable.Length; i++)
            {
                Assert.That(Recorded.Recordable[i], Does.StartWith("marker.").Or.StartWith("discovery.").Or.StartWith("mechanism."));
            }
        }

        [Test]
        public void TheKeeper_WritesTheFigureIntoTheSession_WhenProgressChanges()
        {
            var signals = new SignalBus();
            var session = new SessionService(new IslandClock(0d), null, signals);
            session.BeginNewRun(0, SessionService.DefaultActId, ContentIds.ZoneRibcage, 7);
            var progress = new ProgressService(signals, null);
            var keeper = new RecordKeeper(progress, session, signals);

            Assert.That(session.RecordedPercent, Is.Zero);

            progress.Collect(ContentIds.DiscoveryBrassTag);

            Assert.That(session.RecordedPercent, Is.EqualTo(17));
            keeper.Dispose();
        }
    }
}
