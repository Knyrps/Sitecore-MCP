using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport.Gates
{
    /// <summary>Rejects plaintext requests unless the instance has explicitly opted out of HTTPS.</summary>
    public sealed class HttpsGate : IRequestGate
    {
        private readonly bool _requireHttps;

        /// <summary>Creates the gate; pass false only for an HTTP-only development instance.</summary>
        public HttpsGate(bool requireHttps)
        {
            _requireHttps = requireHttps;
        }

        /// <inheritdoc />
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            if (_requireHttps && !request.IsSecureConnection)
            {
                return GateResult.Reject(403, JsonRpcErrorCodes.InvalidRequest, "HTTPS is required.");
            }

            return GateResult.Pass;
        }
    }
}
