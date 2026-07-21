using System;
using System.Collections.Generic;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Effective configuration for the endpoint, as a plain value object. A Sitecore-coupled
    /// loader populates it from config; the gates and handler read it, so they stay testable.
    /// </summary>
    public sealed class McpSettings
    {
        /// <summary>Whether the endpoint is registered and serving at all. Off by default in production.</summary>
        public bool Enabled { get; set; }

        /// <summary>The route the endpoint is served on, e.g. "sitecore/api/mcp".</summary>
        public string EndpointPath { get; set; } = "sitecore/api/mcp";

        /// <summary>Whether non-HTTPS requests are rejected. Only a dev instance should turn this off.</summary>
        public bool RequireHttps { get; set; } = true;

        /// <summary>Origins permitted to drive the endpoint. A request with an Origin outside this set is rejected.</summary>
        public ISet<string> AllowedOrigins { get; } = New();

        /// <summary>Client IPs permitted to reach the endpoint. Empty means no address restriction.</summary>
        public ISet<string> AllowedAddresses { get; } = New();

        /// <summary>Peer IPs whose X-Forwarded-For header we trust. Empty means the header is always ignored.</summary>
        public ISet<string> TrustedProxies { get; } = New();

        /// <summary>The maximum accepted request body size in bytes.</summary>
        public long MaxRequestBytes { get; set; } = 1024 * 1024;

        /// <summary>The global write switch. Off by default; a client's own AllowWrites is ANDed with this.</summary>
        public bool AllowWrites { get; set; }

        /// <summary>The maximum number of tool calls executed concurrently before further calls are shed.</summary>
        public int MaxConcurrentCalls { get; set; } = 4;

        /// <summary>The length beyond which a field value is truncated in tool output.</summary>
        public int MaxFieldLength { get; set; } = 2000;

        /// <summary>Whether full error detail is returned to the client. Should stay off outside development.</summary>
        public bool VerboseErrors { get; set; }

        /// <summary>The server name reported in the initialize response.</summary>
        public string ServerName { get; set; } = "SitecoreMcp";

        /// <summary>The server version reported in the initialize response.</summary>
        public string ServerVersion { get; set; } = "0.1.0";

        private static ISet<string> New() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
