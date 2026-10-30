namespace SitecoreMcp.Server.Protocol
{
    /// <summary>
    /// The server usage guidance returned in the initialize result. Compliant clients fold this into
    /// the model's context, so it is the portable, every-client home for cross-tool guidance. Keep it
    /// concise; the fuller version lives in docs/TOOL_GUIDE.md.
    /// </summary>
    public static class McpServerInstructions
    {
        /// <summary>
        /// The instructions advertised at initialize to a client that confirms direct changes (every
        /// editor-token client, and fixed-user clients that opted in).
        /// </summary>
        public const string Text = Head + ConfirmingDirectChanges + Tail;

        /// <summary>The instructions advertised at initialize to a client that does not confirm direct changes.</summary>
        public const string WithoutConfirmation = Head + UnconfirmedDirectChanges + Tail;

        /// <summary>The instructions for a client, by whether it confirms direct changes.</summary>
        public static string For(bool confirmDirectChanges) => confirmDirectChanges ? Text : WithoutConfirmation;

        private const string ConfirmingDirectChanges =
@"Direct changes need confirmation: delete_item, move_item, rename_item, publish_item, invoke_workflow, and update_item/reset_item_fields when they change a shared or unversioned field. Called without confirmationToken they validate and return a preview (summary plus details) and change NOTHING. Tell the user what the preview says and stop: the change runs only after a person confirms, when the same call with identical arguments is repeated with the single-use, expiring token - do not invent or reuse a token. move/copy/rename also refuse up front when the user lacks delete (source) / create (destination) / rename rights or the change would cross content roots.

";

        private const string UnconfirmedDirectChanges =
@"Direct changes take effect on the first call for this client (except sitecore_install_package, which always stops at a preview): delete_item, move_item, rename_item, publish_item, invoke_workflow, and update_item/reset_item_fields on a shared or unversioned field run immediately (no preview step; confirmationToken is not needed and is ignored). The result's directChange.summary says what changed - report it. They are still checked like any write (item security, content roots, hidden fields, write permission), so be deliberate: check referrers before delete/move/rename and scope publishes narrowly. move/copy/rename also refuse up front when the user lacks delete (source) / create (destination) / rename rights or the change would cross content roots.

";

        private const string Head =
@"These tools operate a Sitecore instance in-process. Every call runs as a configured Sitecore user, so item and field security apply and writes may be disabled. Call sitecore_get_context first (instance, version, your user, whether writes are allowed, databases, languages).

Finding items - pick the right tool:
- By name: sitecore_search with 'name' (exact) or 'nameContains' (partial). Do NOT use 'text' to find an item by name.
- By path or ID: sitecore_get_item; children via sitecore_get_children; ancestors via sitecore_get_ancestors.
- By indexed content words: sitecore_search 'text' (tokenized _content only; excludes standard/security fields and substrings).
- By raw field value (exact/substring/regex over ANY field, including __standard and __security fields the index cannot see): sitecore_grep (requires rootPath; loads items; scan-capped).
- Counts/distribution: sitecore_facet; a plain total via sitecore_search 'countOnly'.
Rule of thumb: search = metadata + indexed content (cheap, index-backed); grep = raw field values (exact, scoped); facet = counts.

Templates: sitecore_get_template shows an item's fields (own + inherited) with types - call it before setting fields so you use real names. sitecore_list_templates finds a template. Template arguments accept a name as well as a path or ID: search resolves a unique partial name, but write args (create_item.template, create_template.baseTemplates) require a path, ID, or EXACT name and fail loudly (never a fuzzy guess) - important because a duplicate template name across brand folders is common and only a path or ID can disambiguate.

Reads: get_item returns populated non-standard fields by default; an empty field map is normal for structural items and carries a hint plus fieldStats. Pass includeStandardFields/includeEmpty or explicit 'fields' for more. Search hits are grouped by item, listing the languages each matched.

Writes (only if allowed): create/update/move/copy/rename/delete_item, and create_template. update changes only the fields you pass. copy_item copies field DATA, not just structure. delete_item recycles by default; permanent=true destroys irreversibly. lock_item/unlock_item are an explicit checkout (unlock only overrides another user's lock for an administrator with force=true); protect_item/unprotect_item toggle an item's read-only flag. create_template takes base templates (default Standard Template), sections, and typed fields; field types must be exact (e.g. Single-Line Text), field names must be unique across the whole template, and the whole definition is validated before anything is created. Field edits handle item locking automatically for non-admins (lock, edit, restore); a write to an item locked by another user is refused, and a rejected save is reported (never a silent no-op). update_item verifies each change actually persisted and lists any silently dropped (by field security, a computed field, or a save handler) in notPersisted. reset_item_fields is the opposite of update: it removes an item's own field values so they inherit from standard values again (name the fields, or omit to reset every locally-set content field).

";

        private const string Tail =
@"Media: sitecore_get_media_image returns a media library image as image content you can see (scaled to the server's size limits; PNG/JPEG/GIF/WebP only), with its dimensions and alt text. sitecore_upload_media uploads a file from base64 (fileName's extension decides the media type; the request-size limit caps it at a few hundred KB) and returns the media item and URL to reference from image/link fields.

Presentation: sitecore_get_renderings lists the components on an item for a device (each with placeholder, datasource, parameters, and a unique ID) - it reads the effective final layout, resolving inheritance. Target the write tools by unique ID: sitecore_add_rendering, sitecore_set_rendering (only the parts you pass change; a null parameter value removes that key), sitecore_switch_rendering (swap component in place, keeping placeholder/datasource/parameters), sitecore_move_rendering (reposition in render order; add_rendering also takes an index), sitecore_remove_rendering, sitecore_set_layout (assign the device's outer layout), sitecore_reset_layout (back to standard-values inheritance). Writes edit the per-version final layout by default (finalLayout=false targets the shared base for all versions). Rendering references are exact-only on writes, like templates. A rendering inherited from standard values still appears with a unique ID, so an empty final layout does not mean nothing renders.

References: sitecore_get_item_referrers lists what points AT an item - run it before delete_item/move_item/rename_item to see what would break; sitecore_get_item_references lists what an item points at. sitecore_update_item_referrers repoints those incoming links at another item, or removes them when newTarget is omitted (it edits the OTHER items, so check referrers first). All three read Sitecore's Link Database, so results are only as fresh as its last update: an empty referrers result is not proof nothing references the item. A reference with resolved=false is either deleted or unreadable - indistinguishable here.

Workflow: sitecore_get_workflow_history shows an item's workflow, current state, available commands, and transition history; sitecore_invoke_workflow executes a command (Submit/Approve/...) - only commands available in the current state to this user run, and a refusal lists what IS available. Created items and versions (create_item, add_item_version, copy_item, upload_media) start in their workflow's initial state, as in the Content Editor; the result's 'workflow' says which workflow and state.

Structure: sitecore_change_item_template swaps an item's template and diffs every populated field - dropped values are returned in dataLost with their old values, never lost silently. add/remove_base_template edit a template's inheritance (admin). add_item_version creates a version (sourceLanguage seeds a translation); remove_item_version removes one, saying so when it was the language's last. sitecore_query_items runs a Sitecore query (XPath-like axes) for structural lookups - prefer search for text/template.

Publishing: content written to master is NOT live until published. sitecore_publish_item publishes an item (optionally deep, optionally related items) to the configured targets; it starts a background job and returns a handle immediately, so poll sitecore_get_jobs with that handle rather than assuming it finished. Publish targets obey the client's permitted databases, so a master-only client cannot publish to web. A running publish cannot be cancelled - Sitecore offers no safe way to stop one - so scope a publish narrowly (a specific path, deep only when needed) rather than starting a large one you may want to stop.

Dev/ops (administrator clients only): sitecore_rebuild_index rebuilds one index fully by name, or refreshes just a subtree via rootPath (cheaper - prefer after editing a branch); both return job handles to poll. sitecore_populate_solr_schema runs the populate-managed-schema operation (do it after adding indexed fields, BEFORE a rebuild). sitecore_rebuild_link_database refreshes the store behind the reference tools. sitecore_get_logs lists Sitecore's log files or tails one by name/prefix with level and search filters.

Configuration and packages (administrator clients only): sitecore_get_config reads the merged <sitecore> configuration by XPath, as showconfig.aspx shows it - each node's XML, path, attributes and patch:source (the include file that set it); keep it small with nodeOnly/depth/maxNodes, and pass rules (e.g. role) or layers to see another role's configuration. Values whose names look secret (password, secret, key, token, connectionString) are always redacted. Sitecore-MCP does not change configuration. Packages live in the packages folder and are addressed by file name: sitecore_list_packages, sitecore_inspect_package (contents, existing items/files, and whether install would be accepted - without installing), sitecore_create_package (metadata + items with/without subitems + web-root files; secrets, license and App_Data are never packaged), sitecore_upload_package / sitecore_download_package (base64, size-capped). sitecore_install_package ALWAYS returns a preview first, for every client (it can replace content anywhere the package says and, where allowed, deploy code and restart the site): tell the user what the preview says and stop until a person confirms. Existing items and files are skipped by default (itemCollision/fileCollision change that); code files and post steps need the Mcp.Packages.AllowCode setting; with content roots every package item must be inside them. Installed items land in master unpublished.

Membership (administrator clients only; all hidden from a non-admin client): read with sitecore_get_user/get_role/get_domain (by exact identity or a name substring filter; passwords never returned). Write with new_user/remove_user, enable_user/disable_user, unlock_user, new_role/remove_role, add_role_member/remove_role_member (member may be a user OR a role - roles nest), new_domain/remove_domain. Built-in accounts and domains are protected from deletion. Item access rules (also admin-only): sitecore_test_item_acl (does an account have a right on an item, after inheritance and denies), sitecore_add_item_acl / set_item_acl / clear_item_acl (append / replace-for-one-account / remove local rules).

Prefer typed filters (name, template, date ranges) over raw fieldEquals, which needs exact indexed field names (e.g. _name, not name) and silently matches nothing when wrong.";
    }
}
