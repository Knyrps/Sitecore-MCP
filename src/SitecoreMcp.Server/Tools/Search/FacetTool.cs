using System;
using System.Linq;
using System.Linq.Expressions;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.SearchTypes;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>Arguments for <see cref="FacetTool"/>.</summary>
    public sealed class FacetArgs
    {
        /// <summary>The field to group by: "template", "language", or a raw indexed field name.</summary>
        [McpParam(Description = "Field to group by: 'template', 'language', or a raw indexed field name.", Required = true)]
        public string Field { get; set; }

        /// <summary>Free text to scope the facet to matching items.</summary>
        [McpParam(Description = "Optional free text to scope the facet to matching items.")]
        public string Text { get; set; }

        /// <summary>Scope the facet to descendants of this item, by path or ID.</summary>
        [McpParam(Description = "Optional path or ID to scope the facet to a subtree.")]
        public string RootPath { get; set; }

        /// <summary>Scope the facet to a single language.</summary>
        [McpParam(Description = "Optional language code to scope the facet.")]
        public string Language { get; set; }

        /// <summary>Which database's index to query; defaults to master.</summary>
        [McpParam(Description = "Database whose index to use: master, web, or core. Defaults to master.")]
        public string Database { get; set; }

        /// <summary>The minimum count for a value to be included.</summary>
        [McpParam(Description = "Minimum count for a value to be returned. Default 1.")]
        public int? MinCount { get; set; }

        /// <summary>The maximum number of distinct values to return.</summary>
        [McpParam(Description = "Maximum distinct values to return (default 50, max 200).")]
        public int? Limit { get; set; }
    }

    /// <summary>
    /// Returns group-by counts over an indexed field (template distribution, language coverage, any
    /// field's value breakdown), optionally scoped by text, subtree, and language. Answers "what is
    /// here and in what proportion" in one query, without walking the tree.
    /// </summary>
    public sealed class FacetTool : McpTool<FacetArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_facet";

        /// <inheritdoc />
        public override string Description =>
            "Aggregate counts grouped by an indexed field, e.g. how many items use each template " +
            "under a path, language coverage, or the value distribution of a field. Use it to " +
            "understand the shape of the content without reading every item.";

        /// <inheritdoc />
        protected override McpToolResult Execute(FacetArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(string.IsNullOrEmpty(args.Database) ? "master" : args.Database);
            var indexName = $"sitecore_{db.Name}_index";
            var index = ContentSearchManager.GetIndex(indexName);
            if (index == null)
            {
                throw new McpToolException($"Search index '{indexName}' does not exist.");
            }

            var minCount = Math.Max(1, args.MinCount.GetValueOrDefault(1));
            var limit = args.Limit.GetValueOrDefault(DefaultLimit);
            if (limit < 1) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;

            using (var searchContext = index.CreateSearchContext())
            {
                var query = searchContext.GetQueryable<SearchResultItem>();

                if (!string.IsNullOrEmpty(args.Text))
                {
                    var text = args.Text;
                    query = query.Where(i => i.Content.Contains(text));
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

                var results = query.FacetOn(FacetSelector(args.Field), minCount).Take(1).GetResults();
                var category = results.Facets?.Categories?.FirstOrDefault();

                var values = new JArray();
                if (category != null)
                {
                    foreach (var value in category.Values.OrderByDescending(v => v.AggregateCount).Take(limit))
                    {
                        values.Add(new JObject
                        {
                            ["value"] = value.Name,
                            ["count"] = value.AggregateCount
                        });
                    }
                }

                var result = new JObject
                {
                    ["index"] = indexName,
                    ["field"] = args.Field,
                    ["totalMatched"] = results.TotalSearchResults,
                    ["distinctValues"] = category?.Values.Count ?? 0,
                    ["values"] = values
                };

                // A raw field name that yields nothing is usually a wrong indexed-field name rather
                // than a genuinely empty facet, so nudge the caller instead of returning a silent void.
                if (values.Count == 0 && !IsFriendlyField(args.Field) && results.TotalSearchResults > 0)
                {
                    result["hint"] =
                        $"No facet values for '{args.Field}'. It may not be an indexed field name. " +
                        "Try 'template' or 'language', or use the exact Solr field name.";
                }

                return McpToolResult.Structured(result);
            }
        }

        private static bool IsFriendlyField(string field)
        {
            switch (field.ToLowerInvariant())
            {
                case "template":
                case "templatename":
                case "language":
                    return true;
                default:
                    return false;
            }
        }

        private static Expression<Func<SearchResultItem, object>> FacetSelector(string field)
        {
            switch (field.ToLowerInvariant())
            {
                case "template":
                case "templatename":
                    return i => i.TemplateName;
                case "language":
                    return i => i.Language;
                default:
                    // A raw indexed field name (Solr field), e.g. "__workflow state".
                    return i => i[field];
            }
        }
    }
}
