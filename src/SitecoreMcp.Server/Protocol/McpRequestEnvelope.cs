using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    /// <summary>
    /// One parsed JSON-RPC message. Parsing is separate from dispatch so the transport can gate
    /// on the method name (and log it) before anything is executed.
    /// </summary>
    public sealed class McpRequestEnvelope
    {
        public string Method { get; private set; }
        public JToken Id { get; private set; }
        public JObject Params { get; private set; }

        /// <summary>A message with no "id" member. Never produces a response body.</summary>
        public bool IsNotification { get; private set; }

        public static bool TryParse(string body, out McpRequestEnvelope envelope, out DispatchOutcome failure)
        {
            envelope = null;
            failure = null;

            JToken token;
            try
            {
                token = string.IsNullOrWhiteSpace(body) ? null : JToken.Parse(body);
            }
            catch (JsonReaderException ex)
            {
                failure = Reject(JsonRpcErrorCodes.ParseError, "Invalid JSON: " + ex.Message);
                return false;
            }

            if (token == null)
            {
                failure = Reject(JsonRpcErrorCodes.ParseError, "Empty request body.");
                return false;
            }

            // JSON-RPC batching was removed in MCP 2025-06-18.
            if (token.Type == JTokenType.Array)
            {
                failure = Reject(JsonRpcErrorCodes.InvalidRequest, "Batched requests are not supported.");
                return false;
            }

            if (!(token is JObject message))
            {
                failure = Reject(JsonRpcErrorCodes.InvalidRequest, "A JSON-RPC message must be an object.");
                return false;
            }

            var version = message.Value<string>("jsonrpc");
            if (version != "2.0")
            {
                failure = Reject(JsonRpcErrorCodes.InvalidRequest, "Expected \"jsonrpc\": \"2.0\".");
                return false;
            }

            var method = message.Value<string>("method");
            if (string.IsNullOrEmpty(method))
            {
                failure = Reject(JsonRpcErrorCodes.InvalidRequest, "Missing \"method\".");
                return false;
            }

            var hasId = message.TryGetValue("id", out var id);
            if (hasId && id.Type != JTokenType.String && id.Type != JTokenType.Integer && id.Type != JTokenType.Null)
            {
                failure = Reject(JsonRpcErrorCodes.InvalidRequest, "\"id\" must be a string, number, or null.");
                return false;
            }

            envelope = new McpRequestEnvelope
            {
                Method = method,
                Id = hasId ? id : null,
                IsNotification = !hasId,
                Params = message["params"] as JObject
            };
            return true;
        }

        private static DispatchOutcome Reject(int code, string message) =>
            DispatchOutcome.Error(400, JsonRpcResponse.Failure(null, code, message));
    }
}
