using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SitecoreMcp.Bridge;

/// <summary>
/// Forwards one message at a time to the endpoint. Remembers the protocol version negotiated at
/// initialize and sends it on every later request, and keeps stdout to JSON-RPC responses only.
/// When the endpoint is unreachable at initialize (e.g. Sitecore still starting), the bridge answers
/// initialize itself with no tools, keeps retrying in the background, and announces the real tools
/// with notifications/tools/list_changed once the endpoint answers.
/// </summary>
public sealed class BridgeSession
{
    // Mirrors the server's supported versions so an offline answer matches what it will negotiate later.
    private static readonly string[] SupportedVersions = { "2025-06-18", "2025-03-26" };

    private static readonly TimeSpan InitialProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly BridgeOptions _options;
    private readonly TextWriter _stdout;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile string? _protocolVersion;
    private volatile bool _offline;
    private Task? _reconnect;

    /// <summary>Creates a session bound to the HTTP client, options, and stdout writer.</summary>
    public BridgeSession(HttpClient http, BridgeOptions options, TextWriter stdout)
    {
        _http = http;
        _options = options;
        _stdout = stdout;
    }

    /// <summary>Forwards a single JSON-RPC line and relays the response, if the message expects one.</summary>
    public async Task ForwardAsync(string message)
    {
        var (method, hasId) = Inspect(message);

        if (method == "initialize")
        {
            await InitializeAsync(message, hasId);
            return;
        }

        if (_offline)
        {
            await AnswerOfflineAsync(message, method, hasId);
            return;
        }

        try
        {
            using var response = await SendAsync(message, method, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync();

            // A notification never gets a response written.
            if (!hasId)
            {
                return;
            }

            // Only relay a valid JSON-RPC message. An error or startup page (e.g. during an
            // app-domain reload) comes back as HTML, whose newlines would otherwise be split into one
            // bogus stdout line each and flood the client; surface a single JSON-RPC error instead and
            // keep the raw detail on stderr.
            if (LooksLikeJson(body))
            {
                await WriteLineAsync(body);
            }
            else
            {
                Console.Error.WriteLine(
                    $"[sitecore-mcp-bridge] Non-JSON response (HTTP {(int)response.StatusCode}): {Snippet(body)}");
                await WriteLineAsync(ProtocolError(message, (int)response.StatusCode));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sitecore-mcp-bridge] Forwarding failed: {ex.Message}");
            if (hasId)
            {
                await WriteLineAsync(TransportError(message));
            }
        }
    }

    /// <summary>
    /// Tries initialize upstream with a short timeout. A JSON answer (success or error) is relayed;
    /// an unreachable endpoint or a non-JSON page switches to offline mode instead of failing the client.
    /// </summary>
    private async Task InitializeAsync(string message, bool hasId)
    {
        string? body = null;
        try
        {
            using var cts = new CancellationTokenSource(InitialProbeTimeout);
            using var response = await SendAsync(message, "initialize", cts.Token);
            body = await response.Content.ReadAsStringAsync(cts.Token);
            if (!LooksLikeJson(body))
            {
                Console.Error.WriteLine(
                    $"[sitecore-mcp-bridge] Non-JSON initialize response (HTTP {(int)response.StatusCode}): {Snippet(body)}");
                body = null;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sitecore-mcp-bridge] Endpoint not reachable at initialize: {ex.Message}");
        }

        if (body != null)
        {
            CaptureProtocolVersion(body);
            if (hasId)
            {
                await WriteLineAsync(AdvertiseListChanged(body));
            }

            return;
        }

        _offline = true;
        Console.Error.WriteLine(
            $"[sitecore-mcp-bridge] Starting without tools; retrying every {RetryInterval.TotalSeconds:0}s until the endpoint is up.");
        if (hasId)
        {
            await WriteLineAsync(OfflineInitializeResult(message));
        }

        _reconnect ??= Task.Run(() => ReconnectAsync(message));
    }

    /// <summary>Replays initialize until the endpoint answers, then tells the client the tool list changed.</summary>
    private async Task ReconnectAsync(string initializeMessage)
    {
        while (true)
        {
            await Task.Delay(RetryInterval);
            try
            {
                using var response = await SendAsync(initializeMessage, "initialize", CancellationToken.None);
                var body = await response.Content.ReadAsStringAsync();
                if (!LooksLikeJson(body) || !HasResult(body))
                {
                    continue;
                }

                CaptureProtocolVersion(body);
                await SendInitializedAsync();
                _offline = false;
                Console.Error.WriteLine("[sitecore-mcp-bridge] Endpoint is up; announcing tools.");
                await WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}");
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[sitecore-mcp-bridge] Still waiting for endpoint: {ex.Message}");
            }
        }
    }

    private async Task SendInitializedAsync()
    {
        try
        {
            using var _ = await SendAsync(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                "notifications/initialized",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[sitecore-mcp-bridge] Sending initialized failed: {ex.Message}");
        }
    }

    /// <summary>Answers what can be answered locally while the endpoint is down; notifications are dropped.</summary>
    private async Task AnswerOfflineAsync(string message, string? method, bool hasId)
    {
        if (!hasId)
        {
            return;
        }

        var id = ExtractId(message);
        switch (method)
        {
            case "ping":
                await WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{}}}}");
                break;
            case "tools/list":
                await WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[]}}}}");
                break;
            case "tools/call":
                var text = JsonSerializer.Serialize(OfflineReason());
                await WriteLineAsync(
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"isError\":true}}}}");
                break;
            default:
                await WriteLineAsync(JsonRpcError(message, OfflineReason()));
                break;
        }
    }

    private string OfflineReason() =>
        $"The Sitecore endpoint ({_options.Url}) is not reachable yet. The bridge keeps retrying and " +
        "will announce the tools once it is up.";

    private async Task<HttpResponseMessage> SendAsync(string message, string? method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
        {
            Content = new StringContent(message, new UTF8Encoding(false), "application/json")
        };

        // The server exempts initialize from the version header; every other call requires it.
        var version = _protocolVersion;
        if (version != null && method != "initialize")
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task WriteLineAsync(string line)
    {
        // The reconnect loop writes concurrently with the request loop; keep lines whole.
        await _writeLock.WaitAsync();
        try
        {
            await _stdout.WriteLineAsync(line);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void CaptureProtocolVersion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("protocolVersion", out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                _protocolVersion = version.GetString();
            }
        }
        catch (JsonException)
        {
            // A malformed initialize response leaves the version unset; later requests will be rejected.
        }
    }

    /// <summary>
    /// Adds tools.listChanged to a relayed initialize result, so the client is consistent with the
    /// offline path and honours the list_changed notification.
    /// </summary>
    private static string AdvertiseListChanged(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject root &&
                root["result"]?["capabilities"]?["tools"] is JsonObject tools)
            {
                tools["listChanged"] = true;
                return root.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private static string OfflineInitializeResult(string message)
    {
        string? requested = null;
        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("params", out var parameters) &&
                parameters.ValueKind == JsonValueKind.Object &&
                parameters.TryGetProperty("protocolVersion", out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                requested = version.GetString();
            }
        }
        catch (JsonException)
        {
        }

        var negotiated = Array.IndexOf(SupportedVersions, requested) >= 0 ? requested! : SupportedVersions[0];
        var result = new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
            ["serverInfo"] = new JsonObject { ["name"] = "sitecore-mcp-bridge", ["version"] = "offline" },
            ["instructions"] = "The Sitecore instance is not reachable yet. Its tools appear once it is up."
        };

        return $"{{\"jsonrpc\":\"2.0\",\"id\":{ExtractId(message)},\"result\":{result.ToJsonString()}}}";
    }

    private static bool HasResult(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("result", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (string? Method, bool HasId) Inspect(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;
            return (method, root.TryGetProperty("id", out _));
        }
        catch (JsonException)
        {
            // Unparseable input is forwarded anyway; treat it as a request so the server's error is relayed.
            return (null, true);
        }
    }

    private static string TransportError(string message) =>
        JsonRpcError(message, "Bridge could not reach the Sitecore endpoint.");

    private static string ProtocolError(string message, int statusCode)
    {
        var reason = statusCode == 200
            ? "The Sitecore endpoint returned a non-JSON response."
            : $"The Sitecore endpoint returned HTTP {statusCode} with a non-JSON body (often an app-domain reload or an error page). Retry shortly.";
        return JsonRpcError(message, reason);
    }

    private static string JsonRpcError(string message, string reason)
    {
        var id = ExtractId(message);
        var encodedReason = JsonSerializer.Serialize(reason);
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32603,\"message\":{encodedReason}}}}}";
    }

    private static string ExtractId(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("id", out var value))
            {
                return value.GetRawText();
            }
        }
        catch (JsonException)
        {
        }

        return "null";
    }

    private static bool LooksLikeJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Snippet(string body)
    {
        var flattened = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length > 200 ? flattened.Substring(0, 200) + "..." : flattened;
    }
}
