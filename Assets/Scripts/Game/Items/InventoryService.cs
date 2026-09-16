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

        /// <summary>The item held up for use, or null. Transient: never saved.</summary>
        public string Held => _held;

        private string _held;

        /// <summary>Holds an item up for use on the next target. Must be carried.</summary>
        /// <returns>False when it is not carried.</returns>
        public bool Hold(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_inventory.Has(itemId))
            {
                return false;
            }

            if (_held == itemId)
            {
                return true;
            }

            _held = itemId;
            Publish(InventoryChangeKind.Held, itemId);
            return true;
        }

        /// <summary>Puts down whatever is held. Safe when nothing is.</summary>
        public void Release()
        {
            if (_held == null)
            {
                return;
            }

            _held = null;
            Publish(InventoryChangeKind.Held, string.Empty);
        }

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

            _taken.Add(itemId);
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

            if (_held == itemId)
            {
                _held = null;
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

            // A tool is not an ingredient: the multitool teases the rope and is still a multitool.
            if (!ItemIds.IsTool(first))
            {
                Consume(first);
            }

            if (!ItemIds.IsTool(second))
            {
                Consume(second);
            }

            Take(result);
            return true;
        }

        /// <summary>
        /// True when the item has ever been in the player's hands this run, carried now or not.
        /// </summary>
        /// <remarks>
        /// The world rebuilds its pickups with the zone (persistence by rebuild). Before this
        /// the only fact a pickup could ask was "carried?", so a spindle consumed by a
        /// combination grew back on the shore at the next zone entry, and a rope teased into
        /// fibre would have too. "Respawning nothing" (§5:00) needs the taking remembered.
        /// </remarks>
        public bool HasEverTaken(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && _taken.Contains(itemId);
        }

        private readonly HashSet<string> _taken = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>Empties the inventory for a new run.</summary>
        public void ResetForNewRun()
        {
            _held = null;
            _inventory.Clear();
            _taken.Clear();
            Publish(InventoryChangeKind.Replaced, string.Empty);
        }

        // Between the carried list and the ever-taken list. A save from before the second list
        // existed has no record separator and reads as "nothing ever taken", which only means
        // a consumed pickup may grow back once on that older run.
        private const char ListSeparator = (char)30;

        /// <inheritdoc />
        public void Capture(SaveDocument doc)
        {
            if (doc == null)
            {
                return;
            }

            var taken = new List<string>(_taken);
            taken.Sort(System.StringComparer.Ordinal);
            doc.PutSection(SectionId, Join(_inventory.Items) + ListSeparator + Join(taken));
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

            _held = null;
            _taken.Clear();
            var lists = payload.Split(ListSeparator);
            _inventory.RestoreFrom(string.IsNullOrEmpty(lists[0]) ? new string[0] : lists[0].Split(Separator));
            if (lists.Length > 1 && !string.IsNullOrEmpty(lists[1]))
            {
                var taken = lists[1].Split(Separator);
                for (var t = 0; t < taken.Length; t++)
                {
                    _taken.Add(taken[t]);
                }
            }

            for (var c = 0; c < _inventory.Items.Count; c++)
            {
                _taken.Add(_inventory.Items[c]);
            }

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
