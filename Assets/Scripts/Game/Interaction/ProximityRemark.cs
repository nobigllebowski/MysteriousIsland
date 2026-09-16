using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A line said by walking near something. No prompt, no press; once per zone visit.
    /// </summary>
    /// <remarks>
    /// "Basalt. Basalt. That's not basalt." fires only if the player has walked within three
    /// metres of the chert (§7:10 (a)): an observation, not a directive. The same passive
    /// mechanism as the sightline, without the heading. Silent once the item it points at is
    /// carried, because the observation has been made.
    /// </remarks>
    public sealed class ProximityRemark : Interactable
    {
        private string _remarkId;
        private string _nameKey;
        private float _radius;
        private string _silentIfCarrying;
        private bool _said;
        private bool _refused;
        private bool _inside;

        /// <summary>Configures the remark. Called by zone building.</summary>
        /// <param name="remarkId">A <c>ContentIds</c> remark id.</param>
        /// <param name="nameKey">Localization key naming it; never shown, as there is no prompt.</param>
        /// <param name="radius">Metres within which it is said.</param>
        /// <param name="silentIfCarrying">An item id that, once carried, makes this silent. Null for none.</param>
        public void Configure(string remarkId, string nameKey, float radius, string silentIfCarrying)
        {
            _remarkId = remarkId;
            _nameKey = nameKey;
            _radius = radius;
            _silentIfCarrying = silentIfCarrying;
        }

        /// <inheritdoc />
        public override string ContentId => _remarkId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => string.Empty;

        /// <inheritdoc />
        public override bool IsPassive => true;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            return false;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new RemarkCommand(ContentId);
        }

        /// <inheritdoc />
        public override ICommand Observe(Vector3 playerPosition, float yawDegrees, IInteractionServices services)
        {
            var flat = playerPosition - transform.position;
            flat.y = 0f;
            var inside = flat.sqrMagnitude <= _radius * _radius;
            if (!inside)
            {
                _inside = false;
                _refused = false;
                return null;
            }

            if (_inside && _refused)
            {
                return null;
            }

            _inside = true;
            if (_said || (services != null && !string.IsNullOrEmpty(_silentIfCarrying) && services.HasItem(_silentIfCarrying)))
            {
                return null;
            }

            return new RemarkCommand(ContentId);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            _said = true;
        }

        /// <inheritdoc />
        public override void OnObservationRefused(Core.Primitives.ResultCode code)
        {
            _refused = true;
        }
    }
}
