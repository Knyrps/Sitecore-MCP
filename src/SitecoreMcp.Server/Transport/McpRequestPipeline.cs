using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Runs one request through the ordered chain: method check, stateless gates, authentication,
    /// rate limiting, parsing, protocol-version check, a concurrency limit, and finally dispatch
    /// under the client's Sitecore identity. Every path returns an <see cref="McpResponse"/>.
    /// </summary>
    public sealed class McpRequestPipeline
    {
        private readonly McpSettings _settings;
        private readonly GateChain _gates;
        private readonly McpAuthenticator _authenticator;
        private readonly RateLimiter _rateLimiter;
        private readonly McpToolRegistry _registry;
        private readonly SemaphoreSlim _concurrency;

        /// <summary>Creates the pipeline from its collaborators; the concurrency limit comes from settings.</summary>
        public McpRequestPipeline(
            McpSettings settings,
            GateChain gates,
            McpAuthenticator authenticator,
            RateLimiter rateLimiter,
            McpToolRegistry registry)
        {
            _settings = settings;
            _gates = gates;
            _authenticator = authenticator;
            _rateLimiter = rateLimiter;
            _registry = registry;
            _concurrency = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentCalls));
        }

        /// <summary>Handles a request whose body has already been read within the size limit.</summary>
        public McpResponse Handle(IMcpHttpRequest request, string body)
        {
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal))
            {
                return Error(405, JsonRpcErrorCodes.InvalidRequest, "Only POST is supported.",
                    Header("Allow", "POST"));
            }

            var gate = _gates.Evaluate(request);
            if (!gate.Passed)
            {
                return McpResponse.FromOutcome(gate.ToOutcome());
            }

            var client = _authenticator.Authenticate(request);
            if (client == null)
            {
                McpLog.Warn($"Rejected unauthenticated request from {request.RemoteAddress}.");
                return Error(401, JsonRpcErrorCodes.InvalidRequest, "Authentication is required.",
                    Header("WWW-Authenticate", "Bearer"));
            }

            var limit = _rateLimiter.TryAcquire(client.UserName);
            if (!limit.Allowed)
            {
                return Error(429, JsonRpcErrorCodes.InvalidRequest, "Rate limit exceeded.",
                    Header("Retry-After", limit.RetryAfterSeconds.ToString()));
            }

            if (!McpRequestEnvelope.TryParse(body, out var envelope, out var parseFailure))
            {
                return McpResponse.FromOutcome(parseFailure);
            }

            if (!string.Equals(envelope.Method, "initialize", StringComparison.Ordinal))
            {
                var version = request.GetHeader(McpHeaders.ProtocolVersion);
                if (!McpProtocolVersions.IsSupported(version))
                {
                    return Error(400, JsonRpcErrorCodes.InvalidRequest,
                        $"Missing or unsupported {McpHeaders.ProtocolVersion} header.");
                }
            }

            if (!_concurrency.Wait(0))
            {
                return Error(503, JsonRpcErrorCodes.InternalError, "The server is busy; retry shortly.",
                    Header("Retry-After", "1"));
            }

            try
            {
                return Execute(client, envelope);
            }
            finally
            {
                _concurrency.Release();
            }
        }

        private McpResponse Execute(McpClient client, McpRequestEnvelope envelope)
        {
            if (!User.Exists(client.UserName))
            {
                McpLog.Error($"Configured user '{client.UserName}' does not exist.");
                return Error(500, JsonRpcErrorCodes.InternalError, "The configured MCP user does not exist.");
            }

            var user = User.FromName(client.UserName, true);
            var stopwatch = Stopwatch.StartNew();
            DispatchOutcome outcome;

            using (new UserSwitcher(user))
            {
                var context = new McpCallContext(client, user, _settings);
                var catalog = _registry.CreateCatalog(context);
                var dispatcher = new McpDispatcher(catalog, _settings.ServerName, _settings.ServerVersion);
                outcome = dispatcher.Dispatch(envelope);
            }

            stopwatch.Stop();
            Audit(client, envelope, outcome, stopwatch.ElapsedMilliseconds);
            return McpResponse.FromOutcome(outcome);
        }

        private static void Audit(McpClient client, McpRequestEnvelope envelope, DispatchOutcome outcome, long elapsedMs)
        {
            var target = envelope.Method == "tools/call"
                ? envelope.Params?.Value<string>("name") ?? "(unknown)"
                : envelope.Method;

            McpLog.Audit(
                $"user={client.UserName} method={envelope.Method} target={target} " +
                $"status={outcome.StatusCode} durationMs={elapsedMs}");
        }

        private static IReadOnlyDictionary<string, string> Header(string name, string value) =>
            new Dictionary<string, string> { [name] = value };

        private static McpResponse Error(int status, int code, string message,
            IReadOnlyDictionary<string, string> headers = null) =>
            new McpResponse(status, McpJson.Serialize(JsonRpcResponse.Failure(null, code, message)), headers);
    }
}
