using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport.Gates
{
    /// <summary>
    /// Rejects a request whose declared Content-Length exceeds the cap. This is an early check
    /// on the header; the handler still reads through a bounded stream, since Content-Length can
    /// be absent or dishonest.
    /// </summary>
    public sealed class BodySizeGate : IRequestGate
    {
        private readonly long _maxRequestBytes;

        /// <summary>Creates the gate with the maximum accepted body size in bytes.</summary>
        public BodySizeGate(long maxRequestBytes)
        {
            _maxRequestBytes = maxRequestBytes;
        }

        /// <inheritdoc />
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            if (request.ContentLength > _maxRequestBytes)
            {
                return GateResult.Reject(413, JsonRpcErrorCodes.InvalidRequest, "Request body is too large.");
            }

            return GateResult.Pass;
        }
    }
}
