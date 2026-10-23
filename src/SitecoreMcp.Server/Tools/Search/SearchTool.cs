using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Data;
using Sitecore.Security.AccessControl;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Templates;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>Arguments for <see cref="SearchTool"/>.</summary>
    public sealed class SearchArgs
    {
        /// <summary>Free text matched against the indexed content.</summary>
        [McpParam(Description = "Free text matched against indexed content (tokenized; excludes standard/security fields and substrings). To scan raw field values, use sitecore_grep.")]
        public string Text { get; set; }

        /// <summary>Find items whose item name equals this exactly.</summary>
        [McpParam(Description = "Find items by exact item name (the surest way to locate an item by name).")]
        public string Name { get; set; }

        /// <summary>Find items whose item name contains this substring; with several words, every word.</summary>
        [McpParam(Description = "Find items whose item name contains this substring; with several words, the name must contain every word.")]
        public string NameContains { get; set; }

        /// <summary>Restrict to items of a given template, by path, ID, or name.</summary>
        [McpParam(Description = "Restrict to a template by path, ID, or name (exact, or a unique partial name).")]
        public string Template { get; set; }

        /// <summary>Restrict to descendants of this item, by path or ID.</summary>
        [McpParam(Description = "Restrict to items under this path or ID.")]
        public string RootPath { get; set; }

        /// <summary>Restrict to a single language.</summary>
        [McpParam(Description = "Restrict to a language code (e.g. 'en').")]
        public string Language { get; set; }

        /// <summary>Equality filters on raw indexed fields, ANDed together.</summary>
        [McpParam(Description = "Filter by indexed field equality as field->value (raw indexed field names).")]
        public Dictionary<string, string> FieldEquals { get; set; }

        /// <summary>Only items updated on or after this ISO date.</summary>
        [McpParam(Description = "Only items updated on/after this ISO date (e.g. 2026-07-01).")]
        public string UpdatedAfter { get; set; }

        /// <summary>Only items updated on or before this ISO date.</summary>
        [McpParam(Description = "Only items updated on/before this ISO date.")]
        public string UpdatedBefore { get; set; }

        /// <summary>Only items created on or after this ISO date.</summary>
        [McpParam(Description = "Only items created on/after this ISO date.")]
        public string CreatedAfter { get; set; }

        /// <summary>Only items created on or before this ISO date.</summary>
        [McpParam(Description = "Only items created on/before this ISO date.")]
        public string CreatedBefore { get; set; }

        /// <summary>Field to sort by: updated, created, name, template, or a raw indexed field.</summary>
        [McpParam(Description = "Sort by: 'updated', 'created', 'name', 'template', or a raw indexed field.")]
        public string SortBy { get; set; }

        /// <summary>Whether to sort descending; default is ascending.</summary>
        [McpParam(Description = "Sort descending. Default false (ascending).")]
        public bool? SortDesc { get; set; }

        /// <summary>Return only the total match count, without hits.</summary>
        [McpParam(Description = "Return only the total match count, no hits.")]
        public bool? CountOnly { get; set; }

        /// <summary>Which database's index to query; defaults to master.</summary>
        [McpParam(Description = "Database whose index to search: master, web, or core. Defaults to master.")]
        public string Database { get; set; }

        /// <summary>The maximum number of hits to return in one call.</summary>
        [McpParam(Description = "Maximum hits to return (default 20, max 100).")]
        public int? Limit { get; set; }

        /// <summary>The number of hits to skip, for paging.</summary>
        [McpParam(Description = "Number of hits to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Queries a ContentSearch index with bounded, paged results. Combines optional filters (text,
    /// template, subtree, language, field equality, date ranges), sorting, and a count-only mode so
    /// a model can find items, or just count them, without walking the tree.
    /// </summary>
    public sealed class SearchTool : McpTool<SearchArgs>
    {
        private const int DefaultLimit = 20;
        private const int MaxLimit = 100;

        /// <inheritdoc />
        public override string Name => "sitecore_search";

        /// <inheritdoc />
        public override string Description =>
            "Search a ContentSearch index by any combination of item name (exact or partial), free text, " +
            "template, subtree, language, indexed-field equality, and created/updated date ranges. To " +
            "find an item by its name use 'name', not free text. Hits are grouped by item, each " +
            "listing the languages it matched. Sort by a field, page results, or pass countOnly for " +
            "just the total. Prefer this over tree walking to locate items.";

        /// <inheritdoc />
        protected override McpToolResult Execute(SearchArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);
            var indexName = $"sitecore_{db.Name}_index";

            var index = ContentSearchManager.GetIndex(indexName);
            if (index == null)
            {
                throw new McpToolException($"Search index '{indexName}' does not exist.");
            }

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);

            using (var searchContext = index.CreateSearchContext())
            {
                var query = searchContext.GetQueryable<SearchResultItem>();
                query = ApplyFilters(query, db, args, context, out var resolvedTemplate);
                if (query == null)
                {
                    // Outside every search root, or filtering on a hidden field: nothing can match.
                    return McpToolResult.Structured(EmptyResult(indexName, args, range));
                }

                query = ApplySort(query, args, context);

                // The index's free-text content covers every field, hidden ones included, so a text hit
                // is kept only when the terms appear in a visible field; the total is then an upper bound.
                var verifyText = !string.IsNullOrEmpty(args.Text) && context.Scope.HasHiddenFields;

                // Echo the template the filter actually resolved to, so a caller that passed a name
                // (possibly a partial one) learns and can report the real template.
                JObject templateInfo = resolvedTemplate == null ? null : new JObject
                {
                    ["id"] = resolvedTemplate.ID.ToString(),
                    ["name"] = resolvedTemplate.Name,
                    ["path"] = resolvedTemplate.Paths.FullPath
                };

                if (args.CountOnly.GetValueOrDefault(false))
                {
                    var total = query.Take(1).GetResults().TotalSearchResults;
                    var countResult = new JObject
                    {
                        ["index"] = indexName,
                        ["total"] = total,
                        ["countOnly"] = true
                    };
                    if (templateInfo != null) countResult["resolvedTemplate"] = templateInfo;
                    if (verifyText) countResult["totalIsUpperBound"] = true;
                    AddEmptyTextHint(countResult, args, total);
                    return McpToolResult.Structured(countResult);
                }

                var results = query.Skip(range.Offset).Take(range.Limit).GetResults();

                // The index holds one document per item/language/version, so collapse those into one
                // hit per item that lists the languages it matched — otherwise a page is mostly the
                // same items repeated, which inflates counts and buries what the caller is after.
                var order = new List<string>();
                var byItem = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                var rawCount = 0;
                foreach (var hit in results.Hits)
                {
                    rawCount++;
                    var doc = hit.Document;

                    // Defence in depth: the query is already scoped, but never return an item outside
                    // the search roots, nor one that matched the text only through a hidden field.
                    if (!context.Scope.IsInSearchRoot(doc.Path) ||
                        (verifyText && !MatchesVisibleText(db, doc, args.Text, context)))
                    {
                        continue;
                    }
                    var id = doc.ItemId?.ToString() ?? System.Guid.NewGuid().ToString("B");

                    if (!byItem.TryGetValue(id, out var entry))
                    {
                        entry = new JObject
                        {
                            ["id"] = id,
                            ["name"] = doc.Name,
                            ["path"] = doc.Path,
                            ["templateName"] = doc.TemplateName,
                            ["score"] = hit.Score,
                            ["languages"] = new JArray()
                        };
                        byItem[id] = entry;
                        order.Add(id);
                    }

                    var languages = (JArray)entry["languages"];
                    if (!string.IsNullOrEmpty(doc.Language) && !languages.Any(l => (string)l == doc.Language))
                    {
                        languages.Add(doc.Language);
                    }
                }

                var hits = new JArray(order.Select(id => (object)byItem[id]).ToArray());

                // Bespoke envelope: hits are grouped by item, so hasMore compares the raw document
                // count on this page (rawCount), not the grouped hits.Count, to the total.
                var searchResult = new JObject
                {
                    ["index"] = indexName,
                    ["total"] = results.TotalSearchResults,
                    ["offset"] = range.Offset,
                    ["count"] = hits.Count,
                    ["hasMore"] = range.Offset + rawCount < results.TotalSearchResults,
                    ["hits"] = hits
                };
                if (templateInfo != null) searchResult["resolvedTemplate"] = templateInfo;
                if (verifyText) searchResult["totalIsUpperBound"] = true;
                AddEmptyTextHint(searchResult, args, results.TotalSearchResults);
                return McpToolResult.Structured(searchResult);
            }
        }

        private static JObject EmptyResult(string indexName, SearchArgs args, PageRange range)
        {
            var empty = new JObject
            {
                ["index"] = indexName,
                ["total"] = 0
            };

            if (args.CountOnly.GetValueOrDefault(false))
            {
                empty["countOnly"] = true;
                return empty;
            }

            empty["offset"] = range.Offset;
            empty["count"] = 0;
            empty["hasMore"] = false;
            empty["hits"] = new JArray();
            return empty;
        }

        private static bool MatchesVisibleText(Sitecore.Data.Database db, SearchResultItem doc, string text, McpCallContext context)
        {
            if (ID.IsNullOrEmpty(doc.ItemId)) return false;

            var language = !string.IsNullOrEmpty(doc.Language) && Sitecore.Globalization.Language.TryParse(doc.Language, out var parsed)
                ? parsed
                : context.ResolveLanguage(null);
            var item = db.GetItem(doc.ItemId, language);
            if (item == null) return false;

            item.Fields.ReadAll();
            var values = context.VisibleFields(item)
                .Where(field => AuthorizationManager.IsAllowed(field, AccessRight.FieldRead, context.User))
                .Select(field => field.Value)
                .Concat(new[] { item.Name, item.DisplayName });
            return Security.ContentScope.TextFoundIn(text, values);
        }

        // Free-text search only sees indexed content, so an empty result for a text query often
        // means the caller wanted a raw field-value match, which sitecore_grep does.
        private static void AddEmptyTextHint(JObject result, SearchArgs args, long total)
        {
            if (total == 0 && !string.IsNullOrEmpty(args.Text))
            {
                result["hint"] =
                    "No indexed-content matches. To find items whose raw field value contains this " +
                    "text (any field, substring or regex, including standard/security fields), use " +
                    "sitecore_grep with a rootPath.";
            }
        }

        private static IQueryable<SearchResultItem> ApplyFilters(
            IQueryable<SearchResultItem> query, Sitecore.Data.Database db, SearchArgs args, McpCallContext context,
            out Sitecore.Data.Items.Item resolvedTemplate)
        {
            resolvedTemplate = null;

            if (!string.IsNullOrEmpty(args.Text))
            {
                var text = args.Text;
                query = query.Where(i => i.Content.Contains(text));
            }

            if (!string.IsNullOrEmpty(args.Name))
            {
                var name = args.Name;
                query = query.Where(i => i.Name == name);
            }

            // Every word must be in the name (NameTerms): one Contains with several words matches any of them in Solr.
            foreach (var term in NameTerms.Split(args.NameContains))
            {
                var word = term;
                query = query.Where(i => i.Name.Contains(word));
            }

            if (!string.IsNullOrEmpty(args.Template))
            {
                resolvedTemplate = TemplateResolver.Resolve(db, args.Template);
                var templateId = resolvedTemplate.ID;
                query = query.Where(i => i.TemplateId == templateId);
            }

            query = SearchScopeFilter.Restrict(query, db, args.RootPath, context);
            if (query == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(args.Language))
            {
                var language = args.Language;
                query = query.Where(i => i.Language == language);
            }

            if (args.FieldEquals != null)
            {
                foreach (var pair in args.FieldEquals)
                {
                    if (context.Scope.IsHiddenIndexField(pair.Key))
                    {
                        // Treated like a field the index does not have: nothing matches.
                        return null;
                    }

                    var field = pair.Key;
                    var value = pair.Value;
                    query = query.Where(i => i[field] == value);
                }
            }

            if (!string.IsNullOrEmpty(args.UpdatedAfter))
            {
                var d = ParseDate(args.UpdatedAfter, nameof(args.UpdatedAfter));
                query = query.Where(i => i.Updated >= d);
            }

            if (!string.IsNullOrEmpty(args.UpdatedBefore))
            {
                var d = ParseDate(args.UpdatedBefore, nameof(args.UpdatedBefore));
                query = query.Where(i => i.Updated <= d);
            }

            if (!string.IsNullOrEmpty(args.CreatedAfter))
            {
                var d = ParseDate(args.CreatedAfter, nameof(args.CreatedAfter));
                query = query.Where(i => i.CreatedDate >= d);
            }

            if (!string.IsNullOrEmpty(args.CreatedBefore))
            {
                var d = ParseDate(args.CreatedBefore, nameof(args.CreatedBefore));
                query = query.Where(i => i.CreatedDate <= d);
            }

            return query;
        }

        private static IQueryable<SearchResultItem> ApplySort(IQueryable<SearchResultItem> query, SearchArgs args, McpCallContext context)
        {
            // Sorting by a hidden field would order results by its values, so it is ignored.
            if (string.IsNullOrEmpty(args.SortBy) || context.Scope.IsHiddenIndexField(args.SortBy))
            {
                return query;
            }

            var desc = args.SortDesc.GetValueOrDefault(false);
            switch (args.SortBy.ToLowerInvariant())
            {
                case "updated":
                    return desc ? query.OrderByDescending(i => i.Updated) : query.OrderBy(i => i.Updated);
                case "created":
                    return desc ? query.OrderByDescending(i => i.CreatedDate) : query.OrderBy(i => i.CreatedDate);
                case "name":
                    return desc ? query.OrderByDescending(i => i.Name) : query.OrderBy(i => i.Name);
                case "template":
                case "templatename":
                    return desc ? query.OrderByDescending(i => i.TemplateName) : query.OrderBy(i => i.TemplateName);
                default:
                    var field = args.SortBy;
                    return desc ? query.OrderByDescending(i => i[field]) : query.OrderBy(i => i[field]);
            }
        }

        private static DateTime ParseDate(string value, string argName)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
            {
                throw new McpToolException($"'{argName}' is not a valid date: '{value}'. Use an ISO date like 2026-07-01.");
            }

            return result;
        }
    }
}
