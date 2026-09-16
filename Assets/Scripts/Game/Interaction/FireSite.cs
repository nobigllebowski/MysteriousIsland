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
        private string _examineRemarkId;
        private Transform _kit;
        private Transform _panel;
        private Transform _flame;

        /// <summary>Configures the site. Called by zone building, after its children exist.</summary>
        /// <param name="fire">The fire service.</param>
        /// <param name="siteId">A <c>ContentIds</c> fire site id.</param>
        /// <param name="nameKey">Localization key naming the site.</param>
        /// <param name="examineRemarkId">The remark said when the site is examined unlit.</param>
        public void Configure(FireService fire, string siteId, string nameKey, string examineRemarkId)
        {
            _fire = fire;
            _siteId = siteId;
            _nameKey = nameKey;
            _examineRemarkId = examineRemarkId;

            // Looked up once: the scan calls CanInteract every frame for every site, and three
            // name walks per site per frame is a cost with no reader.
            _kit = transform.Find("Kit");
            _panel = transform.Find("Panel");
            _flame = transform.Find("Flame");
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
            // A remark, not an inspection: a site id in the inspected set would be a lie in the
            // save, the same lie a rock id would be. What the site says is its own line.
            var state = State;
            return new RemarkCommand(state != null && state.IsLit ? ContentIds.RemarkFireHolds : _examineRemarkId);
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
            Show(_kit, state != null && state.HasKit && !state.IsLit);
            Show(_panel, state != null && state.PanelPlaced);
            Show(_flame, state != null && state.IsLit);
        }

        private static void Show(Transform child, bool visible)
        {
            if (child != null && child.gameObject.activeSelf != visible)
            {
                child.gameObject.SetActive(visible);
            }
        }
    }
}
