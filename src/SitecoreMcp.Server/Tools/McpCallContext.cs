using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Globalization;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Confirmation;
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

        /// <summary>Whether this call runs as a Sitecore administrator, which gates admin-only tools.</summary>
        public bool IsAdministrator => User != null && User.IsAdministrator;

        /// <summary>
        /// Issues and redeems confirmation tokens for direct changes. Null means confirmation is not
        /// available, and every direct change is refused (fail closed).
        /// </summary>
        public ConfirmationService Confirmations { get; set; }

        /// <summary>
        /// The content roots this call may change. Defaults to unrestricted until content roots are
        /// configured; move, copy and rename refuse to cross them.
        /// </summary>
        public IContentRootBoundary ContentRoots { get; set; } = UnrestrictedContentRoots.Instance;

        /// <summary>
        /// Runs a direct change through server-enforced confirmation: without a token the preview and a
        /// single-use token come back and nothing changes; with the token from that same preview the
        /// change runs. See <see cref="DirectChangeGate"/>.
        /// </summary>
        public McpToolResult ConfirmDirectChange(
            string tool, object arguments, string confirmationToken, DirectChangePreview preview, System.Func<McpToolResult> change) =>
            DirectChangeGate.Run(Confirmations, tool, User?.Name, arguments, confirmationToken, preview, change);

        /// <summary>
        /// Resolves a database by name, defaulting to master when none is given, and rejecting one
        /// outside the client's allow-list or one that does not exist. Throws
        /// <see cref="McpToolException"/> so the failure reaches the model.
        /// </summary>
        public Database ResolveDatabase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = "master";
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
