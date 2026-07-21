using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sitecore.Diagnostics;
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
        private readonly McpCallContext _context;

        /// <summary>Creates the catalog over the registered tools and the current call context.</summary>
        public RequestToolCatalog(IReadOnlyDictionary<string, IMcpTool> tools, McpCallContext context)
        {
            _tools = tools;
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
                Log.Error($"[SitecoreMcp] Tool '{name}' failed.", ex, this);
                return McpToolResult.Failure(_context.Settings.VerboseErrors
                    ? ex.ToString()
                    : "The tool failed unexpectedly. See the Sitecore log.");
            }
        }

        /// <summary>A write tool is invisible to a caller without effective write permission.</summary>
        private bool IsVisible(IMcpTool tool) => !tool.RequiresWrite || _context.AllowWrites;
    }
}
