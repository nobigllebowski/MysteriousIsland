using ForgottenIsle.Core.Commands;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A unique object the player takes once. It leaves the world and stays gone across saves.
    /// </summary>
    /// <remarks>
    /// Persistence works by rebuild, not by bookkeeping. Zones are furnished from scratch on every
    /// entry, so the pickup is recreated each time and then immediately asks progression whether it
    /// was already taken — see <see cref="ApplyRestoredState"/>. That is why collecting survives a
    /// save/load without anything writing object state into the save file.
    /// </remarks>
    public sealed class DiscoveryPickup : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private string _lineKey;

        /// <summary>
        /// Configures the pickup. Called by zone building; there is no Inspector pass.
        /// </summary>
        /// <param name="contentId">A <c>ContentIds</c> discovery id.</param>
        /// <param name="nameKey">Localization key naming it in the prompt.</param>
        /// <param name="lineKey">Localization key of the line shown when taken.</param>
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
        public override string PromptKey => "interact.take";

        /// <summary>The line shown when this is taken.</summary>
        public string LineKey => _lineKey ?? string.Empty;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            return services == null || !services.HasCollected(ContentId);
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new CollectCommand(ContentId);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            // Deactivate rather than Destroy: the object is owned by the furnished root and will go
            // with the zone anyway, and leaving it alive means a future "put it back" needs no
            // respawn path.
            gameObject.SetActive(false);
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            if (services != null && services.HasCollected(ContentId))
            {
                gameObject.SetActive(false);
            }
        }
    }
}
