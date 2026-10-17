using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Trading;
using Verse;

namespace PhinixClient
{
    /// <summary>
    /// Takes items off the map for a bounty and holds them until the server says whether it accepted them.
    /// </summary>
    /// <remarks>
    /// Posting or fulfilling a bounty is a two-step conversation with the server:
    /// <list type="number">
    /// <item>We take the items off the map (so they can't be used twice) and send them along with a random "token".</item>
    /// <item>The server answers with the same token and either Success = true or false.</item>
    /// </list>
    /// In between, the items wait here, unspawned. When the answer comes back, <see cref="Resolve"/> either destroys
    /// them (the server has them now) or puts them back where they came from (the server said no).
    /// <para>
    /// The trade window does exactly the same dance with its pendingItemStacks. This is that idea pulled out into its
    /// own class so both bounty windows can share it.
    /// </para>
    /// </remarks>
    public static class BountyEscrow
    {
        /// <summary>
        /// One request for items: "this many of this item, at least this good".
        /// </summary>
        public struct Request
        {
            public ThingDef Def;
            public int Count;
            public Quality MinQuality;
        }

        /// <summary>
        /// Items taken off the map for one token, and where each one was, so they can be put back.
        /// </summary>
        private class HeldItems
        {
            public List<Thing> Things = new List<Thing>();
            public List<IntVec3> Positions = new List<IntVec3>();
            public List<Map> Maps = new List<Map>();
        }

        /// <summary>
        /// Everything currently waiting on the server, by token.
        /// </summary>
        /// <remarks>
        /// Only ever touched on the main game thread (network replies are handed over with RunOnMainThread first),
        /// so it doesn't need a lock.
        /// </remarks>
        private static readonly Dictionary<string, HeldItems> held = new Dictionary<string, HeldItems>();

        /// <summary>
        /// Whether the colony has enough items to cover every request.
        /// </summary>
        /// <param name="requests">What's needed</param>
        /// <returns>Whether all of it is available</returns>
        public static bool CanTake(IEnumerable<Request> requests)
        {
            return requests.All(request => ColonyItems.Count(request.Def, request.MinQuality) >= request.Count);
        }

        /// <summary>
        /// Takes items off the map to cover every request and holds them under a new token.
        /// Takes nothing at all if any request can't be covered.
        /// </summary>
        /// <param name="requests">What's needed</param>
        /// <param name="token">Token the items are held under, to send to the server</param>
        /// <param name="items">The taken items, already converted for sending</param>
        /// <returns>Whether the items were taken</returns>
        public static bool TryTake(IEnumerable<Request> requests, out string token, out List<ProtoThing> items)
        {
            token = null;
            items = null;

            // Check first, so we never end up taking half the items and then finding the rest are missing
            List<Request> requestList = requests.ToList();
            if (!CanTake(requestList)) return false;

            HeldItems taken = new HeldItems();
            foreach (Request request in requestList)
            {
                int stillNeeded = request.Count;

                // Use the worst acceptable items first, so a bounty asking for "normal or better" doesn't eat the
                // player's legendary gear when a normal one would do. ToList() takes a snapshot, since SplitOff below
                // changes the map while we loop
                List<Thing> candidates = ColonyItems.TradeableItems()
                    .Where(thing => thing.def == request.Def && ColonyItems.MeetsQuality(thing, request.MinQuality))
                    .OrderBy(thing => thing.TryGetQuality(out QualityCategory quality) ? (int) quality : 0)
                    .ToList();

                foreach (Thing thing in candidates)
                {
                    if (stillNeeded <= 0) break;

                    // Remember where it was before it leaves the map. A despawned thing forgets its map and position
                    IntVec3 position = thing.Position;
                    Map map = thing.Map;

                    // SplitOff(n) takes n off the stack and gives them back as their own thing. If n is the whole stack
                    // it hands back the original thing, already removed from the map. Either way the result isn't on the map
                    Thing piece = thing.SplitOff(Math.Min(stillNeeded, thing.stackCount));
                    stillNeeded -= piece.stackCount;

                    taken.Things.Add(piece);
                    taken.Positions.Add(position);
                    taken.Maps.Add(map);
                }
            }

            token = Guid.NewGuid().ToString();
            held.Add(token, taken);
            items = taken.Things.Select(TradingThingConverter.ConvertThingFromVerse).ToList();
            return true;
        }

        /// <summary>
        /// Settles the items held under a token, once the server has answered.
        /// </summary>
        /// <param name="token">Token from the server's answer</param>
        /// <param name="success">True if the server kept the items, false if it refused them</param>
        public static void Resolve(string token, bool success)
        {
            // Unknown token, e.g. we already settled it. Nothing to do
            if (string.IsNullOrEmpty(token) || !held.TryGetValue(token, out HeldItems taken)) return;
            held.Remove(token);

            for (int i = 0; i < taken.Things.Count; i++)
            {
                Thing thing = taken.Things[i];
                if (success)
                {
                    // The server has them now, so ours are no longer needed
                    if (!thing.Destroyed) thing.Destroy();
                }
                else
                {
                    // Put it back as close to where it was as possible. "Near" lets the game pick a free spot if the
                    // original cell has filled up since
                    GenPlace.TryPlaceThing(thing, taken.Positions[i], taken.Maps[i], ThingPlaceMode.Near);
                }
            }
        }
    }
}
