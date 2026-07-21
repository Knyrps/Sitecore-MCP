using System.Collections.Generic;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>The full HTTP response the pipeline produces: status, optional headers, and an optional body.</summary>
    public sealed class McpResponse
    {
        /// <summary>The HTTP status code to write.</summary>
        public int StatusCode { get; }

        /// <summary>The response body, or null to write nothing (as for an acknowledged notification).</summary>
        public string Body { get; }

        /// <summary>Extra response headers such as Allow, Retry-After, or WWW-Authenticate.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Creates a response with the given status, body, and optional headers.</summary>
        public McpResponse(int statusCode, string body, IReadOnlyDictionary<string, string> headers = null)
        {
            StatusCode = statusCode;
            Body = body;
            Headers = headers ?? Empty;
        }

        /// <summary>Wraps a dispatch outcome, adding any headers the transport needs to attach.</summary>
        public static McpResponse FromOutcome(DispatchOutcome outcome, IReadOnlyDictionary<string, string> headers = null) =>
            new McpResponse(outcome.StatusCode, outcome.Body, headers);

        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    }
}
