# Sitecore MCP — Tool Guide (working notes)

Practical notes on using the tools well, gathered from real agent sessions. The condensed
essentials of this ship as the server `instructions` (returned at `initialize`, so every compliant
client folds them into the model's context). This file is the fuller version — keep new lessons
here.

## Call this first

- **`sitecore_get_context`** — instance name, Sitecore version, the user you run as, whether writes
  are allowed, and the available databases and languages. It orients an agent and is the fastest way
  to spot a bad deploy or a misconfigured client.

## Everything runs as your Sitecore user

Calls execute under a configured Sitecore user (`UserSwitcher`). Item and field ACLs apply. A field
you cannot read is **omitted** from output, not nulled (a null would read as "empty"). Databases are
allow-listed per client, and writes may be globally off. So "not found" / "not permitted" results
are real security outcomes, not tool bugs.

## Finding items — pick the right tool

| You want | Tool |
|---|---|
| An item by exact / partial **name** | `sitecore_search { name }` / `{ nameContains }` |
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

## Writes (only when allowed)

- **`create_item`** — parent, name, template (name/path/ID), optional initial fields. Validates the
  name and refuses a duplicate sibling.
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
- **`move_item`** — refuses a destination inside the item's own subtree, or a name collision.
- **`copy_item`** — copies **field data**, not just structure. A deep copy of a site clones *all* its
  content (hostnames, form IDs, settings). If you want "same skeleton, empty fields", copy is the
  wrong tool — build the structure fresh. Reports `childCount`.
- **`rename_item`** — validates the new name, refuses a sibling collision.
- **`delete_item`** — **recycles** by default (recoverable from the Recycle Bin). `permanent: true`
  destroys the item and its subtree irreversibly.

**Item locking.** Field edits (`update_item`, `rename_item`, `create_item`'s initial fields) are
lock-aware. Admins bypass it. For a non-admin on an instance with `RequireLockBeforeEditing`, the
tool locks the item, edits, then restores the prior lock state (unlocking if it wasn't locked
before, unless `AutomaticUnlockOnSaved` already did). A write to an item **locked by another user**
is refused with the owner named, and a save Sitecore rejects (lock/workflow) is reported as an
error — never a silent success. Move/copy/rename/delete structural ops are not gated by this.

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
