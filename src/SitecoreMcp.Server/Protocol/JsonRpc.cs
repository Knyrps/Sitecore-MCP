using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>The standard JSON-RPC 2.0 error codes we return for protocol-level faults.</summary>
    public static class JsonRpcErrorCodes
    {
        /// <summary>The request body was not well-formed JSON.</summary>
        public const int ParseError = -32700;

        /// <summary>The JSON was valid but not a valid JSON-RPC request object.</summary>
        public const int InvalidRequest = -32600;

        /// <summary>The requested method does not exist.</summary>
        public const int MethodNotFound = -32601;

        /// <summary>The method exists but the supplied parameters are invalid.</summary>
        public const int InvalidParams = -32602;

        /// <summary>An unexpected server-side error occurred while handling the request.</summary>
        public const int InternalError = -32603;
    }

    /// <summary>Shared JSON serialization settings and helpers for the protocol layer.</summary>
    public static class McpJson
    {
        /// <summary>Serializer settings used for every protocol payload: compact, null-omitting, and date-agnostic.</summary>
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateParseHandling = DateParseHandling.None
        };

        /// <summary>Serializes a value to a compact JSON string using <see cref="Settings"/>.</summary>
        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
    }

    /// <summary>The error member of a failed JSON-RPC response.</summary>
    public sealed class JsonRpcError
    {
        /// <summary>One of the <see cref="JsonRpcErrorCodes"/> values identifying the fault.</summary>
        [JsonProperty("code")]
        public int Code { get; set; }

        /// <summary>A short human-readable description of the fault.</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>Optional structured detail about the fault, omitted when absent.</summary>
        [JsonProperty("data")]
        public JToken Data { get; set; }
    }

    /// <summary>A JSON-RPC 2.0 response carrying either a result or an error, never both.</summary>
    public sealed class JsonRpcResponse
    {
        /// <summary>The JSON-RPC protocol version, always "2.0".</summary>
        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        /// <summary>The id echoed from the request, or null when it could not be determined.</summary>
        [JsonProperty("id")]
        public JToken Id { get; set; }

        /// <summary>The successful result payload; null on failure.</summary>
        [JsonProperty("result")]
        public JToken Result { get; set; }

        /// <summary>The error detail; null on success.</summary>
        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }

        /// <summary>Builds a successful response echoing <paramref name="id"/> and carrying <paramref name="result"/>.</summary>
        public static JsonRpcResponse Success(JToken id, object result) => new JsonRpcResponse
        {
            Id = id ?? JValue.CreateNull(),
            Result = result == null ? new JObject() : JToken.FromObject(result, JsonSerializer.Create(McpJson.Settings))
        };

        /// <summary>Builds an error response with the given code, message, and optional structured data.</summary>
        public static JsonRpcResponse Failure(JToken id, int code, string message, JToken data = null) => new JsonRpcResponse
        {
            Id = id ?? JValue.CreateNull(),
            Error = new JsonRpcError { Code = code, Message = message, Data = data }
        };
    }
}
