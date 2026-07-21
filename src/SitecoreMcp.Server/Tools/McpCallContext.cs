using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Globalization;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Transport;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Everything a tool needs about the current call: who it runs as, what it may do, and how to
    /// resolve the database and language it targets. Built once per request after authentication.
    /// </summary>
    public sealed class McpCallContext
    {
        /// <summary>Creates the context for an authenticated client running as the given user.</summary>
        public McpCallContext(McpClient client, User user, McpSettings settings)
        {
            Client = client;
            User = user;
            Settings = settings;
        }

        /// <summary>The authenticated client behind this call.</summary>
        public McpClient Client { get; }

        /// <summary>The Sitecore user whose security context the call runs under.</summary>
        public User User { get; }

        /// <summary>The effective settings for this request.</summary>
        public McpSettings Settings { get; }

        /// <summary>Whether writes are permitted: the client must allow them and so must the global switch.</summary>
        public bool AllowWrites => Client.AllowWrites && Settings.AllowWrites;

        /// <summary>
        /// Resolves a database by name, rejecting one outside the client's allow-list or one that
        /// does not exist. Throws <see cref="McpToolException"/> so the failure reaches the model.
        /// </summary>
        public Database ResolveDatabase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new McpToolException("No database was specified.");
            }

            if (!Client.CanUseDatabase(name))
            {
                throw new McpToolException($"Database '{name}' is not permitted for this client.");
            }

            var database = Factory.GetDatabase(name, false);
            if (database == null)
            {
                throw new McpToolException($"Database '{name}' does not exist on this instance.");
            }

            return database;
        }

        /// <summary>
        /// Resolves a language by name, falling back to the context language when none is given.
        /// Throws <see cref="McpToolException"/> when the name is not a valid language.
        /// </summary>
        public Language ResolveLanguage(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Sitecore.Context.Language;
            }

            if (!Language.TryParse(name, out var language))
            {
                throw new McpToolException($"'{name}' is not a valid language.");
            }

            return language;
        }
    }
}
