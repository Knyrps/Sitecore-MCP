using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// A configured API client: its secret key, whose Sitecore identity its calls run as, and the
    /// limits on what it may do. A client either maps to exactly one fixed Sitecore user, or
    /// requires an editor token on every call and runs as the editor that token names. Never both.
    /// </summary>
    public sealed class McpClient
    {
        /// <summary>Creates a client binding a key to a fixed Sitecore user and its permissions.</summary>
        public McpClient(string key, string userName, bool allowWrites, IEnumerable<string> databases)
            : this(null, key, userName, false, null, allowWrites, databases, null, false)
        {
        }

        private McpClient(string id, string key, string userName, bool requiresEditorToken, string editorTokenAudience,
            bool allowWrites, IEnumerable<string> databases, ContentScope scope, bool confirmDirectChanges)
        {
            Id = id;
            Key = key;
            UserName = userName;
            RequiresEditorToken = requiresEditorToken;
            EditorTokenAudience = editorTokenAudience;
            AllowWrites = allowWrites;
            Databases = new HashSet<string>(databases ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Scope = scope ?? ContentScope.Unrestricted;
            ConfirmDirectChanges = confirmDirectChanges;
        }

        /// <summary>
        /// Creates a fixed-user client with a config id. Its direct changes run on the first call unless
        /// <paramref name="confirmDirectChanges"/> opts it in to server-enforced confirmation.
        /// </summary>
        public static McpClient ForUser(string id, string key, string userName, bool allowWrites, IEnumerable<string> databases,
            ContentScope scope = null, bool confirmDirectChanges = false) =>
            new McpClient(id, key, userName, false, null, allowWrites, databases, scope, confirmDirectChanges);

        /// <summary>
        /// Creates a client that has no fixed user: every call must carry an editor token issued for
        /// <paramref name="audience"/>, and runs as the editor it names. Its direct changes always need
        /// confirmation: the editor confirms them in the client (CorePilot), and that cannot be turned off.
        /// </summary>
        public static McpClient ForEditorToken(string id, string key, string audience, bool allowWrites, IEnumerable<string> databases,
            ContentScope scope = null) =>
            new McpClient(id, key, null, true, audience, allowWrites, databases, scope, true);

        /// <summary>The client's config id, used in the audit log; may be null for clients built in code.</summary>
        public string Id { get; }

        /// <summary>The resolved secret key this client presents, compared in constant time.</summary>
        public string Key { get; }

        /// <summary>
        /// The fixed Sitecore user (e.g. "sitecore\mcp-agent") whose security context calls run under;
        /// null for a client that requires an editor token.
        /// </summary>
        public string UserName { get; }

        /// <summary>Whether every call must carry a valid editor token, and run as the editor it names.</summary>
        public bool RequiresEditorToken { get; }

        /// <summary>The audience an editor token must be issued for to be accepted from this client.</summary>
        public string EditorTokenAudience { get; }

        /// <summary>Whether this client may invoke write tools, subject also to the global write switch.</summary>
        public bool AllowWrites { get; }

        /// <summary>The databases this client may target; anything outside the set is refused before a tool runs.</summary>
        public ISet<string> Databases { get; }

        /// <summary>
        /// The client's own content roots, search roots and hidden fields from its config node. Each
        /// unconfigured part is null, so the global scope applies; see <see cref="ConfiguredContentScopeProvider"/>.
        /// </summary>
        public ContentScope Scope { get; }

        /// <summary>
        /// Whether direct changes (delete, move, rename, publish, workflow commands, shared-field writes)
        /// need server-enforced confirmation for this client: the first call returns a preview and a
        /// token, and the change runs only on a repeat with that token. Always true for an editor-token
        /// client; for a fixed-user client false unless configured, so its changes run on the first call.
        /// </summary>
        public bool ConfirmDirectChanges { get; }

        /// <summary>Whether the given database name is in this client's allow-list.</summary>
        public bool CanUseDatabase(string database) =>
            !string.IsNullOrEmpty(database) && Databases.Contains(database);
    }
}
