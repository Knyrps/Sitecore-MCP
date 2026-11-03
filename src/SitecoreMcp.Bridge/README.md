# SitecoreMcp.Bridge

A stdio-to-HTTP shim for MCP clients that cannot speak Streamable HTTP. It reads
newline-delimited JSON-RPC from stdin, POSTs each message to the Sitecore endpoint, and writes
the response to stdout. It is a dumb pipe — it holds no MCP logic and needs no MCP SDK.

Clients that speak HTTP should point at the Sitecore endpoint directly and skip this entirely.
On a local instance with a self-signed certificate, though, prefer the bridge: it is a .NET
process and trusts the Windows certificate store, so it connects where a Node/Bun client would
reject the cert.

For the full end-to-end setup — including VS Code and opencode client config — see the
**Local build & setup** section of the [root README](../../README.md).

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

## Starting before Sitecore is up

If the endpoint cannot be reached at `initialize` (connection refused, 10 s timeout, or a non-JSON
startup page), the bridge answers `initialize` itself instead of failing, so the client stays
connected. Until Sitecore answers, `tools/list` is empty, `ping` succeeds, and `tools/call` returns an
error result. The bridge replays `initialize` every 5 s; once it succeeds it sends
`notifications/tools/list_changed` and the client fetches the real tools. `tools.listChanged` is
always advertised, so the client knows to listen for it.

## Building

```powershell
dotnet publish -c Release        # single-file sitecore-mcp-bridge.exe
```
