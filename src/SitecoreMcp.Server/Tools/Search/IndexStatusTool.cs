using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>Arguments for <see cref="IndexStatusTool"/>.</summary>
    public sealed class IndexStatusArgs
    {
        /// <summary>A specific index to report; when omitted, all indexes are reported.</summary>
        [McpParam(Description = "A specific index name to report. Omit to report every index.")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Reports ContentSearch index health: document counts, last update, and whether an index is out
    /// of date. Useful before trusting search results or deciding whether a rebuild is needed.
    /// </summary>
    public sealed class IndexStatusTool : McpTool<IndexStatusArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_index_status";

        /// <inheritdoc />
        public override string Description =>
            "Report ContentSearch index status: document count, last updated, field count, and whether " +
            "the index is out of date. Pass a name for one index, or omit to list all.";

        /// <inheritdoc />
        protected override McpToolResult Execute(IndexStatusArgs args, McpCallContext context)
        {
            var indexes = new JArray();

            if (!string.IsNullOrEmpty(args.Name))
            {
                var index = ContentSearchManager.GetIndex(args.Name);
                if (index == null)
                {
                    throw new McpToolException($"Search index '{args.Name}' does not exist.");
                }

                indexes.Add(Describe(index));
            }
            else
            {
                foreach (var index in ContentSearchManager.Indexes)
                {
                    indexes.Add(Describe(index));
                }
            }

            return McpToolResult.Structured(new JObject
            {
                ["count"] = indexes.Count,
                ["indexes"] = indexes
            });
        }

        private static JObject Describe(ISearchIndex index)
        {
            var result = new JObject { ["name"] = index.Name };

            // One unreadable index (e.g. a stopped Solr core) should not fail the whole report.
            try
            {
                // Summary is marked obsolete but remains the standard, sufficient source of these
                // stats; the suggested replacements add a lot of surface for no extra value here.
#pragma warning disable CS0618
                var summary = index.Summary;
#pragma warning restore CS0618
                result["documents"] = summary.NumberOfDocuments;
                result["fields"] = summary.NumberOfFields;
                result["lastUpdated"] = summary.LastUpdated.ToString("o");
                result["outOfDate"] = summary.OutOfDateIndex;
            }
            catch (System.Exception ex)
            {
                result["error"] = ex.Message;
            }

            return result;
        }
    }
}
