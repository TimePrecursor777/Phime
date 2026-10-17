using System;
using System.Collections.Generic;
using System.Linq;
using Authentication;
using Connections;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using UserManagement;
using Utils;

namespace Agora
{
    /// <summary>
    /// Server half of the market board. Holds every listing, accepts new ones and removals,
    /// and keeps every logged-in player's copy of the board up to date.
    /// </summary>
    /// <remarks>
    /// Modelled on <c>Chat.ServerChat</c>, so compare the two if anything here is unclear.
    /// </remarks>
    public class ServerAgora : Agora
    {
        /// <inheritdoc/>
        public override event EventHandler<LogEventArgs> OnLogEntry;
        /// <inheritdoc/>
        public override void RaiseLogEntry(LogEventArgs e) => OnLogEntry?.Invoke(this, e);

        /// <summary>
        /// <see cref="NetServer"/> instance to bind the packet handler to.
        /// </summary>
        private NetServer netServer;

        /// <summary>
        /// <see cref="ServerAuthenticator"/> instance used to check session validity.
        /// </summary>
        private ServerAuthenticator authenticator;

        /// <summary>
        /// <see cref="ServerUserManager"/> instance used to check login state and source UUID validity.
        /// </summary>
        private ServerUserManager userManager;

        /// <summary>
        /// Every listing on the board, looked up by listing ID. Works like a Python dict.
        /// </summary>
        private Dictionary<string, Listing> listings = new Dictionary<string, Listing>();
        /// <summary>
        /// Lock object to prevent race conditions when accessing <see cref="listings"/>.
        /// Packets from different players can arrive at the same time, so every read or write goes inside
        /// <c>lock (listingsLock) { ... }</c>, which lets only one thread in at once.
        /// </summary>
        private object listingsLock = new object();

        /// <summary>
        /// Limits how many listings each user can post in a short period, to stop spam.
        /// </summary>
        private RateLimiter postRateLimiter;

        /// <summary>
        /// Most listings one user can have on the board at once.
        /// </summary>
        private int maxListingsPerUser;

        /// <summary>
        /// Most items one listing can hold.
        /// </summary>
        public const int MAX_ITEMS_PER_LISTING = 10;

        /// <summary>
        /// Longest a listing's note can be, in characters.
        /// </summary>
        public const int MAX_NOTE_LENGTH = 200;

        /// <summary>
        /// Initialises a new <see cref="ServerAgora"/> instance.
        /// </summary>
        /// <param name="netServer"><see cref="NetServer"/> to receive packets from</param>
        /// <param name="authenticator"><see cref="ServerAuthenticator"/> to check sessions with</param>
        /// <param name="userManager"><see cref="ServerUserManager"/> to check logins with</param>
        /// <param name="maxListingsPerUser">Most listings one user can have on the board at once</param>
        /// <param name="postRateLimitPosts">Most listings one user can post within <paramref name="postRateLimitSeconds"/></param>
        /// <param name="postRateLimitSeconds">Length of the rate limit window in seconds</param>
        public ServerAgora(NetServer netServer, ServerAuthenticator authenticator, ServerUserManager userManager, int maxListingsPerUser = 3, int postRateLimitPosts = 2, int postRateLimitSeconds = 60)
        {
            this.netServer = netServer;
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.maxListingsPerUser = maxListingsPerUser;
            this.postRateLimiter = new RateLimiter(postRateLimitPosts, TimeSpan.FromSeconds(postRateLimitSeconds));

            // Ask to be handed every packet addressed to this module, and to be told when someone logs in
            netServer.RegisterPacketHandler(MODULE_NAME, packetHandler);
            userManager.OnLogin += loginHandler;
        }

        /// <summary>
        /// Sends the whole board to a player as soon as they log in.
        /// </summary>
        private void loginHandler(object sender, ServerLoginEventArgs args)
        {
            sendSyncListings(args.ConnectionId);
        }

        /// <summary>
        /// Handles incoming packets, sending each one to the right handler by its type.
        /// </summary>
        /// <param name="module">Target module</param>
        /// <param name="connectionId">Original connection ID</param>
        /// <param name="data">Data payload</param>
        private void packetHandler(string module, string connectionId, byte[] data)
        {
            // Discard packet if it fails validation
            if (!ProtobufPacketHelper.ValidatePacket(typeof(ServerAgora).Namespace, MODULE_NAME, module, data, out Any message, out TypeUrl typeUrl)) return;

            // Determine what to do with the packet, like a chain of if/elif in Python
            switch (typeUrl.Type)
            {
                case "PostListingPacket":
                    RaiseLogEntry(new LogEventArgs(string.Format("Got a PostListingPacket from {0}", connectionId.Highlight(HighlightType.ConnectionID)), LogLevel.DEBUG));
                    postListingPacketHandler(connectionId, message.Unpack<PostListingPacket>());
                    break;
                case "RemoveListingPacket":
                    RaiseLogEntry(new LogEventArgs(string.Format("Got a RemoveListingPacket from {0}", connectionId.Highlight(HighlightType.ConnectionID)), LogLevel.DEBUG));
                    removeListingPacketHandler(connectionId, message.Unpack<RemoveListingPacket>());
                    break;
                default:
                    RaiseLogEntry(new LogEventArgs("Got an unknown packet type (" + typeUrl.Type + "), discarding...", LogLevel.DEBUG));
                    break;
            }
        }

        /// <summary>
        /// Handles incoming <see cref="PostListingPacket"/>s.
        /// </summary>
        /// <param name="connectionId">Original connection ID</param>
        /// <param name="packet">Incoming <see cref="PostListingPacket"/></param>
        private void postListingPacketHandler(string connectionId, PostListingPacket packet)
        {
            if (!authenticator.IsAuthenticated(connectionId, packet.SessionId))
            {
                sendPostListingResponse(connectionId, false, "", "Session is not valid");
                return;
            }
            if (!userManager.IsLoggedIn(connectionId, packet.Uuid))
            {
                sendPostListingResponse(connectionId, false, "", "Session is not valid");
                return;
            }
            if (!postRateLimiter.TryAct(packet.Uuid))
            {
                sendPostListingResponse(connectionId, false, "", "You are posting too fast!");
                return;
            }
            if (packet.Listing == null)
            {
                sendPostListingResponse(connectionId, false, "", "No listing was sent");
                return;
            }
            if (packet.Listing.Items.Count == 0 || packet.Listing.Items.Count > MAX_ITEMS_PER_LISTING)
            {
                sendPostListingResponse(connectionId, false, "", string.Format("A listing needs between 1 and {0} items", MAX_ITEMS_PER_LISTING));
                return;
            }
            lock (listingsLock)
            {
                // Count how many listings this user already has up, like sum(1 for l in listings.values() if l.PosterUuid == uuid)
                int alreadyPosted = listings.Values.Count(existing => existing.PosterUuid == packet.Uuid);
                if (alreadyPosted >= maxListingsPerUser)
                {
                    sendPostListingResponse(connectionId, false, "", string.Format("You can only have {0} listings up at once. Remove one first", maxListingsPerUser));
                    return;
                }
            }
            Listing listing = packet.Listing.Clone();                        // make a copy called "listing"
            listing.ListingId = Guid.NewGuid().ToString();                   // replace its ID with a new random one
            listing.PosterUuid = packet.Uuid;                                // replace the poster with who really sent it
            listing.PostedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();  // replace the time with the server's clock
            listing.Note = TextHelper.SanitiseRichText(listing.Note);        // replace the note with a cleaned version
            if (listing.Note.Length > MAX_NOTE_LENGTH) listing.Note = listing.Note.Substring(0, MAX_NOTE_LENGTH); // and cut it short if it's too long
            lock (listingsLock)
            {
                listings.Add(listing.ListingId, listing);
            }

            sendPostListingResponse(connectionId, true, listing.ListingId, "");
            broadcastSyncListings();
            // TODO (yours): fill this in. ServerChat.chatMessagePacketHandler does almost the same steps.
            //
            // 1. Refuse it if the session isn't authenticated:   authenticator.IsAuthenticated(connectionId, packet.SessionId)
            // 2. Refuse it if the user isn't logged in:           userManager.IsLoggedIn(connectionId, packet.Uuid)
            // 3. Refuse it if they're posting too fast:           postRateLimiter.TryAct(packet.Uuid)
            //    For each refusal, call sendPostListingResponse(connectionId, false, "", "some reason") and then return
            //
            // 4. Don't trust anything the client filled in that the server should decide. Copy the listing and overwrite:
            //        Listing listing = packet.Listing.Clone();
            //        listing.ListingId = Guid.NewGuid().ToString();                  // a fresh unique ID
            //        listing.PosterUuid = packet.Uuid;                                // who actually sent it
            //        listing.PostedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();  // the server's clock
            //        listing.Note = TextHelper.SanitiseRichText(listing.Note);        // strip sneaky formatting
            //
            // 5. Add it to the board inside the lock:             lock (listingsLock) { listings.Add(listing.ListingId, listing); }
            // 6. Tell the poster it worked:                       sendPostListingResponse(connectionId, true, listing.ListingId, "")
            // 7. Send everyone the updated board:                 broadcastSyncListings()
        }

        /// <summary>
        /// Handles incoming <see cref="RemoveListingPacket"/>s.
        /// </summary>
        /// <param name="connectionId">Original connection ID</param>
        /// <param name="packet">Incoming <see cref="RemoveListingPacket"/></param>
        private void removeListingPacketHandler(string connectionId, RemoveListingPacket packet)
        {
            if (!authenticator.IsAuthenticated(connectionId, packet.SessionId))
            {
                sendPostListingResponse(connectionId, false, "", "Session is not valid");
                return;
            }
            

            if (!userManager.IsLoggedIn(connectionId, packet.Uuid))
            {
                sendPostListingResponse(connectionId, false, "", "Session is not valid");
                return;
            }

            lock (listingsLock)
            {
                if (!listings.TryGetValue(packet.ListingId, out Listing listing)) return;  // not found
                if (listing.PosterUuid != packet.Uuid) return;                            // not theirs
                listings.Remove(packet.ListingId);
            }

            broadcastSyncListings();
            
            // TODO (yours): fill this in.
            //
            // 1. Ignore it (just return) if the session isn't authenticated or the user isn't logged in, same checks as above
            // 2. Inside lock (listingsLock):
            //      - look the listing up:   listings.TryGetValue(packet.ListingId, out Listing listing)
            //      - ignore it if it doesn't exist, or if listing.PosterUuid != packet.Uuid
            //        (that second check is what stops people deleting other players' listings)
            //      - otherwise remove it:   listings.Remove(packet.ListingId);
            // 3. Send everyone the updated board:   broadcastSyncListings()
        }

        /// <summary>
        /// Builds a <see cref="SyncListingsPacket"/> holding every listing on the board.
        /// </summary>
        /// <returns>Packed packet ready to send</returns>
        private Any packSyncListings()
        {
            SyncListingsPacket packet = new SyncListingsPacket();
            lock (listingsLock)
            {
                packet.Listings.AddRange(listings.Values);
            }

            return ProtobufPacketHelper.Pack(packet);
        }

        /// <summary>
        /// Sends the whole board to one connection.
        /// </summary>
        /// <param name="connectionId">Destination connection ID</param>
        private void sendSyncListings(string connectionId)
        {
            if (!netServer.TrySend(connectionId, MODULE_NAME, packSyncListings().ToByteArray()))
            {
                RaiseLogEntry(new LogEventArgs("Failed to send SyncListingsPacket to connection " + connectionId.Highlight(HighlightType.ConnectionID), LogLevel.ERROR));
            }
        }

        /// <summary>
        /// Sends the whole board to every logged-in player.
        /// </summary>
        private void broadcastSyncListings()
        {
            byte[] data = packSyncListings().ToByteArray();
            foreach (string connectionId in userManager.GetConnections())
            {
                if (!netServer.TrySend(connectionId, MODULE_NAME, data))
                {
                    RaiseLogEntry(new LogEventArgs("Failed to send SyncListingsPacket to connection " + connectionId.Highlight(HighlightType.ConnectionID), LogLevel.ERROR));
                }
            }
        }

        /// <summary>
        /// Tells a player whether their listing was posted.
        /// </summary>
        /// <param name="connectionId">Destination connection ID</param>
        /// <param name="success">Whether the listing was posted</param>
        /// <param name="listingId">ID the server gave the listing, empty on failure</param>
        /// <param name="failureMessage">Why it failed, empty on success</param>
        private void sendPostListingResponse(string connectionId, bool success, string listingId, string failureMessage)
        {
            PostListingResponsePacket packet = new PostListingResponsePacket
            {
                Success = success,
                ListingId = listingId,
                FailureMessage = failureMessage
            };

            if (!netServer.TrySend(connectionId, MODULE_NAME, ProtobufPacketHelper.Pack(packet).ToByteArray()))
            {
                RaiseLogEntry(new LogEventArgs("Failed to send PostListingResponsePacket to connection " + connectionId.Highlight(HighlightType.ConnectionID), LogLevel.ERROR));
            }
        }
    }
}
