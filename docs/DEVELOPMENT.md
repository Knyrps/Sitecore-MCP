# Developing the server

For working **on** this MCP server. If you want to roll it out on a Sitecore instance, see the
[README](../README.md) instead.

## Repository layout

| Path | Role |
|---|---|
| `src/SitecoreMcp.Server` | The module deployed to the instance: protocol, transport, tools. |
| `src/SitecoreMcp.Bridge` | stdio-to-HTTP shim for stdio-only clients (net8.0). |
| `tests/SitecoreMcp.Server.Tests` | Unit tests for the parts needing no running Sitecore. |
| `deploy/` | Dev deployment and verification scripts. |
| `docs/` | Tool guide, adaptation plan, implementation notes. |

Inside the server:

| Folder | Contents |
|---|---|
| `Protocol/` | JSON-RPC 2.0 and MCP envelope. **No Sitecore references** — this is what keeps the unit tests runnable without an instance. |
| `Transport/` | Route registration, `IHttpHandler`, request gates, authentication, configuration binding. |
| `Tools/` | Tool framework: `IMcpTool`, `McpTool<TArgs>`, registry, per-call catalog, call context, paging. |
| `Tools/Items`, `Tools/Templates`, `Tools/Presentation`, `Tools/Search`, `Tools/Membership`, `Tools/Workflow`, `Tools/Jobs`, `Tools/Publishing`, `Tools/Diagnostics` | Tools grouped by concern, each with its own resolver/describer helpers. |
| `Schema/` | Reflection-based POCO-to-JSON-Schema generator, driven by `[McpParam]`. |

Keep `Protocol/` and `Schema/` free of Sitecore references. Gate logic in `Transport/` should stay
pure where it can, so it remains testable off-instance.

## Prerequisites

- A local **Sitecore XM/XP 10.x** instance on IIS. The server compiles against *that instance's own*
  assemblies, so 10.3 and 10.4 each need their own build.
- The **.NET SDK** and, to run the bridge, the **.NET 8 runtime**.
- An **elevated PowerShell** for the deploy scripts (web root, app-pool environment, pool restart),
  or `-SkipAdminRequirement` if your account already holds those rights.

## Point the build at your instance

Create a gitignored `Directory.Build.user.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <SitecoreWebRoot>C:\inetpub\wwwroot\my-instance</SitecoreWebRoot>
  </PropertyGroup>
</Project>
```

The server references `Sitecore.Kernel`, `Newtonsoft.Json`, `Sitecore.Logging`, and the ContentSearch
assemblies straight from that web root, so the build binds to the exact versions the instance runs.

## Build and test

```powershell
dotnet build -c Release
dotnet test
```

A plain build never writes into the web root — deployment is a separate, opt-in step.

## Deploy to your dev instance

```powershell
./deploy/Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\my-instance
```

The script builds the chosen configuration first (so it can never ship a stale artifact), copies the
DLL and configs, writes a local `SitecoreMcp.Dev.config` that enables the endpoint with an
admin-mapped client, sets `SITECORE_MCP_KEY` on the app pool, **verifies the copy by hash**, and
restarts the pool — printing the generated key. Pass `-Key <key>` to pin your own so it survives
redeploys.

For testing non-admin behaviour (locking, ACLs, the admin gate) use the two-client variant, which
also registers a limited user:

```powershell
./deploy/Deploy-SitecoreMcp-TwoClients.ps1 -WebRoot C:\inetpub\wwwroot\my-instance
```

Then verify:

```powershell
./deploy/Verify-SitecoreMcp.ps1 -Url https://my-instance/sitecore/api/mcp -Key <key>
```

See [deploy/README.md](../deploy/README.md) for details and troubleshooting.

### Fast iteration

Once the config and key are in place, a code-only change just needs the assembly replaced —
overwriting a `/bin` assembly triggers an ASP.NET app-domain reload, so no pool restart is needed:

```powershell
dotnet build src/SitecoreMcp.Server -c Debug
Copy-Item src/SitecoreMcp.Server/bin/Debug/SitecoreMcp.Server.dll C:\inetpub\wwwroot\my-instance\bin\ -Force
```

Config changes need the app domain recycled (touch `web.config`) and, for app-pool environment
variables, a full worker restart.

## Building the bridge

```powershell
dotnet publish src/SitecoreMcp.Bridge -c Release
# -> src/SitecoreMcp.Bridge/bin/Release/net8.0/win-x64/publish/sitecore-mcp-bridge.exe
```

Running MCP clients hold the exe open, so quit them before republishing.

Two things the bridge must get right:

1. **Remember the negotiated protocol version** from the `initialize` response and send it as
   `MCP-Protocol-Version` on every later request — the server rejects requests without it.
2. **stdout carries JSON-RPC and nothing else.** Only a response that parses as JSON is relayed;
   anything else (an error page during an app-domain reload, a YSOD) becomes a single JSON-RPC error
   with the detail on stderr. Forwarding an HTML page verbatim floods the client with one parse
   error per line.

## Adding a tool

1. Create the class in the right `Tools/<Concern>` folder, deriving from `McpTool<TArgs>`.
2. Give the args POCO `[McpParam]` attributes — the same POCO drives binding *and* the JSON schema,
   so they cannot drift.
3. Override `RequiresWrite` for anything that mutates, and `RequiresAdmin` for schema, security, or
   dev/ops operations. The code default is a floor: config can add an admin requirement with
   `admin="true"` on the `<tool>` element but **can never remove one**.
4. Register it in `App_Config/Include/SitecoreMcp/SitecoreMcp.config`.
5. Update [TOOL_GUIDE.md](TOOL_GUIDE.md) **and** `Protocol/McpServerInstructions.cs` — the condensed
   guidance shipped at `initialize` is what most clients actually give the model.
6. Add the tool to the README's catalogue.

### Conventions that matter

- **Verify writes.** Never report success for something that did not happen: `WriteFields` re-reads
  and reports `notPersisted`; `change_item_template` diffs fields and reports `dataLost` with old
  values. New write tools should carry the same discipline.
- **Fail loudly, usefully.** A refusal should name the reason and, where possible, the alternatives
  (available workflow commands, present unique IDs, closest field-type names).
- **Resolve exactly on writes.** Templates, renderings, and layouts resolve by path, ID, or *exact*
  name for writes — never a fuzzy match that could target the wrong item. `allowPartial: true` is for
  discovery tools like search.
- **Respect security.** Everything runs under the caller's Sitecore user. `SecurityDisabler` is never
  used, and item/field permission failures are real outcomes to report, not bugs to work around.
- **Use the shared helpers** — `ItemResolver`, `ItemEditor`, `ItemProjector`, `Paging`, and the
  per-concern resolvers/describers — rather than re-deriving their behaviour.

## Verifying against a real instance

The unit tests cover the Sitecore-independent parts (protocol, schema generation, gates, paging).
Anything touching the Kernel needs a live instance: deploy, then exercise the tool over HTTP or
through an MCP client and confirm the result against a separate read.

Two habits that repeatedly caught real bugs:

- **Check what is actually deployed.** Hash the deployed DLL against the build before trusting a
  test result; a stale artifact has burned an afternoon more than once.
- **Confirm Kernel APIs by reflection before coding against them.** Several assumptions turned out
  wrong: jobs are `BaseJob` (not `Job`), `IndexCustodian` has no per-index stop, `FullRebuild`'s
  second parameter is `start` (passing `false` creates a job that never runs), publish handles are
  not job handles, and `Settings.DataFolder` is a virtual path needing `FileUtil.MapPath`.

The endpoint rate-limits by default (30 burst, 1/sec refill), so pace scripted test bursts.

### Cross-version API obsolescence

The build binds to whichever Kernel your `Directory.Build.user.props` points at, so a build that is
clean on 10.3 can still fail on 10.4 where an API has since been deprecated — that is exactly how
`MediaCreatorOptions.FileBased` shipped broken. When adding Kernel API usage:

- **Prefer not setting a property whose value equals its default** — that alone avoided the
  `FileBased` breakage, since the property was redundant.
- **Suppress with both codes** when an obsolete member is genuinely the right call:
  `#pragma warning disable CS0618, CS0619` — a later Kernel can escalate a warning-level obsolete to
  an error-level one.
- **Build against every supported version before releasing.** There is no substitute; only the
  compiler bound to that Kernel can tell you.

## Design notes

- **Hand-rolled protocol.** The official `ModelContextProtocol` NuGet targets net8.0/netstandard2.0
  and drags in a `System.Text.Json` / `Microsoft.Extensions.*` graph that collides with Sitecore's
  binding redirects. The surface actually needed is about five methods.
- **Stateless.** No session IDs, so app-pool recycles cost nothing.
- **Synchronous.** Every Kernel API is sync; async-over-sync in classic ASP.NET invites
  `SynchronizationContext` deadlocks.
- **Responses are JSON, never SSE** — permitted by the spec, and it avoids IIS response-buffering pain.
- **Lenient schemas.** No `additionalProperties: false`: MCP clients cache tool schemas, so a strict
  schema would hard-reject a later-added parameter until the client re-fetches. Unknown arguments are
  ignored and reported in the result instead.
- **Tools are registered through config**, not compiled in, so a solution can add its own without
  recompiling this assembly.

## Documents

| File | Purpose |
|---|---|
| [TOOL_GUIDE.md](TOOL_GUIDE.md) | Working notes on using the tools well; the condensed version ships as the server `instructions`. |
| [SPE_TOOL_PLAN.md](SPE_TOOL_PLAN.md) | Triage of the SPE tool list: what to adapt, combine, or skip, and why. |
| [SPE_TOOL_BACKLOG.md](SPE_TOOL_BACKLOG.md) | The implementation checklist derived from that triage. |
| [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) | Per-tool design: arguments, Kernel APIs, safety, result shape. Includes decision records for the two tools deliberately not built. |
