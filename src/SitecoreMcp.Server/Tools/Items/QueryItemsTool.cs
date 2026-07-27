using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="QueryItemsTool"/>.</summary>
    public sealed class QueryItemsArgs
    {
        /// <summary>The Sitecore query to run.</summary>
        [McpParam(Description = "Sitecore query, e.g. /sitecore/content//*[@@templatename='Page'] or fast:/sitecore/content/Home//*. XPath-like axes and @fieldname comparisons are supported.", Required = true)]
        public string Query { get; set; }

        /// <summary>The database to query; defaults to master.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }

        /// <summary>The maximum number of items to return in one call.</summary>
        [McpParam(Description = "Maximum items to return (default 50, max 200).")]
        public int? Limit { get; set; }

        /// <summary>The number of items to skip, for paging.</summary>
        [McpParam(Description = "Number of items to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Runs a Sitecore query - the XPath-like axis language - for lookups that are awkward in the
    /// index-backed search (positional predicates, ancestor axes) or in grep. Item security applies.
    /// </summary>
    public sealed class QueryItemsTool : McpTool<QueryItemsArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_query_items";

        /// <inheritdoc />
        public override string Description =>
            "Run a Sitecore query (XPath-like axes over the content tree, e.g. " +
            "/sitecore/content//*[@@templatename='Page']; prefix fast: for a database-level fast " +
            "query). Use for structural lookups search and grep are awkward at - axes, positional " +
            "predicates, @field comparisons. Walks the tree live, so scope the query; prefer " +
            "sitecore_search for text and template lookups. Item security applies.";

        /// <inheritdoc />
        protected override McpToolResult Execute(QueryItemsArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);

            Sitecore.Data.Items.Item[] items;
            try
            {
                items = db.SelectItems(args.Query) ?? new Sitecore.Data.Items.Item[0];
            }
            catch (Exception ex)
            {
                // Sitecore surfaces a malformed query as a deep parser exception; return the message
                // as a tool failure the model can correct from.
                throw new McpToolException($"The query could not be run: {ex.Message}");
            }

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);
            var projector = new ItemProjector(context);
            var page = new JArray(items
                .Skip(range.Offset)
                .Take(range.Limit)
                .Select(item => (object)projector.ProjectSummary(item))
                .ToArray());

            var result = Paging.Envelope("items", page, items.Length, range);
            result["query"] = args.Query;
            result["database"] = db.Name;
            return McpToolResult.Structured(result);
        }
    }
}
