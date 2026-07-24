using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// The protocol layer's view of the tools for one call. Write tools are hidden and refused when
    /// the caller lacks write permission, and every failure is turned into an isError result rather
    /// than escaping as an exception.
    /// </summary>
    public sealed class RequestToolCatalog : IToolCatalog
    {
        private readonly IReadOnlyDictionary<string, IMcpTool> _tools;
        private readonly IReadOnlyDictionary<string, bool> _requiresAdmin;
        private readonly McpCallContext _context;

        /// <summary>Creates the catalog over the registered tools, their resolved admin requirements, and the call context.</summary>
        public RequestToolCatalog(
            IReadOnlyDictionary<string, IMcpTool> tools,
            IReadOnlyDictionary<string, bool> requiresAdmin,
            McpCallContext context)
        {
            _tools = tools;
            _requiresAdmin = requiresAdmin;
            _context = context;
        }

        /// <inheritdoc />
        public IReadOnlyList<ToolDescriptor> List()
        {
            var descriptors = new List<ToolDescriptor>();
            foreach (var tool in _tools.Values)
            {
                if (!IsVisible(tool))
                {
                    continue;
                }

                descriptors.Add(new ToolDescriptor
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = JsonSchemaGenerator.Generate(tool.ArgumentType)
                });
            }

            return descriptors;
        }

        /// <inheritdoc />
        public bool Contains(string name) =>
            name != null && _tools.TryGetValue(name, out var tool) && IsVisible(tool);

        /// <inheritdoc />
        public McpToolResult Invoke(string name, JObject arguments)
        {
            if (name == null || !_tools.TryGetValue(name, out var tool) || !IsVisible(tool))
            {
                return McpToolResult.Failure($"Unknown tool '{name}'.");
            }

            try
            {
                return tool.Invoke(arguments, _context);
            }
            catch (McpToolException ex)
            {
                return McpToolResult.Failure(ex.Message);
            }
            catch (System.Exception ex)
            {
                // Unexpected: log the full detail, return something safe unless verbose is on.
                McpLog.Error($"Tool '{name}' failed.", ex);
                return McpToolResult.Failure(_context.Settings.VerboseErrors
                    ? ex.ToString()
                    : "The tool failed unexpectedly. See the Sitecore log.");
            }
        }

        /// <summary>
        /// A tool is invisible to a caller that lacks its required write permission or, when it needs
        /// an administrator, is not running as one. Hidden tools are also refused, so the gate holds
        /// whether or not the client consults the list first.
        /// </summary>
        private bool IsVisible(IMcpTool tool)
        {
            if (tool.RequiresWrite && !_context.AllowWrites)
            {
                return false;
            }

            if (RequiresAdmin(tool) && !_context.IsAdministrator)
            {
                return false;
            }

            return true;
        }

        /// <summary>Whether a tool requires an administrator, as resolved at registration (default plus any config override).</summary>
        private bool RequiresAdmin(IMcpTool tool) =>
            _requiresAdmin.TryGetValue(tool.Name, out var requiresAdmin) ? requiresAdmin : tool.RequiresAdmin;
    }
}
