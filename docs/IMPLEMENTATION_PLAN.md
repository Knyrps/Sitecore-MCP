# Implementation plan

Design-level plan for every tool in [SPE_TOOL_BACKLOG.md](SPE_TOOL_BACKLOG.md) **except** `Tools/Jobs`
§4a (trigger update jobs: `rebuild_index`, `rebuild_link_database`) and Diagnostics
(`sitecore_get_logs`) — both deferred per current direction. Each entry: arguments, the Sitecore
Kernel APIs it wraps, behavior, safety/edge cases, and result shape. This is a spec to build from,
not finished code — Sitecore API names are accurate to the best of available knowledge but should
be confirmed against the actual Kernel assembly during implementation.

Every tool follows this project's existing conventions unless noted otherwise: `ItemQueryArgs`
(path-or-ID + database + language) for anything addressing one item, `McpToolException` for
failures that should reach the model as a clear message, `RequiresWrite => true` on anything
mutating state, and — critically — **never report success when the underlying change didn't
actually stick**, the same discipline `WriteFields`/`notPersisted` already established.

---

## Tier 1 — `Tools/Publishing` (new) + `Tools/Jobs` §4b manage jobs (new)

Grouped together: a publish *is* a Sitecore job (`PublishManager.PublishItem` returns a `Handle`
immediately), so `get_jobs` is what makes `publish_item` practically usable — you need it to know
when a publish actually finished.

New helper: `Tools/Jobs/JobDescriber.cs` — projects a `Sitecore.Jobs.Job` to JSON (handle, name,
category, state, processed/total, messages). Shared by `get_jobs` and as the return shape of
`publish_item`.

### `sitecore_publish_item`
**Scope corrected after reflection:** `PublishSmart`/`Republish`/`PublishIncremental` all take a
**source `Database`**, not an item — they are database-wide operations. Only `PublishItem` is
item-scoped. Folding them into an item tool would have meant an `item` argument that is silently
ignored for most modes. So this tool is item-scoped only, and `mode` maps to the item-level
equivalent (`compareRevisions`). A database-wide `sitecore_publish_database` can be added later if
wanted; it is deliberately not this tool.

- **Args:** `ItemQueryArgs` + `mode` (`smart` | `full`, default `smart`), `targetDatabases`
  (string[], default: all configured publishing targets), `languages` (string[], default the item's
  language), `deep` (bool, default `false` — publish subitems), `publishRelatedItems` (bool, default
  `false`).
- **Sitecore APIs:** `PublishManager.PublishItem(Item, Database[], Language[], bool deep,
  bool compareRevisions, bool publishRelatedItems)`. `mode: "smart"` → `compareRevisions: true`
  (publish only what changed); `mode: "full"` → `compareRevisions: false` (force-republish the item).
  Defaults come from `PublishManager.GetPublishingTargets(sourceDb)`, reading each target item's
  `Target database` field.
- **Behavior:** resolve item, targets, and languages; start the publish; return the `Handle`
  immediately plus the job projection if `JobManager.GetJob(handle)` resolves. Never block.
- **Safety / edge cases:** `RequiresWrite => true`. Target databases are resolved through
  `context.ResolveDatabase`, so **the client's `databases` allow-list is enforced on publish
  targets** — a client scoped to `master` cannot push content to `web`. That is intentional and
  configurable (widen the client's `databases` to permit it). An unresolvable target is a clear
  `McpToolException`. The description must tell callers to poll `sitecore_get_jobs`, since a
  returned handle means *started*, not *finished*.
- **Returns:** `{ handle, item, mode, deep, targetDatabases, languages, job }` — no `success` field;
  publish is async and success is determined by polling.

### `sitecore_get_jobs`
- **Args:** `handle` (string, optional — a specific job) `category` (string, optional filter),
  `activeOnly` (bool, default `true`).
- **Sitecore APIs:** `Sitecore.Jobs.JobManager.GetJobs()`, `JobManager.GetJob(Handle)`.
- **Behavior:** list jobs (optionally filtered), or describe one by handle. Use `JobDescriber`.
- **Safety / edge cases:** read-only; no `RequiresWrite`. An unknown handle returns an empty result
  with a hint, not an error — a job can finish and be reaped between calls.
- **Returns:** `{ jobs: [ { handle, name, category, state, processed, total, messages } ] }`.

### Stopping a job — built, measured, and removed

A `sitecore_stop_job` was designed, implemented, and then deleted once tested against a real
long-running publish. Recorded here so the idea is not revisited from scratch.

**What Sitecore actually offers.** There is no `BaseJob.Abort()` and no `JobManager.Stop()`. The
original design guessed `IndexCustodian.Stop(ISearchIndex)`, which does not exist — `IndexCustodian`
only has a *global* `StopIndexing()`. What does exist is a cooperative protocol:
`BaseJobOptions.Abortable` (jobs declare it), `BaseJobStatus.State` (settable), and a `JobState` with
`AbortRequested`/`Aborted`.

**Why it was removed.** Measured on 10.3: every publish job reports `abortable: false`, and setting
`AbortRequested` anyway is accepted but **ignored** — the signalled `Publish to 'web'` carried on from
1,598 to 2,927 items and ran to completion, with the state left reading `AbortRequested` the whole
time. So the tool could not stop the one thing anyone would want to stop, while implying otherwise.

**Rejected alternatives.**
- `Thread.Abort()` on the job's thread — available on net48, but the thread runs in the *same worker
  process as the live site*, so it can fire mid-write (half-written item, leaked connection, stranded
  item lock). A publish that runs too long is a far cheaper failure than an inconsistent `web`.
- Setting `Status.State = Finished` plus a back-dated `Status.Expiry` (a widely-circulated snippet) —
  this only marks the status finished and reaps the entry from the job list. The thread keeps
  publishing. It makes `get_jobs` report `Finished` for work that is still running and destroys the
  ability to observe it: strictly worse than offering nothing.

**Where that leaves us.** `sitecore_get_jobs` still reports state and progress, and its projection
notes when a job sits in `AbortRequested` without stopping (a state anything else on the instance may
set). The guidance is to scope publishes narrowly rather than rely on cancelling them. If a genuinely
abortable long-running job type appears later, revisit with the cooperative signal only.

---

## Tier 2 — `Tools/Items` reference & impact analysis

New helper: none needed beyond what `Sitecore.Links.LinkDatabase` already provides — thin wrappers.

**Cross-dependency to flag explicitly in each tool's description:** these tools read from the Link
Database (`Sitecore.Globals.LinkDatabase`), which is only as fresh as its last rebuild. If it's
stale (bulk import, direct SQL changes, a disabled link-tracking event handler), results can be
incomplete. Once `sitecore_rebuild_link_database` exists (deferred, §4a), the tool descriptions
should point callers at it when results look suspiciously empty.

### `sitecore_get_item_references`
- **Args:** `ItemQueryArgs`.
- **Sitecore APIs:** `Sitecore.Globals.LinkDatabase.GetReferences(Item)` → `ItemLink[]`.
- **Behavior:** resolve the item, return each outgoing link's target (path/ID if resolvable — a
  target ID with no resolvable item is a broken/dangling reference, worth flagging explicitly
  rather than silently omitting) and the source field.
- **Safety / edge cases:** read-only. A dangling reference (target no longer exists) is reported as
  `{ targetId, targetPath: null, broken: true }`, not dropped.
- **Returns:** `{ item, references: [ { sourceField, targetId, targetPath, broken } ] }`.

### `sitecore_get_item_referrers`
- **Args:** `ItemQueryArgs`.
- **Sitecore APIs:** `LinkDatabase.GetReferrers(Item)` → `ItemLink[]`.
- **Behavior:** inverse of the above — who links to this item, and from which field.
- **Safety / edge cases:** field-level security still applies to what the caller can see about the
  *source* item (name/path can be omitted if unreadable, same pattern as `ItemProjector`). Read-only.
- **Returns:** `{ item, referrers: [ { sourceId, sourcePath, sourceField } ] }`.

### `sitecore_update_item_referrers`
- **Args:** `ItemQueryArgs` (the item being retargeted) + `newTarget` (path/ID, optional — omit to
  remove the links instead of retargeting).
- **Sitecore APIs:** `LinkDatabase.GetReferrers(item)`, then per link `Sitecore.Data.Fields.Field.Relink(ItemLink,
  Item newTarget)` or `Field.RemoveLink(ItemLink)` for fields implementing `IRelinkable`, wrapped in
  `ItemEditor.Edit` per source item.
- **Behavior:** for every referrer, either relink the field to `newTarget` or remove the link,
  batched per source item (one `BeginEdit`/`EndEdit` per source item touching multiple fields, not
  one per field).
- **Safety / edge cases:** `RequiresWrite => true`. This is the highest-risk tool in this tier — it
  mutates content the caller may not have directly named. Report per-source-item success/failure
  individually (some sources may be locked by another user or field-write-denied) rather than an
  all-or-nothing result, mirroring `WriteFields`' honesty about partial outcomes. A field type that
  doesn't implement `IRelinkable` is reported as `unsupported`, not silently skipped.
- **Returns:** `{ item, newTarget, updated: [...], removed: [...], failed: [ { sourceId, reason } ] }`.

---

## Tier 3 — `Tools/Presentation` (new)

New helpers, mirroring the `Tools/Templates` precedent:
- `Tools/Presentation/LayoutEditor.cs` — parse `Sitecore.Layouts.LayoutField`/`LayoutDefinition` from
  an item, mutate its `DeviceDefinition`/`RenderingDefinition` list, and write it back inside
  `ItemEditor.Edit`. The one place that understands Sitecore's layout XML shape.
- `Tools/Presentation/RenderingResolver.cs` — resolves a rendering reference (path/ID/exact name),
  following the same `allowPartial: false`-on-write discipline `TemplateResolver` established, for
  the same reason (a duplicate rendering name should never silently pick the wrong one).
- `Tools/Presentation/PresentationDescriber.cs` — projects a `LayoutDefinition` to JSON.

All presentation tools take a `device` argument (string, default `"Default"`) rather than exposing
`get-layout-device`/`get-default-layout-device` as their own tools (folded per the plan doc).

### `sitecore_get_renderings`
- **Args:** `ItemQueryArgs` + `device` (default `"Default"`) + `shared` (bool, default `true` — read
  `__Renderings`; `false` reads `__Final Renderings`).
- **Sitecore APIs:** `Sitecore.Layouts.LayoutField` on the item, `LayoutDefinition.Parse(...)`.
- **Behavior:** parse the layout field for the requested device, list each rendering: unique ID,
  rendering item (ID + resolved name/path), placeholder, datasource, and parameters (parsed from the
  query-string-shaped parameter blob into a JSON object).
- **Safety / edge cases:** an item with no layout assigned (inherits from standard values or has
  none) returns an empty list with a hint, not an error — same pattern as `get_item`'s empty-field
  handling.
- **Returns:** `{ item, device, renderings: [ { uniqueId, renderingId, renderingName, placeholder, datasource, parameters } ] }`.

### `sitecore_add_rendering`
- **Args:** `ItemQueryArgs` + `rendering` (path/ID/exact name, resolved via `RenderingResolver`,
  `allowPartial: false`) + `placeholder` (required) + `device` (default `"Default"`) + `datasource`
  (optional) + `parameters` (string-keyed dict, optional) + `index` (optional position).
- **Sitecore APIs:** `LayoutEditor` — add a new `RenderingDefinition` to the device's rendering list.
- **Behavior:** resolve the item and rendering, build the new definition, write via `ItemEditor.Edit`.
- **Safety / edge cases:** `RequiresWrite => true`. Unknown/ambiguous rendering reference fails
  loudly with near matches, exactly like `create_template.baseTemplates`.
- **Returns:** the added rendering's projection (uniqueId included, so a follow-up `set_rendering`/
  `switch_rendering` call can target it precisely).

### `sitecore_set_rendering`
- **Args:** `ItemQueryArgs` + `uniqueId` (required — identifies the specific instance) + `device` +
  optional `datasource`, `placeholder`, `parameters` (only supplied keys change; this is also where
  the SPE get/set/remove-rendering-parameter tools fold in — omit a parameter key to leave it,
  explicit `null` to remove it).
- **Sitecore APIs:** `LayoutEditor` — find by `uniqueId`, mutate, write back.
- **Behavior:** same "only touch what's passed" discipline as `update_item`.
- **Safety / edge cases:** `RequiresWrite => true`. Unknown `uniqueId` is a clear error listing what
  *is* present (from `get_renderings`) rather than a bare "not found".
- **Returns:** the updated rendering's projection.

### `sitecore_remove_rendering`
- **Args:** `ItemQueryArgs` + `uniqueId` + `device`.
- **Sitecore APIs:** `LayoutEditor` — remove by `uniqueId`.
- **Safety / edge cases:** `RequiresWrite => true`. Same unknown-`uniqueId` handling as above.
- **Returns:** `{ item, removed: uniqueId }`.

### `sitecore_switch_rendering`
- **Args:** `ItemQueryArgs` + `uniqueId` + `newRendering` (path/ID/exact name) + `device`.
- **Sitecore APIs:** `LayoutEditor` — find by `uniqueId`, replace only its rendering-item reference,
  preserving placeholder/datasource/parameters/position.
- **Behavior:** the reason this is its own tool rather than "remove + add": atomic, so a failure
  can't leave the placeholder empty.
- **Safety / edge cases:** `RequiresWrite => true`; same exact-name-only resolution as `add_rendering`.
- **Returns:** the updated rendering's projection.

### `sitecore_reset_layout`
- **Args:** `ItemQueryArgs` + `device` (optional — omit to reset all devices) + `shared` (bool,
  default `true`).
- **Sitecore APIs:** `item.Fields[Sitecore.FieldIDs.LayoutField].Reset()` (or the final-layout field
  ID when `shared: false`), inside `ItemEditor.Edit`.
- **Behavior:** revert the layout field to standard-values inheritance — the presentation-specific
  case of the general "undo a local override" pattern `reset_item_fields` implements below.
- **Safety / edge cases:** `RequiresWrite => true`. A no-op (already inheriting) is reported as such,
  not an error — same benign-no-op handling `WriteFields` already has.
- **Returns:** `{ item, device, reset: true }`.

---

## Tier 4 — `sitecore_reset_item_fields` (`Tools/Items`)

- **Args:** `ItemQueryArgs` + `fields` (string[], optional — omit to reset every locally-overridden
  field).
- **Sitecore APIs:** `Sitecore.Data.Fields.Field.Reset()` per field, inside `ItemEditor.Edit`.
- **Behavior:** general form of what `reset_layout` does specifically for the layout field. Validate
  field names against `item.Fields` up front (same "reject before writing anything" discipline as
  `WriteFields`), reset each, verify with a fresh read afterward — a field that still shows a local
  value after reset (e.g. a save handler re-applies it) belongs in `notPersisted`, reusing that exact
  concept from `ItemEditor.WriteFields`.
- **Safety / edge cases:** `RequiresWrite => true`. Omitting `fields` (reset everything) is powerful
  enough to warrant echoing back exactly which fields were touched, not just a count.
- **Returns:** `{ item, reset: [...], notPersisted: [...] }`.

---

## Tier 5 — `Tools/Membership` (new)

New helper: `Tools/Membership/MembershipResolver.cs` — resolves user/role/domain names (these live
in the security provider, not the content tree, so `ItemResolver` doesn't apply). Follows the same
"exact match, fail loudly on ambiguity" discipline as `TemplateResolver`, even though usernames are
typically unique — a domain-qualified vs. bare name could still collide.

### Read tools (no `RequiresWrite`)

- **`sitecore_get_user`** — `identity` (username) or `filter` (substring), `Sitecore.Security.Accounts.User.FromName`
  / `UserManager` enumeration + filter. Returns profile basics, roles, enabled state — never the
  password hash or security-sensitive profile fields.
- **`sitecore_get_domain`** — `name` (optional, omit to list). `Sitecore.Security.Accounts.Domain.GetDomain`
  / `DomainManager.GetDomains()`.
- **`sitecore_get_role`** — `identity` or `filter`, `includeMembers` (bool). `Sitecore.Security.Accounts.Role.FromName`,
  `RolesInRolesManager.GetRoleMembers` for members.

### Write tools (`RequiresWrite => true`)

- **`sitecore_new_user`** / **`sitecore_remove_user`** — `UserManager.CreateUser`/`.DeleteUser`. New
  user requires an initial password — see the `set_user_password` note below on why that argument
  needs the same sensitivity flag.
- **`sitecore_enable_user`** / **`sitecore_disable_user`** — toggles `user.RuntimeSettings`/profile
  enabled flag (exact property to confirm against the Kernel version in use).
- **`sitecore_unlock_user`** — clears the membership provider's lockout state (failed-login lockout,
  distinct from item locking).
- **`sitecore_new_domain`** / **`sitecore_remove_domain`** — `DomainManager.CreateDomain`/`RemoveDomain`.
  Removing a domain is high-blast-radius (every user/role in it) — require the domain be empty, or
  make that explicit in the error rather than cascading silently.
- **`sitecore_new_role`** / **`sitecore_remove_role`** — `Role` creation/removal via `RolesInRolesManager`.
- **`sitecore_add_role_member`** / **`sitecore_remove_role_member`** — `RolesInRolesManager.AddUserToRole`/
  `RemoveUserFromRole`. Accepts a user or a role as the member (nested roles are valid in Sitecore).

### `sitecore_set_user_password` — flagged, needs its own review pass
- Sets a user's password without knowing the old one (admin reset), which in stock ASP.NET
  membership requires `Membership.Provider.ResetPassword` (only works if the provider allows
  retrieval/reset) or direct provider manipulation — the exact mechanism is provider-config-dependent
  and needs confirming against this instance's membership provider before committing to an API.
  **Before implementing:** decide whether this tool is available to non-admin clients at all (I'd
  default to admin-only, unlike the rest of the write surface which just needs `AllowWrites`), and
  make sure every call is unmissable in the audit log (`mcp.log`) given the sensitivity — this one
  more than any other tool in the whole server touches something credential-adjacent.

### ACL tools (`Tools/Items`, since they operate on an item's security, not a user/role)

- **`sitecore_test_item_acl`** (read-only) — `Sitecore.Security.AccessControl.AuthorizationManager.IsAllowed(item,
  right, account)`. Args: `ItemQueryArgs` + `account` (user or role) + `right` (enum matching
  `AccessRight`, e.g. `ItemRead`/`ItemWrite`/`FieldWrite:<fieldName>`).
- **`sitecore_add_item_acl`** / **`sitecore_set_item_acl`** / **`sitecore_clear_item_acl`** —
  `AuthorizationManager.GetAccessRules`/`SetAccessRules`, building `AccessRule`/`AccessRuleCollection`
  (account, right, `PropagationType`, Allow/Deny). Kept as three separate tools rather than one
  action-enum tool, matching this server's existing convention of never overloading a single tool
  with a mode switch (`create_item` vs `delete_item`, not one `modify_item`). `add` appends a rule,
  `set` replaces the whole rule set, `clear` removes all local rules (reverting to inherited) — the
  distinction matters enough to name explicitly rather than infer from arguments.

### `sitecore_lock_item` / `sitecore_unlock_item` (`Tools/Items`)
- **Args:** `ItemQueryArgs`.
- **Sitecore APIs:** `item.Locking.Lock()` / `.Unlock()`, `.GetOwner()`.
- **Behavior:** distinct from `ItemEditor`'s *automatic* lock-acquire-edit-restore around a write —
  this is an explicit "hold this item" (e.g. before a long manual edit session) or "free a stuck
  lock" tool. Must not be confused with or interfere with `ItemEditor.EnsureEditable`'s bookkeeping;
  it operates independently and reports the current owner honestly, including when the lock belongs
  to someone else (`unlock_item` on another user's lock should require explicit confirmation this is
  intentional — an admin-only override, surfaced clearly in the result, not silently allowed for
  every client).
- **Returns:** `{ item, locked: bool, owner }`.

### `sitecore_protect_item` / `sitecore_unprotect_item` (`Tools/Items`)
- **Args:** `ItemQueryArgs`.
- **Sitecore APIs:** the `__Read Only` standard field (`Sitecore.FieldIDs.ReadOnly`), toggled inside
  `ItemEditor.Edit`.
- **Behavior:** intention-revealing wrapper rather than routing through generic field-write, so the
  tool description can explain what "protected" means without the caller needing to know the
  standard-field name.
- **Returns:** `{ item, protected: bool }`.

---

## Tier 6 — higher-stakes / more involved

### `Tools/Workflow` (new)

New helper: `Tools/Workflow/WorkflowDescriber.cs` — projects `IWorkflow`/`WorkflowEvent`/`WorkflowCommand`
to JSON.

- **`sitecore_get_workflow_history`** (read-only) — `ItemQueryArgs`. `item.State.GetWorkflow().GetHistory(item)`
  → `WorkflowEvent[]` (date, old/new state, user, comments).
- **`sitecore_invoke_workflow`** (`RequiresWrite => true`) — `ItemQueryArgs` + `command` (name or ID)
  + `comments` (optional). First call `workflow.GetCommands(item)` to validate the command is
  actually available in the item's current state — an invalid/unavailable command is a clear
  `McpToolException` listing what *is* available, not a generic failure. Then `workflow.Execute(commandId,
  item, comments, checkSecurity: true, ...)` — **`checkSecurity` stays `true`**, consistent with this
  server never running as an implicit security bypass.

### `sitecore_change_item_template` (`Tools/Items`) — the highest-care tool in this whole plan
- **Args:** `ItemQueryArgs` + `newTemplate` (path/ID/exact name, `allowPartial: false`) +
  `fieldMappings` (optional dict of old-field-name → new-field-name, for fields that don't share a
  name across templates).
- **Sitecore APIs:** `Sitecore.Data.Managers.TemplateChangeInfo` (old template, new template, field
  mappings), `Sitecore.Data.Managers.TemplateManager.ChangeTemplate(item, changeInfo)`.
- **Behavior:** **read every field value before the change**, perform it, then **read every field
  value after** and diff — any pre-change value that has no post-change home (not preserved by
  Sitecore's own name-matching, and not covered by an explicit mapping) goes in the result's
  `dataLost` list. This is the same verify-after-write discipline as `WriteFields`/`notPersisted`,
  applied to the one operation on this whole list capable of silently discarding real content.
  **Do not ship this tool without that verification** — it is the entire point of doing it carefully
  rather than quickly.
- **Safety / edge cases:** `RequiresWrite => true`. Unknown/ambiguous target template fails loudly
  (reuses `TemplateResolver`).
- **Returns:** `{ item, oldTemplate, newTemplate, preserved: [...], dataLost: [ { field, value } ] }`.

### `sitecore_add_base_template` / `sitecore_remove_base_template` (`Tools/Templates`)
- **Args:** `ItemQueryArgs` (must resolve to a template item) + `baseTemplate` (path/ID/exact name).
- **Sitecore APIs:** reuses `TemplateBuilder` — read the current `__Base template` field
  (pipe-delimited IDs), append/remove the target ID, write back via `ItemEditor.Edit`. `add` is
  additive (existing bases untouched); `remove` fails clearly if the target isn't currently a base
  rather than silently no-op-ing.
- **Safety / edge cases:** `RequiresWrite => true`. Removing a base template that fields still depend
  on doesn't delete data (fields just stop being inherited going forward) — worth stating plainly in
  the tool description so it isn't mistaken for a destructive operation.
- **Returns:** the template's updated `baseTemplates` list (reuses `TemplateDescriber`).

### `sitecore_add_item_version` / `sitecore_remove_item_version` (`Tools/Items`)
- **Args:** `ItemQueryArgs` + (`add` only) `sourceLanguage` (optional — base the new version on a
  specific language's field values rather than the target language's current version).
- **Sitecore APIs:** `item.Versions.AddVersion()` / `.RemoveVersion()`. Cross-language version
  creation needs confirming empirically — likely: resolve the item in `sourceLanguage`, then add the
  version against the *target* language item, copying field values across (behavior needs verifying
  against a real instance during implementation; do not assume field fallback handles this for free).
- **Safety / edge cases:** `RequiresWrite => true`. `remove_item_version` on an item's last remaining
  version is a real destructive edge case — Sitecore's own behavior here (does it leave the item
  versionless, or refuse?) needs confirming before this ships, and the tool should surface whichever
  it is rather than assume.
- **Returns:** `{ item, language, version }`.

### `sitecore_query_items` (`Tools/Items`)
- **Args:** `query` (Sitecore query string, e.g. `/sitecore/content//*[@@templatename='Page']`) +
  `database` (default master).
- **Sitecore APIs:** `Sitecore.Data.Database.SelectItems(string query)` (absolute queries) or
  `Item.Axes.SelectItems` (relative to a root — expose via an optional `rootPath` arg).
- **Behavior:** run the query, project each result item as a summary (reuse `ItemProjector.ProjectSummary`).
- **Safety / edge cases:** item/field security is enforced the normal way — `SelectItems` still goes
  through `Database.GetItem` per result under the caller's real Sitecore user (`UserSwitcher`), so
  **never wrap this in `SecurityDisabler`** — that would be the one place in this server that bypasses
  the security model every other tool respects. A malformed query is a clear `McpToolException`, not
  a silent empty result (Sitecore query syntax errors can otherwise surface as unhelpful exceptions
  deep in the Kernel).
- **Returns:** `{ query, count, items: [...] }`, paged like `search`/`list_templates` (reuse `Paging`).

---

## Summary of new folders and helpers

| Folder | New helpers |
|---|---|
| `Tools/Jobs` | `JobDescriber` |
| `Tools/Presentation` | `LayoutEditor`, `RenderingResolver`, `PresentationDescriber` |
| `Tools/Membership` | `MembershipResolver` |
| `Tools/Workflow` | `WorkflowDescriber` |
| `Tools/Items` (extended) | none new — reuses `ItemEditor`, `ItemResolver`, `ItemProjector` |
| `Tools/Templates` (extended) | none new — reuses `TemplateBuilder`, `TemplateResolver` |

Every tool needs config registration (`SitecoreMcp.config`), server-instructions/TOOL_GUIDE updates
per the standing convention, and unit test coverage where the logic is Sitecore-independent (schema
binding, argument validation) — matching how the existing tool suite is tested today.
