using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Data;
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

        /// <summary>Restrict to items of a given template, by path or ID.</summary>
        [McpParam(Description = "Restrict to a template, by path or ID.")]
        public string Template { get; set; }

        /// <summary>Restrict to descendants of this item, by path or ID.</summary>
        [McpParam(Description = "Restrict to items under this path or ID.")]
        public string RootPath { get; set; }

        /// <summary>Restrict to a single language.</summary>
        [McpParam(Description = "Restrict to a language code (e.g. 'en').")]
        public string Language { get; set; }

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
    /// template, subtree, language) so a model can find items without walking the tree.
    /// </summary>
    public sealed class SearchTool : McpTool<SearchArgs>
    {
        private const int DefaultLimit = 20;
        private const int MaxLimit = 100;

        /// <inheritdoc />
        public override string Name => "sitecore_search";

        /// <inheritdoc />
        public override string Description =>
            "Search a Sitecore ContentSearch index by any combination of free text, template, subtree, " +
            "and language. Results are paged. Prefer this over tree walking to locate items.";

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

                if (!string.IsNullOrEmpty(args.Text))
                {
                    var text = args.Text;
                    query = query.Where(i => i.Content.Contains(text));
                }

                if (!string.IsNullOrEmpty(args.Template))
                {
                    var templateItem = db.GetItem(args.Template);
                    if (templateItem == null)
                    {
                        throw new McpToolException($"Template '{args.Template}' was not found.");
                    }

                    var templateId = templateItem.ID;
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

                return McpToolResult.Structured(new JObject
                {
                    ["index"] = indexName,
                    ["total"] = results.TotalSearchResults,
                    ["offset"] = offset,
                    ["count"] = hits.Count,
                    ["hasMore"] = offset + hits.Count < results.TotalSearchResults,
                    ["hits"] = hits
                });
            }
        }
    }
}
