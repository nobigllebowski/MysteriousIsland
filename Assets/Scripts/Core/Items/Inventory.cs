using System.Collections.Generic;

namespace ForgottenIsle.Core.Items
{
    /// <summary>
    /// What the player is carrying. A set of item ids, with no counts and no ordering rules.
    /// </summary>
    /// <remarks>
    /// NO STACKS, NO WEIGHT, NO SLOT LIMIT, and those are design decisions rather than omissions.
    /// Every item in this game is a single specific object — <em>the</em> brass tag, <em>the</em>
    /// sluice key — so a count of two is not a state the fiction can produce. A capacity limit
    /// would add an inventory-management minigame to a game whose subject is noticing things.
    /// <para>
    /// Insertion order is preserved because the UI shows the list and a set that reshuffles itself
    /// between frames is unreadable. That is the only reason; nothing depends on the order.
    /// </para>
    /// </remarks>
    public sealed class Inventory
    {
        private readonly List<string> _items = new List<string>(16);
        private readonly HashSet<string> _lookup = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>Items in the order they were acquired.</summary>
        public IReadOnlyList<string> Items => _items;

        /// <summary>How many distinct items are carried.</summary>
        public int Count => _items.Count;

        /// <summary>True when the player is carrying <paramref name="itemId"/>.</summary>
        public bool Has(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && _lookup.Contains(itemId);
        }

        /// <summary>Adds an item.</summary>
        /// <returns>False when the id is empty or already carried, so nothing changed.</returns>
        public bool Add(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_lookup.Add(itemId))
            {
                return false;
            }

            _items.Add(itemId);
            return true;
        }

        /// <summary>Removes an item.</summary>
        /// <returns>False when it was not carried, so nothing changed.</returns>
        public bool Remove(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !_lookup.Remove(itemId))
            {
                return false;
            }

            _items.Remove(itemId);
            return true;
        }

        /// <summary>Empties the inventory for a new run.</summary>
        public void Clear()
        {
            _items.Clear();
            _lookup.Clear();
        }

        /// <summary>Replaces the contents from a loaded save.</summary>
        /// <param name="itemIds">Ids in acquisition order. Null is treated as empty.</param>
        public void RestoreFrom(IEnumerable<string> itemIds)
        {
            Clear();
            if (itemIds == null)
            {
                return;
            }

            foreach (var id in itemIds)
            {
                Add(id);
            }
        }
    }
}
