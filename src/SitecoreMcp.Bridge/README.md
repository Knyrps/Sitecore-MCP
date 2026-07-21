# SitecoreMcp.Bridge

A stdio-to-HTTP shim for MCP clients that cannot speak Streamable HTTP. It reads
newline-delimited JSON-RPC from stdin, POSTs each message to the Sitecore endpoint, and writes
the response to stdout. It is a dumb pipe — it holds no MCP logic and needs no MCP SDK.

Clients that speak HTTP should point at the Sitecore endpoint directly and skip this entirely.

## Configuration

| Variable | Meaning |
|---|---|
| `SITECORE_MCP_URL` | Endpoint, e.g. `https://my-instance/sitecore/api/mcp` |
| `SITECORE_MCP_KEY` | API key, sent as `Authorization: Bearer` |

Both can also be passed as arguments.

## Two things it must get right

1. **Remember the negotiated protocol version** from the `initialize` response and send it as
   `MCP-Protocol-Version` on every later request. The server rejects requests without it.
2. **stdout carries JSON-RPC and nothing else.** All logging goes to stderr, and an empty/`202`
   response produces no stdout line at all. Stray stdout output is the most common way stdio
   bridges break clients.

## Building

```powershell
dotnet publish -c Release        # single-file sitecore-mcp-bridge.exe
```
