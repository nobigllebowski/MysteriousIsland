using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Fire;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Game.Fire;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// A place a fire can be laid. Examine it for what the wind does here; hold an item and use it.
    /// </summary>
    /// <remarks>
    /// The world half of the dry-fire problem. The rules are the site's state in Core; this
    /// class only asks the fire service, marks the record when it lights, and keeps three
    /// children in step with the state: "Kit" (something laid), "Panel" (the windbreak up) and
    /// "Flame" (burning, with its light). Named by convention because zone content is built at
    /// runtime and has no Inspector.
    /// </remarks>
    public sealed class FireSite : Interactable
    {
        private FireService _fire;
        private string _siteId;
        private string _nameKey;

        /// <summary>Configures the site. Called by zone building.</summary>
        public void Configure(FireService fire, string siteId, string nameKey)
        {
            _fire = fire;
            _siteId = siteId;
            _nameKey = nameKey;
            SyncVisuals();
        }

        /// <inheritdoc />
        public override string ContentId => _siteId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => "interact.examine";

        private FireSiteState State => _fire != null ? _fire.Site(_siteId) : null;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            // The kit can change while the player is elsewhere (carried to the lee by a hint);
            // the per-frame scan is the cheapest place to catch up.
            SyncVisuals();
            return true;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            var state = State;
            return state != null && state.IsLit
                ? new InspectCommand(ContentId, ContentIds.RemarkFireHolds)
                : new InspectCommand(ContentId);
        }

        /// <inheritdoc />
        public override UseOutcome Use(string itemId, IInteractionServices services)
        {
            if (_fire == null)
            {
                return UseOutcome.Nothing;
            }

            var hasMultitool = services != null && services.HasItem(ItemIds.Multitool);
            var act = _fire.Apply(_siteId, itemId, hasMultitool);
            if (act == FireAct.Nothing)
            {
                return UseOutcome.Nothing;
            }

            if (act == FireAct.Lit && services != null)
            {
                services.MarkSolved(ContentIds.MechanismFire);
            }

            SyncVisuals();

            var key = FireRules.NarrationKey(act);
            if (FireRules.Consumes(act))
            {
                return UseOutcome.Spent(key);
            }

            return FireRules.IsRefusal(act) ? UseOutcome.Refused(key) : UseOutcome.Worked(key);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            // A place. It stays.
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            SyncVisuals();
        }

        private void SyncVisuals()
        {
            var state = State;
            Show("Kit", state != null && state.HasKit && !state.IsLit);
            Show("Panel", state != null && state.PanelPlaced);
            Show("Flame", state != null && state.IsLit);
        }

        private void Show(string childName, bool visible)
        {
            var child = transform.Find(childName);
            if (child != null && child.gameObject.activeSelf != visible)
            {
                child.gameObject.SetActive(visible);
            }
        }
    }
}
