namespace SitecoreMcp.Server.Transport
{
    /// <summary>A single stateless check applied to a request before it is parsed or dispatched.</summary>
    public interface IRequestGate
    {
        /// <summary>Evaluates the request and returns <see cref="GateResult.Pass"/> or a rejection.</summary>
        GateResult Evaluate(IMcpHttpRequest request);
    }
}
