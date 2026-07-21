namespace SitecoreMcp.Server.Protocol
{
    /// <summary>HTTP-shaped result of handling one message. A null <see cref="Body"/> means write nothing.</summary>
    public sealed class DispatchOutcome
    {
        public int StatusCode { get; }
        public string Body { get; }

        private DispatchOutcome(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public static DispatchOutcome Json(JsonRpcResponse response) =>
            new DispatchOutcome(200, McpJson.Serialize(response));

        /// <summary>Notifications are acknowledged with no body, per the Streamable HTTP transport.</summary>
        public static DispatchOutcome Accepted() => new DispatchOutcome(202, null);

        public static DispatchOutcome Error(int statusCode, JsonRpcResponse response) =>
            new DispatchOutcome(statusCode, McpJson.Serialize(response));
    }
}
