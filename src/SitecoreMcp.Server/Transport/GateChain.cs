using System.Collections.Generic;
using SitecoreMcp.Server.Transport.Gates;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// The ordered set of stateless gates every request clears before it is read or dispatched.
    /// Order is deliberate: cheap transport checks first, then framing checks. Stateful concerns
    /// (auth, rate limiting, protocol version, concurrency) run in the handler after the body is read.
    /// </summary>
    public sealed class GateChain
    {
        private readonly IReadOnlyList<IRequestGate> _gates;

        /// <summary>Creates a chain from an explicit, ordered list of gates.</summary>
        public GateChain(IReadOnlyList<IRequestGate> gates)
        {
            _gates = gates;
        }

        /// <summary>Builds the standard chain in canonical order from the given settings.</summary>
        public static GateChain FromSettings(McpSettings settings) => new GateChain(new IRequestGate[]
        {
            new HttpsGate(settings.RequireHttps),
            new OriginGate(settings.AllowedOrigins),
            new ClientAddressGate(settings.AllowedAddresses, settings.TrustedProxies),
            new BodySizeGate(settings.MaxRequestBytes),
            new MediaTypeGate()
        });

        /// <summary>Runs the gates in order and returns the first rejection, or <see cref="GateResult.Pass"/>.</summary>
        public GateResult Evaluate(IMcpHttpRequest request)
        {
            foreach (var gate in _gates)
            {
                var result = gate.Evaluate(request);
                if (!result.Passed)
                {
                    return result;
                }
            }

            return GateResult.Pass;
        }
    }
}
