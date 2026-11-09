# Developing the server

For working **on** this MCP server. If you want to roll it out on a Sitecore instance, see the
[README](../README.md) instead.

## Repository layout

| Path | Role |
|---|---|
| `src/SitecoreMcp.Server` | The module deployed to the instance: protocol, transport, tools. |
| `src/SitecoreMcp.Bridge` | stdio-to-HTTP shim for stdio-only clients (net8.0). |
| `tests/SitecoreMcp.Server.Tests` | Unit tests for the parts needing no running Sitecore. |
| `tests/SitecoreMcp.Server.Portable.Tests` | Unit tests that compile the server's Sitecore-free sources directly, so they build and run without a Sitecore web root, on any OS. |
| `deploy/` | Dev deployment and verification scripts. |
| `docs/` | Tool guide, adaptation plan, implementation notes. |

Inside the server:

| Folder | Contents |
|---|---|
| `Protocol/` | JSON-RPC 2.0 and MCP envelope. **No Sitecore references** — this is what keeps the unit tests runnable without an instance. |
| `Transport/` | Route registration, `IHttpHandler`, request gates, authentication, configuration binding. |
| `Tools/` | Tool framework: `IMcpTool`, `McpTool<TArgs>`, registry, per-call catalog, call context, paging. |
| `Tools/Items`, `Tools/Templates`, `Tools/Presentation`, `Tools/Search`, `Tools/Membership`, `Tools/Workflow`, `Tools/Jobs`, `Tools/Publishing`, `Tools/Diagnostics`, `Tools/Configuration`, `Tools/Packages` | Tools grouped by concern, each with its own resolver/describer helpers. |
| `Tools/Configuration/Portable`, `Tools/Packages/Portable` | The decision logic of the config and package tools (XPath projection, redaction, role/layer rules, package names and paths, file classes, manifest, install policy). **No Sitecore references**; the whole folders are compiled into the portable tests. |
| `Schema/` | Reflection-based POCO-to-JSON-Schema generator, driven by `[McpParam]`. |
| `Security/` | Editor tokens: issuing, validation, and the issue policy. **No Sitecore references** — compiled into the portable tests. |

Keep `Protocol/`, `Schema/`, and `Security/` free of Sitecore references. Gate logic in `Transport/` should stay
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

Without a Sitecore web root (CI, Linux, macOS) only the portable suite builds:

```bash
dotnet test tests/SitecoreMcp.Server.Portable.Tests -f net10.0
```

It also targets `net48` — `dotnet build` of it proves the linked sources compile for the shipping
framework; on Windows `dotnet test` runs both targets.

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

## Test coverage

`dotnet test` runs 579 cases. Most are per-tool: every tool is discovered by reflection and fed
through the contract theories, so each one appears as its own test result.

| Suite | Covers |
|---|---|
| `Tools/ToolContractTests` | Per tool: name shape, description length, argument POCO is constructible, generated schema is a lenient object schema, `[McpParam(Required)]` matches the schema's `required` array, every argument carries a description. |
| `Tools/ToolPermissionTests` | The admin-gated and write sets, written out in full, plus the rule that an admin tool says so in its description. |
| `Tools/ToolRegistrationTests` | Config and code agree: nothing unregistered, nothing registered that does not exist, no duplicates. |
| `Protocol/`, `Schema/`, `Transport/` | JSON-RPC envelope, version negotiation, dispatch, schema generation, gates, rate limiting, constant-time key comparison. |
| `Portable.Tests/Security` | Editor tokens: issued token round-trips; missing, malformed, expired, not-yet-valid, wrong-issuer, wrong-audience, other-secret, tampered, and `alg: none` tokens are refused; issue policy (audience, domains). |
| `Portable.Tests/Tools/WorkflowStartPolicyTests` | Whether a created item, version or copy is started in its workflow: no state, a final state or a copied state is started; the initial state is left alone; nothing without a workflow, in a database without a workflow provider, or on standard values/branch items; the item's own `__Workflow` wins over `__Default workflow`. |
| `Portable.Tests/Transport` | Effective identity per request: an editor-token client runs as the token's editor and is refused without a valid token (or without a configured secret); fixed-user clients run as their user and ignore editor tokens. Client config rules: an editor-token client cannot have a fixed user, and always confirms direct changes (`confirmDirectChanges="false"` skips it); a fixed-user client defaults to no confirmation (`Mcp.ConfirmDirectChanges`). The audit line's `confirmed=` marker. |
| `Portable.Tests/Tools/DirectChangeGateTests`, `DirectChangeDescriptionTests`, `Protocol/ServerInstructionsTests` | Confirmation tokens (preview, single use, expiry, binding); per client: a fixed-user client's direct change runs on the first call (token ignored, summary in `directChange`), an opted-in or editor-token client gets the preview; `tools/list` descriptions and initialize `instructions` per client, with unchanged schemas; an always-confirmed tool (package install) is listed unchanged to every client. |
| `Portable.Tests/Tools/Configuration` | `sitecore_get_config`: XPath selection (elements and attributes), readable paths, node-only and depth pruning, patch sources kept or stripped, invalid and empty XPaths, node and character caps, secret redaction (attribute names, named settings, subtrees; names that only name a secret kept), the live document never changed; the role/layer view rules as showconfig builds them. |
| `Portable.Tests/Tools/Packages` | Package file names stay in the packages folder; web-root paths; file classes (forbidden: web.config, connection strings, license, App_Data, keys, Sitecore-MCP's own files; code: /bin, App_Config, executable extensions); folder expansion with forbidden files skipped and a file cap; the manifest from entry keys (metadata, items with versions, files, blobs, security, author-set collision modes, unsafe and unknown entries); collision modes; the install policy (databases, code and post steps behind `Mcp.Packages.AllowCode`, content roots incl. existing items and files, empty packages, warnings). |

The permission sets are deliberately spelled out rather than derived. They are security decisions, so
removing a gate should fail a test instead of passing quietly.

**What these tests cannot cover.** `Sitecore.Kernel` is not loadable in the test host, so no tool's
`Execute` runs here and `McpCallContext` cannot be constructed. Gate *enforcement* is a single shared
path in `RequestToolCatalog`, verified against a live instance; the per-tool unit tests pin the
declarations that feed it. Everything touching items, search, publishing, or security is verified on
a real instance as described below.

## Verifying against a real instance

### Manual check: acting as the logged-in editor (10.3)

The portable tests prove which identity a call resolves to; only a live instance proves the call then
*runs* as that editor. With `SITECORE_MCP_EDITOR_TOKEN_SECRET` and `SITECORE_MCP_KEY_COREPILOT` set on
the app pool and a `corepilot` client (`requiresEditorToken="true"`, no `user`) deployed:

1. Create editor `sitecore\mcp-editor` (role `Author`) and an item `/sitecore/content/Home/Restricted`
   whose security denies that user `item:read`.
2. Log in as `sitecore\mcp-editor`; in the browser console on the CM origin run
   `await (await fetch('/sitecore/api/sitecore-mcp/editor-token', {method:'POST'})).json()` and copy
   `token`. (Logged out, the endpoint returns `401`.)
3. Call `sitecore_get_context` with `Authorization: Bearer <corepilot key>` and
   `X-Mcp-Editor-Token: <token>` — `user` is `sitecore\mcp-editor`, `isAdministrator` false.
4. `sitecore_get_item` on the restricted path returns not-found / no access; the same call as an
   admin's token returns the item.
5. Refusals: omit the header, alter one character of the token, wait past the lifetime (or set
   `Mcp.EditorToken.LifetimeSeconds` to 30), and use a token minted for another audience — each is
   `401`. Disable the user — `403`.
6. A `user`-mapped client still works with no editor token; adding `user=` to the corepilot client
   makes it skipped at startup (warning in `mcp.log`).
7. `mcp.log` AUDIT lines show `user=sitecore\mcp-editor client=corepilot identity=editor-token`.

The unit tests cover the Sitecore-independent parts (protocol, schema generation, gates, paging).
Anything touching the Kernel needs a live instance: deploy, then exercise the tool over HTTP or
through an MCP client and confirm the result against a separate read.

### Manual check: created items enter their workflow (10.3 and 10.4)

The policy is unit-tested; only an instance proves Sitecore applies it. On master, with a template
whose standard values set `__Default workflow` to the Sample Workflow (initial state Draft, final
state Approved), calling as a non-admin editor:

1. `sitecore_get_context` — note `site` (not `shell`) and `workflowContextEnabled: false`: Sitecore
   on its own would not start workflows on this endpoint.
2. `sitecore_create_item` from that template — result `workflow.state.displayName` is `Draft`;
   `statistics.createdBy` and `statistics.owner` are the editor. In the Content Editor, the item's
   Workflow section shows Draft and the Quick Info shows the editor as creator and owner.
3. Approve the item (`sitecore_invoke_workflow` Submit then Approve), then `sitecore_add_item_version`
   — the new version is in `Draft` (`workflow.started` may be false: Sitecore started it), the previous
   version stays Approved, and a publish of the item publishes the approved version only.
4. `sitecore_copy_item` of the approved item — every version of the copy is in `Draft`
   (`workflow.inWorkflow` equals `workflow.versions`).
5. `sitecore_upload_media` into a media folder whose media template has a default workflow — the
   media item is in the initial state.
6. A dictionary item (`/sitecore/templates/System/Dictionary/Dictionary entry`, or the site's own
   dictionary entry template) with a default workflow, created under a dictionary folder — Draft, as
   in the Content Editor.
7. The same create in the `web` database (if the client may write there) starts nothing
   (`workflow.reason: workflowsUnavailable`).

### Manual check: configuration and packages (10.3 and 10.4)

Only an instance proves the Sitecore-coupled halves. As an administrator client with writes on:

1. `sitecore_get_config` with `/sitecore/settings/setting[@name='Mcp.Enabled']` — value and `patchSources`
   match showconfig.aspx; `//pipelines/httpRequestBegin` with `depth: 1`; an invalid XPath and a
   no-match XPath fail clearly; a setting holding a password reads `[redacted]`.
2. The same XPath with `rules: {"role": "ContentDelivery"}` and with `layers: "Sitecore"` — compare with
   `showconfig.aspx?role=ContentDelivery` and `?layer=Sitecore`; an unknown rule/layer is refused.
3. `sitecore_create_package` with an item (single and with subitems) and `/App_Config/Include` as a file
   source: `ConnectionStrings.config` is not in it (and `/web.config` as a source is refused); the zip opens in
   the Installation Wizard and, with `saveProject`, the project opens in the Package Designer.
4. `sitecore_inspect_package` on it and on a wizard-built package: items, versions, files, collisions.
5. `sitecore_install_package` with a fixed-user client whose `confirmDirectChanges="false"`: the first call
   is a preview (audit `confirmed=pending`); repeating with the token installs; existing items are left
   untouched under the default `skip`; `merge` replaces same-numbered versions; the job shows in
   `sitecore_get_jobs` (category `packageInstall`); an entry appears under
   `/sitecore/system/Packages/Installation history` in core.
6. Refusals: a package with a `/bin` DLL while `Mcp.Packages.AllowCode` is false; a package into `web` for a
   master-only client; a client with content roots and a package item outside them.
7. A large package (a few thousand items) returns the job handle after 20 s and finishes in the background.
8. `sitecore_upload_package` / `sitecore_download_package` round-trip a small zip; an oversized download is
   refused naming `Mcp.Packages.MaxDownloadBytes`.

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
| [archive/](archive/) | Finished planning documents, kept for history: |
| [archive/SPE_TOOL_PLAN.md](archive/SPE_TOOL_PLAN.md) | Triage of the SPE tool list: what to adapt, combine, or skip, and why. |
| [archive/SPE_TOOL_BACKLOG.md](archive/SPE_TOOL_BACKLOG.md) | The implementation checklist derived from that triage. |
| [archive/IMPLEMENTATION_PLAN.md](archive/IMPLEMENTATION_PLAN.md) | Per-tool design: arguments, Kernel APIs, safety, result shape. Includes decision records for the two tools deliberately not built. |
