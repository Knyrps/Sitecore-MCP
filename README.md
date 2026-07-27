# Sitecore MCP Server

An [MCP](https://modelcontextprotocol.io) server that runs **inside** Sitecore as a .NET Framework
assembly, giving an AI agent 66 tools over the real Kernel API — items, templates, presentation,
media, search, publishing, security, and workflow — under a real Sitecore user.

**Target:** Sitecore XM/XP 10.x · .NET Framework 4.8 · IIS or containers.
Developed and continuously verified against 10.3 (Kernel 18.0.0.0); builds against 10.4
(Kernel 19.0.0.0), which needs its own build — see [Installing](#installing).

> Working on the server itself? See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Why in-process

Existing Sitecore integrations — the Sitecore CLI, PowerShell Remoting, the Item Service, GraphQL —
sit outside the platform and reach in over the network. That means a second runtime to install and
version, an extra hop, a DTO layer that drifts from the real item model, and whatever permission
model the chosen API happened to expose.

This module is a DLL in your instance's `/bin`. It gets `Sitecore.Data.Database`, `Sitecore.Context`,
the real security model, and the publishing and indexing pipelines as **in-process calls**. Tools are
thin wrappers over the Kernel API, so what the agent sees is what Sitecore actually does.

### Design principles

- **Never report success for something that did not happen.** A field that saves but reads back
  unchanged is returned in `notPersisted`; a template change that drops a value returns it in
  `dataLost` with the old value; a refused write names the reason and the alternatives.
- **Real security, always.** Every call runs as a configured Sitecore user via `UserSwitcher`.
  `SecurityDisabler` is never used, so item and field ACLs, workflow, and auditing apply normally.
- **Fail loudly on ambiguity.** Write arguments resolve templates, renderings, and layouts by path,
  ID, or *exact* name — never a fuzzy guess that could target the wrong item.

## Architecture

```
MCP client (Claude Code, Claude Desktop, VS Code, opencode)
      │  stdio JSON-RPC
      ▼
sitecore-mcp-bridge         ← optional: only for stdio-only clients, or instances
      │                       whose self-signed cert Node/Bun runtimes reject
      │  HTTPS POST /sitecore/api/mcp   (Authorization: Bearer <key>)
      ▼
SitecoreMcp.Server.dll  — in the CM worker process
      │  in-process Kernel API, as the caller's real Sitecore user
      ▼
Sitecore
```

Clients that speak Streamable HTTP connect to the endpoint directly and skip the bridge.

**Deploy it to Content Management only.** It is an authoring and development surface; CD servers
should never expose it. The config supports Sitecore's role scoping, so a single patch can enforce
that across a topology (see [Deploying](#deploying)).

## Requirements

| | |
|---|---|
| **Sitecore** | XM or XP 10.x, on the CM role |
| **Runtime** | .NET Framework 4.8 (the instance's own) |
| **Build-time** | .NET SDK, once, to produce the assembly for your Sitecore version |
| **Client-side** | .NET 8 runtime only if you use the stdio bridge |

The assembly binds to the exact `Sitecore.Kernel` your instance runs, so **10.3 and 10.4 need
different builds**. Produce one artifact per Sitecore version you target and reuse it across the
environments running that version.

## Installing

### 1. Produce the artifact

Point the build at any instance running your target Sitecore version — a gitignored
`Directory.Build.user.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <SitecoreWebRoot>C:\inetpub\wwwroot\my-instance</SitecoreWebRoot>
  </PropertyGroup>
</Project>
```

```powershell
dotnet build src/SitecoreMcp.Server -c Release
```

That produces the two files you ship:

```
src/SitecoreMcp.Server/bin/Release/SitecoreMcp.Server.dll
src/SitecoreMcp.Server/App_Config/Include/SitecoreMcp/SitecoreMcp.config
```

Version the artifact by Sitecore version (e.g. `SitecoreMcp-10.3.zip`) and publish it to wherever
your team keeps build outputs. This is a one-time step per Sitecore version, not per deployment.

### 2. Deploy through your normal mechanism

The module is an ordinary Sitecore extension: **one assembly into `/bin`, one config into
`App_Config/Include`**. Use whatever already delivers code to your instances.

| Topology | How it lands |
|---|---|
| **On-prem / IIS** | Include the two files in your solution's publish output or deployment package, so they deploy with everything else. |
| **Containers** | Add a `COPY` layer to your **CM** Dockerfile — the assembly to `/bin`, the config to `App_Config/Include/SitecoreMcp/`. |
| **Azure PaaS** | Ship alongside your CM App Service artifacts. |
| **Sitecore package** | Wrap both files in a `.zip`/`.update` for the Installation Wizard if that is your convention. |

Nothing is written to the database and no items are installed, so deployment is just file copy and
rollback is deleting the two files.

### 3. Configure it

The shipped `SitecoreMcp.config` is **safe by default**: the endpoint is disabled, HTTPS is required,
writes are off, and no clients are defined. A disabled instance registers no route and behaves
exactly like one without the module.

Enabling is a **separate environment patch** you own — keep it out of the base config so it can
differ per environment and never leaks to production by accident. Create
`App_Config/Include/zzz/SitecoreMcp.Environment.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration xmlns:patch="http://www.sitecore.net/xmlconfig/"
               xmlns:role="http://www.sitecore.net/xmlconfig/role/">
  <sitecore role:require="Standalone or ContentManagement">

    <settings>
      <setting name="Mcp.Enabled">
        <patch:attribute name="value">true</patch:attribute>
      </setting>
      <setting name="Mcp.AllowWrites">
        <patch:attribute name="value">true</patch:attribute>
      </setting>
    </settings>

    <sitecoreMcp>
      <clients>
        <!-- One client per person or purpose. The key lives in an environment
             variable named here - never in this file. -->
        <client id="alice"
                keyEnvVar="SITECORE_MCP_KEY_ALICE"
                user="sitecore\svc-mcp-alice"
                allowWrites="true"
                databases="master" />
      </clients>
    </sitecoreMcp>

  </sitecore>
</configuration>
```

`role:require` means the same patch can ship everywhere and only activate on CM.

### 4. Set the keys

A key is any high-entropy secret (64 hex characters is a reasonable default). It is read from the
named environment variable **at application start** — never from config, so it stays out of source
control.

| Host | Where the variable goes |
|---|---|
| **IIS** | App-pool *environment variables* (`applicationHost.config`). Requires a full worker restart — a recycle does not re-read the process environment. |
| **Containers** | Compose `environment:` / Kubernetes secret mounted as an env var. |
| **Azure App Service** | Application settings. |

A client whose variable is unset is silently disabled, which is the intended fail-closed behaviour.

### 5. Verify

Any HTTP client works — no tooling from this repo is needed:

```bash
curl -sS https://cm.example.com/sitecore/api/mcp \
  -H "Authorization: Bearer $SITECORE_MCP_KEY" \
  -H "Content-Type: application/json" \
  -H "MCP-Protocol-Version: 2025-06-18" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sitecore_get_context","arguments":{}}}'
```

A healthy response reports the instance, the resolved user, whether writes are allowed, and the
permitted databases — confirming authentication, identity mapping, and permissions in one call.
Then check `App_Data/logs/mcp.log.<date>.txt` for the matching `AUDIT` line.

## Connecting a client

**Direct HTTP** — any client speaking Streamable HTTP points at
`https://cm.example.com/sitecore/api/mcp` with an `Authorization: Bearer <key>` header. Preferred
wherever the instance has a trusted certificate.

**stdio bridge** — for stdio-only clients, or local instances whose self-signed certificate Node/Bun
runtimes reject. The bridge is a small .NET process whose `HttpClient` trusts the Windows certificate
store, so it connects with no cert wrangling:

```powershell
dotnet publish src/SitecoreMcp.Bridge -c Release
# -> src/SitecoreMcp.Bridge/bin/Release/net8.0/win-x64/publish/sitecore-mcp-bridge.exe
```

<details>
<summary><b>Claude Code / Claude Desktop</b></summary>

```json
{
  "mcpServers": {
    "sitecore": {
      "command": "C:\\path\\to\\sitecore-mcp-bridge.exe",
      "env": {
        "SITECORE_MCP_URL": "https://cm.example.com/sitecore/api/mcp",
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
        "SITECORE_MCP_URL": "https://cm.example.com/sitecore/api/mcp",
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
        "SITECORE_MCP_URL": "https://cm.example.com/sitecore/api/mcp",
        "SITECORE_MCP_KEY": "{file:./.sitecore-mcp-key}"
      },
      "enabled": true
    }
  }
}
```
</details>

Keep keys out of committed config — use an environment variable or a gitignored file (as above, with
`.sitecore-mcp-key` in `.gitignore`).

> Connecting a Node/Bun client **directly** over HTTPS to a self-signed instance requires trusting
> the cert (`NODE_EXTRA_CA_CERTS` plus a full client restart). The bridge avoids this entirely.

## Operating it

### Recommended posture per environment

| Environment | Enabled | Writes | Client | Notes |
|---|---|---|---|---|
| **Local dev** | Yes | Yes | Admin user is fine | `Mcp.RequireHttps=false` and `Mcp.VerboseErrors=true` are reasonable locally. |
| **Shared dev / QA** | Yes | Yes | One limited user **per developer** | Per-person keys make the audit log meaningful. Scope `databases` to `master`. |
| **Staging / UAT** | Read-only, or off | No | Limited | Enable only if agents genuinely need it there. |
| **Production** | **Off** | — | — | Leave `Mcp.Enabled=false`. If a read-only case is unavoidable, pair it with an address allow-list and a user restricted to the branches it needs. |

### Designing clients

A client is a key, a Sitecore user, and its limits. Two decisions matter:

- **One client per person, not one per team.** The audit log records the mapped user, so shared keys
  cost you attribution — the main thing you want when an agent changes content.
- **Grant the Sitecore user, not the client.** Permissions come from the mapped user's roles and item
  ACLs, so scope the *user* to the branches it should touch. `allowWrites` and `databases` are a
  second, coarser fence on top.

Each `<client>` needs a unique `id` attribute — Sitecore's config merge collapses sibling elements
without one, and only the last would survive.

### Security model

| Layer | Behaviour |
|---|---|
| **Enablement** | `Mcp.Enabled` is `false` in the base config; no route is registered when off. |
| **Authentication** | A key from an environment variable maps to one Sitecore user. Compared in constant time, rate-limited (30 burst, 1/sec refill by default). |
| **Identity** | Calls run as that user via `UserSwitcher`. Item/field ACLs, workflow, and auditing apply. `SecurityDisabler` is never used. |
| **Writes** | Off globally *and* per client by default. Write tools are hidden from `tools/list` when either switch is off. |
| **Databases** | Allow-listed per client. A `master`-only client cannot read `core` or publish to `web`. |
| **Admin gate** | Schema, security, and dev/ops tools require an administrator client and are hidden from others. Config can **add** an admin requirement (`admin="true"` on a `<tool>`) but never remove one, so a config mistake cannot expose a privileged tool. |
| **Transport** | HTTPS required by default, plus Origin, client-address, body-size, and media-type gates. |
| **Audit** | Every call is logged to `mcp.log` — user, tool, target, status, duration — separate from the main Sitecore log. |

### Settings reference

All under `<sitecore><settings>`; patch what you need per environment.

| Setting | Default | Purpose |
|---|---|---|
| `Mcp.Enabled` | `false` | Master switch. Off means no route at all. |
| `Mcp.EndpointPath` | `sitecore/api/mcp` | Route the endpoint serves on. |
| `Mcp.RequireHttps` | `true` | Reject non-HTTPS requests. Turn off only locally. |
| `Mcp.AllowWrites` | `false` | Global write switch, ANDed with each client's `allowWrites`. |
| `Mcp.MaxRequestBytes` | `1048576` | Request body cap — also the practical ceiling on media uploads. |
| `Mcp.MaxConcurrentCalls` | `4` | Tool calls executed concurrently before shedding. |
| `Mcp.MaxFieldLength` | `2000` | Field values are truncated beyond this in output. |
| `Mcp.VerboseErrors` | `false` | Full error detail in responses. Development only. |
| `Mcp.RateLimit.Capacity` | `30` | Burst size per client. |
| `Mcp.RateLimit.RefillPerSecond` | `1` | Sustained request rate per client. |
| `Mcp.ServerName` | `SitecoreMcp` | Name reported at `initialize`. Worth setting per environment if a client connects to several. |
| `Mcp.ServerVersion` | `0.1.0` | Version reported at `initialize`. |

Also available: `<allowedOrigins>`, `<allowedAddresses>` (defaults to localhost — widen it for a
shared instance), and `<trustedProxies>` for `X-Forwarded-For` handling.

### Troubleshooting

| Symptom | Cause |
|---|---|
| `401` for a key that should work | The worker has not re-read its environment. App-pool env vars need a full stop/start, not a recycle. |
| Only one client works | Each `<client>` needs a unique `id`; without one the config merge collapses them. |
| Tool missing from `tools/list` | It is write-gated (check `Mcp.AllowWrites` and the client's `allowWrites`) or admin-gated (the mapped user is not an administrator). |
| Endpoint returns HTML | The app domain is restarting, or the route is not registered — check the Sitecore log for an initialize-pipeline error. |
| `Rate limit exceeded` | Expected under scripted bursts; raise `Mcp.RateLimit.*` or pace the calls. |

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

Bounded by `Mcp.MaxRequestBytes` — suited to icons and documents, not video.
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

## License

MIT — see [LICENSE](LICENSE).
