using System.Collections.Generic;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport.Gates
{
    /// <summary>
    /// Restricts the endpoint to an allow-list of client IPs. X-Forwarded-For is consulted only
    /// when the immediate peer is itself a trusted proxy; otherwise the header is ignored, since
    /// an unconditionally trusted forwarding header would let any caller spoof its address.
    /// </summary>
    public sealed class ClientAddressGate : IRequestGate
    {
        private readonly ISet<string> _allowedAddresses;
        private readonly ISet<string> _trustedProxies;

        /// <summary>Creates the gate over the permitted client addresses and the proxies whose XFF we trust.</summary>
        public ClientAddressGate(ISet<string> allowedAddresses, ISet<string> trustedProxies)
        {
            _allowedAddresses = allowedAddresses;
            _trustedProxies = trustedProxies;
        }

        /// <inheritdoc />
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            if (_allowedAddresses == null || _allowedAddresses.Count == 0)
            {
                return GateResult.Pass;
            }

            if (_allowedAddresses.Contains(ResolveClientAddress(request)))
            {
                return GateResult.Pass;
            }

            return GateResult.Reject(403, JsonRpcErrorCodes.InvalidRequest, "Client address is not allowed.");
        }

        private string ResolveClientAddress(IMcpHttpRequest request)
        {
            var peer = request.RemoteAddress;

            if (_trustedProxies == null || !_trustedProxies.Contains(peer))
            {
                return peer;
            }

            var forwarded = request.GetHeader("X-Forwarded-For");
            if (string.IsNullOrEmpty(forwarded))
            {
                return peer;
            }

            // Left-most entry is the originating client the trusted proxy saw.
            var first = forwarded.Split(',')[0].Trim();
            return string.IsNullOrEmpty(first) ? peer : first;
        }
    }
}
