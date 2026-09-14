using ForgottenIsle.Core.Commands;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A carved stone or cut surface the player can read. Records itself, never disappears.
    /// </summary>
    /// <remarks>
    /// The quiet half of the interaction pair. A marker is re-readable forever: it is the game's
    /// environmental storytelling channel, and a story beat you can only hear once is one a player
    /// who walked away mid-sentence has lost permanently.
    /// </remarks>
    public sealed class AncientMarker : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private string _lineKey;

        /// <summary>
        /// Configures the marker. Called by zone building; there is no Inspector pass.
        /// </summary>
        /// <param name="contentId">A <c>ContentIds</c> marker id.</param>
        /// <param name="nameKey">Localization key naming it in the prompt.</param>
        /// <param name="lineKey">Localization key of the line shown when read.</param>
        public void Configure(string contentId, string nameKey, string lineKey)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _lineKey = lineKey;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => "interact.inspect";

        /// <summary>The line this marker shows. Read by the handler that narrates it.</summary>
        public string LineKey => _lineKey ?? string.Empty;

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new InspectCommand(ContentId);
        }
    }
}
