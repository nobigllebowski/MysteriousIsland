using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// Something built that does not currently work, and one specific object that makes it work.
    /// </summary>
    /// <remarks>
    /// THIS IS THE SHAPE OF EVERY PUZZLE IN THIS GAME. The mystery is a maintenance problem, so a
    /// puzzle is a machine somebody built, left, and stopped servicing — and solving it is doing the
    /// job they stopped doing. Not a lock with a key hidden behind a riddle: a mechanism with a part
    /// missing, where the part is somewhere a person would have left it.
    /// <para>
    /// A mechanism therefore tells the player three different things depending on what they are
    /// holding. Empty-handed it describes what is wrong with it, which is the clue. Holding the
    /// wrong object it says why that object does not fit. Holding the right one it works, once, and
    /// stays working — because a machine that un-fixes itself is a machine nobody maintained, and
    /// that is the opposite of this island's whole premise.
    /// </para>
    /// </remarks>
    public sealed class Mechanism : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private string _idleLineKey;
        private string _solvedLineKey;
        private string _requiredItemId;
        private bool _consumesItem;
        private bool _solved;

        /// <summary>
        /// Configures the mechanism. Called by zone building; there is no Inspector pass.
        /// </summary>
        /// <param name="contentId">A <c>ContentIds</c> id.</param>
        /// <param name="nameKey">Localization key naming it in the prompt.</param>
        /// <param name="idleLineKey">Line shown when examined without the part it needs.</param>
        /// <param name="solvedLineKey">Line shown the moment it is made to work.</param>
        /// <param name="requiredItemId">The one item that fits.</param>
        /// <param name="consumesItem">Whether the part stays in the machine.</param>
        public void Configure(
            string contentId,
            string nameKey,
            string idleLineKey,
            string solvedLineKey,
            string requiredItemId,
            bool consumesItem)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _idleLineKey = idleLineKey;
            _solvedLineKey = solvedLineKey;
            _requiredItemId = requiredItemId;
            _consumesItem = consumesItem;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <summary>True once the mechanism has been made to work.</summary>
        public bool IsSolved => _solved;

        /// <summary>
        /// Shows what pressing the button would actually do.
        /// </summary>
        /// <remarks>
        /// Three verbs rather than one, because a prompt that always says the same thing teaches the
        /// player nothing. USE appears only when they are carrying the part that fits, which turns
        /// the prompt itself into the confirmation that they solved it — before they press.
        /// </remarks>
        public override string PromptKey
        {
            get
            {
                if (_solved)
                {
                    return "interact.examine";
                }

                return _hasPart ? "interact.use" : "interact.examine";
            }
        }

        private bool _hasPart;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            // Latched here so PromptKey stays a pure property. The prompt is read every frame the
            // player is in range; it must not go asking the inventory each time.
            _hasPart = !_solved
                       && services != null
                       && !string.IsNullOrEmpty(_requiredItemId)
                       && services.HasItem(_requiredItemId);

            return true;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            // With the part in hand this is a use; without it, examining is all the player can do,
            // and the idle line is the clue that tells them what to look for.
            if (_hasPart)
            {
                return new UseItemCommand(_requiredItemId, ContentId);
            }

            return new InspectCommand(ContentId);
        }

        /// <inheritdoc />
        public override UseOutcome Use(string itemId, IInteractionServices services)
        {
            if (_solved)
            {
                return UseOutcome.Refused(_solvedLineKey);
            }

            if (string.IsNullOrEmpty(_requiredItemId) || itemId != _requiredItemId)
            {
                return UseOutcome.Nothing;
            }

            _solved = true;
            OnSolved();

            return _consumesItem
                ? UseOutcome.Spent(_solvedLineKey)
                : UseOutcome.Worked(_solvedLineKey);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            // Nothing. A mechanism stays in the world whether it worked or not -- it is a fixture,
            // not a pickup, and the player will want to come back and look at what they repaired.
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            // A solved mechanism is not restored here, and that is deliberate rather than an
            // oversight: what it unlocks is recorded in progression, and progression is what
            // survives a save. The machine itself going back to its broken pose on re-entry is
            // correct -- the door it opened stays open.
        }

        private void OnSolved()
        {
            // The visible half of the repair: the part of the mechanism marked as the moving one
            // swings clear. Named by convention rather than wired in an Inspector, because zone
            // content is built at runtime and has no Inspector to wire.
            var moving = transform.Find("Moving");
            if (moving != null)
            {
                moving.localRotation = Quaternion.Euler(0f, 0f, -72f);
            }
        }
    }
}
