using System.Collections.Generic;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;

namespace ForgottenIsle.Game.Items
{
    /// <summary>
    /// Owns what the player is carrying, and is the fourth <see cref="ISaveParticipant"/>.
    /// </summary>
    /// <remarks>
    /// Registered in the same phase that introduces it, per ADR-0011. An inventory that is not in
    /// the save is worse than no inventory: the player solves a puzzle, quits, and comes back to a
    /// run where the thing they earned is gone and the door it opens is still shut.
    /// </remarks>
    public sealed class InventoryService : ISaveParticipant
    {
        /// <summary>On-disk section id, owned by <see cref="SaveSections"/> like every other one.</summary>
        public const string SectionId = SaveSections.Inventory;

        /// <summary>
        /// Separator between ids in the saved payload. ASCII unit separator: never valid inside an
        /// id, so no escaping is needed and no id can forge a delimiter.
        /// </summary>
        private const char Separator = '';

        private readonly Inventory _inventory = new Inventory();
        private readonly SignalBus _signals;
        private readonly ICoreLog _log;

        /// <param name="signals">Bus the inventory signal is published on. Null tolerated.</param>
        /// <param name="log">Diagnostics sink. Null tolerated.</param>
        public InventoryService(SignalBus signals, ICoreLog log)
        {
            _signals = signals;
            _log = log;
        }

        /// <inheritdoc />
        public string ParticipantId => SectionId;

        /// <summary>The carried items.</summary>
        public Inventory Inventory => _inventory;

        /// <summary>True when the player is carrying <paramref name="itemId"/>.</summary>
        public bool Has(string itemId)
        {
            return _inventory.Has(itemId);
        }

        /// <summary>Adds an item and announces it.</summary>
        /// <returns>False when nothing changed.</returns>
        public bool Take(string itemId)
        {
            if (!_inventory.Add(itemId))
            {
                return false;
            }

            Publish(InventoryChangeKind.Added, itemId);
            return true;
        }

        /// <summary>Removes an item and announces it.</summary>
        /// <returns>False when it was not carried.</returns>
        public bool Consume(string itemId)
        {
            if (!_inventory.Remove(itemId))
            {
                return false;
            }

            Publish(InventoryChangeKind.Removed, itemId);
            return true;
        }

        /// <summary>
        /// Puts two carried items together, replacing both with what they make.
        /// </summary>
        /// <remarks>
        /// Both inputs are consumed only after the recipe is known to match, so a failed attempt
        /// costs the player nothing. That matters more than it sounds: in a game with no way to get
        /// an item back, a combination that eats its inputs on a wrong guess is a soft lock.
        /// </remarks>
        /// <param name="first">One carried item.</param>
        /// <param name="second">The other carried item.</param>
        /// <param name="result">What they became, when they made something.</param>
        /// <returns>True when a combination happened.</returns>
        public bool Combine(string first, string second, out string result)
        {
            result = null;

            if (!_inventory.Has(first) || !_inventory.Has(second))
            {
                return false;
            }

            if (!Combinations.TryCombine(first, second, out result))
            {
                return false;
            }

            Consume(first);
            Consume(second);
            Take(result);
            return true;
        }

        /// <summary>Empties the inventory for a new run.</summary>
        public void ResetForNewRun()
        {
            _inventory.Clear();
            Publish(InventoryChangeKind.Replaced, string.Empty);
        }

        /// <inheritdoc />
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            doc.PutSection(SectionId, Join(_inventory.Items));
        }

        /// <inheritdoc />
        public void Restore(SaveDocument doc)
        {
            string payload;
            if (doc == null || !doc.TryGetSection(SectionId, out payload) || string.IsNullOrEmpty(payload))
            {
                // A save written before the inventory existed. An older run, not corruption.
                _inventory.Clear();
                Publish(InventoryChangeKind.Replaced, string.Empty);
                return;
            }

            _inventory.RestoreFrom(payload.Split(Separator));
            Publish(InventoryChangeKind.Replaced, string.Empty);
        }

        private static string Join(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(ids.Count * 24);
            for (var i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(Separator);
                }

                builder.Append(ids[i]);
            }

            return builder.ToString();
        }

        private void Publish(InventoryChangeKind kind, string itemId)
        {
            if (_signals == null)
            {
                return;
            }

            // A copy, not the live list. The panel on the other end of this signal is UI code that
            // is not allowed to hold a reference into a service, and a list handed out once would
            // keep changing under it -- the same leak, just slower to notice.
            var snapshot = new string[_inventory.Items.Count];
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = _inventory.Items[i];
            }

            _signals.Publish(new InventoryChangedSignal(kind, itemId, snapshot));
        }
    }
}
