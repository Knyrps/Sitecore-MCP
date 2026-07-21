using System.Collections.Generic;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport.Gates
{
    /// <summary>
    /// Rejects requests whose Origin header is not allow-listed. This is the DNS-rebinding
    /// defence: a page in a browser cannot drive a locally-bound endpoint it was not granted.
    /// A request with no Origin (a non-browser client) is allowed through to authentication.
    /// </summary>
    public sealed class OriginGate : IRequestGate
    {
        private readonly ISet<string> _allowedOrigins;

        /// <summary>Creates the gate over the configured set of permitted origins.</summary>
        public OriginGate(ISet<string> allowedOrigins)
        {
            _allowedOrigins = allowedOrigins;
        }

        /// <inheritdoc />
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            var origin = request.GetHeader("Origin");
            if (string.IsNullOrEmpty(origin))
            {
                return GateResult.Pass;
            }

            if (_allowedOrigins != null && _allowedOrigins.Contains(origin))
            {
                return GateResult.Pass;
            }

            return GateResult.Reject(403, JsonRpcErrorCodes.InvalidRequest, "Origin is not allowed.");
        }
    }
}
