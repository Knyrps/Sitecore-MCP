using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetChildrenTool"/>.</summary>
    public sealed class GetChildrenArgs : ItemQueryArgs
    {
        /// <summary>The maximum number of children to return in one call.</summary>
        [McpParam(Description = "Maximum children to return (default 50, max 500).")]
        public int? Limit { get; set; }

        /// <summary>The number of children to skip, for paging.</summary>
        [McpParam(Description = "Number of children to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>Lists the immediate children of an item, paged and capped so large folders stay manageable.</summary>
    public sealed class GetChildrenTool : McpTool<GetChildrenArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 500;

        /// <inheritdoc />
        public override string Name => "sitecore_get_children";

        /// <inheritdoc />
        public override string Description =>
            "List the immediate children of a Sitecore item. Results are paged; use 'offset' and " +
            "'limit' to page through large folders.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetChildrenArgs args, McpCallContext context)
        {
            var parent = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var offset = args.Offset.GetValueOrDefault(0);
            if (offset < 0) offset = 0;
            var limit = args.Limit.GetValueOrDefault(DefaultLimit);
            if (limit < 1) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;

            var projector = new ItemProjector(context);
            var all = parent.Children;
            var total = all.Count;

            var children = new JArray();
            var taken = 0;
            for (var i = offset; i < total && taken < limit; i++, taken++)
            {
                children.Add(projector.ProjectSummary(all[i]));
            }

            return McpToolResult.Structured(new JObject
            {
                ["parentId"] = parent.ID.ToString(),
                ["parentPath"] = parent.Paths.FullPath,
                ["total"] = total,
                ["offset"] = offset,
                ["count"] = taken,
                ["hasMore"] = offset + taken < total,
                ["children"] = children
            });
        }
    }
}
