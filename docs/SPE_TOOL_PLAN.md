# SPE tool adaptation plan

Triage of the tool list exported from the SPE (Sitecore PowerShell Extensions) MCP, deciding what
this server should adapt, what should be combined into fewer tools, and what to skip. This is a
planning document, not an implementation — nothing here is built yet.

## Cross-cutting decisions

**Collapse every `-by-id` / `-by-path` pair into one tool.** SPE splits almost every operation into
two cmdlets that differ only in how the target is identified. This server already solves that with
one convention: `ItemQueryArgs`/`ItemResolver` accept a single `path` that may be a path or an ID
in braces (see [TOOL_GUIDE.md](TOOL_GUIDE.md)). Every adaptation below follows that pattern instead
of shipping id/path twins — this alone is the single biggest reduction in tool count.

**Exclude session- and current-user-scoped tools.** This server is stateless and per-call
authenticated: an API key maps to a fixed Sitecore user for that one call, with no session between
calls (see the server README's "Stateless" design note). SPE's `get-current-user`, `login-user`,
`logout-user` assume an interactive session with persistent identity, which does not exist here.
"Current user" is already covered by `sitecore_get_context`.

**Exclude SPE-blocked tools.** `login-user`, `logout-user`, `export-user`, `import-user`,
`export-role`, `import-role` are marked in the source list as blocked by open SPE issues — not
reliably implementable regardless of relevance.

**Exclude anything that could kill the host process.** `restart-application` recycles the app pool
this server runs inside of — the request that triggered it would never get a response. This is a
harder exclusion than a normal skip: it is self-destructive by construction.

**Exclude tools redundant with what already exists.** Several `Common`/`Provider` entries duplicate
`sitecore_get_item`, `sitecore_get_template`, or `sitecore_get_context` outright.

**Fold "parameter" sub-resources into their parent tool.** SPE's rendering-parameter get/set/remove
and (lower priority) placeholder-setting get/add/remove are properties of a rendering, not
independent things to look up. A `sitecore_set_rendering` that accepts a parameters map, and a
`sitecore_get_renderings` that returns each rendering's parameters inline, replace six SPE tools
with two.

**New tool families get their own folder**, mirroring how `Tools/Templates` was split out from
`Tools/Items`: `Tools/Presentation`, `Tools/Workflow`, `Tools/Publishing`. Security/user/role/ACL
tools should NOT be named `Tools/Security` — that could be confused with this server's own
`Transport` security (auth, gates). Proposed: `Tools/Membership`.

## Security → `Tools/Membership`

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `get-current-user` | **Skip** | Session-scoped; redundant with `get_context`. |
| `login-user`, `logout-user` | **Skip** | Session-scoped and SPE-blocked. |
| `export-user`, `import-user`, `export-role`, `import-role` | **Skip** | SPE-blocked. |
| `get-user-by-identity`, `get-user-by-filter` | **Combine** → `sitecore_get_user` (identity or filter as one arg) | Read-only, low risk. |
| `get-domain`, `get-domain-by-name` | **Combine** → `sitecore_get_domain` (omit name to list) | |
| `get-role-by-identity`, `get-role-by-filter`, `get-role-member` | **Combine** → `sitecore_get_role` (+ optional `includeMembers`) | |
| `new-user`, `remove-user`, `enable-user`, `disable-user`, `unlock-user` | **Adapt**, each its own tool | Genuine dev/QA need (provision a test editor, unblock a lockout). Small, single-purpose, matches this server's create/delete tool shape. |
| `new-domain`, `remove-domain` | **Adapt** | Rare but simple. |
| `new-role`, `remove-role`, `add-role-member`, `remove-role-member` | **Adapt** | Needed to test role-gated ACL scenarios end to end. |
| `set-user-password` | **Adapt, flagged sensitive** | Legitimate for resetting a test/editor account, but credential-adjacent — needs its own review pass before shipping (audit-log every call, consider excluding from non-admin clients by default). |
| `test-account` | **Skip** | Marginal; `get-user` + `enable`/`disable` state already answers this. |
| `lock-item-by-id/path`, `unlock-item-by-id/path` | **Combine** → `sitecore_lock_item` / `sitecore_unlock_item` | Distinct from this server's *automatic* write-time locking in `ItemEditor` — this is an explicit "hold this item" or "free a stuck lock" tool. Lives better in `Tools/Items` (it's an item operation) than Membership. |
| `protect-item-by-id/path`, `unprotect-item-by-id/path` | **Combine** → `sitecore_protect_item` / `sitecore_unprotect_item` | Same placement note as lock/unlock. |
| `test-item-acl-by-id/path` | **Combine** → `sitecore_test_item_acl` | Read-only, valuable for "why can't user X see item Y" debugging. |
| `add-item-acl-by-id/path`, `set-item-acl-by-id/path`, `clear-item-acl-by-id/path` | **Combine**, one tool each (add/set/clear) | Security-sensitive writes; keep the three actions as three tools rather than one action-enum tool, consistent with this server never overloading a single tool with a mode switch. |

## Provider → `Tools/Items`

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `get-item-by-id`, `get-item-by-path` | **Skip** | Exactly `sitecore_get_item`. |
| `get-item-by-query` | **Adapt** → `sitecore_query_items` | Sitecore query (fast query / XPath-like axes) is a distinct capability from index-backed `search` and raw-scan `grep` — some lookups (positional, ancestor-or-self) are natural in query syntax and awkward in either existing tool. |

## Presentation → new `Tools/Presentation`

The richest and most over-split category (~30 SPE entries). No existing tool touches presentation
today, so this is a real gap — but most of the granularity should collapse.

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `get-rendering-by-id/path` | **Adapt** → `sitecore_get_renderings` | The practical "what components are on this page" view: renderings per device, each with placeholder, datasource, parameters, unique ID. Primary read tool for this family. |
| `get-layout-by-id/path`, `get-layout-device`, `get-default-layout-device` | **Defer** | Raw layout XML/device plumbing `get_renderings` already surfaces at a usable level; only build this if raw XML access turns out to be needed. |
| `add-rendering-by-id/path` | **Adapt** → `sitecore_add_rendering` | Placeholder, rendering ref (path/ID/name), device, datasource, parameters. Common task (assign a component). |
| `set-rendering-by-id/path` | **Adapt** → `sitecore_set_rendering` | Update datasource/placeholder/parameters of an existing rendering instance (by unique ID). Parameters passed as a map — this is what absorbs the separate get/set/remove-rendering-parameter tools below. |
| `remove-rendering-by-id/path` | **Adapt** → `sitecore_remove_rendering` | |
| `switch-rendering-by-id/path/unique-id` | **Combine** → `sitecore_switch_rendering` | Atomic swap of one rendering definition for another in place; safer than remove+add (no inconsistent intermediate state if the second call fails). |
| `reset-layout-by-id/path` | **Adapt** → `sitecore_reset_layout` | Simple, safe, valuable "undo local override" — same shape as the reset-fields idea below. |
| `set-layout-by-id/path` (raw layout XML) | **Skip** | Redundant with the granular rendering tools above and risky (malformed XML breaks page rendering) with no more safety than hand-editing. |
| `merge-layout-by-id/path` | **Skip** | Diagnostic edge case; low value for the token cost. |
| `get/add/remove-placeholder-setting-by-id/path` | **Defer** | Restricts which components an editor may add to a placeholder — real but advanced/rare; revisit only if asked for. |
| `get/set/remove-rendering-parameter-by-id/path` | **Skip as standalone** | Folded into `get_renderings` (read) and `set_rendering` (write). |

## Indexing → extends existing `Tools/Search`

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `initialize-search-index`, `initialize-search-index-item-by-id/path` | **Combine** → `sitecore_rebuild_index` (index name, or a root item to scope it) | High value — "rebuild after I changed a template" is a routine dev need. One tool covers both the full and scoped SPE variants. |
| `get-search-index` | **Skip** | Redundant with existing `sitecore_index_status`; fold any missing detail into that tool instead of adding a new one. |
| `find-item` | **Skip** | Overlaps existing `sitecore_search`/`sitecore_facet`. |
| `suspend-search-index`, `stop-search-index`, `resume-search-index` | **Defer, combine if built** → single `sitecore_control_index` with an action | Ops-leaning rather than dev-leaning; low priority. |
| `remove-search-index-item-by-id/path` | **Defer** | Niche (manually drop a stale doc); low priority. |
| `initialize-item` | **Skip** | Already marked "no value for MCP server" in the source list. |

## Common → split across `Tools/Items`, `Tools/Templates`, new `Tools/Workflow`, new `Tools/Publishing`

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `publish-item-by-id/path` | **Adapt, high priority** → `sitecore_publish_item` (`Tools/Publishing`) | This server currently has **no publish capability at all** — nothing written to master ever reaches web without it. Likely the single highest-value addition on the whole list. Mode (single/smart/incremental), target databases, languages, publish-subitems. |
| `get-sitecore-job` | **Adapt** → `sitecore_get_jobs` (`Tools/Publishing`) | Poll publish/rebuild-index progress; small and pairs naturally with the two tools above. |
| `get-item-reference-by-id/path` (outgoing) | **Adapt, high priority** → `sitecore_get_item_references` (`Tools/Items`) | "What does this item link to." |
| `get-item-referrer-by-id/path` (incoming) | **Adapt, high priority** → `sitecore_get_item_referrers` (`Tools/Items`) | "What links to this item." Paired with the above, these give real impact analysis before `delete_item`/`move_item` — a gap today. |
| `update-item-referrer-by-id/path` | **Adapt** → `sitecore_update_item_referrers` (`Tools/Items`) | Retarget or clear links to an item; the natural follow-up to referrers, for a safe rename/restructure workflow. |
| `reset-item-field-by-id/path` | **Adapt** → `sitecore_reset_item_fields` (`Tools/Items`) | This server's `update_item` can only set values, never revert to standard-values inheritance — a real gap given how much this project already cares about honest write outcomes (`notPersisted` verification). |
| `set-item-template-by-id/path` | **Adapt, flagged** → `sitecore_change_item_template` (`Tools/Items`) | Genuinely needed (fix a miscategorized item) but Sitecore's field remapping can silently drop data. Must get the same "verify what actually happened" treatment as `WriteFields` — report which fields survived vs. were dropped. Not a quick add. |
| `add-item-version-by-id/path`, `remove-item-version-by-id/path` | **Adapt, combine pairs** → `sitecore_add_item_version`, `sitecore_remove_item_version` (`Tools/Items`) | Common translation/versioning workflow. |
| `add-base-template-by-id/path`, `remove-base-template-by-id/path` | **Adapt, combine pairs** → `sitecore_add_base_template`, `sitecore_remove_base_template` (`Tools/Templates`) | Fills a real gap: `create_template` sets base templates at creation, but nothing today changes an *existing* template's inheritance. Reuses `TemplateBuilder`/`TemplateResolver`. |
| `test-base-template-by-id/path` | **Skip** | `get_template`'s existing `baseTemplates` list already gives a caller what it needs to answer this itself. |
| `get-item-workflow-event-by-id/path` | **Adapt** → `sitecore_get_workflow_history` (`Tools/Workflow`) | |
| `invoke-workflow-by-id/path` | **Adapt** → `sitecore_invoke_workflow` (`Tools/Workflow`) | Drive an item through a workflow state (submit/approve/reject) — needed for QA to test authoring lifecycles programmatically. |
| `new-item-workflow-event-by-id/path` | **Defer** | Manually inserting a history entry without a real transition — mostly a data-migration tool, not a dev one. |
| `get-item-template-by-id/path` | **Skip** | Exactly `sitecore_get_template`. |
| `get-item-field-by-id/path` | **Skip** | Redundant with `sitecore_get_item` (fields) and `sitecore_get_template` (definitions). |
| `get-database` | **Skip** | Redundant with `sitecore_get_context`'s database list. |
| `get-cache` | **Skip** | Ops diagnostic, not a development task. |
| `get-archive`, `get-archive-item`, `remove-archive-item`, `restore-archive-item` | **Defer, combine if built** → `sitecore_list_archived_items`, `sitecore_restore_archived_item` | Companion to the existing recycle/permanent split on `delete_item`, but lower priority than the items above. |
| `get-item-clone-by-id/path`, `new-item-clone-by-id/path`, `convert-from-item-clone-by-id/path` | **Defer** | Clones are a narrow feature; bundle as one lower-priority family if ever requested. |
| `restart-application` | **Skip — hard exclude** | Would recycle the app pool this server runs inside of; self-destructive. |

## Logging

| SPE tool(s) | Decision | Notes |
|---|---|---|
| `get-logs` | **Adapt** → `sitecore_get_logs` (`Tools/Diagnostics` or `Tools/Items` root-level) | This server only writes its own `mcp.log`; reading Sitecore's main log (errors/warnings from other modules) is a real debugging need with no overlap today. |

**Note:** the pasted list was cut off after Logging (the message was interrupted). If there are more
categories, paste the rest and I'll extend this document rather than starting a new one.

## Suggested build order

1. **Publishing** (`publish_item`, `get_jobs`) — nothing an agent writes today ever reaches `web`
   without this; highest leverage of anything on the list.
2. **References/impact analysis** (`get_item_references`, `get_item_referrers`) — directly de-risks
   the existing destructive tools (`delete_item`, `move_item`).
3. **Presentation core** (`get_renderings`, `add_rendering`, `set_rendering`, `remove_rendering`,
   `reset_layout`) — a whole capability area this server currently lacks entirely.
4. **`reset_item_fields`, `rebuild_index`** — small, high-value companions to existing tools.
5. **Membership essentials** (`get_user`, `get_role`, `new_user`/`remove_user`,
   `lock_item`/`unlock_item`, `test_item_acl`) — needed to test the server's own non-admin/ACL
   behavior end to end, which today can only be done by hand.
6. **Workflow**, **base-template edit**, **`change_item_template`**, **`update_item_referrers`** —
   real but more involved; each warrants its own review pass given the data-safety stakes.
7. **Deferred/low-priority** items (clones, archive, placeholder settings, index suspend/resume) —
   revisit only if a concrete need comes up.

Everything under "Adapt"/"Combine" above still needs its own design pass (argument shapes, error
handling, write-safety like the `notPersisted` treatment) before implementation — this document is
scope and prioritization, not a spec.
