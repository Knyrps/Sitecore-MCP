using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>The verdict of a single gate: either the request passes or it is rejected with an HTTP status.</summary>
    public sealed class GateResult
    {
        /// <summary>The shared "request may continue" verdict.</summary>
        public static readonly GateResult Pass = new GateResult(true, 0, 0, null);

        private readonly int _errorCode;
        private readonly string _message;

        private GateResult(bool passed, int statusCode, int errorCode, string message)
        {
            Passed = passed;
            StatusCode = statusCode;
            _errorCode = errorCode;
            _message = message;
        }

        /// <summary>Whether the request cleared this gate.</summary>
        public bool Passed { get; }

        /// <summary>The HTTP status to return when the request was rejected.</summary>
        public int StatusCode { get; }

        /// <summary>Builds a rejection pairing an HTTP status with a JSON-RPC error code and message.</summary>
        public static GateResult Reject(int statusCode, int errorCode, string message) =>
            new GateResult(false, statusCode, errorCode, message);

        /// <summary>Converts a rejection into the outcome the transport writes back.</summary>
        public DispatchOutcome ToOutcome() =>
            DispatchOutcome.Error(StatusCode, JsonRpcResponse.Failure(null, _errorCode, _message));
    }
}
