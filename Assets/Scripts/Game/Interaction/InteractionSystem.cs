using System.Collections.Generic;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// Read-only view of game state that an interactable is allowed to consult.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. An interactable standing in a zone scene can ask "have I been taken
    /// already" and nothing else — it cannot reach the session, the save slots or the state
    /// machine. That is the same type-level containment the UI gets through <c>IUiContext</c>.
    /// </remarks>
    public interface IInteractionServices
    {
        /// <summary>True when this marker has been read.</summary>
        /// <param name="markerId">A content id.</param>
        bool HasInspected(string markerId);

        /// <summary>True when this discovery has been taken.</summary>
        /// <param name="discoveryId">A content id.</param>
        bool HasCollected(string discoveryId);

        /// <summary>True when this zone may be travelled to.</summary>
        /// <param name="zoneId">A scene key.</param>
        bool IsZoneUnlocked(string zoneId);

        /// <summary>True when the player is carrying this item.</summary>
        /// <remarks>
        /// An interactable needs this to answer for itself: a sluice with no key in the player's
        /// hands should say so in its prompt rather than accept the press and refuse afterwards.
        /// </remarks>
        /// <param name="itemId">An <c>ItemIds</c> id.</param>
        bool HasItem(string itemId);
    }

    /// <summary>
    /// Finds what the player is standing next to, offers it, and dispatches it when asked.
    /// </summary>
    /// <remarks>
    /// Driven from <c>PlayerRig.LateUpdate</c> rather than owning an <c>Update</c> of its own. The
    /// project's rule is one central per-frame loop, and the rig already runs after movement with
    /// the player's final position in hand — which is exactly what a proximity scan wants.
    /// <para>
    /// The candidate list is a registry, not a physics query. Zones hold a handful of interactables,
    /// so a linear scan over a registered list is both faster than OverlapSphere and free of the
    /// allocation and layer-mask setup a physics query drags in.
    /// </para>
    /// </remarks>
    public sealed class InteractionSystem : IInteractionServices, IUseTargetResolver
    {
        private readonly List<Interactable> _registered = new List<Interactable>(16);
        private readonly ProgressService _progress;
        private readonly CommandDispatcher _commands;
        private readonly SignalBus _signals;
        private readonly ICoreLog _log;

        private Interactable _current;
        private readonly InventoryService _inventory;
        private bool _suppressed;

        /// <param name="progress">Progression state, consulted by interactables and by unlock rules.</param>
        /// <param name="commands">Dispatcher every interaction routes through.</param>
        /// <param name="signals">Bus the prompt signal is published on. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public InteractionSystem(
            ProgressService progress,
            CommandDispatcher commands,
            SignalBus signals,
            ICoreLog log)
            : this(progress, null, commands, signals, log)
        {
        }

        /// <param name="progress">Progression state, consulted by interactables and by unlock rules.</param>
        /// <param name="inventory">What the player carries. Null tolerated; nothing is then held.</param>
        /// <param name="commands">Dispatcher every interaction routes through.</param>
        /// <param name="signals">Bus the prompt signal is published on. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public InteractionSystem(
            ProgressService progress,
            InventoryService inventory,
            CommandDispatcher commands,
            SignalBus signals,
            ICoreLog log)
        {
            _progress = progress;
            _inventory = inventory;
            _commands = commands;
            _signals = signals;
            _log = log;
        }

        /// <inheritdoc />
        public bool HasItem(string itemId)
        {
            return _inventory != null && _inventory.Has(itemId);
        }

        /// <summary>
        /// Asks the registered interactable with this id what it does with the item.
        /// </summary>
        /// <remarks>
        /// The registry is already the authority on what is in the zone, so it is also the natural
        /// place to answer "what is this thing, and does it want this object". No separate table of
        /// item-to-target pairings exists, and none should: the target decides.
        /// </remarks>
        public UseOutcome Use(string itemId, string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return UseOutcome.Nothing;
            }

            for (var i = 0; i < _registered.Count; i++)
            {
                var candidate = _registered[i];
                if (candidate != null && candidate.ContentId == targetId)
                {
                    return candidate.Use(itemId, this);
                }
            }

            return UseOutcome.Nothing;
        }

        /// <summary>What the player would act on if they pressed the button now. Null when nothing.</summary>
        public Interactable Current => _current;

        /// <summary>How many interactables the current zone registered. Shown on the dev overlay.</summary>
        public int RegisteredCount => _registered.Count;

        /// <inheritdoc />
        public bool HasInspected(string markerId)
        {
            return _progress != null && _progress.HasInspected(markerId);
        }

        /// <inheritdoc />
        public bool HasCollected(string discoveryId)
        {
            return _progress != null && _progress.HasCollected(discoveryId);
        }

        /// <inheritdoc />
        public bool IsZoneUnlocked(string zoneId)
        {
            return _progress != null && _progress.IsZoneUnlocked(zoneId);
        }

        /// <summary>Adds an interactable to the scan. Called by zone furnishing.</summary>
        /// <param name="interactable">The object to offer. Null and duplicates are ignored.</param>
        public void Register(Interactable interactable)
        {
            if (interactable == null || _registered.Contains(interactable))
            {
                return;
            }

            _registered.Add(interactable);
            interactable.ApplyRestoredState(this);
        }

        /// <summary>Removes an interactable.</summary>
        /// <param name="interactable">The object to stop offering.</param>
        public void Unregister(Interactable interactable)
        {
            if (interactable == null)
            {
                return;
            }

            _registered.Remove(interactable);
            if (_current == interactable)
            {
                SetCurrent(null);
            }
        }

        /// <summary>
        /// Drops every registration. Called when a zone unloads.
        /// </summary>
        /// <remarks>
        /// Without this the list would hold destroyed components from the previous zone, and the
        /// scan would compare against Unity's fake-null every frame forever.
        /// </remarks>
        public void Clear()
        {
            _registered.Clear();
            SetCurrent(null);
        }

        /// <summary>Hides the prompt and refuses activation, without losing registrations.</summary>
        /// <param name="suppressed">True while paused, loading, or in a menu.</param>
        public void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed)
            {
                return;
            }

            _suppressed = suppressed;
            if (_suppressed)
            {
                SetCurrent(null);
            }
        }

        /// <summary>
        /// Re-picks the best candidate for the player's position.
        /// </summary>
        /// <param name="playerPosition">Where the player is standing, this frame.</param>
        public void Tick(Vector3 playerPosition)
        {
            if (_suppressed)
            {
                return;
            }

            Interactable best = null;
            var bestDistanceSq = float.MaxValue;

            for (var i = _registered.Count - 1; i >= 0; i--)
            {
                var candidate = _registered[i];
                if (candidate == null)
                {
                    // Destroyed with its zone without unregistering. Drop it rather than testing a
                    // dead reference on every future frame.
                    _registered.RemoveAt(i);
                    continue;
                }

                if (!candidate.isActiveAndEnabled || !candidate.CanInteract(this))
                {
                    continue;
                }

                var offset = candidate.Position - playerPosition;
                var distanceSq = offset.sqrMagnitude;
                var range = candidate.Range;
                if (distanceSq > range * range || distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                best = candidate;
            }

            SetCurrent(best);
        }

        /// <summary>
        /// Acts on whatever is currently offered. Safe to call when nothing is.
        /// </summary>
        /// <returns>True when a command was dispatched and succeeded.</returns>
        public bool Activate()
        {
            if (_suppressed || _current == null || _commands == null)
            {
                return false;
            }

            var target = _current;
            var command = target.BuildCommand(this);
            if (command == null)
            {
                return false;
            }

            // Dispatch is typed on the concrete command, so the interactable's ICommand has to be
            // routed by its runtime type. This is the one place in the project that needs it, and
            // the alternative -- a non-generic dispatch path -- would weaken the typed registry
            // everything else relies on.
            var result = Dispatch(command);
            if (!result.Success)
            {
                if (_log != null)
                {
                    _log.Warn(LogCode.UnknownCommand, "interaction refused: " + result.Code);
                }

                return false;
            }

            target.OnInteracted(this);

            // The world may have changed under the prompt -- a taken discovery is gone, a gate now
            // leads somewhere. Re-pick next frame rather than leaving a stale offer on screen.
            SetCurrent(null);
            return true;
        }

        private CommandResult Dispatch(ICommand command)
        {
            if (command is InspectCommand inspect)
            {
                return _commands.Dispatch(inspect);
            }

            if (command is CollectCommand collect)
            {
                return _commands.Dispatch(collect);
            }

            if (command is TravelToZoneCommand travel)
            {
                return _commands.Dispatch(travel);
            }

            if (_log != null)
            {
                _log.Warn(LogCode.UnknownCommand, "interaction produced an unroutable command");
            }

            return CommandResult.Fail(ResultCode.NoHandler);
        }

        private void SetCurrent(Interactable next)
        {
            if (ReferenceEquals(_current, next))
            {
                return;
            }

            _current = next;

            if (_signals == null)
            {
                return;
            }

            _signals.Publish(next != null
                ? new InteractionTargetChangedSignal(next.NameKey, next.PromptKey, true)
                : new InteractionTargetChangedSignal(string.Empty, string.Empty, false));
        }
    }
}
