using System.Collections.Generic;

namespace ForgottenIsle.Core.Progress
{
    /// <summary>One line in the notebook: a title, a body, and whether it is settled.</summary>
    public readonly struct SlateLine
    {
        /// <summary>Localization key of the heading, e.g. <c>slate.observed.discovery.brass_tag</c>.</summary>
        public readonly string TitleKey;

        /// <summary>Localization key of the body: the same key with <c>.body</c> appended.</summary>
        public readonly string BodyKey;

        /// <summary>For UNRESOLVED: true once the question has an answer. Always false in the prologue.</summary>
        public readonly bool Resolved;

        /// <param name="titleKey">Heading key.</param>
        /// <param name="resolved">Whether the question is settled.</param>
        public SlateLine(string titleKey, bool resolved)
        {
            TitleKey = titleKey;
            BodyKey = titleKey + ".body";
            Resolved = resolved;
        }
    }

    /// <summary>What the notebook needs to know that progression does not carry.</summary>
    /// <remarks>
    /// The radio's facts live in a Game-side service; they are handed in as plain booleans so the
    /// derivation stays engine-free and testable with a struct literal.
    /// </remarks>
    public readonly struct SlateFacts
    {
        public readonly bool RadioFound;
        public readonly bool RadioPowerFixed;
        public readonly bool RadioWorking;
        public readonly bool HeardHull;
        public readonly bool HeardBulletin;
        public readonly bool HeardTheVoice;
        public readonly bool UsedRecorderCells;

        public SlateFacts(
            bool radioFound, bool radioPowerFixed, bool radioWorking,
            bool heardHull, bool heardBulletin, bool heardTheVoice, bool usedRecorderCells)
        {
            RadioFound = radioFound;
            RadioPowerFixed = radioPowerFixed;
            RadioWorking = radioWorking;
            HeardHull = heardHull;
            HeardBulletin = heardBulletin;
            HeardTheVoice = heardTheVoice;
            UsedRecorderCells = usedRecorderCells;
        }
    }

    /// <summary>The three tabs of the Field Slate, derived from the record.</summary>
    public sealed class SlateContents
    {
        public readonly IReadOnlyList<SlateLine> Observed;
        public readonly IReadOnlyList<SlateLine> People;
        public readonly IReadOnlyList<SlateLine> Unresolved;

        public SlateContents(IReadOnlyList<SlateLine> observed, IReadOnlyList<SlateLine> people, IReadOnlyList<SlateLine> unresolved)
        {
            Observed = observed;
            People = people;
            Unresolved = unresolved;
        }

        /// <summary>The number on the UNRESOLVED tab. The entire quest system.</summary>
        public int OpenQuestions
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Unresolved.Count; i++)
                {
                    if (!Unresolved[i].Resolved)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// Nadia's notebook, computed from what has been recorded. Never stored.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="Objectives"/> (ADR-0015): the Slate is a VIEW of progression and
    /// the radio, not a second record that could disagree with them. An entry appears the moment
    /// its fact is true and can never be lost, because the facts it is derived from are in the
    /// save. "The record is never lost in this game" is a property of this function, not a promise.
    /// <para>
    /// The UNRESOLVED titles are the design's (<c>design/04-first-30-minutes.md</c> §3:30, §12:00,
    /// §26:10). SIX HULLS, ONE LINE is not derived yet: the hull-sightline beat it comes from is
    /// not built, and tying it to another observation would put a question in the notebook that
    /// the player never asked. WHO KEEPS THE MAINS ALIVE is this slice's own, from the tape deck.
    /// </para>
    /// </remarks>
    public static class Slate
    {
        // Observed entries, in the order a player meets them on the slice's route.
        private static readonly string[] ObservedOrder =
        {
            ContentIds.MarkerRibStone,
            ContentIds.DiscoveryBrassTag,
            ContentIds.MarkerAqueductCut,
            ContentIds.DiscoveryWaterloggedReel,
            ContentIds.MechanismSluice,
            ContentIds.MechanismTapeDeck
        };

        /// <summary>
        /// Whether a content id has been recorded in the sense its kind means.
        /// </summary>
        /// <remarks>
        /// A mechanism counts when it is SOLVED, not when it is looked at: examining a seized
        /// sluice empty-handed is an InspectCommand and lands in the inspected set, and the
        /// notebook entry for the sluice is written in the past tense of having opened it.
        /// </remarks>
        public static bool IsRecorded(WorldProgress progress, string id)
        {
            if (progress == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (id.StartsWith("mechanism.", System.StringComparison.Ordinal))
            {
                return progress.HasSolved(id);
            }

            if (id.StartsWith("discovery.", System.StringComparison.Ordinal))
            {
                return progress.HasCollected(id);
            }

            return progress.HasInspected(id);
        }

        public static SlateContents Build(WorldProgress progress, SlateFacts facts)
        {
            var observed = new List<SlateLine>(12);
            var people = new List<SlateLine>(3);
            var unresolved = new List<SlateLine>(4);

            if (progress != null)
            {
                for (var i = 0; i < ObservedOrder.Length; i++)
                {
                    var id = ObservedOrder[i];
                    if (IsRecorded(progress, id))
                    {
                        observed.Add(new SlateLine("slate.observed." + id, false));
                    }
                }
            }

            if (facts.RadioFound)
            {
                observed.Add(new SlateLine("slate.observed.radio.found", false));
            }

            if (facts.RadioWorking)
            {
                observed.Add(new SlateLine(
                    facts.UsedRecorderCells ? "slate.observed.radio.working_recorder" : "slate.observed.radio.working", false));
            }

            if (facts.HeardHull)
            {
                observed.Add(new SlateLine("slate.observed.radio.hull_thump", false));
            }

            if (facts.HeardBulletin)
            {
                observed.Add(new SlateLine("slate.observed.radio.bulletin", false));
            }

            if (facts.HeardTheVoice)
            {
                // Text only when the recorder's cells went into the set: a permanent, visible scar
                // in the evidence, which is what the choice was for.
                observed.Add(new SlateLine(
                    facts.UsedRecorderCells ? "slate.observed.radio.the_voice_transcript" : "slate.observed.radio.the_voice", false));
            }

            var haveTag = progress != null && progress.HasCollected(ContentIds.DiscoveryBrassTag);

            // PEOPLE. The SOMEONE silhouette redraws into THE VOICE; they are not two people.
            if (facts.HeardTheVoice)
            {
                people.Add(new SlateLine("slate.people.the_voice", false));
            }
            else if (haveTag || facts.RadioFound)
            {
                people.Add(new SlateLine("slate.people.someone", false));
            }

            if (facts.RadioPowerFixed)
            {
                people.Add(new SlateLine("slate.people.tr", false));
            }

            // UNRESOLVED. Nothing here is resolved in the prologue; the flag exists for Act 2.
            if (haveTag)
            {
                unresolved.Add(new SlateLine("slate.unresolved.who_rewicks_a_lantern", false));
            }

            if (progress != null && progress.HasSolved(ContentIds.MechanismTapeDeck))
            {
                unresolved.Add(new SlateLine("slate.unresolved.who_keeps_the_mains_alive", false));
            }

            if (facts.HeardTheVoice)
            {
                unresolved.Add(new SlateLine("slate.unresolved.what_are_the_gates", false));
                unresolved.Add(new SlateLine("slate.unresolved.why_wont_she_answer", false));
            }

            return new SlateContents(observed, people, unresolved);
        }
    }
}
