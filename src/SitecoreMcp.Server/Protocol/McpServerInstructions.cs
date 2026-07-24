namespace SitecoreMcp.Server.Protocol
{
    /// <summary>
    /// The server usage guidance returned in the initialize result. Compliant clients fold this into
    /// the model's context, so it is the portable, every-client home for cross-tool guidance. Keep it
    /// concise; the fuller version lives in docs/TOOL_GUIDE.md.
    /// </summary>
    public static class McpServerInstructions
    {
        /// <summary>The instructions string advertised at initialize.</summary>
        public const string Text =
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

Writes (only if allowed): create/update/move/copy/rename/delete_item, and create_template. update changes only the fields you pass. copy_item copies field DATA, not just structure. delete_item recycles by default; permanent=true destroys irreversibly. create_template takes base templates (default Standard Template), sections, and typed fields; field types must be exact (e.g. Single-Line Text), field names must be unique across the whole template, and the whole definition is validated before anything is created. Field edits handle item locking automatically for non-admins (lock, edit, restore); a write to an item locked by another user is refused, and a rejected save is reported (never a silent no-op). update_item verifies each change actually persisted and lists any silently dropped (by field security, a computed field, or a save handler) in notPersisted.

Presentation: sitecore_get_renderings lists the components on an item for a device (each with placeholder, datasource, parameters, and a unique ID) - it reads the effective final layout, resolving inheritance. Target the write tools by unique ID: sitecore_add_rendering, sitecore_set_rendering (only the parts you pass change; a null parameter value removes that key), sitecore_switch_rendering (swap component in place, keeping placeholder/datasource/parameters), sitecore_remove_rendering, sitecore_reset_layout (back to standard-values inheritance). Writes edit the per-version final layout by default (finalLayout=false targets the shared base for all versions). Rendering references are exact-only on writes, like templates. A rendering inherited from standard values still appears with a unique ID, so an empty final layout does not mean nothing renders.

References: sitecore_get_item_referrers lists what points AT an item - run it before delete_item/move_item/rename_item to see what would break; sitecore_get_item_references lists what an item points at. sitecore_update_item_referrers repoints those incoming links at another item, or removes them when newTarget is omitted (it edits the OTHER items, so check referrers first). All three read Sitecore's Link Database, so results are only as fresh as its last update: an empty referrers result is not proof nothing references the item. A reference with resolved=false is either deleted or unreadable - indistinguishable here.

Publishing: content written to master is NOT live until published. sitecore_publish_item publishes an item (optionally deep, optionally related items) to the configured targets; it starts a background job and returns a handle immediately, so poll sitecore_get_jobs with that handle rather than assuming it finished. Publish targets obey the client's permitted databases, so a master-only client cannot publish to web. A running publish cannot be cancelled - Sitecore offers no safe way to stop one - so scope a publish narrowly (a specific path, deep only when needed) rather than starting a large one you may want to stop.

Prefer typed filters (name, template, date ranges) over raw fieldEquals, which needs exact indexed field names (e.g. _name, not name) and silently matches nothing when wrong.";
    }
}
