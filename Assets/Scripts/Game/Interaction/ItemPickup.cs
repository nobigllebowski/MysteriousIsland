using ForgottenIsle.Core.Commands;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// An object the player picks up and carries. Gone from the world once taken.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DiscoveryPickup"/>, and the distinction is worth keeping. A
    /// discovery is a story beat: it is recorded, it advances the objective, and it is never used
    /// for anything. An item is a tool: it goes in the hands, it combines, it opens things, and the
    /// story does not care that you have it. Collapsing the two would mean every screwdriver
    /// advances the plot and every revelation clutters the inventory.
    /// <para>
    /// Persistence is by rebuild, like everything else in a zone: the pickup is recreated on entry
    /// and asks the inventory whether it is already carried, rather than anything writing object
    /// state into the save.
    /// </para>
    /// </remarks>
    public sealed class ItemPickup : Interactable
    {
        private string _itemId;
        private string _nameKey;

        /// <summary>
        /// Configures the pickup. Called by zone building; there is no Inspector pass.
        /// </summary>
        /// <param name="itemId">An <c>ItemIds</c> id.</param>
        /// <param name="nameKey">Localization key naming it in the prompt.</param>
        public void Configure(string itemId, string nameKey)
        {
            _itemId = itemId;
            _nameKey = nameKey;
        }

        /// <summary>
        /// The item id, which doubles as the content id.
        /// </summary>
        /// <remarks>
        /// One id rather than two. An item's identity in the world and in the hands is the same
        /// identity, and a second id would only create a pair that can drift apart.
        /// </remarks>
        public override string ContentId => _itemId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => "interact.take";

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            return services == null || !services.HasItem(ContentId);
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new TakeItemCommand(ContentId);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            gameObject.SetActive(false);
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            if (services != null && services.HasItem(ContentId))
            {
                gameObject.SetActive(false);
            }
        }
    }
}
