using System;
using System.Collections.Generic;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// The immutable set of tools registered at startup from config. A request-scoped catalog is
    /// created from it per call, so the registry itself is built once and shared.
    /// </summary>
    public sealed class McpToolRegistry
    {
        private readonly Dictionary<string, IMcpTool> _tools =
            new Dictionary<string, IMcpTool>(StringComparer.Ordinal);

        /// <summary>Registers a tool. Called by Sitecore config binding; a duplicate name is rejected.</summary>
        public void AddTool(IMcpTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            if (string.IsNullOrEmpty(tool.Name)) throw new ArgumentException("Tool name is required.", nameof(tool));
            if (_tools.ContainsKey(tool.Name)) throw new ArgumentException($"Duplicate tool name '{tool.Name}'.", nameof(tool));

            _tools.Add(tool.Name, tool);
        }

        /// <summary>All registered tools, regardless of the caller's permissions.</summary>
        public IReadOnlyCollection<IMcpTool> Tools => _tools.Values;

        /// <summary>Creates a catalog scoped to one call's context, filtering and executing on its behalf.</summary>
        public RequestToolCatalog CreateCatalog(McpCallContext context) =>
            new RequestToolCatalog(_tools, context);
    }
}
