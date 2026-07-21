using System;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// One Sitecore operation exposed to the model. Registered through config, so a solution can
    /// add its own tools without recompiling this assembly.
    /// </summary>
    public interface IMcpTool
    {
        /// <summary>The unique tool name the client calls, e.g. "sitecore_get_item".</summary>
        string Name { get; }

        /// <summary>A description written for the model, explaining when and how to use the tool.</summary>
        string Description { get; }

        /// <summary>The argument POCO type, used to generate the input schema and bind arguments.</summary>
        Type ArgumentType { get; }

        /// <summary>Whether the tool mutates content and so requires write permission to appear and run.</summary>
        bool RequiresWrite { get; }

        /// <summary>Binds the raw arguments and runs the tool under the given context.</summary>
        McpToolResult Invoke(JObject arguments, McpCallContext context);
    }
}
