namespace SitecoreMcp.Server.Protocol
{
    /// <summary>HTTP-shaped result of handling one message. A null <see cref="Body"/> means write nothing.</summary>
    public sealed class DispatchOutcome
    {
        /// <summary>The HTTP status code the transport should return.</summary>
        public int StatusCode { get; }

        /// <summary>The response body to write, or null to write no body at all.</summary>
        public string Body { get; }

        private DispatchOutcome(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        /// <summary>A 200 response carrying a serialized JSON-RPC message.</summary>
        public static DispatchOutcome Json(JsonRpcResponse response) =>
            new DispatchOutcome(200, McpJson.Serialize(response));

        /// <summary>Acknowledges a notification with 202 and no body, per the Streamable HTTP transport.</summary>
        public static DispatchOutcome Accepted() => new DispatchOutcome(202, null);

        /// <summary>An error response pairing a non-200 status with a serialized JSON-RPC error body.</summary>
        public static DispatchOutcome Error(int statusCode, JsonRpcResponse response) =>
            new DispatchOutcome(statusCode, McpJson.Serialize(response));
    }
}
