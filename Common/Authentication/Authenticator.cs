using System;
using System.Reflection;
using Utils;

namespace Authentication
{
    /// <summary>
    /// Provides some common properties for <see cref="ClientAuthenticator"/> and <see cref="ServerAuthenticator"/> classes.
    /// </summary>
    public abstract class Authenticator : ILoggable
    {
        public readonly string MODULE_NAME = typeof(Authenticator).Namespace;
        
        /// <inheritdoc />
        public abstract event EventHandler<LogEventArgs> OnLogEntry;
        /// <inheritdoc />
        public abstract void RaiseLogEntry(LogEventArgs e);
        
        public static readonly Version Version = Assembly.GetAssembly(typeof(Authenticator)).GetName().Version;

        /// <summary>
        /// Version of the network protocol. The server only lets in clients with exactly this number.
        /// </summary>
        /// <remarks>
        /// Add one to this whenever a change means an older mod can't safely play with a newer server, or the
        /// other way round (e.g. new fields that trades rely on). Remember to rebuild both the server and the mod.
        /// </remarks>
        public const int PROTOCOL_VERSION = 1;
        
        /// <summary>
        /// Handles incoming packets for this Authenticator.
        /// </summary>
        /// <param name="module">Target module</param>
        /// <param name="connectionId">Original connection ID</param>
        /// <param name="data">Data payload</param>
        protected abstract void packetHandler(string module, string connectionId, byte[] data);
    }
}
