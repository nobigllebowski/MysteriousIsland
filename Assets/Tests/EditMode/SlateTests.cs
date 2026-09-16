using ForgottenIsle.Core.Progress;
using NUnit.Framework;

namespace ForgottenIsle.Tests.EditMode
{
    /// <summary>CORE TIER. The notebook is a view of the record, and the number on UNRESOLVED is the quest system.</summary>
    [TestFixture]
    public sealed class SlateTests
    {
        private static SlateFacts NoRadio => new SlateFacts(false, false, false, false, false, false, false);

        [Test]
        public void ANewRun_HasAnEmptyNotebook()
        {
            var contents = Slate.Build(new WorldProgress(), NoRadio);

            Assert.That(contents.Observed.Count, Is.Zero);
            Assert.That(contents.People.Count, Is.Zero);
            Assert.That(contents.OpenQuestions, Is.Zero);
        }

        [Test]
        public void TheBrassTag_WritesTheEntry_ShowsSomeone_AndAsksTheFirstQuestion()
        {
            var progress = new WorldProgress();
            progress.Collect(ContentIds.DiscoveryBrassTag);

            var contents = Slate.Build(progress, NoRadio);

            Assert.That(contents.Observed[0].TitleKey, Is.EqualTo("slate.observed." + ContentIds.DiscoveryBrassTag));
            Assert.That(contents.Observed[0].BodyKey, Is.EqualTo("slate.observed." + ContentIds.DiscoveryBrassTag + ".body"));
            Assert.That(contents.People.Count, Is.EqualTo(1));
            Assert.That(contents.People[0].TitleKey, Is.EqualTo("slate.people.someone"));
            Assert.That(contents.OpenQuestions, Is.EqualTo(1), "UNRESOLVED ticks to 1.");
        }

        [Test]
        public void TheVoice_RedrawsSomeoneIntoTheVoice_AndTicksUnresolvedToThree()
        {
            var progress = new WorldProgress();
            progress.Collect(ContentIds.DiscoveryBrassTag);
            var facts = new SlateFacts(true, true, true, false, false, true, false);

            var contents = Slate.Build(progress, facts);

            Assert.That(contents.People.Count, Is.EqualTo(2), "THE VOICE and T.R.; SOMEONE is gone, not doubled.");
            Assert.That(contents.People[0].TitleKey, Is.EqualTo("slate.people.the_voice"));
            Assert.That(contents.People[1].TitleKey, Is.EqualTo("slate.people.tr"));
            Assert.That(contents.OpenQuestions, Is.EqualTo(3), "Lantern, gates, why won't she answer.");
        }

        [Test]
        public void HowTheSetCameToWork_IsFourBodies()
        {
            var progress = new WorldProgress();
            Assert.That(HasTitle(Slate.Build(progress, new SlateFacts(true, true, true, false, false, false, false, false)), "slate.observed.radio.working"), Is.True);
            Assert.That(HasTitle(Slate.Build(progress, new SlateFacts(true, true, true, false, false, false, true, false)), "slate.observed.radio.working_recorder"), Is.True);
            Assert.That(HasTitle(Slate.Build(progress, new SlateFacts(true, true, true, false, false, false, false, true)), "slate.observed.radio.working_cord"), Is.True);
            Assert.That(HasTitle(Slate.Build(progress, new SlateFacts(true, true, true, false, false, false, true, true)), "slate.observed.radio.working_recorder_cord"), Is.True);
        }

        [Test]
        public void ASweptFloor_PutsSomeoneOnThePeopleTab_ButABootPrintDoesNot()
        {
            var progress = new WorldProgress();
            progress.Inspect(ContentIds.MarkerBootPrint);
            Assert.That(Slate.Build(progress, NoRadio).People.Count, Is.Zero, "A print is not yet a person.");

            progress.Inspect(ContentIds.MarkerBroomArc);
            var contents = Slate.Build(progress, NoRadio);
            Assert.That(contents.People.Count, Is.EqualTo(1));
            Assert.That(contents.People[0].TitleKey, Is.EqualTo("slate.people.someone"));
            Assert.That(contents.Observed.Count, Is.EqualTo(2));
        }

        [Test]
        public void TheCutVine_IsTheLastEntry_AndTheFourthQuestion()
        {
            var progress = new WorldProgress();
            progress.Inspect(ContentIds.MarkerCutVine);

            var contents = Slate.Build(progress, NoRadio);

            Assert.That(contents.Observed[contents.Observed.Count - 1].TitleKey, Is.EqualTo("slate.observed." + ContentIds.MarkerCutVine));
            Assert.That(HasTitle(contents, "slate.unresolved.who_cut_the_vine"), Is.True);
            Assert.That(contents.People[0].TitleKey, Is.EqualTo("slate.people.someone"), "A blade means a hand.");
        }

        [Test]
        public void SpendingTheRecorder_LeavesAScarInTheEvidence()
        {
            var spent = Slate.Build(new WorldProgress(), new SlateFacts(true, true, true, false, false, true, true));
            var kept = Slate.Build(new WorldProgress(), new SlateFacts(true, true, true, false, false, true, false));

            Assert.That(HasTitle(spent, "slate.observed.radio.the_voice_transcript"), Is.True);
            Assert.That(HasTitle(kept, "slate.observed.radio.the_voice"), Is.True);
            Assert.That(HasTitle(kept, "slate.observed.radio.the_voice_transcript"), Is.False);
        }

        [Test]
        public void Observed_KeepsTheRoutesOrder_WhateverOrderThingsWereFoundIn()
        {
            var progress = new WorldProgress();
            progress.Solve(ContentIds.MechanismTapeDeck);
            progress.Inspect(ContentIds.MarkerRibStone);

            var contents = Slate.Build(progress, NoRadio);

            Assert.That(contents.Observed[0].TitleKey, Does.EndWith(ContentIds.MarkerRibStone));
            Assert.That(contents.Observed[1].TitleKey, Does.EndWith(ContentIds.MechanismTapeDeck));
            Assert.That(contents.OpenQuestions, Is.EqualTo(1), "The deck asks who keeps the mains alive.");
        }

        [Test]
        public void NothingInThePrologue_IsEverResolved()
        {
            var progress = new WorldProgress();
            progress.Collect(ContentIds.DiscoveryBrassTag);
            progress.Solve(ContentIds.MechanismTapeDeck);
            var contents = Slate.Build(progress, new SlateFacts(true, true, true, true, true, true, false));

            for (var i = 0; i < contents.Unresolved.Count; i++)
            {
                Assert.That(contents.Unresolved[i].Resolved, Is.False);
            }

            Assert.That(contents.OpenQuestions, Is.EqualTo(contents.Unresolved.Count));
        }

        [Test]
        public void LookingAtASeizedMechanism_WritesNothing_UntilItIsFixed()
        {
            // Examining empty-handed is an InspectCommand and lands in the inspected set; the
            // notebook entry is written in the past tense of having opened it, so it waits.
            var progress = new WorldProgress();
            progress.Inspect(ContentIds.MechanismSluice);

            Assert.That(Slate.Build(progress, NoRadio).Observed.Count, Is.Zero);
            Assert.That(Recorded.Percent(progress), Is.Zero, "And the record does not credit it either.");

            progress.Solve(ContentIds.MechanismSluice);
            Assert.That(Slate.Build(progress, NoRadio).Observed.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryRecordableId_HasAKind_AndAnUnknownIdRecordsAsNothing()
        {
            for (var i = 0; i < Recorded.Recordable.Length; i++)
            {
                Assert.That(ContentIds.KindOf(Recorded.Recordable[i]), Is.Not.EqualTo(ContentKind.Unknown), Recorded.Recordable[i]);
            }

            var progress = new WorldProgress();
            progress.Inspect("something.unlisted");
            Assert.That(Slate.IsRecorded(progress, "something.unlisted"), Is.False);
            Assert.That(ContentIds.KindOf(ContentIds.RadioSet), Is.EqualTo(ContentKind.Radio));
            Assert.That(ContentIds.KindOf(ContentIds.RemarkHullLinePartial), Is.EqualTo(ContentKind.Remark));
            Assert.That(System.Array.IndexOf(Recorded.Recordable, ContentIds.RemarkHullLinePartial), Is.EqualTo(-1),
                "A remark is said, never recorded.");
        }

        private static bool HasTitle(SlateContents contents, string key)
        {
            for (var i = 0; i < contents.Observed.Count; i++)
            {
                if (contents.Observed[i].TitleKey == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
