using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    public sealed class McpContent
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        public static McpContent FromText(string text) => new McpContent { Type = "text", Text = text };
    }

    public sealed class McpToolResult
    {
        [JsonProperty("content")]
        public List<McpContent> Content { get; set; } = new List<McpContent>();

        [JsonProperty("structuredContent")]
        public JObject StructuredContent { get; set; }

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
