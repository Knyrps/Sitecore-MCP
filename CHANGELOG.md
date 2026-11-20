# Changelog

The notable changes to Sitecore-MCP, newest first. There are no tagged releases yet and the server still reports
`0.1.0` (`Mcp.ServerVersion`), so entries are grouped by the month they reached `main`.

## November 2026

### Added

- **Configuration reading.** `sitecore_get_config` reads the merged `<sitecore>` configuration by XPath, as
  `showconfig.aspx` shows it: the live configuration, or a role/layer view rebuilt the way showconfig does. It returns
  node XML with path, attributes and patch sources, and always redacts secret-looking values. Admin-only. Sitecore-MCP
  never writes configuration. ([#5](https://github.com/Knyrps/Sitecore-MCP/pull/5))
- **Sitecore package tools**, all admin-only: `sitecore_list_packages`, `sitecore_inspect_package` (without installing),
  `sitecore_create_package`, `sitecore_install_package`, `sitecore_upload_package` and `sitecore_download_package`.
  ([#5](https://github.com/Knyrps/Sitecore-MCP/pull/5))
  - `install_package` always needs confirmation, even for a client with confirmation off, and skips existing items and
    files by default.
  - It never installs web.config, connection strings, the license, App_Data or Sitecore-MCP's own files, and with
    content roots it refuses a package reaching outside them.
  - Code and configuration files and post steps install only with the new `Mcp.Packages.AllowCode` setting (default
    `false`). `Mcp.Packages.MaxDownloadBytes` caps what `download_package` returns.
- **Bridge: stays connected when Sitecore is not up at startup.** It answers `initialize` locally, retries in the
  background, and sends `notifications/tools/list_changed` once the endpoint answers.

### Fixed

- **Calls hung after the rate limit was reached.** The server answered 429 (rate limit) and 503 (busy) with
  `"id": null`, which a client cannot match to its pending request. Every early rejection (authentication, rate
  limit, busy, protocol version) now echoes the request id. The bridge also waits for `Retry-After` and resends on
  429/503 (up to 60 s in total; both are returned before anything runs, so a retry never repeats a change) and fills
  in a missing id on any response it relays.
- `sitecore_create_package` failed with "The type initializer for 'Sitecore.Install.Serialization.IOUtils' threw an
  exception". Every call into Sitecore's install API now runs in the shell site with the core database, as the Package
  Designer does. An instance that already hit the failure needs its app pool recycled once.
  ([#6](https://github.com/Knyrps/Sitecore-MCP/pull/6))

## October 2026

### Added

- **Editor tokens.** A client configured with `requiresEditorToken="true"` (CorePilot, inside the Content Editor) acts
  as the logged-in editor instead of a service account. Sitecore issues a short-lived HS256 token for that user, the
  client sends it as `X-Mcp-Editor-Token` on every call, and there is no fallback identity. Rate limiting and the audit
  log key on the effective user. ([#1](https://github.com/Knyrps/Sitecore-MCP/pull/1))
- **Content scope**, globally or per client: content roots (items outside them are read-only), search roots (`search`,
  `grep`, `facet` and `query_items` only look under them) and hidden fields (never read, found, shown or written). A
  host can supply the scope through `IContentScopeProvider`. ([#1](https://github.com/Knyrps/Sitecore-MCP/pull/1))
- **Confirmation for direct changes.** Delete, move, rename, publish, workflow commands and writes to shared or
  unversioned fields can return a preview and a single-use, expiring confirmation token, and run only when the call is
  repeated with that token. The token travels only in `structuredContent`, so a host app can keep it from the model.
  ([#1](https://github.com/Knyrps/Sitecore-MCP/pull/1))
- **Images as MCP image content.** `sitecore_get_media_image` returns a media-library image, resized within
  `Mcp.Images.MaxDimension` and `Mcp.Images.MaxBytes`, as the calling user.
  ([#1](https://github.com/Knyrps/Sitecore-MCP/pull/1))
- `sitecore_get_context` reports the context site, whether Sitecore's workflow context is on, and
  `confirmDirectChanges`.

### Changed

- **Confirmation is a per-client setting** (`confirmDirectChanges` on `<client>`). Editor-token clients always confirm.
  Fixed-user clients default to no confirmation (`Mcp.ConfirmDirectChanges`, default `false`): their direct changes run
  on the first call again, with every other check intact, and are audited as `confirmed=skipped`.
  ([#4](https://github.com/Knyrps/Sitecore-MCP/pull/4))
- The README now covers what the code does today and shrinks from 725 to 235 lines; per-tool detail lives in
  `docs/TOOL_GUIDE.md`, and the finished planning docs moved to `docs/archive/`.
  ([#3](https://github.com/Knyrps/Sitecore-MCP/pull/3))

### Fixed

- **Created items and versions start their workflow**, as in the Content Editor. Items, versions, copies, media uploads
  and templates created through the tools got no workflow state, which Sitecore treats as approved and publishable.
  They now land in the workflow's initial state, and the tools report `workflow` and `statistics` in their results.
  ([#2](https://github.com/Knyrps/Sitecore-MCP/pull/2))
- `sitecore_search`: `nameContains` with several words requires every word in the item name, instead of matching items
  with any one of them.

## July 2026

The initial build.

### Added

- The MCP server as a .NET Framework 4.8 assembly inside Sitecore XM/XP 10.3 and 10.4 (CM only): Streamable HTTP
  endpoint, API-key authentication, rate limiting, request gates, and an audit log in `mcp.log`. Every call runs as a
  real Sitecore user.
- The stdio-to-HTTP bridge (`sitecore-mcp-bridge`) for stdio-only clients and local instances with a self-signed
  certificate.
- Tools for reading, searching (`search`, `grep`, `facet`, `index_status`), writing items, templates, presentation,
  media upload, references and impact analysis, workflow, publishing and background jobs.
- Admin-only tools for users, roles, domains and item access rules, and for dev/ops: index and link-database rebuilds,
  Solr schema populate, and log reading. The admin gate is configurable per tool; config can add an admin requirement
  but never remove one.
- Tool-usage guidance shipped as the server's MCP `instructions` and in `docs/TOOL_GUIDE.md`.
- Deploy and verify scripts, and per-tool contract, permission and registration tests.
