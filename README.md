# SitecoreMCP

An MCP server that runs **inside** Sitecore as a .NET Framework assembly, rather than as an
external process talking to Sitecore over HTTP.

Existing Sitecore MCP servers (and the Sitecore CLI, PowerShell Remoting, the Item Service,
GraphQL) all sit outside the platform and reach in over the network. That means a second
runtime to install and version, an extra hop, a DTO layer that drifts from the real item model,
and whatever permission model the chosen API happened to expose.

This module is a DLL in the instance's `/bin`. It gets `Sitecore.Data.Database`,
`Sitecore.Context`, the real security model, and the publishing and indexing pipelines as
in-process calls. Tools are thin wrappers over the Kernel API.

**Target:** Sitecore XM/XP 10.x, .NET Framework 4.8, IIS.
Verified on 10.3 (Kernel 18.0.0.0) and 10.4 (Kernel 19.0.0.0).

## How it fits together

```
MCP client (Claude Code, Copilot)
      │  stdio JSON-RPC
      ▼
SitecoreMcp.Bridge            ← optional; only for clients that cannot speak HTTP
      │  HTTP POST /sitecore/api/mcp
      ▼
SitecoreMcp.Server (in the Sitecore worker process)
      │  in-process Kernel API, under the caller's real Sitecore user
      ▼
Sitecore
```

Clients that speak Streamable HTTP connect to the endpoint directly and skip the bridge.

## Structure

| Path | Role |
|---|---|
| `src/SitecoreMcp.Server` | The module deployed to the instance. Protocol, transport, tools. |
| `src/SitecoreMcp.Bridge` | stdio-to-HTTP shim for stdio-only MCP clients. |
| `tests/SitecoreMcp.Server.Tests` | Unit tests for the parts that need no running Sitecore. |
| `docs/` | Design notes and setup guides. |

## Security posture

The endpoint is **disabled by default**. An API key maps to a dedicated Sitecore user and every
call runs under that user via `UserSwitcher`, so item and field ACLs, workflow, and auditing all
apply normally. `SecurityDisabler` is never used. Writes and non-`master` databases are opt-in
per client. Locally the mapped user can be an admin; on shared instances it should not be.

## Local build & setup

End to end: build against your instance, deploy, verify, and connect a client.

### Prerequisites

- A local **Sitecore XM/XP 10.x** instance on IIS. The server compiles against *that instance's
  own* assemblies, so 10.3 and 10.4 each need a build targeting them.
- The **.NET SDK** (builds the net48 server, the net8.0 bridge, and the tests) and the
  **.NET 8 runtime** (to run the bridge).
- An **elevated PowerShell** for deployment — writing into the web root, setting the app-pool
  environment variable, and recycling the pool are all admin-only.

### 1. Point the build at your instance

The server references `Sitecore.Kernel`, `Newtonsoft.Json`, and `Sitecore.Logging` straight from
the target web root, so it must be built against the instance it will run in. Create a gitignored
`Directory.Build.user.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <SitecoreWebRoot>C:\inetpub\wwwroot\my-instance</SitecoreWebRoot>
  </PropertyGroup>
</Project>
```

### 2. Build and test

```powershell
dotnet build -c Release
dotnet test
```

A plain build never writes into the web root — deployment is a separate, opt-in step.

### 3. Deploy to the instance (elevated)

From an **elevated** PowerShell:

```powershell
./deploy/Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\my-instance
```

This copies the DLL and `SitecoreMcp.config`, writes a local `SitecoreMcp.Dev.config` that
enables the endpoint over HTTP with an admin-mapped client, sets `SITECORE_MCP_KEY` on the app
pool, and recycles it — printing the generated key. Pass `-Key <key>` to pin your own so it does
not rotate between deploys. See [deploy/README.md](deploy/README.md) for details and the
production posture (real cert, dedicated limited user, writes off).

### 4. Verify

```powershell
./deploy/Verify-SitecoreMcp.ps1 -Url https://my-instance/sitecore/api/mcp -Key <key>
```

Expect an `initialize` result, the tool list, and a `sitecore_get_context` payload. Then check
`App_Data/logs/mcp.log.<date>.txt` for an `AUDIT` line — all MCP activity is logged there, not in
the main Sitecore log.

### 5. Connect an MCP client

Two transports; choose by your cert situation.

**Direct HTTP** — any client that speaks Streamable HTTP points at
`https://my-instance/sitecore/api/mcp` with an `Authorization: Bearer <key>` header. Best on
instances with a real (trusted) certificate.

**stdio bridge (recommended for local dev)** — local instances use a self-signed certificate that
Node/Bun-based clients (VS Code, opencode) reject. The bridge is a .NET process whose `HttpClient`
trusts the Windows certificate store, so it connects with no cert wrangling. Publish it once:

```powershell
dotnet publish src/SitecoreMcp.Bridge -c Release
# -> src/SitecoreMcp.Bridge/bin/Release/net8.0/win-x64/publish/sitecore-mcp-bridge.exe
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "sitecore": {
      "type": "stdio",
      "command": "C:\\path\\to\\sitecore-mcp-bridge.exe",
      "env": {
        "SITECORE_MCP_URL": "https://my-instance/sitecore/api/mcp",
        "SITECORE_MCP_KEY": "<key>"
      }
    }
  }
}
```

opencode (`opencode.json`, project-scoped):

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "sitecore": {
      "type": "local",
      "command": ["C:\\path\\to\\sitecore-mcp-bridge.exe"],
      "environment": {
        "SITECORE_MCP_URL": "https://my-instance/sitecore/api/mcp",
        "SITECORE_MCP_KEY": "{file:./.sitecore-mcp-key}"
      },
      "enabled": true
    }
  }
}
```

Keep the key out of committed config — reference an env var or, as above, a gitignored file
(`{file:./.sitecore-mcp-key}`, with `.sitecore-mcp-key` added to `.gitignore`).

> **Connecting a Node/Bun client directly over HTTPS to a self-signed instance** needs the cert
> trusted (e.g. `NODE_EXTRA_CA_CERTS` pointing at the exported cert, then a full client restart).
> The bridge avoids this entirely, which is why it is the local default.

## License

MIT — see [LICENSE](LICENSE).
