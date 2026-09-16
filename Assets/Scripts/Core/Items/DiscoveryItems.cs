using ForgottenIsle.Core.Progress;

namespace ForgottenIsle.Core.Items
{
    /// <summary>
    /// Discoveries that are also objects: recorded as a story beat AND put in the player's hands.
    /// </summary>
    /// <remarks>
    /// The waterlogged reel is the case. It is a discovery -- finding it advances the objective
    /// and is written to the record -- and it is a thing the player carries, combines with the
    /// spindle and threads into the deck. The first version made it only a discovery, and the
    /// entire combination chain was unreachable: nothing ever put the reel in the inventory.
    /// <para>
    /// A table rather than a flag on the pickup, because the pickup is rebuilt with the zone and
    /// the handler that grants the item never sees it. One place, two ids, checked by a test.
    /// </para>
    /// </remarks>
    public static class DiscoveryItems
    {
        /// <summary>The item a discovery puts in the hands, if any.</summary>
        /// <param name="discoveryId">A <see cref="ContentIds"/> discovery id.</param>
        /// <param name="itemId">The <see cref="ItemIds"/> id, or null.</param>
        /// <returns>True when collecting the discovery also grants an item.</returns>
        public static bool TryItemFor(string discoveryId, out string itemId)
        {
            switch (discoveryId)
            {
                case ContentIds.DiscoveryWaterloggedReel:
                    itemId = ItemIds.WaterloggedReel;
                    return true;
                default:
                    itemId = null;
                    return false;
            }
        }
    }
}
