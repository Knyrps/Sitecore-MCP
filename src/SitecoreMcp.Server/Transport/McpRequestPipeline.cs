using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Runs one request through the ordered chain: method check, stateless gates, authentication,
    /// rate limiting, parsing, protocol-version check, a concurrency limit, and finally dispatch
    /// under the call's effective Sitecore identity - the client's fixed user, or for an editor-token
    /// client the editor its token names. Every path returns an <see cref="McpResponse"/>.
    /// </summary>
    public sealed class McpRequestPipeline
    {
        private readonly McpSettings _settings;
        private readonly GateChain _gates;
        private readonly McpAuthenticator _authenticator;
        private readonly RateLimiter _rateLimiter;
        private readonly McpToolRegistry _registry;
        private readonly SemaphoreSlim _concurrency;
        private readonly IContentScopeProvider _scopeProvider;

        /// <summary>Creates the pipeline from its collaborators; the concurrency limit comes from settings.</summary>
        public McpRequestPipeline(
            McpSettings settings,
            GateChain gates,
            McpAuthenticator authenticator,
            RateLimiter rateLimiter,
            McpToolRegistry registry,
            IContentScopeProvider scopeProvider = null)
        {
            _settings = settings;
            _gates = gates;
            _authenticator = authenticator;
            _rateLimiter = rateLimiter;
            _registry = registry;
            _concurrency = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentCalls));
            _scopeProvider = scopeProvider ?? new ConfiguredContentScopeProvider(null);
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

            var authentication = _authenticator.AuthenticateCall(request);
            if (authentication.Client == null)
            {
                McpLog.Warn($"Rejected unauthenticated request from {request.RemoteAddress}.");
                return Error(401, JsonRpcErrorCodes.InvalidRequest, "Authentication is required.",
                    Header("WWW-Authenticate", "Bearer"));
            }

            if (!authentication.Succeeded)
            {
                // An editor-token client with no usable token. There is no fixed-user fallback.
                McpLog.Warn($"Rejected request from client '{authentication.Client.Id}': editor token {Describe(authentication.EditorTokenFailure)}.");
                return Error(401, JsonRpcErrorCodes.InvalidRequest,
                    $"A valid editor token is required ({McpHeaders.EditorToken}); the token presented was {Describe(authentication.EditorTokenFailure)}.",
                    Header("WWW-Authenticate", "Bearer error=\"invalid_token\""));
            }

            // Rate limiting and audit key on the effective user, so editors sharing one client
            // neither starve each other nor blur attribution.
            var limit = _rateLimiter.TryAcquire(authentication.UserName);
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
                return Execute(authentication, envelope);
            }
            finally
            {
                _concurrency.Release();
            }
        }

        private McpResponse Execute(McpAuthentication authentication, McpRequestEnvelope envelope)
        {
            var client = authentication.Client;
            var userName = authentication.UserName;

            if (!User.Exists(userName))
            {
                if (authentication.IsEditor)
                {
                    // A validly signed token for an account deleted since it was issued.
                    McpLog.Warn($"Rejected editor token for '{userName}' from client '{client.Id}': the user no longer exists.");
                    return Error(401, JsonRpcErrorCodes.InvalidRequest, "The editor named by the token does not exist.");
                }

                McpLog.Error($"Configured user '{userName}' does not exist.");
                return Error(500, JsonRpcErrorCodes.InternalError, "The configured MCP user does not exist.");
            }

            if (authentication.IsEditor && !IsActive(userName))
            {
                // Disabled or locked out since the token was issued: the token must not outlive the account.
                McpLog.Warn($"Rejected editor token for '{userName}' from client '{client.Id}': the account is disabled or locked out.");
                return Error(403, JsonRpcErrorCodes.InvalidRequest, "The editor's account is disabled or locked out.");
            }

            var user = User.FromName(userName, true);
            var stopwatch = Stopwatch.StartNew();
            DispatchOutcome outcome;

            using (new UserSwitcher(user))
            {
                var context = new McpCallContext(client, user, _settings, ResolveScope(client));
                var catalog = _registry.CreateCatalog(context);
                var dispatcher = new McpDispatcher(catalog, _settings.ServerName, _settings.ServerVersion);
                outcome = dispatcher.Dispatch(envelope);
            }

            stopwatch.Stop();
            Audit(authentication, envelope, outcome, stopwatch.ElapsedMilliseconds);
            return McpResponse.FromOutcome(outcome);
        }

        /// <summary>
        /// The call's content scope, with the names of hidden fields configured by ID added so that
        /// name-only checks (index fields, queries) catch them too.
        /// </summary>
        private ContentScope ResolveScope(McpClient client)
        {
            var scope = _scopeProvider.GetScope(client) ?? new ContentScope(new string[0], new string[0], null);
            var ids = scope.HiddenFieldIds;
            if (ids.Count == 0) return scope;

            // Runs inside the call's UserSwitcher, so definitions are read as the calling user
            // (SecurityDisabler is never used).
            var names = new List<string>();
            foreach (var databaseName in client.Databases)
            {
                var database = Sitecore.Configuration.Factory.GetDatabase(databaseName, false);
                if (database == null) continue;

                foreach (var id in ids)
                {
                    var definition = database.GetItem(Sitecore.Data.ID.Parse(id));
                    if (definition != null) names.Add(definition.Name);
                }
            }

            return names.Count == 0 ? scope : scope.WithHiddenFieldNames(names);
        }

        private static bool IsActive(string userName)
        {
            var membershipUser = System.Web.Security.Membership.GetUser(userName, false);
            return membershipUser != null && membershipUser.IsApproved && !membershipUser.IsLockedOut;
        }

        private static string Describe(EditorTokenFailure failure)
        {
            switch (failure)
            {
                case EditorTokenFailure.Missing: return "missing";
                case EditorTokenFailure.Expired: return "expired";
                case EditorTokenFailure.NotYetValid: return "not yet valid";
                case EditorTokenFailure.WrongAudience: return "issued for another client";
                case EditorTokenFailure.WrongIssuer: return "issued by someone else";
                case EditorTokenFailure.InvalidSignature: return "not validly signed";
                case EditorTokenFailure.NotConfigured: return "unverifiable (editor tokens are not configured on this instance)";
                default: return "malformed";
            }
        }

        private static void Audit(McpAuthentication authentication, McpRequestEnvelope envelope, DispatchOutcome outcome, long elapsedMs)
        {
            var target = envelope.Method == "tools/call"
                ? envelope.Params?.Value<string>("name") ?? "(unknown)"
                : envelope.Method;

            McpLog.Audit(
                $"user={authentication.UserName} client={authentication.Client.Id ?? "(unnamed)"} " +
                $"identity={(authentication.IsEditor ? "editor-token" : "client-user")} " +
                $"method={envelope.Method} target={target} " +
                $"status={outcome.StatusCode} durationMs={elapsedMs}");
        }

        private static IReadOnlyDictionary<string, string> Header(string name, string value) =>
            new Dictionary<string, string> { [name] = value };

        private static McpResponse Error(int status, int code, string message,
            IReadOnlyDictionary<string, string> headers = null) =>
            new McpResponse(status, McpJson.Serialize(JsonRpcResponse.Failure(null, code, message)), headers);
    }
}
