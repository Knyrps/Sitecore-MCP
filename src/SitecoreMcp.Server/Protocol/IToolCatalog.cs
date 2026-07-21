using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>
    /// The dispatcher's only view of the tools. Deliberately free of Sitecore types: the
    /// implementation closes over the resolved user, database, and write permission, so the
    /// protocol layer never sees them and stays testable without an instance.
    /// </summary>
    public interface IToolCatalog
    {
        /// <summary>Tools the current caller may use. Tools they cannot use must not appear.</summary>
        IReadOnlyList<ToolDescriptor> List();

        /// <summary>Whether a tool with the given name is available to the current caller.</summary>
        bool Contains(string name);

        /// <summary>
        /// Runs the named tool with the given arguments. Must not throw: execution failures
        /// belong in an <see cref="McpToolResult.IsError"/> result.
        /// </summary>
        McpToolResult Invoke(string name, JObject arguments);
    }
}
