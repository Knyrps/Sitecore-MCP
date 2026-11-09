# Sitecore MCP Server

An [MCP](https://modelcontextprotocol.io) server that runs **inside** Sitecore as a .NET Framework assembly. It gives an
AI agent 74 tools over the real Kernel API (items, templates, presentation, media, search, publishing, security,
workflow, configuration and packages), and every call runs as a real Sitecore user, so item and field ACLs, locks,
workflow and auditing apply.

**Target:** Sitecore XM/XP 10.3 and 10.4, CM role only · .NET Framework 4.8 · IIS or containers.

- Using the tools well (and what each one does): [docs/TOOL_GUIDE.md](docs/TOOL_GUIDE.md)
- Working on the server itself: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)

```
MCP client (Claude Code, VS Code, opencode, CorePilot, ...)
      │  Streamable HTTP, or stdio through the optional bridge
      ▼
POST https://cm.example.com/sitecore/api/mcp   (X-Mcp-Key: <key>  or  Authorization: Bearer <key>)
      ▼
SitecoreMcp.Server.dll in the CM worker process  →  Sitecore Kernel API, as the caller's Sitecore user
```

**Deploy it to Content Management only.** It is an authoring surface; CD servers must never expose it.

## Install

### 1. Build for your Sitecore version

The assembly binds to the exact `Sitecore.Kernel` it was built against (10.3 = 18.0.0.0, 10.4 = 19.0.0.0), so build once
per Sitecore version. Point the build at any instance of that version in a gitignored `Directory.Build.user.props`:

```xml
<Project><PropertyGroup><SitecoreWebRoot>C:\inetpub\wwwroot\my-instance</SitecoreWebRoot></PropertyGroup></Project>
```

```powershell
dotnet build src/SitecoreMcp.Server -c Release
```

You ship two files: `src/SitecoreMcp.Server/bin/Release/SitecoreMcp.Server.dll` (to `bin/`) and
`src/SitecoreMcp.Server/App_Config/Include/SitecoreMcp/SitecoreMcp.config` (to `App_Config/Include/SitecoreMcp/`).
Deploy them the way you deploy everything else (solution publish, Dockerfile `COPY` layer on the CM image, App Service
artifacts). Nothing is written to the database, so rollback is deleting the two files.

> **Using CorePilot?** Its release package installs Sitecore-MCP with it and writes the client config below for you.
> See CorePilot's `docs/sitecore-package.md`.

### 2. Enable it per environment

The shipped config is safe by default: endpoint off, HTTPS required, writes off, no clients. Enable it in a separate
environment patch you own, e.g. `App_Config/Include/zzz/SitecoreMcp.Environment.config`:

```xml
<configuration xmlns:patch="http://www.sitecore.net/xmlconfig/" xmlns:role="http://www.sitecore.net/xmlconfig/role/">
  <sitecore role:require="Standalone or ContentManagement">
    <settings>
      <setting name="Mcp.Enabled"><patch:attribute name="value">true</patch:attribute></setting>
      <setting name="Mcp.AllowWrites"><patch:attribute name="value">true</patch:attribute></setting>
    </settings>
    <sitecoreMcp>
      <clients>
        <!-- One client per person or purpose; the key lives in the environment variable named here. -->
        <client id="alice" keyEnvVar="SITECORE_MCP_KEY_ALICE" user="sitecore\svc-mcp-alice"
                allowWrites="true" databases="master" />
        <!-- Optional: confirmDirectChanges="true" makes delete/move/rename/publish/workflow/shared-field
             writes return a preview first and run only when repeated with its token. -->
      </clients>
    </sitecoreMcp>
  </sitecore>
</configuration>
```

Each `<client>` needs a unique `id` (Sitecore's config merge collapses siblings without one). Give each person their own
client so the audit log means something, and scope the mapped **Sitecore user's** rights; `allowWrites` and `databases`
are a coarser fence on top. A fixed-user client runs direct changes (delete, move, rename, publish, workflow commands,
shared-field writes) on the first call, since its own tool (Claude Code, an IDE) asks the person before a call; set
`confirmDirectChanges="true"` on the client, or the `Mcp.ConfirmDirectChanges` setting for every fixed-user client, to
require a confirmation step instead. For a client embedded in the Content Editor, see [Editor tokens](#editor-tokens).

### 3. Set the key

Keys are read from the environment variable named by `keyEnvVar` **at application start**, never from config. Generate
one per client (`-join ((1..32 | % { '{0:x2}' -f (Get-Random -Max 256) }))`) and set it on the CM process:

| Host | How |
|---|---|
| IIS | App-pool environment variable (IIS Manager > app pool > Advanced Settings > Environment Variables, or `Add-WebConfigurationProperty` on `system.applicationHost/applicationPools/add[@name='<pool>']/environmentVariables`). Then **stop and start** the pool; a recycle does not re-read the environment. |
| Docker / Kubernetes | A container environment variable from an `.env` file or a secret. |
| Azure App Service | An application setting (a Key Vault reference works). |

A client whose variable is unset is silently disabled. That is the first thing to check when a key returns `401`.

### 4. Verify

Call `sitecore_get_context`. It confirms the key, the resolved user, whether writes are allowed, the databases, the
content scope, and whether Sitecore's workflow context is on for these requests:

```powershell
$body = '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"sitecore_get_context","arguments":{}}}'
Invoke-RestMethod -Method Post -Uri 'https://cm.example.com/sitecore/api/mcp' -ContentType 'application/json' `
  -Headers @{ 'X-Mcp-Key' = $env:SITECORE_MCP_KEY_ALICE; 'MCP-Protocol-Version' = '2025-06-18' } -Body $body
```

Against a dev certificate add `-SkipCertificateCheck` (PowerShell 7), or `-k --ssl-no-revoke` with curl. Then check
`App_Data/logs/mcp.log.<date>.txt` for `Endpoint registered` and the matching `AUDIT` line.

## Connect a client

Clients that speak Streamable HTTP connect to the endpoint directly with the key header. For stdio-only clients, or a
local instance whose self-signed certificate Node/Bun rejects, use the bridge, which trusts the Windows certificate store:

```powershell
dotnet publish src/SitecoreMcp.Bridge -c Release   # -> .../net8.0/win-x64/publish/sitecore-mcp-bridge.exe
```

```json
{ "mcpServers": { "sitecore": {
    "command": "C:\\path\\to\\sitecore-mcp-bridge.exe",
    "env": { "SITECORE_MCP_URL": "https://cm.example.com/sitecore/api/mcp", "SITECORE_MCP_KEY": "<key>" } } } }
```

The same `command` + `env` works in VS Code (`.vscode/mcp.json`, `"type": "stdio"`) and opencode. Keep keys out of
committed config.

## Security model

| Layer | Behaviour |
|---|---|
| **Enablement** | `Mcp.Enabled` is off in the base config; no route is registered when off. |
| **Authentication** | A key from an environment variable maps to one client, compared in constant time. Rate-limited per effective user. |
| **Identity** | Calls run as the client's fixed user, or, for an editor-token client, as the editor named by a valid editor token, with no fallback. `SecurityDisabler` is never used. |
| **Writes** | Off globally *and* per client by default; write tools are hidden from `tools/list` unless both are on. |
| **Direct changes** | Delete, move, rename, publish, workflow commands, and writes to shared or unversioned fields. Per client (`confirmDirectChanges`): with confirmation, a call without `confirmationToken` validates, returns a preview and a single-use, expiring token, and changes nothing; the same call with that token runs it, and a host app can keep the token from the model so only a person confirms. Editor-token clients always confirm (one set to `false` is skipped at startup). Fixed-user clients default to no confirmation (`Mcp.ConfirmDirectChanges`): the change runs on the first call, with every other check intact, and is audited as `confirmed=skipped`. |
| **Content scope** | Optional content roots (where items may change), search roots and hidden fields, globally or per client. See [Content scope](#content-scope). |
| **New items and versions** | Created with Sitecore's workflow context on, then checked: an item whose template or item has a workflow lands in the workflow's initial state, never stateless (Sitecore treats a workflow item without a state as approved). Statistics and owner are the calling user. Details in [TOOL_GUIDE](docs/TOOL_GUIDE.md#new-items-and-versions--workflow-state-and-statistics). |
| **Databases** | Allow-listed per client. |
| **Admin gate** | Schema, security and dev/ops tools need an administrator and are hidden from others. Config can add an admin requirement (`admin="true"` on a `<tool>`) but never remove one. |
| **Transport** | HTTPS required by default, plus Origin, client-address (`<allowedAddresses>`, localhost by default), body-size and media-type gates; `<trustedProxies>` for `X-Forwarded-For`. |
| **Configuration & packages** | Admin-only. `get_config` reads the merged configuration with secret-looking values always redacted; Sitecore-MCP never writes configuration (a temporary override tool was evaluated and rejected, see [TOOL_GUIDE](docs/TOOL_GUIDE.md#temporary-config-overrides--evaluated-not-built)). Packages are addressed by name inside the packages folder. `install_package` always needs confirmation, even for a client with confirmation off; skips existing items and files by default; never installs web.config, connection strings, the license, App_Data or Sitecore-MCP's own files; installs code (`/bin`, `App_Config`, `.dll`, `.config`, `.aspx`, `.cshtml`...) and runs post steps only with `Mcp.Packages.AllowCode`; and with content roots refuses any package reaching outside them. |
| **Audit** | Every call is logged to `mcp.log`: user, client, identity source, tool, target, status, duration, and for a direct change how it was confirmed (`confirmed=pending\|yes\|rejected\|skipped`). |

**Per environment:** local dev may use an admin user with `Mcp.RequireHttps=false` and `Mcp.VerboseErrors=true`. Shared
dev/QA: one limited user per developer, `databases="master"`. Staging: read-only or off. **Production: off.**

### Editor tokens

A client inside the Content Editor (CorePilot) should act as the editor using it, not a service account. Configure it
with `requiresEditorToken="true"` and **no** `user` (a client with both is skipped at startup). Its direct changes always
need confirmation; `confirmDirectChanges="false"` on it is refused and the client is skipped:

```xml
<client id="corepilot" keyEnvVar="SITECORE_MCP_KEY_COREPILOT" requiresEditorToken="true" allowWrites="true" databases="master" />
```

1. Sitecore issues a short-lived HS256 editor token for the logged-in user: in-process via
   `EditorTokenIssuer.TryIssueForContextUser(...)`, or from the browser with
   `POST /sitecore/api/sitecore-mcp/editor-token?audience=<client id>` (authenticated by the Sitecore login cookie).
2. The client sends it on every call as `X-Mcp-Editor-Token`, next to its own key.
3. Every call is verified and runs as that editor. Missing, expired, tampered or other-audience tokens get `401` (the
   message says which), a disabled or locked-out user `403`. The admin gate follows the live user, not the token.

The signing secret (32+ characters, the same on every CM) is in the variable named by `Mcp.EditorToken.SecretEnvVar`
(default `SITECORE_MCP_EDITOR_TOKEN_SECRET`); unset, no token is issued or accepted. Tokens are issued only to enabled
users of `Mcp.EditorToken.AllowedDomains` (default `sitecore`).

### Content scope

| Limit | Effect |
|---|---|
| **Content roots** | Outside every content root an item is read-only: every write tool refuses before anything changes. |
| **Search roots** | `sitecore_search`, `grep`, `facet` and `query_items` only look under them. |
| **Hidden fields** | Never read, found, shown or written; writing one fails like a field the template lacks. |

```xml
<contentRoots hint="list:AddContentRoot"><root>/sitecore/content/Site</root></contentRoots>
<searchRoots hint="list:AddSearchRoot"><root>/sitecore/content/Site</root></searchRoots>
<hiddenFields hint="list:AddHiddenField"><field>Api Key</field></hiddenFields>
<!-- per client: contentRoots="/a|/b" searchRoots="..." hiddenFields="..." -->
```

Unset limits restrict nothing. A client's roots replace the global ones; its hidden fields add to them. A host can
supply the scope from elsewhere with `<contentScopeProvider type="..."/>` implementing
`SitecoreMcp.Server.Security.IContentScopeProvider` (CorePilot does).

## Settings

All under `<sitecore><settings>`.

| Setting | Default | Purpose |
|---|---|---|
| `Mcp.Enabled` | `false` | Master switch. |
| `Mcp.EndpointPath` | `sitecore/api/mcp` | Route of the endpoint. |
| `Mcp.RequireHttps` | `true` | Reject plain HTTP. Turn off only locally. |
| `Mcp.AllowWrites` | `false` | Global write switch, ANDed with each client's `allowWrites`. |
| `Mcp.MaxRequestBytes` | `1048576` | Request body cap, which also caps media uploads. |
| `Mcp.MaxConcurrentCalls` | `4` | Concurrent tool calls before shedding with `503`. |
| `Mcp.RateLimit.Capacity` / `.RefillPerSecond` | `30` / `1` | Burst and sustained requests per effective user. |
| `Mcp.MaxFieldLength` | `2000` | Field values are truncated beyond this in output. |
| `Mcp.Images.MaxDimension` / `.MaxBytes` | `1024` / `1048576` | Limits for images returned by `sitecore_get_media_image`. |
| `Mcp.VerboseErrors` | `false` | Full error detail in responses. Development only. |
| `Mcp.ServerName` / `Mcp.ServerVersion` | `SitecoreMcp` / `0.1.0` | Reported at `initialize`. |
| `Mcp.EditorToken.SecretEnvVar` | `SITECORE_MCP_EDITOR_TOKEN_SECRET` | Variable holding the editor-token secret. |
| `Mcp.EditorToken.Issuer` / `.LifetimeSeconds` / `.ClockSkewSeconds` | `SitecoreMcp` / `900` / `60` | Editor-token issuer, lifetime (min 30) and clock tolerance. |
| `Mcp.EditorToken.EndpointPath` | `sitecore/api/sitecore-mcp/editor-token` | Route of the issuing endpoint. |
| `Mcp.EditorToken.AllowedDomains` | `sitecore` | Domains (pipe-separated) whose users may get a token. |
| `Mcp.Packages.AllowCode` | `false` | Let `sitecore_install_package` install code and configuration files and run post steps. Enabling it lets an administrator's client deploy code into the site. |
| `Mcp.Packages.MaxDownloadBytes` | `1048576` | Largest package `sitecore_download_package` returns. |

## Troubleshooting

| Symptom | Cause |
|---|---|
| Certificate error (`CRYPT_E_NO_REVOCATION_CHECK`, self-signed) | A dev certificate; see [Verify](#4-verify). |
| `401` for a key that should work | The pool was recycled instead of stopped and started, `keyEnvVar` is unset, or a shell variable did not expand. |
| `401` "A valid editor token is required" | Send `X-Mcp-Editor-Token`; the message says whether it was missing, expired, for another client or badly signed, or that the secret is unset. |
| An editor-token client never authenticates | It was skipped at startup (it has a `user`, `confirmDirectChanges="false"`, or no `id`); see `mcp.log`. |
| Only one client works | Each `<client>` needs a unique `id`. |
| Tool missing from `tools/list` | Write-gated (`Mcp.AllowWrites` and the client's `allowWrites`) or admin-gated. |
| A write returns `confirmationRequired` | `sitecore_install_package` always does. Otherwise the client confirms direct changes (always for an editor-token client; for a fixed-user client only with `confirmDirectChanges="true"` or `Mcp.ConfirmDirectChanges`). Repeat the call with the returned `confirmationToken` after a person confirms, or turn confirmation off for that fixed-user client. `sitecore_get_context` shows `confirmDirectChanges`. |
| `429` / `503` "server is busy" | Rate limit or `Mcp.MaxConcurrentCalls`; raise them or pace the calls. |
| Endpoint returns HTML | The app is restarting or the route is not registered; check the Sitecore log. |

## Tools

74 for an administrator, 41 for other users. Call `sitecore_get_context` first. Every tool and how to use it well is in
[docs/TOOL_GUIDE.md](docs/TOOL_GUIDE.md); the condensed version ships as the server's MCP `instructions`.

| Area | Tools (`sitecore_` prefix omitted) |
|---|---|
| Reading | `get_context`, `get_item`, `get_children`, `get_ancestors`, `query_items` |
| Search | `search`, `grep`, `facet`, `index_status` |
| Items | `create_item`, `update_item`, `reset_item_fields`, `move_item`, `copy_item`, `rename_item`, `delete_item`, `change_item_template`, `add_item_version`, `remove_item_version`, `lock_item`, `unlock_item`, `protect_item`, `unprotect_item` |
| Templates | `get_template`, `list_templates`, `create_template`, `add_base_template`, `remove_base_template` |
| Presentation | `get_renderings`, `add_rendering`, `set_rendering`, `move_rendering`, `switch_rendering`, `remove_rendering`, `set_layout`, `reset_layout` |
| Media | `get_media_image` (returns MCP image content), `upload_media` |
| References | `get_item_referrers`, `get_item_references`, `update_item_referrers` |
| Workflow & publishing | `get_workflow_history`, `invoke_workflow`, `publish_item`, `get_jobs` |
| Security (admin) | `get_user`, `new_user`, `remove_user`, `enable_user`, `disable_user`, `unlock_user`, `get_role`, `new_role`, `remove_role`, `add_role_member`, `remove_role_member`, `get_domain`, `new_domain`, `remove_domain`, `test_item_acl`, `add_item_acl`, `set_item_acl`, `clear_item_acl` |
| Dev/ops (admin) | `rebuild_index`, `populate_solr_schema`, `rebuild_link_database`, `get_logs` |
| Configuration & packages (admin) | `get_config` (merged config by XPath, secrets redacted), `list_packages`, `inspect_package`, `create_package`, `install_package` (always confirmed), `upload_package`, `download_package` |

Tools are registered in `SitecoreMcp.config`, so a solution can add its own (`McpTool<TArgs>`) without recompiling this
assembly.

## License

MIT — see [LICENSE](LICENSE).
