using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// Something in the world the player can walk up to and act on.
    /// </summary>
    /// <remarks>
    /// A MonoBehaviour base class rather than an interface, because every interactable is a thing
    /// standing in a zone with a position, and the position is most of what the system needs. An
    /// <c>IInteractable</c> would have to re-declare a Transform accessor on every implementation
    /// to buy nothing.
    /// <para>
    /// Subclasses answer three questions: what do I look like in the prompt, may I be used right
    /// now, and what happens when I am. Everything else — proximity, arbitration, the prompt, the
    /// input — belongs to <see cref="InteractionSystem"/>, so a new interactable is one small file.
    /// </para>
    /// </remarks>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>Default reach, in metres. Generous, because this is a touchscreen.</summary>
        public const float DefaultRange = 2.6f;

        [SerializeField, Tooltip("Metres the player must be within for this to be offered.")]
        private float _range = DefaultRange;

        /// <summary>Stable content id. Empty for interactables that record nothing.</summary>
        public abstract string ContentId { get; }

        /// <summary>Localization key naming this object in the prompt.</summary>
        public abstract string NameKey { get; }

        /// <summary>Localization key of the verb — INSPECT, TAKE, TRAVEL.</summary>
        public abstract string PromptKey { get; }

        /// <summary>How close the player must be.</summary>
        public float Range => _range;

        /// <summary>Where the prompt measures distance from.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// Whether this can be used right now.
        /// </summary>
        /// <remarks>
        /// Checked every frame by the proximity scan, so it must stay cheap — a set lookup or a
        /// bool, never a search. A false result hides the object from the prompt entirely rather
        /// than offering a verb that will be refused, because an offered action that does nothing
        /// is worse than no offer.
        /// </remarks>
        /// <param name="services">Read-only access to progression.</param>
        public virtual bool CanInteract(IInteractionServices services)
        {
            return true;
        }

        /// <summary>
        /// Builds the command this interaction dispatches.
        /// </summary>
        /// <remarks>
        /// Returns a command rather than performing the work, so that every world interaction goes
        /// through the same validate/execute path as a menu button and mutates state in exactly one
        /// place. This is ADR-0002's "UI never mutates state" applied to the world: nothing in a
        /// zone scene touches a service directly either.
        /// </remarks>
        /// <param name="services">Read-only access to progression.</param>
        /// <returns>The command to dispatch, or null when there is nothing to do.</returns>
        public abstract ICommand BuildCommand(IInteractionServices services);

        /// <summary>
        /// Called after the command succeeded, for purely visual consequences.
        /// </summary>
        /// <remarks>
        /// State has already changed by the time this runs. It exists so a pickup can remove its
        /// own mesh — a view concern — without the service that owns progression needing to know
        /// that a GameObject exists.
        /// </remarks>
        /// <param name="services">Read-only access to progression.</param>
        public virtual void OnInteracted(IInteractionServices services)
        {
        }

        /// <summary>
        /// Hides or removes this object because progress says it is already gone.
        /// </summary>
        /// <remarks>
        /// Called once when a zone is furnished, which is what makes a collected discovery stay
        /// collected across a save/load: the world is rebuilt from scratch on every zone entry, and
        /// this is the hook that re-applies what the player already did to it.
        /// </remarks>
        /// <param name="services">Read-only access to progression.</param>
        /// <summary>
        /// What this object does when the player applies a carried item to it.
        /// </summary>
        /// <remarks>
        /// Default is nothing, because most things in the world are not machines. Overriding this
        /// is how a mechanism declares what it wants without any central table knowing about it.
        /// </remarks>
        /// <param name="itemId">The carried item being applied.</param>
        /// <param name="services">Read-only view of progression and inventory.</param>
        /// <returns>What happened.</returns>
        public virtual UseOutcome Use(string itemId, IInteractionServices services)
        {
            return UseOutcome.Nothing;
        }

        public virtual void ApplyRestoredState(IInteractionServices services)
        {
        }
    }
}
