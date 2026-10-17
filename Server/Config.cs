using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Xml;
using Authentication;

namespace PhinixServer
{
    /// <inheritdoc />
    /// <summary>
    /// Server configuration class able to read from and write to a configuration file.
    /// </summary>
    [DataContract]
    public class Config
    {
        /// <summary>
        /// IP address to listen on.
        /// </summary>
        [IgnoreDataMember]
        public IPAddress Address = IPAddress.Any;

        /// <summary>
        /// Textual representation of Address.
        /// This is reserved for (de)serialisation purposes.
        /// </summary>
        [DataMember(Name = "IPAddress", Order = 0)]
        private string addressString = "";

        /// <summary>
        /// Port to listen on.
        /// </summary>
        [DataMember(Name = "Port", Order = 1)]
        public int Port = 16200;

        /// <summary>
        /// Maximum number of connections to accept at once.
        /// </summary>
        [DataMember(Name = "MaxConnections", Order = 2)]
        public int MaxConnections = 1000;

        /// <summary>
        /// Path to the log file.
        /// </summary>
        [DataMember(Name = "LogFile", Order = 3)]
        public string LogPath = "server.log";

        /// <summary>
        /// Save interval in milliseconds.
        /// </summary>
        [DataMember(Name = "SaveInterval", Order = 4)]
        public int SaveInterval = 60000;

        /// <summary>
        /// The minimum verbosity level for a message to be displayed in the console.
        /// </summary>
        [DataMember(Name = "DisplayVerbosity", Order = 5)]
        public Verbosity DisplayVerbosity = Verbosity.INFO;

        /// <summary>
        /// The minimum verbosity level for a message to be recorded in the log file.
        /// </summary>
        [DataMember(Name = "LogVerbosity", Order = 6)]
        public Verbosity LogVerbosity = Verbosity.INFO;

        /// <summary>
        /// Path to the user database file.
        /// </summary>
        [DataMember(Name = "UserDatabaseFile", Order = 7)]
        public string UserDatabasePath = "users";

        /// <summary>
        /// Path to the credential database file.
        /// </summary>
        [DataMember(Name = "CredentialDatabaseFile", Order = 8)]
        public string CredentialDatabasePath = "credentials";

        /// <summary>
        /// Path to the chat history file.
        /// </summary>
        [DataMember(Name = "ChatHistoryFile", Order = 9)]
        public string ChatHistoryPath = "chatHistory";

        /// <summary>
        /// Path to the chat history file.
        /// </summary>
        [DataMember(Name = "TradeDatabaseFile", Order = 10)]
        public string TradeDatabasePath = "trades";

        /// <summary>
        /// Name of the server as shown to clients.
        /// </summary>
        [DataMember(Name = "ServerName", Order = 11)]
        public string ServerName = "Phinix Server";

        /// <summary>
        /// Description of the server as shown to clients.
        /// </summary>
        [DataMember(Name = "ServerDescription", Order = 12)]
        public string ServerDescription = "A Phinix server.";

        /// <summary>
        /// Authentication type clients must use when connecting.
        /// </summary>
        [DataMember(Name = "AuthType", Order = 13)]
        public AuthTypes AuthType = AuthTypes.ClientKey;

        /// <summary>
        /// Maximum display name length for users.
        /// </summary>
        [DataMember(Name = "MaxDisplayNameLength", Order = 14)]
        public int MaxDisplayNameLength = 100;

        /// <summary>
        /// Maximum number of chat messages to store in the chat history buffer.
        /// </summary>
        [DataMember(Name = "ChatHistoryLength", Order = 15)]
        public int ChatHistoryLength = 40;

        /// <summary>
        /// Maximum number of chat messages a user can send within <see cref="ChatRateLimitSeconds"/> before further messages are refused.
        /// </summary>
        [DataMember(Name = "ChatRateLimitMessages", Order = 16)]
        public int ChatRateLimitMessages = 3;

        /// <summary>
        /// Length in seconds of the window used by <see cref="ChatRateLimitMessages"/>.
        /// </summary>
        [DataMember(Name = "ChatRateLimitSeconds", Order = 17)]
        public int ChatRateLimitSeconds = 10;

        /// <summary>
        /// Whether chat history is saved to disk and loaded again when the server restarts.
        /// Off by default, so every restart starts with an empty chat.
        /// </summary>
        [DataMember(Name = "SaveChatHistory", Order = 18)]
        public bool SaveChatHistory = false;

        /// <summary>
        /// Most listings one user can have on the market board at once.
        /// </summary>
        [DataMember(Name = "MarketMaxListingsPerUser", Order = 19)]
        public int MarketMaxListingsPerUser = 3;

        /// <summary>
        /// Most listings one user can post within <see cref="MarketPostRateLimitSeconds"/>.
        /// </summary>
        [DataMember(Name = "MarketPostRateLimitPosts", Order = 20)]
        public int MarketPostRateLimitPosts = 2;

        /// <summary>
        /// Length in seconds of the window used by <see cref="MarketPostRateLimitPosts"/>.
        /// </summary>
        [DataMember(Name = "MarketPostRateLimitSeconds", Order = 21)]
        public int MarketPostRateLimitSeconds = 60;

        /// <summary>
        /// Path to the bounty database file. Bounties are always saved, because they hold players' reward items.
        /// </summary>
        [DataMember(Name = "BountyDatabaseFile", Order = 22)]
        public string BountyDatabasePath = "bounties";

        /// <summary>
        /// Most bounties one user can have up at once.
        /// </summary>
        [DataMember(Name = "BountyMaxPerUser", Order = 23)]
        public int BountyMaxPerUser = 2;

        /// <summary>
        /// How many days a bounty stays up before its reward goes back to the poster.
        /// </summary>
        [DataMember(Name = "BountyExpiryDays", Order = 24)]
        public int BountyExpiryDays = 7;

        /// <summary>
        /// Loads a <see cref="Config"/> object from the given file path. Will return a default <see cref="Config"/> if the file does not exist.
        /// </summary>
        /// <param name="filePath">Config file path</param>
        /// <returns>Loaded <see cref="Config"/> object</returns>
        public static Config Load(string filePath)
        {
            // Give a fresh new config if the given file doesn't exist
            if (!File.Exists(filePath))
            {
                Config config = new Config();
                config.Save(filePath); // Save it first to make sure it's present next time
                return config;
            }

            Config result;

            using (XmlReader reader = XmlReader.Create(filePath))
            {
                result = new DataContractSerializer(typeof(Config)).ReadObject(reader) as Config;
            }

            return result;
        }

        /// <summary>
        /// Saves the <see cref="Config"/> object to an XML document at the given path.
        /// This will overwrite the file if it already exists.
        /// </summary>
        /// <param name="filePath">Destination file path</param>
        public void Save(string filePath)
        {
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
            using (XmlWriter writer = XmlWriter.Create(filePath, settings))
            {
                new DataContractSerializer(typeof(Config)).WriteObject(writer, this);
            }
        }

        /// <summary>
        /// Called before the <see cref="Config"/> is serialised.
        /// Used to convert complex types to something easier to edit by hand.
        /// </summary>
        /// <param name="context"></param>
        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            this.addressString = Address.ToString();
        }

        [OnDeserializing]
        private void OnDeserialising(StreamingContext context)
        {
            if (string.IsNullOrEmpty(addressString)) addressString = IPAddress.Any.ToString();
            if (Port < 1 || Port > 65535) Port = 16200;
            if (MaxConnections < 0) MaxConnections = 1000;
            if (string.IsNullOrEmpty(LogPath)) LogPath = "server.log";
            if (SaveInterval < 1) SaveInterval = 60000;
            // Ignore Display- and LogVerbosity since they always have a value
            if (string.IsNullOrEmpty(UserDatabasePath)) UserDatabasePath = "users";
            if (string.IsNullOrEmpty(CredentialDatabasePath)) CredentialDatabasePath = "credentials";
            if (string.IsNullOrEmpty(ChatHistoryPath)) ChatHistoryPath = "chatHistory";
            if (string.IsNullOrEmpty(TradeDatabasePath)) TradeDatabasePath = "trades";
            if (string.IsNullOrEmpty(ServerName)) ServerName = "Phinix Server";
            if (string.IsNullOrEmpty(ServerDescription)) ServerDescription = "A Phinix server.";
            // Ignore AuthType since it always has a value
            if (MaxDisplayNameLength < 1) MaxDisplayNameLength = 100;
            if (ChatHistoryLength < 0) ChatHistoryLength = 40;
            if (ChatRateLimitMessages < 1) ChatRateLimitMessages = 3;
            // Ignore SaveChatHistory since a missing value means false, which is the default anyway
            if (MarketMaxListingsPerUser < 1) MarketMaxListingsPerUser = 3;
            if (MarketPostRateLimitPosts < 1) MarketPostRateLimitPosts = 2;
            if (MarketPostRateLimitSeconds < 1) MarketPostRateLimitSeconds = 60;
            if (string.IsNullOrEmpty(BountyDatabasePath)) BountyDatabasePath = "bounties";
            if (BountyMaxPerUser < 1) BountyMaxPerUser = 2;
            if (BountyExpiryDays < 1) BountyExpiryDays = 7;
            if (ChatRateLimitSeconds < 1) ChatRateLimitSeconds = 10;
        }

        /// <summary>
        /// Called after the <see cref="Config"/> is deserialised.
        /// Used to convert easy-to-edit types back into their complex counterparts.
        /// </summary>
        /// <param name="context"></param>
        [OnDeserialized]
        private void OnDeserialised(StreamingContext context)
        {
            if (!IPAddress.TryParse(addressString, out Address))
            {
                throw new ConfigItemDeserialisationException(typeof(string), typeof(IPAddress), nameof(addressString));
            }
        }
    }
}
