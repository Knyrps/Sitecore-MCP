using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    public static class JsonRpcErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }

    public static class McpJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateParseHandling = DateParseHandling.None
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
    }

    public sealed class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public JToken Data { get; set; }
    }

    public sealed class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("id")]
        public JToken Id { get; set; }

        [JsonProperty("result")]
        public JToken Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }

        public static JsonRpcResponse Success(JToken id, object result) => new JsonRpcResponse
        {
            Id = id ?? JValue.CreateNull(),
            Result = result == null ? new JObject() : JToken.FromObject(result, JsonSerializer.Create(McpJson.Settings))
        };

        public static JsonRpcResponse Failure(JToken id, int code, string message, JToken data = null) => new JsonRpcResponse
        {
            Id = id ?? JValue.CreateNull(),
            Error = new JsonRpcError { Code = code, Message = message, Data = data }
        };
    }
}
