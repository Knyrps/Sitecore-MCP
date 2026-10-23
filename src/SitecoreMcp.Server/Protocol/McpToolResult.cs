using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>
    /// One content block in a tool result: a "text" block, or an "image" block carrying base64 data
    /// and its MIME type, as the MCP spec defines them. Members a block does not use are omitted.
    /// </summary>
    public sealed class McpContent
    {
        /// <summary>The content block type: "text" or "image".</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>The textual payload of a text block.</summary>
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        /// <summary>The base64-encoded bytes of an image block.</summary>
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        /// <summary>The MIME type of an image block, e.g. "image/png".</summary>
        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }

        /// <summary>Creates a text content block wrapping <paramref name="text"/>.</summary>
        public static McpContent FromText(string text) => new McpContent { Type = "text", Text = text };

        /// <summary>Creates an image content block from raw image bytes and their MIME type.</summary>
        public static McpContent FromImage(byte[] data, string mimeType) => new McpContent
        {
            Type = "image",
            Data = Convert.ToBase64String(data ?? throw new ArgumentNullException(nameof(data))),
            MimeType = string.IsNullOrEmpty(mimeType) ? throw new ArgumentException("An image needs a MIME type.", nameof(mimeType)) : mimeType
        };
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

        /// <summary>
        /// An image the client can pass to a model, followed by <paramref name="metadata"/> (what the
        /// image is, its size) as structured content and as text for clients that drop images or
        /// do not understand structured output.
        /// </summary>
        public static McpToolResult Image(byte[] data, string mimeType, JObject metadata)
        {
            var result = new McpToolResult { Content = { McpContent.FromImage(data, mimeType) } };
            if (metadata != null)
            {
                result.StructuredContent = metadata;
                result.Content.Add(McpContent.FromText(metadata.ToString(Formatting.None)));
            }

            return result;
        }

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
