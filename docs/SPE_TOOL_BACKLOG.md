# SPE tool implementation backlog

The subset of [SPE_TOOL_PLAN.md](SPE_TOOL_PLAN.md) actually slated for implementation — every
"Adapt" and "Combine" decision, with everything marked Skip/Defer left out. Grouped by target
folder and ordered by the plan's suggested build order. See SPE_TOOL_PLAN.md for the full triage,
rationale, and the deferred/skipped tools.

## 1. `Tools/Publishing` (new)

- [ ] `sitecore_publish_item` — publish an item (mode: single/smart/incremental, target databases,
      languages, publish-subitems)

## 2. `Tools/Items` — reference & impact analysis

- [ ] `sitecore_get_item_references` — outgoing links (what this item references)
- [ ] `sitecore_get_item_referrers` — incoming links (what references this item)
- [ ] `sitecore_update_item_referrers` — retarget or clear links to an item

## 3. `Tools/Presentation` (new)

- [ ] `sitecore_get_renderings` — renderings per device: placeholder, datasource, parameters, unique ID
- [ ] `sitecore_add_rendering`
- [ ] `sitecore_set_rendering` — updates datasource/placeholder/parameters of an existing instance
- [ ] `sitecore_remove_rendering`
- [ ] `sitecore_switch_rendering` — atomic swap of one rendering definition for another
- [ ] `sitecore_reset_layout` — revert to standard-values inheritance

## 4. Small companions to existing tools

- [ ] `sitecore_reset_item_fields` — revert fields to standard-values inheritance (`Tools/Items`)

## 4a. `Tools/Jobs` (new) — trigger update jobs

Index rebuilds and link database rebuilds are long-running background jobs, not quick calls — they
belong here rather than as a blocking "small companion", so the design accounts for async job
semantics (start, return a handle, poll) from the outset rather than blocking the request thread.

- [ ] `sitecore_rebuild_index` — full index rebuild, or scoped to a root item; returns a job handle
- [ ] `sitecore_rebuild_link_database` — **new**, not in the original SPE list. The Link Database is
      what backs `get_item_references`/`get_item_referrers` (§2); if it's stale, those tools return
      wrong answers. Worth having once reference tooling exists.

*(Deferred alongside Diagnostics for this planning pass — see the implementation plan.)*

## 4b. `Tools/Jobs` — manage jobs

Sitecore doesn't natively support stopping an arbitrary running job. This category is scoped so it
never claims a stop succeeded when it didn't — see the implementation plan for the per-job-type
design.

- [ ] `sitecore_get_jobs` — list running Sitecore jobs; poll publish/rebuild-index progress
- [ ] `sitecore_stop_job` — **new, custom (no native SPE/Sitecore equivalent)**. Real stop for index
      jobs via `IndexCustodian.Stop`; honest "not supported" for job types with no safe stop path.
      Explicitly excludes `Thread.Abort`-style hard kills — see rationale in the implementation plan.

## 5. `Tools/Membership` (new)

- [ ] `sitecore_get_user`
- [ ] `sitecore_get_domain`
- [ ] `sitecore_get_role`
- [ ] `sitecore_new_user` / `sitecore_remove_user`
- [ ] `sitecore_enable_user` / `sitecore_disable_user`
- [ ] `sitecore_unlock_user`
- [ ] `sitecore_new_domain` / `sitecore_remove_domain`
- [ ] `sitecore_new_role` / `sitecore_remove_role`
- [ ] `sitecore_add_role_member` / `sitecore_remove_role_member`
- [ ] `sitecore_set_user_password` — **flagged sensitive**, needs its own review pass before shipping
- [ ] `sitecore_test_item_acl`
- [ ] `sitecore_add_item_acl` / `sitecore_set_item_acl` / `sitecore_clear_item_acl`
- [ ] `sitecore_lock_item` / `sitecore_unlock_item` *(item operation, but grouped here since it
      shipped alongside the Membership batch in planning — placement TBD: could live in `Tools/Items`)*
- [ ] `sitecore_protect_item` / `sitecore_unprotect_item` *(same placement note as lock/unlock)*

## 6. Higher-stakes / more involved

- [ ] `sitecore_get_workflow_history` (`Tools/Workflow`, new)
- [ ] `sitecore_invoke_workflow` (`Tools/Workflow`, new)
- [ ] `sitecore_change_item_template` (`Tools/Items`) — **flagged**: Sitecore's field remapping can
      silently drop data; needs the same "verify what actually persisted" treatment as `WriteFields`
- [ ] `sitecore_add_base_template` / `sitecore_remove_base_template` (`Tools/Templates`)
- [ ] `sitecore_add_item_version` / `sitecore_remove_item_version` (`Tools/Items`)
- [ ] `sitecore_query_items` (`Tools/Items`) — Sitecore query language (fast query / XPath-like axes)

## Diagnostics — deferred

- [ ] `sitecore_get_logs` — Sitecore's main log (distinct from this server's own `mcp.log`)

---

**Total: ~36 tools** (34 from the original triage, plus `rebuild_link_database` and `stop_job`,
added during restructuring). `Tools/Jobs` §4a and Diagnostics are **deferred** — see
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for the detailed design of everything else.
