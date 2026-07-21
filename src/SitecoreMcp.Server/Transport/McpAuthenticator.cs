using System;
using System.Collections.Generic;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Resolves the API key on a request to a configured client. Comparison is constant-time and
    /// every configured client is checked, so neither the key value nor which client matched leaks
    /// through timing.
    /// </summary>
    public sealed class McpAuthenticator
    {
        private readonly IReadOnlyList<McpClient> _clients;

        /// <summary>Creates an authenticator over the configured set of clients.</summary>
        public McpAuthenticator(IReadOnlyList<McpClient> clients)
        {
            _clients = clients ?? new McpClient[0];
        }

        /// <summary>Returns the client whose key matches the request, or null when authentication fails.</summary>
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
