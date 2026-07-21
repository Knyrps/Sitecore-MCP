using System;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport.Gates
{
    /// <summary>
    /// Requires a JSON request body and an Accept header that admits a JSON response. Since we
    /// only ever reply with application/json, a client that cannot accept it is rejected up front.
    /// </summary>
    public sealed class MediaTypeGate : IRequestGate
    {
        private const string Json = "application/json";

        /// <inheritdoc />
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            var contentType = request.GetHeader("Content-Type");
            if (!IsJsonContentType(contentType))
            {
                return GateResult.Reject(415, JsonRpcErrorCodes.InvalidRequest, "Content-Type must be application/json.");
            }

            if (!AcceptsJson(request.GetHeader("Accept")))
            {
                return GateResult.Reject(406, JsonRpcErrorCodes.InvalidRequest, "Accept must allow application/json.");
            }

            return GateResult.Pass;
        }

        private static bool IsJsonContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return false;

            // Strip any parameters such as "; charset=utf-8".
            var mediaType = contentType.Split(';')[0].Trim();
            return mediaType.Equals(Json, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AcceptsJson(string accept)
        {
            // Absent Accept means the client accepts anything.
            if (string.IsNullOrEmpty(accept)) return true;

            foreach (var part in accept.Split(','))
            {
                var mediaType = part.Split(';')[0].Trim();
                if (mediaType.Equals(Json, StringComparison.OrdinalIgnoreCase) ||
                    mediaType == "*/*" ||
                    mediaType.Equals("application/*", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
