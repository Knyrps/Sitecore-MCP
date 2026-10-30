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
        /// <summary>
        /// Creates the context for an authenticated client running as the given user, under the given
        /// content scope (null meaning unrestricted).
        /// </summary>
        public McpCallContext(McpClient client, User user, McpSettings settings, ContentScope scope = null)
        {
            Client = client;
            User = user;
            Settings = settings;
            Scope = scope ?? ContentScope.Unrestricted;
        }

        /// <summary>The authenticated client behind this call.</summary>
        public McpClient Client { get; }

        /// <summary>The Sitecore user whose security context the call runs under.</summary>
        public User User { get; }

        /// <summary>The effective settings for this request.</summary>
        public McpSettings Settings { get; }

        /// <summary>
        /// The content roots (outside them items are read-only), search roots (search only looks there)
        /// and hidden fields (they do not exist for this call) every tool must respect.
        /// </summary>
        public ContentScope Scope { get; }

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
        /// The content roots this call may change, as a boundary over <see cref="Scope"/> (the pipeline
        /// sets it per call; unrestricted by default). Move, copy and rename refuse to cross them.
        /// </summary>
        public IContentRootBoundary ContentRoots { get; set; } = UnrestrictedContentRoots.Instance;

        /// <summary>Whether direct changes need confirmation on this call: the calling client's setting.</summary>
        public bool ConfirmDirectChanges => Client.ConfirmDirectChanges;

        /// <summary>How this call's direct change went through confirmation, for the audit log; null when it made none.</summary>
        public DirectChangeOutcome? DirectChange { get; private set; }

        /// <summary>
        /// Runs a direct change through server-enforced confirmation: without a token the preview and a
        /// single-use token come back and nothing changes; with the token from that same preview the
        /// change runs. For a client that does not confirm direct changes the change runs at once (every
        /// guard the tool applied before this call still holds). See <see cref="DirectChangeGate"/>.
        /// </summary>
        public McpToolResult ConfirmDirectChange(
            string tool, object arguments, string confirmationToken, DirectChangePreview preview, System.Func<McpToolResult> change) =>
            DirectChangeGate.Run(ConfirmDirectChanges, Confirmations, tool, User?.Name, arguments, confirmationToken, preview, change,
                outcome => DirectChange = outcome);

        /// <summary>
        /// Runs a direct change that always needs confirmation, whatever the calling client's setting (a
        /// package install can deploy code and restart the site). Same gate as <see cref="ConfirmDirectChange"/>
        /// with confirmation required; the tool implements <see cref="IAlwaysConfirmedDirectChange"/> so it
        /// is described the same way to every client.
        /// </summary>
        public McpToolResult ConfirmDirectChangeAlways(
            string tool, object arguments, string confirmationToken, DirectChangePreview preview, System.Func<McpToolResult> change) =>
            DirectChangeGate.Run(true, Confirmations, tool, User?.Name, arguments, confirmationToken, preview, change,
                outcome => DirectChange = outcome);

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
