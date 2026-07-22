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

Templates: sitecore_get_template shows an item's fields (own + inherited) with types - call it before setting fields so you use real names. sitecore_list_templates finds a template. Template arguments accept a name (exact or unique partial), not just a path or ID.

Reads: get_item returns populated non-standard fields by default; an empty field map is normal for structural items and carries a hint plus fieldStats. Pass includeStandardFields/includeEmpty or explicit 'fields' for more. Search hits are grouped by item, listing the languages each matched.

Writes (only if allowed): create/update/move/copy/rename/delete_item. update changes only the fields you pass. copy_item copies field DATA, not just structure. delete_item recycles by default; permanent=true destroys irreversibly. Field edits handle item locking automatically for non-admins (lock, edit, restore); a write to an item locked by another user is refused, and a rejected save is reported (never a silent no-op).

Prefer typed filters (name, template, date ranges) over raw fieldEquals, which needs exact indexed field names (e.g. _name, not name) and silently matches nothing when wrong.";
    }
}
