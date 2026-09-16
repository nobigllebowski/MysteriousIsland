using ForgottenIsle.Core.Commands;
using ForgottenIsle.Game.Fire;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A marker that can only be read by firelight.
    /// </summary>
    /// <remarks>
    /// The chalk on the trawler's plate (design §3.1, redundant source 2): <c>5240</c> and a tally
    /// of five-bar gates, half rained off, visible only when the light is low and from the side —
    /// which is what a fire in the hull's lee is. Until a fire burns it is not offered at all: not
    /// hidden as an object, just not a thing the prompt finds, the way a chalk mark in flat grey
    /// daylight is not a thing the eye finds. Recorded as any marker is.
    /// </remarks>
    public sealed class FirelitMark : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private FireService _fire;

        /// <summary>Configures the mark. Called by zone building.</summary>
        /// <param name="contentId">A <c>ContentIds</c> marker id.</param>
        /// <param name="nameKey">Localization key naming it.</param>
        /// <param name="fire">Whose light it needs. Null means never readable.</param>
        public void Configure(string contentId, string nameKey, FireService fire)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _fire = fire;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => "interact.inspect";

        /// <summary>True while there is light to read it by.</summary>
        public bool IsReadable => _fire != null && _fire.IsLit;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            return IsReadable;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new InspectCommand(ContentId);
        }
    }
}
