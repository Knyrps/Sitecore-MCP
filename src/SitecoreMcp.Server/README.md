# SitecoreMcp.Server

The MCP server itself. A net48 assembly deployed to `<webroot>/bin`, plus config patches to
`<webroot>/App_Config/Include`. No `.ashx` and no Sitecore package — the endpoint is registered
as an ASP.NET route from the `initialize` pipeline.

To build, deploy, and connect a client, see the **Local build & setup** section of the
[root README](../../README.md); this file covers the internal structure and design.

## Folders

| Folder | Role |
|---|---|
| `Protocol/` | JSON-RPC 2.0 and MCP envelope. **No Sitecore references** — this is what keeps the unit tests runnable without an instance. |
| `Transport/` | Route registration, `IHttpHandler`, request guards, authentication, audit logging. |
| `Tools/` | Tool framework: `IMcpTool`, the `McpTool<TArgs>` base, the registry and per-call catalog, the call context, and shared paging. |
| `Tools/Items/` | Item tools (get/create/update/move/copy/rename/delete) and their helpers (resolve, edit, project). |
| `Tools/Templates/` | Template tools (get/list/create) and their helpers (resolve, build, describe). |
| `Tools/Search/` | Index-backed tools: search, grep, facet, index status. |
| `Schema/` | Reflection-based POCO-to-JSON-Schema generator, driven by `[McpParam]`. |
| `App_Config/Include/` | Sitecore config patches. |

Keep `Protocol/` and `Schema/` free of Sitecore references. Guard logic in `Transport/` should be
pure where it can be, so it stays testable off-instance; the handler orchestrates, the guards
decide.

## Design notes

- **Hand-rolled protocol.** The official `ModelContextProtocol` NuGet targets net8.0/netstandard2.0
  and pulls in a `System.Text.Json` / `Microsoft.Extensions.*` graph that collides with Sitecore's
  binding redirects. The surface we need is about five methods.
- **Stateless.** No session IDs, so app-pool recycles are free. The cost is relaxed lifecycle
  enforcement — see the config patch comments.
- **Synchronous.** Every Kernel API is sync; async-over-sync in classic ASP.NET invites
  `SynchronizationContext` deadlocks.
- **Responses are JSON, never SSE.** The spec permits it and it avoids IIS response-buffering pain.

Tools are registered through config, not compiled in, so a solution can add its own without
recompiling this assembly.
