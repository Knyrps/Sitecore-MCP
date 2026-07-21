using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>One content block in a tool result. We only emit text blocks.</summary>
    public sealed class McpContent
    {
        /// <summary>The content block type, always "text" here.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>The textual payload of the block.</summary>
        [JsonProperty("text")]
        public string Text { get; set; }

        /// <summary>Creates a text content block wrapping <paramref name="text"/>.</summary>
        public static McpContent FromText(string text) => new McpContent { Type = "text", Text = text };
    }

    /// <summary>The result of a tools/call, carrying content blocks and optional structured output.</summary>
    public sealed class McpToolResult
    {
        /// <summary>The content blocks returned to the client; every result carries at least one.</summary>
        [JsonProperty("content")]
        public List<McpContent> Content { get; set; } = new List<McpContent>();

        /// <summary>The structured payload for clients that understand it; null when not applicable.</summary>
        [JsonProperty("structuredContent")]
        public JObject StructuredContent { get; set; }

        /// <summary>True when the tool failed in a way the model should see and can act on.</summary>
        [JsonProperty("isError")]
        public bool IsError { get; set; }

        /// <summary>
        /// Emits the payload twice: structured for clients that understand 2025-06-18, and
        /// serialized text for those that do not.
        /// </summary>
        public static McpToolResult Structured(JObject payload) => new McpToolResult
        {
            StructuredContent = payload,
            Content = { McpContent.FromText(payload.ToString(Formatting.None)) }
        };

        /// <summary>A plain-text success result with no structured payload.</summary>
        public static McpToolResult Text(string text) => new McpToolResult
        {
            Content = { McpContent.FromText(text) }
        };

        /// <summary>
        /// A failure the model should see and can act on. Deliberately not a JSON-RPC error:
        /// those are reserved for protocol faults the model cannot recover from.
        /// </summary>
        public static McpToolResult Failure(string message) => new McpToolResult
        {
            IsError = true,
            Content = { McpContent.FromText(message) }
        };
    }
}
