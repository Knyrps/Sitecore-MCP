namespace SitecoreMcp.Server.Transport
{
    /// <summary>The non-standard HTTP header names the transport reads and writes.</summary>
    public static class McpHeaders
    {
        /// <summary>The negotiated protocol version, required on every request after initialize.</summary>
        public const string ProtocolVersion = "MCP-Protocol-Version";

        /// <summary>The alternative API-key header, used when no Authorization bearer is present.</summary>
        public const string ApiKey = "X-Mcp-Key";
    }
}
