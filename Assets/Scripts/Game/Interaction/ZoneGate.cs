using ForgottenIsle.Core.Commands;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A passage to another zone. Offers TRAVEL when open, and says why when it is not.
    /// </summary>
    /// <remarks>
    /// A locked gate stays visible and stays interactable — it just offers a different verb and a
    /// line of text instead of a load. That is deliberate: a gate that vanishes while locked
    /// teaches the player nothing, and a gate that silently refuses reads as a bug. Seeing the way
    /// on, and being told what it wants, is the thing that makes the brass tag worth finding.
    /// </remarks>
    public sealed class ZoneGate : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private string _destinationZoneId;
        private string _lockedLineKey;

        /// <summary>
        /// Configures the gate. Called by zone building; there is no Inspector pass.
        /// </summary>
        /// <param name="contentId">A <c>ContentIds</c> gate id.</param>
        /// <param name="nameKey">Localization key naming the passage.</param>
        /// <param name="destinationZoneId">Scene key this leads to.</param>
        /// <param name="lockedLineKey">Localization key shown while it is locked.</param>
        public void Configure(string contentId, string nameKey, string destinationZoneId, string lockedLineKey)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _destinationZoneId = destinationZoneId;
            _lockedLineKey = lockedLineKey;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <summary>Scene key on the far side.</summary>
        public string DestinationZoneId => _destinationZoneId ?? string.Empty;

        /// <summary>The line shown when the player tries a locked gate.</summary>
        public string LockedLineKey => _lockedLineKey ?? string.Empty;

        /// <summary>True when progression allows travel through here.</summary>
        public bool IsOpen(IInteractionServices services)
        {
            return services == null || services.IsZoneUnlocked(DestinationZoneId);
        }

        /// <inheritdoc />
        public override string PromptKey => _lastKnownOpen ? "interact.travel" : "interact.examine";

        private bool _lastKnownOpen;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            // The verb shown depends on whether the gate is open, and PromptKey has no access to
            // services -- so the state is latched here, during the per-frame scan that already
            // asks this question. Cheap, and it keeps PromptKey a pure property.
            _lastKnownOpen = IsOpen(services);
            return true;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            if (!IsOpen(services))
            {
                // A locked gate still produces a real interaction: an Inspect against the gate's own
                // id, which narrates the locked line. Returning null here would make the button do
                // visibly nothing, which reads as broken rather than as locked.
                return new InspectCommand(ContentId);
            }

            return new TravelToZoneCommand(DestinationZoneId);
        }
    }
}
