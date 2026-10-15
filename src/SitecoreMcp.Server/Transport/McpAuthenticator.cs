using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Resolves the API key on a request to a configured client. Comparison is constant-time and
    /// every configured client is checked, so neither the key value nor which client matched leaks
    /// through timing. A client configured to require an editor token is then resolved to the editor
    /// its token names; such a client has no fixed user to fall back to.
    /// </summary>
    public sealed class McpAuthenticator
    {
        private readonly IReadOnlyList<McpClient> _clients;
        private readonly EditorTokenService _editorTokens;

        /// <summary>
        /// Creates an authenticator over the configured set of clients. Without an editor-token service
        /// (no signing secret configured) every editor-token client is refused: fail closed.
        /// </summary>
        public McpAuthenticator(IReadOnlyList<McpClient> clients, EditorTokenService editorTokens = null)
        {
            _clients = clients ?? new McpClient[0];
            _editorTokens = editorTokens;
        }

        /// <summary>
        /// Authenticates a request end to end: resolves the key to a client, then the Sitecore user the
        /// call runs as. A fixed-user client runs as its user and any editor token it sends is ignored;
        /// an editor-token client runs as the editor named by a valid token issued for its audience, and
        /// is refused otherwise.
        /// </summary>
        public McpAuthentication AuthenticateCall(IMcpHttpRequest request)
        {
            var client = Authenticate(request);
            if (client == null)
            {
                return McpAuthentication.Unauthenticated();
            }

            if (!client.RequiresEditorToken)
            {
                return McpAuthentication.AsClientUser(client);
            }

            if (_editorTokens == null)
            {
                return McpAuthentication.EditorTokenRefused(client, EditorTokenFailure.NotConfigured);
            }

            var validation = _editorTokens.Validate(request.GetHeader(McpHeaders.EditorToken), client.EditorTokenAudience);
            return validation.Succeeded
                ? McpAuthentication.AsEditor(client, validation.UserName)
                : McpAuthentication.EditorTokenRefused(client, validation.Failure);
        }

        /// <summary>Returns the client whose key matches the request, or null when the key is missing or wrong. Does not check editor tokens.</summary>
        public McpClient Authenticate(IMcpHttpRequest request)
        {
            var presented = ExtractKey(request);
            if (string.IsNullOrEmpty(presented))
            {
                return null;
            }

            McpClient matched = null;
            foreach (var client in _clients)
            {
                // No early break: comparing against every client keeps timing independent of which matched.
                if (FixedTimeComparer.Equals(client.Key, presented))
                {
                    matched = client;
                }
            }

            return matched;
        }

        private static string ExtractKey(IMcpHttpRequest request)
        {
            var authorization = request.GetHeader("Authorization");
            if (!string.IsNullOrEmpty(authorization) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authorization.Substring("Bearer ".Length).Trim();
            }

            return request.GetHeader("X-Mcp-Key");
        }
    }
}
