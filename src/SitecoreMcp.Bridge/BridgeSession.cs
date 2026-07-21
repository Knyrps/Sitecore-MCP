using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SitecoreMcp.Bridge;

/// <summary>
/// Forwards one message at a time to the endpoint. Remembers the protocol version negotiated at
/// initialize and sends it on every later request, and keeps stdout to JSON-RPC responses only.
/// </summary>
public sealed class BridgeSession
{
    private readonly HttpClient _http;
    private readonly BridgeOptions _options;
    private readonly TextWriter _stdout;
    private string? _protocolVersion;

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

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
            {
                Content = new StringContent(message, new UTF8Encoding(false), "application/json")
            };

            // The server exempts initialize from the version header; every other call requires it.
            if (_protocolVersion != null && method != "initialize")
            {
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
            }

            using var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (method == "initialize")
            {
                CaptureProtocolVersion(body);
            }

            // A notification never gets a response written, and an empty body has nothing to relay.
            if (hasId && !string.IsNullOrEmpty(body))
            {
                await WriteLineAsync(body);
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

    private async Task WriteLineAsync(string line)
    {
        await _stdout.WriteLineAsync(line);
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

    private static string TransportError(string message)
    {
        var id = "null";
        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("id", out var value))
            {
                id = value.GetRawText();
            }
        }
        catch (JsonException)
        {
        }

        return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32603,\"message\":\"Bridge could not reach the Sitecore endpoint.\"}}}}";
    }
}
