using System;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Protocol
{
    public sealed class McpDispatcher
    {
        private readonly IToolCatalog _catalog;
        private readonly string _serverName;
        private readonly string _serverVersion;

        public McpDispatcher(IToolCatalog catalog, string serverName, string serverVersion)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _serverName = serverName;
            _serverVersion = serverVersion;
        }

        public DispatchOutcome Dispatch(McpRequestEnvelope request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Notifications are acknowledged and never answered, including ones we do not know.
            // Replying to a notification is a protocol violation, so this check precedes routing.
            if (request.IsNotification)
            {
                return DispatchOutcome.Accepted();
            }

            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        return Respond(request, Initialize(request.Params));
                    case "ping":
                        return Respond(request, new JObject());
                    case "tools/list":
                        return Respond(request, ListTools());
                    case "tools/call":
                        return CallTool(request);
                    default:
                        return Fail(request, JsonRpcErrorCodes.MethodNotFound, "Unknown method: " + request.Method);
                }
            }
            catch (Exception ex)
            {
                return Fail(request, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }

        private JObject Initialize(JObject parameters)
        {
            var requested = parameters?.Value<string>("protocolVersion");

            return new JObject
            {
                ["protocolVersion"] = McpProtocolVersions.Negotiate(requested),
                // Declares that we support tools; empty because the set is fixed at pipeline time,
                // so we never advertise listChanged and never fire tools/list_changed.
                ["capabilities"] = new JObject { ["tools"] = new JObject() },
                ["serverInfo"] = new JObject
                {
                    ["name"] = _serverName,
                    ["version"] = _serverVersion
                }
            };
        }

        private JObject ListTools()
        {
            var tools = new JArray();
            foreach (var tool in _catalog.List())
            {
                tools.Add(JObject.FromObject(tool, Newtonsoft.Json.JsonSerializer.Create(McpJson.Settings)));
            }

            return new JObject { ["tools"] = tools };
        }

        private DispatchOutcome CallTool(McpRequestEnvelope request)
        {
            var name = request.Params?.Value<string>("name");
            if (string.IsNullOrEmpty(name))
            {
                return Fail(request, JsonRpcErrorCodes.InvalidParams, "Missing tool name.");
            }

            // An unlistable tool is a protocol-level mistake, not something the model can retry,
            // so it is a JSON-RPC error rather than an isError result.
            if (!_catalog.Contains(name))
            {
                return Fail(request, JsonRpcErrorCodes.InvalidParams, "Unknown tool: " + name);
            }

            var arguments = request.Params?["arguments"] as JObject ?? new JObject();

            McpToolResult result;
            try
            {
                result = _catalog.Invoke(name, arguments);
            }
            catch (Exception ex)
            {
                result = McpToolResult.Failure(ex.Message);
            }

            return Respond(request, result ?? McpToolResult.Failure("The tool returned no result."));
        }

        private static DispatchOutcome Respond(McpRequestEnvelope request, object result) =>
            DispatchOutcome.Json(JsonRpcResponse.Success(request.Id, result));

        private static DispatchOutcome Fail(McpRequestEnvelope request, int code, string message) =>
            DispatchOutcome.Json(JsonRpcResponse.Failure(request.Id, code, message));
    }
}
