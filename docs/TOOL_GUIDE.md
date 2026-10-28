# Sitecore MCP — Tool Guide (working notes)

Practical notes on using the tools well, gathered from real agent sessions. The condensed
essentials of this ship as the server `instructions` (returned at `initialize`, so every compliant
client folds them into the model's context). This file is the fuller version — keep new lessons
here.

## Call this first

- **`sitecore_get_context`** — instance name, Sitecore version, the user you run as, whether writes
  are allowed, and the available databases and languages. It orients an agent and is the fastest way
  to spot a bad deploy or a misconfigured client.

## Membership — users, roles, domains

Security-account tools, **all administrator-only** (hidden from a non-admin client). Names are
domain-qualified (`sitecore\editor`); a bare name assumes the `sitecore` domain.

- **`get_user`** — by exact name (`identity`) or a name substring (`filter`). Returns identity, admin
  flag, profile basics, enable/lock state, and roles. **Never returns a password.**
- **`get_role`** — by `identity` or `filter`; `includeMembers` lists the users and nested roles in it
  (Sitecore roles nest — e.g. `Author` here contains 14 child roles, no direct users).
- **`get_domain`** — one domain, or all of them, with user counts.

Writes (also admin-only; a write client):
- **`new_user`** (name + password, optional email/full-name/admin) / **`remove_user`** (refuses the
  built-in administrator and the client's own user).
- **`enable_user`** / **`disable_user`** (membership approval) / **`unlock_user`** (clears a
  failed-login lockout — the membership lockout, not an item lock).
- **`new_role`** / **`remove_role`** (removing a role detaches its members; it does not delete them).
- **`add_role_member`** / **`remove_role_member`** — the member may be a **user or a role** (Sitecore
  roles nest). An administrator user is implicitly in every role, so its explicit membership may not
  show in `get_role`/`get_user` — that's Sitecore's behaviour, not a failed write.
- **`new_domain`** / **`remove_domain`** (refuses the built-in `sitecore`/`extranet`/`default`).

Item access rules (admin-only; they govern item security):
- **`test_item_acl`** — does a user/role have a right (read/write/delete/…) on an item, after
  inheritance and denies. Answers "why can't account X edit this?".
- **`add_item_acl`** — append an allow/deny rule (right, account, propagation: entity/descendants/any).
- **`set_item_acl`** — replace the rule for one account+right (so allow→deny cleanly, no stacking).
- **`clear_item_acl`** — remove all local rules, or just one account's, reverting to inherited security.
- *Edge case:* the virtual `Everyone` role does not round-trip into per-item rules like a concrete
  user/role — name specific accounts.

## Admin-gated tools

Some tools require an **administrator** client. A tool that needs admin is hidden from `tools/list`
and refused for a client whose Sitecore user is not an administrator — exactly like a write tool is
hidden when writes are off. Each tool declares a code default, and the `<tool>` config element can
add an admin requirement via `admin="true"` (e.g. to make publishing admin-only in a stricter
environment).

**Config can only tighten the gate, never loosen it.** A tool that requires admin in code stays admin
whatever config says — `admin="false"` on such a tool is ignored and logged. The code requirement is
a safety net: a config mistake (or an ill-advised patch) must not be able to expose a privileged tool
like a future password reset. `create_template` (schema) and `index_status` (an index diagnostic)
require admin in code today; the upcoming membership/security and schema tools will too.

## Everything runs as your Sitecore user

Calls execute under a configured Sitecore user (`UserSwitcher`). Item and field ACLs apply. A field
you cannot read is **omitted** from output, not nulled (a null would read as "empty"). Databases are
allow-listed per client, and writes may be globally off. So "not found" / "not permitted" results
are real security outcomes, not tool bugs.

## Finding items — pick the right tool

| You want | Tool |
|---|---|
| An item by exact / partial **name** | `sitecore_search { name }` / `{ nameContains }` (several words: every word must be in the name) |
| An item by **path or ID** | `sitecore_get_item` |
| **Children** / **ancestors** | `sitecore_get_children` / `sitecore_get_ancestors` |
| Items mentioning a **word** (indexed content) | `sitecore_search { text }` |
| Items whose **field value** contains X (exact/substring/regex, any field incl. `__` standard/security) | `sitecore_grep` |
| **How many** / distribution | `sitecore_facet`, or `sitecore_search { countOnly }` |

**Rule of thumb:** `search` = metadata + indexed content (cheap, index-backed). `grep` = raw field
values (exact, scoped, reads items). `facet` = counts / distribution.

**Why not text-search for a name?** `text` matches the tokenized `_content` index — an item's *name*
need not appear in any content field, and `_content` excludes standard/security fields. Names get
tokenized and buried among thousands of loose matches. Use `name`/`nameContains`, which query the
correct `_name` field.

## Templates & schema

- **`sitecore_get_template`** — an item's template with all fields (own **and inherited**), types,
  and sections. Call it before writing fields so you use real field names instead of guessing.
- **`sitecore_list_templates`** — find a template by a name substring.
- Template arguments accept a **name**, not just a path/ID — but partial matching differs by intent.
  `search.template` (discovery) resolves a **unique partial** name and echoes `resolvedTemplate` so
  you see what matched — e.g. `"Local Datasource"` → `"Local Datasource Folder"`. The **write** args
  (`create_item.template`, `create_template.baseTemplates`) accept only a path, ID, or **exact**
  name: a template that sets structure or inheritance is never chosen by a fuzzy guess. A name with
  no exact match fails **loudly** (listing near matches) rather than silently resolving to the wrong
  template — decisive because a duplicate name (e.g. `Site` under several brand folders) is common
  and can only be told apart by path/ID.

## Reads — output shape

- `get_item` returns **populated, non-standard** fields by default. An empty field map is **normal**
  for structural items (Site Root, folders) — they have no content fields. The result carries a
  `hint` and `fieldStats` (`populated` / `empty` / `standard`) telling you what exists and how to see
  it. Pass `includeStandardFields` / `includeEmpty`, or an explicit `fields` list, for the full set.
- Long field values are truncated; child/result lists are paged (`limit` / `offset` / `hasMore`).
- `search` hits are **grouped by item**, each listing the `languages` it matched — the index holds
  one document per item × language × version, so ungrouped results are mostly the same items
  repeated. `total` is the raw document count; `count` is distinct items on the page.

## Direct changes need confirmation

Some changes take effect immediately and cannot be discarded: `delete_item`, `move_item`,
`rename_item`, `publish_item`, `invoke_workflow`, and `update_item` / `reset_item_fields` **when they
change a shared or unversioned field** are *direct changes*. Whether the server makes the editor
confirm them is a **per-client** setting, `confirmDirectChanges` on the `<client>`;
`sitecore_get_context` reports it for the calling client, and the server `instructions` and tool
descriptions are worded to match.

| Client | Confirmation |
|---|---|
| `requiresEditorToken="true"` (CorePilot) | Always. `confirmDirectChanges="false"` is refused: the client is skipped at startup. |
| Fixed `user` (IDE agents, Claude Code, scripts) | Off by default (`Mcp.ConfirmDirectChanges`, default `false`); `confirmDirectChanges="true"` opts in. |

**Without confirmation** the change runs on the first call. Every other check still applies first
(item and field security, rights, content roots, hidden fields, the move/copy/rename pre-checks, the
write and admin gates). A `confirmationToken` is ignored. The result is the tool's usual result plus
`directChange: { summary, confirmation: "skipped" }` (the summary is the preview sentence, also the
first text block), and the audit line ends `confirmed=skipped`. The calling tool's own permission
prompt is then the check before a change.

**With confirmation:**

- Called **without** `confirmationToken`, a direct-change tool validates everything it can (item
  exists, rights, content roots, collisions) and returns a **preview** — nothing is changed. The text
  content is a human-readable summary; `structuredContent` carries `status: "confirmationRequired"`,
  `summary`, `preview` (the details) and `confirmation: { token, expiresAt, argument }`.
- Called again with the **identical arguments** plus `confirmationToken`, the change runs. The token
  is **single-use**, **expires** (`Mcp.Confirmation.LifetimeSeconds`, default 300s), and is bound to
  the tool, the editor, the canonical arguments and the target's state (ID, path, revision) at preview
  time. Different arguments, another editor, an item edited/moved since the preview, a reused or
  expired token all return `isError` with `status: "confirmationRejected"` and a `reason`
  (`mismatch`, `alreadyUsed`, `expired`, `malformed`) — and change nothing.
- The token is **only** in `structuredContent`, never in the text, so a client can show the preview
  to the model while withholding the token until a person confirms. A model should not try to
  confirm on its own.
- Tokens are signed with a per-process key and held in memory: valid only on the instance that
  issued them, and lost on an app-pool recycle (preview again).

**Move, copy and rename pre-checks.** Before previewing (or copying), these refuse when the editor
lacks the rights — **delete** on the source and **create** on the destination for a move, **create**
on the destination for a copy, **rename** on the item for a rename — or when the change would leave
or cross the configured **content roots** (a content root itself cannot be moved or renamed). Until
content roots are configured every path counts as inside one root, so only rights are checked.
`delete_item` likewise checks delete rights and the content roots before previewing. Without
confirmation the same checks run before the change.

## Writes (only when allowed)

- **`create_item`** — parent, name, template (name/path/ID), optional initial fields. Validates the
  name and refuses a duplicate sibling. Starts the template's default workflow, as the Content
  Editor does (see *New items and versions* below).
- **`create_template`** — parent, name, optional `baseTemplates` (name/path/ID, default the Standard
  Template), `sections` (each a name plus typed `fields`), and `createStandardValues`. Field `type`
  must be an **exact** Sitecore field type (e.g. `Single-Line Text`); an unknown type is rejected
  with the **closest real types on this instance** named in the error (or the full valid set when
  nothing is close), so a model can self-correct in one turn. That set is read live from the core
  field-type registry, so **custom field types are supported** — anything registered under
  `/sitecore/system/Field types`.
  The **whole definition is validated before anything is created** — a bad name, unknown type, or a
  duplicate **section or field name** fails with nothing written; a field name must be unique across
  the *entire* template (not just its section), since `Fields[name]` resolves template-wide. A build
  that fails partway recycles the half-made template. The result echoes the created sections and
  fields read back from the item tree, so it reflects what actually persisted.
- **`update_item`** — changes only the fields you pass; an unknown or unwritable field is rejected
  **before** anything is saved. Writing a field to its current value is a benign no-op. After saving
  it verifies each change actually stuck: a field that reports saved but reads back with its old
  value (dropped by field security, a computed field, or a save handler — e.g. a limited user
  editing `__Display name`) is listed in `notPersisted` with a warning, not a false success.
- **`reset_item_fields`** — the opposite of `update_item`: it removes an item's *own* values so the
  fields inherit from the template's standard values again (`update` can only set a value, never
  un-override). Name the fields, or omit them to reset every locally-set **content** field (standard
  `__` fields are skipped unless you name them, since a blanket reset of presentation/workflow/security
  would be a footgun). A named-but-already-inherited field is a benign no-op; a field that reads back
  with a local value after reset (a save handler re-applied it) is reported in `notPersisted`.
- **`move_item`** — refuses a destination inside the item's own subtree, or a name collision.
  Direct change (see above).
- **`copy_item`** — copies **field data**, not just structure. A deep copy of a site clones *all* its
  content (hostnames, form IDs, settings). If you want "same skeleton, empty fields", copy is the
  wrong tool — build the structure fresh. Reports `childCount`. Every version of the copy that has a
  workflow is reset to its initial state (a copy of approved content is not publishable).
- **`rename_item`** — validates the new name, refuses a sibling collision. Direct change.
- **`delete_item`** — **recycles** by default (recoverable from the Recycle Bin). `permanent: true`
  destroys the item and its subtree irreversibly. Direct change: the preview reports descendant and
  referrer counts.
- **`lock_item`** / **`unlock_item`** — an explicit checkout that persists until released (distinct
  from the automatic lock write tools take around one edit). `unlock_item` frees your own lock freely;
  **another user's lock is only overridden by an administrator with `force: true`**, so a lock is
  never stolen by accident.
- **`protect_item`** / **`unprotect_item`** — toggle an item's read-only flag, so editors cannot
  change it in the Content Editor. No-ops report as such.

**Item locking.** Field edits (`update_item`, `rename_item`, `create_item`'s initial fields) are
lock-aware. Admins bypass it. For a non-admin on an instance with `RequireLockBeforeEditing`, the
tool locks the item, edits, then restores the prior lock state (unlocking if it wasn't locked
before, unless `AutomaticUnlockOnSaved` already did). A write to an item **locked by another user**
is refused with the owner named, and a save Sitecore rejects (lock/workflow) is reported as an
error — never a silent success. Move/copy/rename/delete structural ops are not gated by this.

## Presentation — the components on a page

Sitecore holds presentation in two layout fields: a **shared** base (`__Renderings`) and a
**final**, per-version layout (`__Final Renderings`) stored as a *delta* over the base. Only the
resolved effective layout is meaningful, so these tools read and write through Sitecore's layout API,
never the raw field.

- **`get_renderings`** — the components on an item for a device, each with its placeholder,
  datasource, parameters, and **unique ID**. Reads the effective **final** layout by default (what
  actually renders, resolving inheritance + the page's own overrides). The unique IDs are how you
  target the write tools.
- **`add_rendering`** — place a rendering (by path/ID/exact name) in a placeholder, with optional
  datasource and parameters. Returns the new instance's unique ID.
- **`set_rendering`** — change an existing instance's datasource, placeholder, or parameters. Only
  what you pass changes; for parameters, only supplied keys are touched and a **null value removes**
  that key (this is also how you read-modify-write parameters).
- **`switch_rendering`** — swap the component in place, keeping placeholder/datasource/parameters/
  position and the same unique ID. Atomic — never leaves the placeholder empty the way remove+add can.
- **`remove_rendering`** — drop an instance by unique ID.
- **`move_rendering`** — reposition an instance in the device's rendering order (1-based `position` —
  the order `get_renderings` lists, which is render order within a placeholder). `add_rendering` also
  takes an optional `index` to insert rather than append.
- **`set_layout`** — assign which **layout** (the outer view) a device uses, by path/ID/exact name
  under `/sitecore/layout/Layouts`; empty string clears it. `get_renderings` reports the device's
  current layout. Distinct from the renderings placed *into* the layout's placeholders.
- **`reset_layout`** — revert the layout field to standard-values inheritance. A no-op on an item with
  no local layout is reported as `reset: false`, not an error.

**Sharp edges:**
- **`finalLayout` defaults to true** — edits land on the per-version final layout, the Experience
  Editor's behaviour and the safe default (it doesn't change other versions or the template's standard
  values). Pass `finalLayout: false` to change the **shared** base for all versions.
- **You can target an inherited rendering.** Because reads resolve the effective layout, a rendering
  inherited from standard values shows up with a unique ID; a `set`/`remove` against it on the final
  layout creates a per-page override of that inherited component — which is intentional, but means an
  empty final layout does not imply "nothing to change".
- **Rendering references are exact-only on writes** (path, ID, or exact name), same as template
  writes — a name like `Container` is far from unique, so a fuzzy match could place the wrong component.

## Media

- **`get_media_image`** — look at a media library image. Returns it as image content the model can
  see (PNG, JPEG, GIF, WebP), scaled down to the server's limits (`Mcp.Images.MaxDimension`, default
  1024 px; `Mcp.Images.MaxBytes`, default 1 MB), plus its path, dimensions and alt text. Pass
  `maxDimension` for a smaller preview. Use it to describe an image or draft alt text. SVG, PDFs and
  documents are refused — they are not images a model can take.
- **`upload_media`** — upload a file into the media library from base64 content; the extension in
  `fileName` decides the media type (Image/File/Pdf/…), `alt` sets alternate text, and an existing
  sibling fails unless `overwrite: true`. The endpoint's request-size limit applies (default ~1 MB
  body ≈ 700 KB of file), so this suits icons and documents, not videos. The result includes the
  `mediaUrl`. Reference the uploaded item from image/link fields via its ID or path.

## References — what breaks if I change this?

Sitecore's **Link Database** records every field that points at another item. These tools read it,
and they are the impact check to run *before* `delete_item`, `move_item`, or `rename_item`.

- **`get_item_references`** — what this item points **at** (its dependencies), with the field each
  link comes from.
- **`get_item_referrers`** — what points **at** this item. `total` is the honest impact number.
- **`update_item_referrers`** — repoint every incoming link at a different item (`newTarget`), or
  **remove** them when `newTarget` is omitted. It edits *other* items, so check `get_item_referrers`
  first. Edits are grouped per referring item and reported individually — one item being locked or
  unwritable does not stop the rest, and failures are listed rather than swallowed.

**Sharp edges:**
- **Results are only as fresh as the Link Database.** After a bulk import, a serialization sync, or
  with link tracking disabled, it can be stale — an empty `referrers` result is **not proof** nothing
  references the item, and the tool says so.
- **`resolved: false` on a reference is ambiguous by nature** — the target is either deleted or
  outside what this client can read, and those are indistinguishable from here. In practice a real
  site carries plenty of genuinely dangling links (deleted datasources still wired into
  `__Final Renderings`), so this is a useful way to find them.
- **Unreadable referrers are counted, not listed.** They still reference the item, so hiding them
  entirely would produce a false "safe to delete" signal; they appear as `unreadable`.
- **Only link-shaped fields can be rewritten.** `update_item_referrers` asks Sitecore's field type
  whether it knows how to relink itself; a field that doesn't is skipped rather than having its raw
  value guessed at.

## Workflow

- **`get_workflow_history`** — an item's workflow in one call: which workflow, current state, the
  **commands available right now** (what `invoke_workflow` could execute), and the transition history
  (who, when, old → new state, comments). An item not in a workflow says so plainly.
- **`invoke_workflow`** — execute a command (by display name or ID: `Submit`, `Approve`, …). Only
  commands available in the item's current state *to this client's user* can run — workflow security
  applies — and an unavailable command is refused **listing what is available**. The result reports
  the old and new state from a fresh read, never assuming the transition happened.

## New items and versions — workflow state and statistics

Every tool that creates an item or a version (`create_item`, `add_item_version`, `copy_item`,
`upload_media`, `create_template`) leaves it as the Content Editor would: **in its workflow's
initial state** whenever the item (`__Workflow`) or its template (`__Default workflow` on the
standard values) names a workflow, with `__Created by` / `__Updated by` / `__Owner` set to the
calling editor. Each result carries `workflow` (`id`, `name`, `state` with `final`, `started`: whether
the tool had to start it, `reason`, `note`) and `statistics` (`created`, `createdBy`, `updated`,
`updatedBy`, `owner`); a copy's `workflow` also counts `versions`, `inWorkflow` and `startedVersions`.
`sitecore_get_context` reports the context `site` and `workflowContextEnabled`.

**Why it needs doing.** Sitecore starts a workflow only while its *WorkflowContext* is enabled, and
that follows the context site's `enableWorkflow` (Sitecore.Workflows.WorkflowContext.Enabled). The
Content Editor runs in the `shell` site (`enableWorkflow="true"`); `/sitecore/api/...` resolves to a
content site, where the attribute defaults to false - so a plain `Item.Add` from this endpoint created
items with no `__Workflow state`. **An item with a workflow but no state counts as approved**
(`Workflow.IsApproved` returns true when the state item is missing), i.e. publishable. The tools now run
the create inside `WorkflowContextStateSwitcher(Enabled)` - Sitecore's own mechanism - and then check
explicitly (`Tools/Workflow/WorkflowStartPolicy`, Sitecore-free and unit-tested): a version with a
workflow and any state other than the initial one is started (`IWorkflow.Start`), one already in the
initial state is left alone (starting again would clear its history). Nothing is started in a database
without a workflow provider (by default every database but master), nor on standard values or branch
items - Sitecore skips those too.

**What Sitecore itself does** (decompiled Sitecore.Kernel 10.3.0 and 10.4.0; identical in both for all
of this, except that 10.3 also gates on `Settings.Workflows.Enabled`, which is only false for a Commerce
Server standalone edition and is deprecated in 10.4):

| Operation | Workflow | Statistics, owner, lock |
|---|---|---|
| `Item.Add` / `ItemManager.AddFromTemplate` (data template) | `CreateItem`, then `AddVersion` for version 1; the `AddedVersion` event lets `WorkflowEngine` call `IWorkflow.Start` from `__Default workflow` - **only when WorkflowContext is enabled** (never for standard values or branch items, never without a workflow provider). Branch templates: each copied item gets version 1 the same way. | `AddVersion` sets `__Created`, `__Created by`, `__Revision`, `__Updated`, `__Updated by` (context user), `__Valid from`/`__Valid to`, and `__Owner` when the item has no own value. `__Lock` stays empty. |
| `uiAddFromTemplate` (Content Editor insert) | `Context.Workflow.AddItem`: the same `Item.Add` in the shell site (enabled), then `StartEditing`, which for a non-admin **locks** the new item. | As above. |
| `Versions.AddVersion` | The SQL provider copies every versioned field of the base version **except `__Workflow state`**; then `WorkflowEngine` restarts from `__Default workflow` when WorkflowContext is enabled. So a new version of an Approved item is *not* Approved - but with the context off (or only `__Workflow` set, no `__Default workflow`) it has a workflow and **no state, which is publishable**. The Content Editor's *Add version* and the automatic version on editing an approved item (`StartEditing`) both go through this with the context on: the new version lands in the initial state. | `__Created`/`__Created by`/`__Revision`/`__Updated`/`__Updated by` reset to now and the context user; `__Owner` and **`__Lock` are copied** from the base version. |
| `Item.CopyTo` / `uiCopyItems` / duplicate | `CopyTo` copies **all** fields including `__Workflow state` (an Approved source gives an Approved copy) and no start happens. The Content Editor's copy and duplicate use `Context.Workflow.CopyItem`/`DuplicateItem`, which call `Start` on every version (all languages) of the copy and its subtree that has a workflow and state, unlock the copy, then `StartEditing`. | The SQL copy keeps the source's `__Created`, `__Created by`, `__Owner`; it skips `__Lock` (and `UnlockAfterCopy`, default true, unlocks). The `item:copied` handler saves the copy, setting `__Updated`/`__Updated by`/`__Revision`. |
| `MediaCreator` | Adds the media item with `Item.Add` (or, on overwrite, removes all versions and calls `AddVersion`) under a `SecurityDisabler`: the workflow starts only when WorkflowContext is enabled. | As `Item.Add`; the context user (the editor - `SecurityDisabler` does not switch user) is `__Created by`. |

`IWorkflow.Start` clears the version's workflow history, writes `__Workflow` and `__Workflow state`
(initial state) through a normal save (so `__Updated`/`__Updated by`/`__Revision` change), adds the
"Item created" history entry and runs the initial state's actions. It never touches `__Lock`.

**Deliberate differences from the Content Editor.** The tools do not lock what they create (the
Content Editor's `StartEditing` locks a non-admin's new item; use `lock_item`). Where the Content
Editor would leave a version with a workflow but no state - a new version of an item with only
`__Workflow` set, or a copy of an item that was never started - the tools start it, since that state
is publishable. While the create runs with WorkflowContext enabled, workflow-state write security
applies, as it does in the Content Editor: a user may be refused a new version of an item whose state
denies them write.

## Structure changes — template swaps, inheritance, versions

- **`change_item_template`** — swaps an item's template. Sitecore carries values across by matching
  field names; anything else is **dropped**. The result diffs every populated content field:
  `preserved`, `changed` (survived with a different value), and `dataLost` — each dropped field **with
  its old value**, so nothing disappears silently. Same-template is a no-op.
- **`add_base_template`** / **`remove_base_template`** (admin) — edit an existing template's
  inheritance. Removing a base doesn't delete stored values; items just stop inheriting those fields
  (re-adding the base restores them). Removing a non-base fails listing the current bases.
- **`add_item_version`** — new version in a language; `sourceLanguage` seeds it with another
  language's latest field values (a translation starting point — shared and `__` fields excluded).
  The new version starts in its workflow's initial state, never in a copied approved state.
- **`remove_item_version`** — remove one version (`version`, default latest). Removing the last
  version means the item stops existing in that language, and the result says so.
- **`query_items`** — Sitecore query (XPath-like axes, `fast:` supported) for structural lookups that
  search/grep are awkward at. Walks the tree live: scope it, and prefer `search` for text/template
  lookups. Item security applies.

## Publishing & background jobs

**Content written to `master` is not live until it is published.** Every write tool above changes the
authoring database only — `sitecore_publish_item` is what pushes an item to the publishing targets
(typically `web`).

- **`publish_item`** — `mode` (`smart` publishes only what changed, the default; `full`
  force-republishes), `deep` (descendants), `publishRelatedItems` (datasources and media the item
  references), plus explicit `targetDatabases` / `languages` when you don't want the configured
  defaults.
- **Publishing is asynchronous.** The tool returns a **handle** as soon as the publish starts — a
  handle means *started*, never *finished*. Poll **`sitecore_get_jobs`** with it to read `state`
  (`Initializing` → `Finished`), `processed`, and the messages (`Items created/updated/skipped`).
- **A publish handle is not a job handle.** Sitecore tracks a publish separately from the jobs it
  spawns, so the handle `publish_item` returns will never appear in the `get_jobs` *list* — the list
  shows the underlying `Publish to 'web'` job under its own handle. `get_jobs` resolves **both** kinds
  when given a `handle` and tags which it found via `kind` (`publish` or `job`), so polling works
  whichever you hold.
- **Targets respect the client's `databases` allow-list.** A client scoped to `master` alone cannot
  publish to `web` — that's deliberate (a limited client shouldn't push content live). Widen the
  client's `databases` in config if it should be able to.
- **A running publish cannot be stopped — scope it instead.** There is no tool to cancel one, because
  Sitecore offers no safe way to do it: publish jobs report `abortable: false`, and setting the abort
  state anyway is simply ignored (measured on 10.3 — a signalled `Publish to 'web'` carried on from
  1,598 to 2,927 items and ran to completion). Killing the job's thread is not an option either: it
  runs in the same worker process as the site, so aborting it can leave a half-written item, a leaked
  connection, or a stranded lock. Prefer a narrow path and `deep: false` over starting a large publish
  you may regret.

## Dev/ops — indexes, link database, logs (all admin-only)

- **`rebuild_index`** — `name` fully rebuilds one index (background job, poll `get_jobs` — a ~10k-item
  tree takes ~30s here); `rootPath` refreshes just a subtree in every index covering it, which is the
  cheap option after editing a branch. Check `index_status` afterwards.
- **`populate_solr_schema`** — the Control Panel "Populate Solr Managed Schema" operation, per index
  or all. Run it after adding indexed/computed fields **before** a rebuild — a rebuild can't index a
  field the schema doesn't know. Reports `notApplicable` on a non-Solr instance.
- **`rebuild_link_database`** — rebuilds the reference store behind
  `get_item_references`/`referrers`; run when reference results look stale. Background job.
- **`get_logs`** — list the log files, or tail one (`file` is a name or prefix — `'log'`, `'mcp.log'`,
  `'Crawling'` — picking the most recent match) filtered by `level`/`search`. Only the final 1 MB is
  scanned. This reads Sitecore's own logs; the MCP audit trail is `mcp.log`.

## Search — the full query surface

`sitecore_search` combines any of: `name` / `nameContains`, `text`, `template`, `rootPath`,
`language`, `fieldEquals`, `createdAfter/Before`, `updatedAfter/Before`, `sortBy` (+`sortDesc`),
`countOnly`, `limit` / `offset`. `sitecore_facet` groups by a field (`template`, `language`, or a raw
indexed field) with the same scoping filters.

## Gotchas / sharp edges

- **Raw `fieldEquals` needs the exact indexed field name** (`_name`, not `name`; `__workflow state`
  as indexed, etc.). A wrong name **silently matches nothing** and can look like "no such item."
  Prefer the typed filters (`name`, `template`, date ranges) — they translate to the right field.
  Unknown *top-level* arguments are flagged in the result (`ignored...`); wrong field names *inside*
  `fieldEquals` are not, so double-check those.
- **grep is scoped and capped.** It requires a `rootPath`, loads each item, and stops at `maxScan`;
  `scanTruncated` says whether it hit the cap. It searches field **values**, so it cannot find an
  item by its **name** — use `search { name }` for that.
- **An empty search is not proof of absence** when using `text` — the result hints to try `grep` for
  raw field values.
- **Audit** — every call is logged to `mcp.log` (user, tool, target item, status, duration),
  separate from the main Sitecore log.
