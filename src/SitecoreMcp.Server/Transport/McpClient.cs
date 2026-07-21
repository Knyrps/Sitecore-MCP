using System;
using System.Collections.Generic;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// A configured API client: its secret key, the Sitecore user its calls run as, and the
    /// limits on what it may do. One client maps to exactly one Sitecore identity.
    /// </summary>
    public sealed class McpClient
    {
        /// <summary>Creates a client binding a key to a Sitecore user and its permissions.</summary>
        public McpClient(string key, string userName, bool allowWrites, IEnumerable<string> databases)
        {
            Key = key;
            UserName = userName;
            AllowWrites = allowWrites;
            Databases = new HashSet<string>(databases ?? new string[0], StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The resolved secret key this client presents, compared in constant time.</summary>
        public string Key { get; }

        /// <summary>The Sitecore user (e.g. "sitecore\mcp-agent") whose security context calls run under.</summary>
        public string UserName { get; }

        /// <summary>Whether this client may invoke write tools, subject also to the global write switch.</summary>
        public bool AllowWrites { get; }

        /// <summary>The databases this client may target; anything outside the set is refused before a tool runs.</summary>
        public ISet<string> Databases { get; }

        /// <summary>Whether the given database name is in this client's allow-list.</summary>
        public bool CanUseDatabase(string database) =>
            !string.IsNullOrEmpty(database) && Databases.Contains(database);
    }
}
