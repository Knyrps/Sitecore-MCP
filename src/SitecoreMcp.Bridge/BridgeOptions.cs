namespace SitecoreMcp.Bridge;

/// <summary>The bridge's resolved configuration: where to forward and which key to present.</summary>
public sealed class BridgeOptions
{
    /// <summary>The Sitecore MCP endpoint URL to POST messages to.</summary>
    public required string Url { get; init; }

    /// <summary>The API key sent as a bearer token.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Resolves options from arguments (--url, --key) then environment variables
    /// (SITECORE_MCP_URL, SITECORE_MCP_KEY). Throws when either value is missing.
    /// </summary>
    public static BridgeOptions Resolve(string[] args)
    {
        string? url = null;
        string? key = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--url") url = args[i + 1];
            else if (args[i] == "--key") key = args[i + 1];
        }

        url ??= Environment.GetEnvironmentVariable("SITECORE_MCP_URL");
        key ??= Environment.GetEnvironmentVariable("SITECORE_MCP_KEY");

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("No endpoint URL. Set SITECORE_MCP_URL or pass --url.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No API key. Set SITECORE_MCP_KEY or pass --key.");
        }

        return new BridgeOptions { Url = url, Key = key };
    }
}
