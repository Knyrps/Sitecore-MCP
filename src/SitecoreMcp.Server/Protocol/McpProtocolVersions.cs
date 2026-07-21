using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Protocol
{
    public static class McpProtocolVersions
    {
        public const string V20250618 = "2025-06-18";
        public const string V20250326 = "2025-03-26";

        public const string Preferred = V20250618;

        public static readonly IReadOnlyList<string> Supported = new[] { V20250618, V20250326 };

        public static bool IsSupported(string version) =>
            !string.IsNullOrEmpty(version) &&
            Supported.Contains(version, StringComparer.Ordinal);

        /// <summary>Echo the client's version when we speak it, otherwise answer with ours.</summary>
        public static string Negotiate(string requested) =>
            IsSupported(requested) ? requested : Preferred;
    }
}
