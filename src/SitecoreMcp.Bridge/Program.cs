using System.Text;
using System.Text.Json;
using SitecoreMcp.Bridge;

// stdio <-> HTTP bridge. Reads newline-delimited JSON-RPC on stdin, forwards each message to the
// Sitecore endpoint, and writes responses to stdout. stdout carries JSON-RPC and nothing else;
// everything diagnostic goes to stderr.

BridgeOptions options;
try
{
    options = BridgeOptions.Resolve(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[sitecore-mcp-bridge] {ex.Message}");
    return 1;
}

var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
http.DefaultRequestHeaders.Add("Authorization", "Bearer " + options.Key);
http.DefaultRequestHeaders.Add("Accept", "application/json");

var session = new BridgeSession(http, options, stdout);

string? line;
while ((line = await input.ReadLineAsync()) != null)
{
    if (line.Length == 0)
    {
        continue;
    }

    await session.ForwardAsync(line);
}

return 0;
