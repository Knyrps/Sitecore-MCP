using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// The outcome of authenticating one request: which client sent it and which Sitecore user the
    /// call runs as, or why it was refused.
    /// </summary>
    public sealed class McpAuthentication
    {
        private McpAuthentication(McpClient client, string userName, bool isEditor, EditorTokenFailure editorTokenFailure)
        {
            Client = client;
            UserName = userName;
            IsEditor = isEditor;
            EditorTokenFailure = editorTokenFailure;
        }

        /// <summary>Whether the request may proceed.</summary>
        public bool Succeeded => Client != null && UserName != null;

        /// <summary>The client whose key matched, or null when the key was missing or wrong.</summary>
        public McpClient Client { get; }

        /// <summary>The effective Sitecore user the call runs as; null on failure.</summary>
        public string UserName { get; }

        /// <summary>Whether the identity came from an editor token rather than the client's fixed user.</summary>
        public bool IsEditor { get; }

        /// <summary>
        /// Why an editor-token client's token was refused; <see cref="Security.EditorTokenFailure.None"/>
        /// when the key itself failed or the client does not use editor tokens.
        /// </summary>
        public EditorTokenFailure EditorTokenFailure { get; }

        internal static McpAuthentication Unauthenticated() =>
            new McpAuthentication(null, null, false, EditorTokenFailure.None);

        internal static McpAuthentication AsClientUser(McpClient client) =>
            new McpAuthentication(client, client.UserName, false, EditorTokenFailure.None);

        internal static McpAuthentication AsEditor(McpClient client, string userName) =>
            new McpAuthentication(client, userName, true, EditorTokenFailure.None);

        internal static McpAuthentication EditorTokenRefused(McpClient client, EditorTokenFailure failure) =>
            new McpAuthentication(client, null, false, failure);
    }
}
