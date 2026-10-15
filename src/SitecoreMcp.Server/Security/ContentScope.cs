using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// What part of the content tree a call may change, where its search looks, and which fields do
    /// not exist for it:
    /// <list type="bullet">
    /// <item><b>Content roots</b> - paths under which items may be changed. Outside every content root
    /// an item is read-only.</item>
    /// <item><b>Search roots</b> - paths under which search looks. Independent of content roots.</item>
    /// <item><b>Hidden fields</b> - fields (by name or ID) that are never read, found, shown or written;
    /// to the caller they do not exist.</item>
    /// </list>
    /// A dimension that is not configured (null) does not restrict, so an instance without scope
    /// config behaves as before. Kept free of Sitecore so the rules are unit-testable; the tools pass
    /// item paths and field names/IDs in.
    /// </summary>
    public sealed class ContentScope
    {
        /// <summary>A scope that restricts nothing.</summary>
        public static readonly ContentScope Unrestricted = new ContentScope(null, null, null);

        private readonly List<string> _contentRoots;
        private readonly List<string> _searchRoots;
        private readonly HashSet<string> _hiddenFieldNames;
        private readonly HashSet<Guid> _hiddenFieldIds;

        /// <summary>
        /// Creates a scope. A null list leaves that dimension unrestricted; an empty content-root list
        /// makes every item read-only and an empty search-root list makes search find nothing.
        /// Hidden fields are matched by name (case-insensitive) or by ID (with or without braces).
        /// </summary>
        public ContentScope(IEnumerable<string> contentRoots, IEnumerable<string> searchRoots, IEnumerable<string> hiddenFields)
        {
            _contentRoots = NormalizeRoots(contentRoots);
            _searchRoots = NormalizeRoots(searchRoots);

            if (hiddenFields != null)
            {
                _hiddenFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _hiddenFieldIds = new HashSet<Guid>();
                foreach (var entry in hiddenFields)
                {
                    var trimmed = entry?.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (Guid.TryParse(trimmed, out var id)) _hiddenFieldIds.Add(id);
                    else _hiddenFieldNames.Add(trimmed);
                }
            }
        }

        /// <summary>The normalized content roots, or null when writes are not restricted by path.</summary>
        public IReadOnlyList<string> ContentRoots => _contentRoots;

        /// <summary>The normalized search roots, or null when search is not restricted by path.</summary>
        public IReadOnlyList<string> SearchRoots => _searchRoots;

        /// <summary>The configured hidden field names and IDs, or null when no field is hidden.</summary>
        public IReadOnlyCollection<string> HiddenFields =>
            _hiddenFieldNames == null
                ? null
                : _hiddenFieldNames.Concat(_hiddenFieldIds.Select(id => id.ToString("B").ToUpperInvariant())).ToList();

        /// <summary>
        /// Layers a narrower scope over a broader one: the overlay's content roots and search roots
        /// replace the base's where the overlay configures them; hidden fields are the union of both.
        /// </summary>
        public static ContentScope Combine(ContentScope baseScope, ContentScope overlay)
        {
            baseScope = baseScope ?? Unrestricted;
            overlay = overlay ?? Unrestricted;

            var baseHidden = baseScope.HiddenFields;
            var overlayHidden = overlay.HiddenFields;
            var hidden = baseHidden == null && overlayHidden == null
                ? null
                : (baseHidden ?? new string[0]).Concat(overlayHidden ?? new string[0]);

            return new ContentScope(
                overlay.ContentRoots ?? baseScope.ContentRoots,
                overlay.SearchRoots ?? baseScope.SearchRoots,
                hidden);
        }

        /// <summary>Whether an item at <paramref name="path"/> may be changed.</summary>
        public bool IsInContentRoot(string path) => _contentRoots == null || IsUnderAny(path, _contentRoots);

        /// <summary>
        /// Throws <see cref="McpToolException"/> when an item at <paramref name="path"/> is outside every
        /// content root, so the write is refused before anything changes.
        /// </summary>
        public void EnsureWritable(string path)
        {
            if (!IsInContentRoot(path))
            {
                throw new McpToolException(
                    $"'{path}' is outside every content root, so it is read-only. Content roots: {string.Join(", ", _contentRoots)}.");
            }
        }

        /// <summary>Whether search may return an item at <paramref name="path"/>.</summary>
        public bool IsInSearchRoot(string path) => _searchRoots == null || IsUnderAny(path, _searchRoots);

        /// <summary>
        /// The paths a search should start from, given the root the caller asked for (null for none).
        /// Without search roots that is just the requested root (null meaning the whole database). With
        /// them: every search root when none was requested; the requested root when it lies inside a
        /// search root; the search roots below it when it lies above some; otherwise nothing, so the
        /// search returns no results. Results must still be filtered with <see cref="IsInSearchRoot"/>,
        /// because an index or query can return items outside the start paths.
        /// </summary>
        public IReadOnlyList<string> SearchStartPaths(string requestedRoot)
        {
            var requested = NormalizePath(requestedRoot);
            if (_searchRoots == null) return new[] { requested };
            if (requested == null) return _searchRoots.ToList();
            if (_searchRoots.Any(root => IsUnder(requested, root))) return new[] { requested };
            return _searchRoots.Where(root => IsUnder(root, requested)).ToList();
        }

        /// <summary>Whether any field is hidden at all, so callers can skip per-field work when none is.</summary>
        public bool HasHiddenFields => _hiddenFieldNames != null && (_hiddenFieldNames.Count > 0 || _hiddenFieldIds.Count > 0);

        /// <summary>
        /// Whether the field with this name and/or ID is hidden. Either may be null when the caller only
        /// knows one (index field names, link-database field IDs); a field is hidden when either matches.
        /// </summary>
        public bool IsHiddenField(string name, string id)
        {
            if (!HasHiddenFields) return false;
            if (!string.IsNullOrEmpty(name) && _hiddenFieldNames.Contains(name.Trim())) return true;
            return !string.IsNullOrEmpty(id) && Guid.TryParse(id, out var guid) && _hiddenFieldIds.Contains(guid);
        }

        /// <summary>The fields that are not hidden, in their original order.</summary>
        public IEnumerable<T> VisibleFields<T>(IEnumerable<T> fields, Func<T, string> nameOf, Func<T, string> idOf)
        {
            if (!HasHiddenFields) return fields;
            return fields.Where(field => field != null && !IsHiddenField(nameOf(field), idOf(field)));
        }

        /// <summary>
        /// Looks a field up by the name or ID the caller gave, and returns null when it does not exist or
        /// is hidden. The hidden check runs on the field actually found (its real name and ID), so asking
        /// by ID, or by name in another case, cannot reach a hidden field.
        /// </summary>
        public T FindVisibleField<T>(string requested, Func<string, T> lookup, Func<T, string> nameOf, Func<T, string> idOf)
            where T : class
        {
            if (string.IsNullOrEmpty(requested)) return null;
            if (IsHiddenField(requested, requested)) return null;

            var field = lookup(requested);
            if (field == null || IsHiddenField(nameOf(field), idOf(field))) return null;
            return field;
        }

        /// <summary>
        /// Like <see cref="FindVisibleField{T}"/>, but throws the same "field not found" error for a
        /// hidden field as for one that does not exist, so a write cannot tell them apart.
        /// </summary>
        public T RequireVisibleField<T>(string requested, Func<string, T> lookup, Func<T, string> nameOf, Func<T, string> idOf,
            string templateName) where T : class
        {
            var field = FindVisibleField(requested, lookup, nameOf, idOf);
            if (field == null)
            {
                throw new McpToolException(FieldNotFound(requested, templateName));
            }

            return field;
        }

        /// <summary>The hidden field IDs, braced upper-case, so the host can look up their names.</summary>
        public IReadOnlyList<string> HiddenFieldIds =>
            _hiddenFieldIds == null
                ? new string[0]
                : _hiddenFieldIds.Select(id => id.ToString("B").ToUpperInvariant()).ToList();

        /// <summary>
        /// A copy that also hides the given field names. Used to add the names of fields configured by
        /// ID, so name-only checks (index fields, queries) catch them too.
        /// </summary>
        public ContentScope WithHiddenFieldNames(IEnumerable<string> names) =>
            new ContentScope(_contentRoots, _searchRoots, (HiddenFields ?? new string[0]).Concat(names ?? new string[0]));

        /// <summary>
        /// Whether a raw search-index field name belongs to a hidden field. Index field names are the
        /// lower-cased field name with spaces as underscores, optionally followed by a Solr type suffix
        /// and a language (e.g. "api_secret_t_en").
        /// </summary>
        public bool IsHiddenIndexField(string indexFieldName)
        {
            if (!HasHiddenFields || string.IsNullOrWhiteSpace(indexFieldName)) return false;

            var normalized = NormalizeIndexName(indexFieldName);
            foreach (var hidden in _hiddenFieldNames)
            {
                var name = NormalizeIndexName(hidden);
                if (normalized == name) return true;
                if (normalized.StartsWith(name + "_", StringComparison.Ordinal) &&
                    IndexSuffix.IsMatch(normalized.Substring(name.Length + 1))) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a Sitecore query tests a hidden field (@Name or @#Name#). Conservative: any such
        /// reference counts, so a query cannot find items by a hidden field's value.
        /// </summary>
        public bool MentionsHiddenField(string query)
        {
            if (!HasHiddenFields || string.IsNullOrEmpty(query)) return false;

            return _hiddenFieldNames.Any(name =>
                Regex.IsMatch(query, "@#?\\s*" + Regex.Escape(name),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        /// <summary>
        /// Whether every whitespace-separated term of a free-text query appears (case-insensitive) in at
        /// least one of the visible values. Used to drop index hits that matched only through a hidden
        /// field, since the index's content field covers every field.
        /// </summary>
        public static bool TextFoundIn(string text, IEnumerable<string> visibleValues)
        {
            var values = (visibleValues ?? new string[0]).Where(v => !string.IsNullOrEmpty(v)).ToList();
            var terms = (text ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0) return true;

            return terms.All(term => values.Any(value => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static readonly Regex IndexSuffix =
            new Regex("^[a-z]{1,3}(_[a-z]{2}(-[a-z]{2})?)?$",
                RegexOptions.CultureInvariant);

        private static string NormalizeIndexName(string name) =>
            name.Trim().ToLowerInvariant().Replace(' ', '_');

        /// <summary>The one "field not found" message, used for missing and hidden fields alike.</summary>
        public static string FieldNotFound(string name, string templateName) =>
            $"Field '{name}' does not exist on template '{templateName}'.";

        private static bool IsUnderAny(string path, IEnumerable<string> roots)
        {
            var normalized = NormalizePath(path);
            if (normalized == null) return false;
            return roots.Any(root => IsUnder(normalized, root));
        }

        /// <summary>Path-segment-aware, case-insensitive: /a/b is under /a, /ab is not.</summary>
        private static bool IsUnder(string normalizedPath, string normalizedRoot) =>
            string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedRoot == "/" ||
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);

        private static List<string> NormalizeRoots(IEnumerable<string> roots)
        {
            if (roots == null) return null;
            return roots.Select(NormalizePath).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizePath(string path)
        {
            if (path == null) return null;
            var trimmed = path.Trim().Replace('\\', '/');
            if (trimmed.Length == 0) return null;
            if (!trimmed.StartsWith("/", StringComparison.Ordinal)) trimmed = "/" + trimmed;
            while (trimmed.Length > 1 && trimmed.EndsWith("/", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }
    }
}
