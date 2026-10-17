using System;
using Verse;

namespace PhinixClient
{
    internal struct PendingThings
    {
        /// <summary>
        /// Collection of stacked items that were added to the trade.
        /// </summary>
        public Thing[] Things;

        /// <summary>
        /// The map each of <see cref="Things"/> was on before it was despawned, in the same order.
        /// A despawned thing forgets its map, so we need this to put it back if the server rejects the update.
        /// </summary>
        public Map[] Maps;

        /// <summary>
        /// Time the trade update was created.
        /// </summary>
        public DateTime Timestamp;
    }
}