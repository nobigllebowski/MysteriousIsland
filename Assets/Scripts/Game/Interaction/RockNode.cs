using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Game.Fire;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A rock on the strand line. Tap it with the multitool: basalt knocks, chert rings.
    /// </summary>
    /// <remarks>
    /// The whole clue of the spark problem is that one rock breaks differently, and the test
    /// is the sound (§7:10 (a)). Every node looks like a rock and examines as a rock; only the
    /// multitool tells them apart, and only a chert that has rung offers TAKE. Basalt is the
    /// negative half of the lesson and is never takeable.
    /// </remarks>
    public sealed class RockNode : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private bool _isChert;
        private bool _rang;
        private bool _canTake;
        private FireService _fire;

        /// <summary>Configures the node. Called by zone building.</summary>
        /// <param name="contentId">A unique rock id.</param>
        /// <param name="nameKey">Localization key naming it.</param>
        /// <param name="isChert">Whether it rings.</param>
        /// <param name="fire">Remembers which chert has rung, across zone rebuilds and saves. Null tolerated.</param>
        public void Configure(string contentId, string nameKey, bool isChert, FireService fire)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _isChert = isChert;
            _fire = fire;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <summary>True for the chert. Never shown; the sound is the tell.</summary>
        public bool IsChert => _isChert;

        /// <summary>True once the multitool has rung it.</summary>
        public bool HasRung => _rang;

        /// <inheritdoc />
        public override string PromptKey => _canTake ? "interact.take" : "interact.examine";

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            _canTake = _isChert && _rang && (services == null || !services.HasItem(ItemIds.ChertNodule));
            return true;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            // Examining a rock records nothing: it is a remark, not an inspection. Sixteen rock
            // ids in the inspected set would be sixteen lies in the save.
            return _canTake
                ? (ICommand)new TakeItemCommand(ItemIds.ChertNodule)
                : new RemarkCommand(ContentIds.RemarkRockLook);
        }

        /// <inheritdoc />
        public override UseOutcome Use(string itemId, IInteractionServices services)
        {
            if (itemId != ItemIds.Multitool)
            {
                return UseOutcome.Nothing;
            }

            if (!_isChert)
            {
                return UseOutcome.Worked("narration." + ContentIds.RemarkRockKnock);
            }

            _rang = true;
            if (_fire != null)
            {
                _fire.MarkRung(ContentId);
            }

            return UseOutcome.Worked("narration." + ContentIds.RemarkRockRing);
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            // A nodule that rang stays rung: the zone is rebuilt on every entry, and a chert that
            // forgot it had chipped would ask the player to prove it twice.
            _rang = _isChert && _fire != null && _fire.HasRung(ContentId);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            if (_canTake)
            {
                // Taken. The other nodules stay where they are; a nodule is not consumed by
                // striking, so one is all the run needs.
                gameObject.SetActive(false);
            }
        }
    }
}
