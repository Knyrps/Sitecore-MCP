using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>The MCP protocol versions this server speaks and the rules for negotiating one.</summary>
    public static class McpProtocolVersions
    {
        /// <summary>The 2025-06-18 protocol revision.</summary>
        public const string V20250618 = "2025-06-18";

        /// <summary>The 2025-03-26 protocol revision.</summary>
        public const string V20250326 = "2025-03-26";

        /// <summary>The version we answer with when the client requests one we do not support.</summary>
        public const string Preferred = V20250618;

        /// <summary>All protocol versions this server accepts, newest first.</summary>
        public static readonly IReadOnlyList<string> Supported = new[] { V20250618, V20250326 };

        /// <summary>Whether the given version string is one this server supports.</summary>
        public static bool IsSupported(string version) =>
            !string.IsNullOrEmpty(version) &&
            Supported.Contains(version, StringComparer.Ordinal);

        /// <summary>Echoes the client's version when we speak it, otherwise answers with ours.</summary>
        public static string Negotiate(string requested) =>
            IsSupported(requested) ? requested : Preferred;
    }
}
