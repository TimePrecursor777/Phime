using System;
using System.Collections.Generic;
using System.Linq;
using Authentication;
using Connections;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Trading;
using UserManagement;
using Utils;

namespace Agora
{
    /// <summary>
    /// Client half of the market board. Keeps a copy of the board the server sends, and posts or removes
    /// this player's listings.
    /// </summary>
    /// <remarks>
    /// Modelled on <c>Chat.ClientChat</c>, so compare the two if anything here is unclear.
    /// <para>
    /// Like ServerAgora, this is a "partial" class: the bounty half lives in ClientAgora.Bounties.cs.
    /// </para>
    /// </remarks>
    public partial class ClientAgora : Agora
    {
        /// <inheritdoc/>
        public override event EventHandler<LogEventArgs> OnLogEntry;
        /// <inheritdoc/>
        public override void RaiseLogEntry(LogEventArgs e) => OnLogEntry?.Invoke(this, e);

        /// <summary>
        /// Raised whenever a new copy of the board arrives, so the market board window knows to redraw.
        /// </summary>
        /// <remarks>
        /// An event is a list of functions to call when something happens, a bit like a list of callbacks in Python.
        /// Other code subscribes with <c>clientAgora.OnListingsChanged += myFunction;</c>
        /// </remarks>
        public event EventHandler OnListingsChanged;

        /// <summary>
        /// Raised when the server answers one of our posts, saying whether it went up.
        /// </summary>
        public event EventHandler<PostListingResponsePacket> OnPostListingResponse;

        /// <summary>
        /// <see cref="NetClient"/> instance to bind the packet handler to.
        /// </summary>
        private NetClient netClient;

        /// <summary>
        /// <see cref="ClientAuthenticator"/> to get the session ID from.
        /// </summary>
        private ClientAuthenticator authenticator;

        /// <summary>
        /// <see cref="ClientUserManager"/> to get our UUID and login state from.
        /// </summary>
        private ClientUserManager userManager;

        /// <summary>
        /// Our copy of the board, replaced whenever the server sends a new one.
        /// </summary>
        private List<Listing> listings = new List<Listing>();
        /// <summary>
        /// Lock object to prevent race conditions when accessing <see cref="listings"/>.
        /// Packets arrive on the network thread while the UI reads the board on the game thread.
        /// </summary>
        private object listingsLock = new object();

        /// <summary>
        /// Initialises a new <see cref="ClientAgora"/> instance.
        /// </summary>
        /// <param name="netClient"><see cref="NetClient"/> to send and receive packets with</param>
        /// <param name="authenticator"><see cref="ClientAuthenticator"/> to get the session ID from</param>
        /// <param name="userManager"><see cref="ClientUserManager"/> to get our UUID from</param>
        public ClientAgora(NetClient netClient, ClientAuthenticator authenticator, ClientUserManager userManager)
        {
            this.netClient = netClient;
            this.authenticator = authenticator;
            this.userManager = userManager;

            netClient.RegisterPacketHandler(MODULE_NAME, packetHandler);
            netClient.OnDisconnect += disconnectHandler;
        }

        /// <summary>
        /// Returns a copy of every listing on the board, newest first.
        /// </summary>
        /// <returns>Listings on the board</returns>
        public Listing[] GetListings()
        {
            lock (listingsLock)
            {
                // Hand out a copy so the UI can loop over it while new packets replace the real list
                return listings.OrderByDescending(listing => listing.PostedTime).ToArray();
            }
        }

        /// <summary>
        /// Asks the server to put a listing on the board.
        /// The answer arrives later through <see cref="OnPostListingResponse"/>.
        /// </summary>
        /// <param name="type">Whether we're offering or wanting these items</param>
        /// <param name="items">Items on offer or wanted</param>
        /// <param name="note">Short message to go with the listing</param>
        public void PostListing(ListingType type, IEnumerable<ProtoThing> items, string note)
        {
            if (!authenticator.Authenticated) return;
            if (!userManager.LoggedIn) return;
            
            Listing listing = new Listing { Type = type, Note = note ?? "" };
            listing.Items.AddRange(items);
            PostListingPacket packet = new PostListingPacket
            {
                SessionId = authenticator.SessionId,
                Uuid = userManager.Uuid,
                Listing = listing
            };

            Any packedPacket = ProtobufPacketHelper.Pack(packet);
            netClient.Send(MODULE_NAME, packedPacket.ToByteArray());
        }

        /// <summary>
        /// Asks the server to take one of our listings off the board.
        /// </summary>
        /// <param name="listingId">ID of the listing to remove</param>
        public void RemoveListing(string listingId)
        {
            if (!authenticator.Authenticated || !userManager.LoggedIn)
            {
                return;
            }
            
            RemoveListingPacket packet = new RemoveListingPacket
            {
                SessionId = authenticator.SessionId,
                Uuid = userManager.Uuid,
                ListingId = listingId
            };
            
            Any packedPacket = ProtobufPacketHelper.Pack(packet);
            netClient.Send(MODULE_NAME, packedPacket.ToByteArray());
        }

        /// <summary>
        /// Forgets the board when we disconnect, since it may change before we reconnect.
        /// </summary>
        private void disconnectHandler(object sender, EventArgs e)
        {
            lock (listingsLock)
            {
                listings.Clear();
            }

            OnListingsChanged?.Invoke(this, EventArgs.Empty);

            // Forget the bounty board too (in ClientAgora.Bounties.cs)
            clearBounties();
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
            if (!ProtobufPacketHelper.ValidatePacket(typeof(ClientAgora).Namespace, MODULE_NAME, module, data, out Any message, out TypeUrl typeUrl)) return;

            switch (typeUrl.Type)
            {
                case "SyncListingsPacket":
                    RaiseLogEntry(new LogEventArgs("Got a SyncListingsPacket", LogLevel.DEBUG));
                    syncListingsPacketHandler(message.Unpack<SyncListingsPacket>());
                    break;
                case "PostListingResponsePacket":
                    RaiseLogEntry(new LogEventArgs("Got a PostListingResponsePacket", LogLevel.DEBUG));
                    OnPostListingResponse?.Invoke(this, message.Unpack<PostListingResponsePacket>());
                    break;

                // Bounty packets, handled in ClientAgora.Bounties.cs
                case "SyncBountiesPacket":
                    RaiseLogEntry(new LogEventArgs("Got a SyncBountiesPacket", LogLevel.DEBUG));
                    syncBountiesPacketHandler(message.Unpack<SyncBountiesPacket>());
                    break;
                case "PostBountyResponsePacket":
                    RaiseLogEntry(new LogEventArgs("Got a PostBountyResponsePacket", LogLevel.DEBUG));
                    OnPostBountyResponse?.Invoke(this, message.Unpack<PostBountyResponsePacket>());
                    break;
                case "FulfilBountyResponsePacket":
                    RaiseLogEntry(new LogEventArgs("Got a FulfilBountyResponsePacket", LogLevel.DEBUG));
                    OnFulfilBountyResponse?.Invoke(this, message.Unpack<FulfilBountyResponsePacket>());
                    break;
                case "BountyDeliveryPacket":
                    RaiseLogEntry(new LogEventArgs("Got a BountyDeliveryPacket", LogLevel.DEBUG));
                    OnBountyDelivery?.Invoke(this, message.Unpack<BountyDeliveryPacket>());
                    break;

                default:
                    RaiseLogEntry(new LogEventArgs("Got an unknown packet type (" + typeUrl.Type + "), discarding...", LogLevel.DEBUG));
                    break;
            }
        }

        /// <summary>
        /// Replaces our copy of the board with the one the server sent.
        /// </summary>
        /// <param name="packet">Incoming <see cref="SyncListingsPacket"/></param>
        private void syncListingsPacketHandler(SyncListingsPacket packet)
        {
            lock (listingsLock)
            {
                listings = packet.Listings.ToList();
            }

            // "?." only calls Invoke if anyone has subscribed, like "if callbacks: ..." in Python
            OnListingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
