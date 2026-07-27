# Sitecore MCP Server

An [MCP](https://modelcontextprotocol.io) server that runs **inside** Sitecore as a .NET Framework
assembly, giving an AI agent 66 tools over the real Kernel API — items, templates, presentation,
media, search, publishing, security, and workflow — under a real Sitecore user.

**Target:** Sitecore XM/XP 10.x · .NET Framework 4.8 · IIS.
Verified on 10.3 (Kernel 18.0.0.0) and 10.4 (Kernel 19.0.0.0).

## Why in-process

Existing Sitecore integrations — the Sitecore CLI, PowerShell Remoting, the Item Service, GraphQL —
sit outside the platform and reach in over the network. That means a second runtime to install and
version, an extra hop, a DTO layer that drifts from the real item model, and whatever permission
model the chosen API happened to expose.

This module is a DLL in the instance's `/bin`. It gets `Sitecore.Data.Database`, `Sitecore.Context`,
the real security model, and the publishing and indexing pipelines as **in-process calls**. Tools are
thin wrappers over the Kernel API, so what the agent sees is what Sitecore actually does.

### Design principles

- **Never report success for something that did not happen.** A field that saves but reads back
  unchanged is returned in `notPersisted`; a template change that drops a value returns it in
  `dataLost` with the old value; a refused write names the reason and, where useful, the alternatives.
- **Real security, always.** Every call runs as a configured Sitecore user via `UserSwitcher`.
  `SecurityDisabler` is never used, so item and field ACLs, workflow, and auditing apply normally.
- **Fail loudly on ambiguity.** Write arguments resolve templates, renderings, and layouts by path,
  ID, or *exact* name — never a fuzzy guess that could target the wrong item.

## How it fits together

```
MCP client (Claude Code, Claude Desktop, VS Code, opencode)
      │  stdio JSON-RPC
      ▼
SitecoreMcp.Bridge          ← optional: only for stdio-only clients, or local instances
      │                       whose self-signed cert Node/Bun runtimes reject
      │  HTTP POST /sitecore/api/mcp
      ▼
SitecoreMcp.Server (in the Sitecore worker process)
      │  in-process Kernel API, as the caller's real Sitecore user
      ▼
Sitecore
```

Clients that speak Streamable HTTP connect to the endpoint directly and skip the bridge.

## Getting started

### Prerequisites

- A **Sitecore XM/XP 10.x** instance on IIS. The server compiles against *that instance's own*
  assemblies, so 10.3 and 10.4 each need their own build.
- The **.NET SDK** (builds the net48 server, the net8.0 bridge, and the tests) and the **.NET 8
  runtime** (to run the bridge).
- An **elevated PowerShell** for deployment — the web root, app-pool environment, and pool restart
  are admin-only. (Pass `-SkipAdminRequirement` if your account already holds those rights.)

### 1. Point the build at your instance

Create a gitignored `Directory.Build.user.props` at the repo root:

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

### 3. Deploy (elevated)

```powershell
./deploy/Deploy-SitecoreMcp.ps1 -WebRoot C:\inetpub\wwwroot\my-instance
```

The script builds the chosen configuration, copies the DLL and `SitecoreMcp.config`, writes a local
`SitecoreMcp.Dev.config` enabling the endpoint with an admin-mapped client, sets `SITECORE_MCP_KEY`
on the app pool, verifies the copy by hash, and restarts the pool — printing the generated key. Pass
`-Key <key>` to pin your own. See [deploy/README.md](deploy/README.md) for the two-client (admin +
non-admin) variant and the production posture.

### 4. Verify

```powershell
./deploy/Verify-SitecoreMcp.ps1 -Url https://my-instance/sitecore/api/mcp -Key <key>
```

Expect an `initialize` result, the tool list, and a `sitecore_get_context` payload. Then check
`App_Data/logs/mcp.log.<date>.txt` for an `AUDIT` line — every call is logged there, separate from
the main Sitecore log.

## Client configuration

**Direct HTTP** — any client speaking Streamable HTTP points at
`https://my-instance/sitecore/api/mcp` with an `Authorization: Bearer <key>` header. Best where the
instance has a trusted certificate.

**stdio bridge (recommended locally)** — local instances use a self-signed certificate that Node/Bun
clients reject. The bridge is a .NET process whose `HttpClient` trusts the Windows certificate store,
so it connects with no cert wrangling:

```powershell
dotnet publish src/SitecoreMcp.Bridge -c Release
# -> src/SitecoreMcp.Bridge/bin/Release/net8.0/win-x64/publish/sitecore-mcp-bridge.exe
```

<details>
<summary><b>Claude Code / Claude Desktop</b> (<code>claude_desktop_config.json</code>)</summary>

```json
{
  "mcpServers": {
    "sitecore": {
      "command": "C:\\path\\to\\sitecore-mcp-bridge.exe",
      "env": {
        "SITECORE_MCP_URL": "https://my-instance/sitecore/api/mcp",
        "SITECORE_MCP_KEY": "<key>"
      }
    }
  }
}
```
</details>

<details>
<summary><b>VS Code</b> (<code>.vscode/mcp.json</code>)</summary>

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
</details>

<details>
<summary><b>opencode</b> (<code>opencode.json</code>, project-scoped)</summary>

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
</details>

Keep the key out of committed config — use an environment variable or a gitignored file (as in the
opencode example, with `.sitecore-mcp-key` in `.gitignore`).

> Connecting a Node/Bun client **directly** over HTTPS to a self-signed instance requires trusting
> the cert (`NODE_EXTRA_CA_CERTS` plus a full client restart). The bridge avoids this entirely.

## Security model

The endpoint is **disabled by default** and every layer is opt-in.

| Layer | Behaviour |
|---|---|
| **Enablement** | `Mcp.Enabled` is `false` in the base config. A disabled instance registers no route. |
| **Authentication** | An API key (from an app-pool environment variable, never config) maps to one Sitecore user. Keys are compared in constant time and rate-limited. |
| **Identity** | Calls run as that user via `UserSwitcher`. Item/field ACLs, workflow, and auditing apply. `SecurityDisabler` is never used. |
| **Writes** | Off globally by default *and* per client. Write tools are hidden from `tools/list` when either switch is off. |
| **Databases** | Allow-listed per client. A `master`-only client cannot read `core` or publish to `web`. |
| **Admin gate** | Schema, security, and dev/ops tools require an administrator client and are hidden from others. Config can **add** an admin requirement (`admin="true"` on a `<tool>`) but never remove one, so a config mistake cannot expose a privileged tool. |
| **Transport** | HTTPS required by default, plus Origin, client-address, body-size, and media-type gates. |
| **Audit** | Every call is logged to `mcp.log` (user, tool, target, status, duration). |

Two clients on the same instance can hold different keys, users, and permissions — an admin client
for developers and a limited one for content agents. Locally the mapped user may be an admin; on a
shared instance it should not be.

## Tools

66 tools for an administrator client; 40 for a non-admin one (26 are admin-gated). Call
**`sitecore_get_context`** first — it reports the user, whether writes are allowed, and which
databases and languages are available.

<details>
<summary><b>Reading &amp; navigation</b> (5)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_context` | Instance, version, your user, write permission, databases, languages. |
| `sitecore_get_item` | One item by path or ID; populated non-standard fields by default. |
| `sitecore_get_children` | Immediate children, paged. |
| `sitecore_get_ancestors` | Root-down path to the item's parent. |
| `sitecore_query_items` | Sitecore query (XPath-like axes) for structural lookups. |
</details>

<details>
<summary><b>Search</b> (4)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_search` | Index-backed search: name, free text, template, subtree, language, field equality, date ranges. Hits grouped by item. |
| `sitecore_grep` | Literal/regex match over **raw field values**, including standard and security fields the index cannot see. |
| `sitecore_facet` | Counts grouped by an indexed field — template distribution, language coverage. |
| `sitecore_index_status` 🔒 | Document count, last update, and whether an index is stale. |

Rule of thumb: **search** = metadata and indexed content (cheap), **grep** = raw field values
(exact, scoped), **facet** = counts.
</details>

<details>
<summary><b>Items — writing</b> (12)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_create_item` | Create from a template, with optional initial fields. |
| `sitecore_update_item` | Change only the fields passed; verifies each one persisted. |
| `sitecore_reset_item_fields` | Revert fields to standard-values inheritance (the un-override). |
| `sitecore_move_item` | Re-parent, refusing collisions and own-subtree moves. |
| `sitecore_copy_item` | Copy, optionally deep. Copies field **data**, not just structure. |
| `sitecore_rename_item` | Rename, refusing sibling collisions. |
| `sitecore_delete_item` | Recycles by default; `permanent: true` destroys irreversibly. |
| `sitecore_change_item_template` | Swap template, diffing every field so dropped values are reported in `dataLost`. |
| `sitecore_add_item_version` / `sitecore_remove_item_version` | Language versions; `sourceLanguage` seeds a translation. |
| `sitecore_lock_item` / `sitecore_unlock_item` | Explicit checkout. Another user's lock needs admin + `force`. |
| `sitecore_protect_item` / `sitecore_unprotect_item` | Toggle the read-only flag. |
</details>

<details>
<summary><b>Templates</b> (5)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_template` | An item's template: base templates and every field, own and inherited. |
| `sitecore_list_templates` | Find a template by name substring, paged. |
| `sitecore_create_template` 🔒 | Create with base templates, sections, typed fields, and standard values. |
| `sitecore_add_base_template` 🔒 / `sitecore_remove_base_template` 🔒 | Edit an existing template's inheritance. |

Field types are validated against the live registry, so a typo is refused **with the closest real
type names** rather than silently creating a broken field.
</details>

<details>
<summary><b>Presentation</b> (8)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_renderings` | Components on an item for a device: placeholder, datasource, parameters, unique ID, and the device's layout. |
| `sitecore_add_rendering` | Place a component, optionally at a given `index`. |
| `sitecore_set_rendering` | Change datasource, placeholder, or parameters (merged key by key). |
| `sitecore_move_rendering` | Reposition in render order. |
| `sitecore_switch_rendering` | Swap the component in place, keeping everything else. Atomic. |
| `sitecore_remove_rendering` | Remove an instance by unique ID. |
| `sitecore_set_layout` | Assign the device's outer layout. |
| `sitecore_reset_layout` | Revert to inherited presentation. |

Writes target the **final (per-version) layout** by default — the Experience Editor's behaviour —
with `finalLayout: false` for the shared base.
</details>

<details>
<summary><b>Media</b> (1)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_upload_media` | Upload a file from base64 into the media library; the extension decides the media type. Returns the item and its `mediaUrl`. |
</details>

<details>
<summary><b>References &amp; impact analysis</b> (3)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_item_referrers` | What points **at** an item — run before delete/move/rename. |
| `sitecore_get_item_references` | What an item points **at**. |
| `sitecore_update_item_referrers` | Repoint or remove incoming links, reported per referring item. |

Results come from the Link Database, so an empty result is not proof of absence when it is stale —
the tools say so, and `sitecore_rebuild_link_database` refreshes it.
</details>

<details>
<summary><b>Publishing &amp; jobs</b> (2)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_publish_item` | Publish to the configured targets, optionally deep and including related items. Returns a job handle. |
| `sitecore_get_jobs` | List jobs, or poll one by handle (job **or** publish handle). |

Publishing is asynchronous: a handle means *started*, never *finished*. A running publish cannot be
cancelled — Sitecore offers no safe way — so scope publishes narrowly.
</details>

<details>
<summary><b>Workflow</b> (2)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_workflow_history` | Workflow, current state, **available commands**, and transition history. |
| `sitecore_invoke_workflow` | Execute a command (Submit, Approve, …). An unavailable command is refused listing what is available. |
</details>

<details>
<summary><b>Security &amp; membership</b> (17, all admin-only)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_get_user` 🔒 / `sitecore_get_role` 🔒 / `sitecore_get_domain` 🔒 | Read accounts by exact name or substring. Passwords are never returned. |
| `sitecore_new_user` 🔒 / `sitecore_remove_user` 🔒 | Create or delete a user; built-ins and the calling user are protected. |
| `sitecore_enable_user` 🔒 / `sitecore_disable_user` 🔒 / `sitecore_unlock_user` 🔒 | Account state and failed-login lockout. |
| `sitecore_new_role` 🔒 / `sitecore_remove_role` 🔒 | Role lifecycle. |
| `sitecore_add_role_member` 🔒 / `sitecore_remove_role_member` 🔒 | Membership; a member may be a user **or** a role. |
| `sitecore_new_domain` 🔒 / `sitecore_remove_domain` 🔒 | Domains; built-ins are protected. |
| `sitecore_test_item_acl` 🔒 | Does an account have a right here, after inheritance and denies? |
| `sitecore_add_item_acl` 🔒 / `sitecore_set_item_acl` 🔒 / `sitecore_clear_item_acl` 🔒 | Item access rules. |

Setting a user's password is deliberately **not** offered.
</details>

<details>
<summary><b>Dev / ops</b> (4, all admin-only)</summary>

| Tool | Purpose |
|---|---|
| `sitecore_rebuild_index` 🔒 | Full rebuild by name, or a cheap subtree refresh via `rootPath`. Background job. |
| `sitecore_populate_solr_schema` 🔒 | Populate the Solr managed schema — run **before** a rebuild after adding indexed fields. |
| `sitecore_rebuild_link_database` 🔒 | Refresh the store behind the reference tools. |
| `sitecore_get_logs` 🔒 | List Sitecore's log files or tail one, filtered by level and text. |
</details>

🔒 = administrator client only.

Fuller usage notes — the sharp edges, which tool to reach for, and how results are shaped — are in
[docs/TOOL_GUIDE.md](docs/TOOL_GUIDE.md). A condensed version ships as the server `instructions`, so
compliant clients give the model that guidance automatically.

## Repository layout

| Path | Role |
|---|---|
| `src/SitecoreMcp.Server` | The module deployed to the instance: protocol, transport, tools. |
| `src/SitecoreMcp.Bridge` | stdio-to-HTTP shim for stdio-only clients. |
| `tests/SitecoreMcp.Server.Tests` | Unit tests for the parts needing no running Sitecore. |
| `deploy/` | Deployment and verification scripts. |
| `docs/` | Tool guide, adaptation plan, and implementation notes. |

## Design notes

- **Hand-rolled protocol.** The official `ModelContextProtocol` NuGet targets net8.0/netstandard2.0
  and drags in a `System.Text.Json` / `Microsoft.Extensions.*` graph that collides with Sitecore's
  binding redirects. The surface actually needed is about five methods.
- **Stateless.** No session IDs, so app-pool recycles cost nothing.
- **Synchronous.** Every Kernel API is sync; async-over-sync in classic ASP.NET invites
  `SynchronizationContext` deadlocks.
- **Responses are JSON, never SSE** — permitted by the spec, and it avoids IIS response-buffering pain.
- **Tools are registered through config**, not compiled in, so a solution can add its own without
  recompiling this assembly.

## License

MIT — see [LICENSE](LICENSE).
