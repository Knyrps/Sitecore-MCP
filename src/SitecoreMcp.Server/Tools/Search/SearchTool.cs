using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.SearchTypes;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>Arguments for <see cref="SearchTool"/>.</summary>
    public sealed class SearchArgs
    {
        /// <summary>Free text matched against item content.</summary>
        [McpParam(Description = "Free text to match against item content.")]
        public string Text { get; set; }

        /// <summary>Find items whose item name equals this exactly.</summary>
        [McpParam(Description = "Find items by exact item name (the surest way to locate an item by name).")]
        public string Name { get; set; }

        /// <summary>Find items whose item name contains this substring.</summary>
        [McpParam(Description = "Find items whose item name contains this substring.")]
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
            "find an item by its name use 'name', not free text. Sort by a field, page results, or pass " +
            "countOnly for just the total. Prefer this over tree walking to locate items.";

        /// <inheritdoc />
        protected override McpToolResult Execute(SearchArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(string.IsNullOrEmpty(args.Database) ? "master" : args.Database);
            var indexName = $"sitecore_{db.Name}_index";

            var index = ContentSearchManager.GetIndex(indexName);
            if (index == null)
            {
                throw new McpToolException($"Search index '{indexName}' does not exist.");
            }

            var offset = Math.Max(0, args.Offset.GetValueOrDefault(0));
            var limit = args.Limit.GetValueOrDefault(DefaultLimit);
            if (limit < 1) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;

            using (var searchContext = index.CreateSearchContext())
            {
                var query = searchContext.GetQueryable<SearchResultItem>();
                query = ApplyFilters(query, db, args, out var resolvedTemplate);
                query = ApplySort(query, args);

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
                    return McpToolResult.Structured(countResult);
                }

                var results = query.Skip(offset).Take(limit).GetResults();

                var hits = new JArray();
                foreach (var hit in results.Hits)
                {
                    var doc = hit.Document;
                    hits.Add(new JObject
                    {
                        ["id"] = doc.ItemId?.ToString(),
                        ["name"] = doc.Name,
                        ["path"] = doc.Path,
                        ["templateName"] = doc.TemplateName,
                        ["language"] = doc.Language,
                        ["score"] = hit.Score
                    });
                }

                var searchResult = new JObject
                {
                    ["index"] = indexName,
                    ["total"] = results.TotalSearchResults,
                    ["offset"] = offset,
                    ["count"] = hits.Count,
                    ["hasMore"] = offset + hits.Count < results.TotalSearchResults,
                    ["hits"] = hits
                };
                if (templateInfo != null) searchResult["resolvedTemplate"] = templateInfo;
                return McpToolResult.Structured(searchResult);
            }
        }

        private static IQueryable<SearchResultItem> ApplyFilters(
            IQueryable<SearchResultItem> query, Sitecore.Data.Database db, SearchArgs args,
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

            if (!string.IsNullOrEmpty(args.NameContains))
            {
                var nameContains = args.NameContains;
                query = query.Where(i => i.Name.Contains(nameContains));
            }

            if (!string.IsNullOrEmpty(args.Template))
            {
                resolvedTemplate = TemplateResolver.Resolve(db, args.Template);
                var templateId = resolvedTemplate.ID;
                query = query.Where(i => i.TemplateId == templateId);
            }

            if (!string.IsNullOrEmpty(args.RootPath))
            {
                var root = db.GetItem(args.RootPath);
                if (root == null)
                {
                    throw new McpToolException($"Root '{args.RootPath}' was not found.");
                }

                var rootId = root.ID;
                query = query.Where(i => i.Paths.Contains(rootId));
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

        private static IQueryable<SearchResultItem> ApplySort(IQueryable<SearchResultItem> query, SearchArgs args)
        {
            if (string.IsNullOrEmpty(args.SortBy))
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
