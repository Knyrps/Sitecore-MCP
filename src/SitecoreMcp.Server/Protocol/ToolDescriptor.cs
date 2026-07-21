using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    public sealed class ToolDescriptor
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }

        [JsonProperty("outputSchema")]
        public JObject OutputSchema { get; set; }
    }
}
