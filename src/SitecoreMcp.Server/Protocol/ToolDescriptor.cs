using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>One tool as advertised in a tools/list response.</summary>
    public sealed class ToolDescriptor
    {
        /// <summary>The unique tool name the client passes to tools/call.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>An optional human-friendly display name for the tool.</summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>A description written for the model, explaining when and how to use the tool.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>The JSON Schema describing the tool's arguments.</summary>
        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }

        /// <summary>The JSON Schema describing the tool's structured result, when its shape is stable.</summary>
        [JsonProperty("outputSchema")]
        public JObject OutputSchema { get; set; }
    }
}
