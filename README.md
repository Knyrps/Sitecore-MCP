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
Developed against 10.4.1 (Sitecore.Kernel 19.0.0.0, Newtonsoft.Json 13.0.0.0).

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

## Building

```powershell
dotnet build                                        # build only
dotnet build /p:DeployToSitecore=true               # build and xcopy into the instance
dotnet test
```

Point the build at your own instance by creating `Directory.Build.user.props` (gitignored):

```xml
<Project>
  <PropertyGroup>
    <SitecoreWebRoot>C:\inetpub\wwwroot\my-instance</SitecoreWebRoot>
  </PropertyGroup>
</Project>
```

Deployment is opt-in so a plain build never writes into a live web root.
