using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Returns the chain of ancestors from the root down to an item, for orientation in the tree.</summary>
    public sealed class GetAncestorsTool : McpTool<ItemQueryArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_ancestors";

        /// <inheritdoc />
        public override string Description =>
            "List the ancestors of a Sitecore item from the root down to its parent, giving the " +
            "model the item's position in the content tree.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ItemQueryArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var projector = new ItemProjector(context);

            var ancestors = new JArray();
            foreach (var ancestor in item.Axes.GetAncestors())
            {
                ancestors.Add(projector.ProjectReference(ancestor));
            }

            return McpToolResult.Structured(new JObject
            {
                ["itemId"] = item.ID.ToString(),
                ["itemPath"] = item.Paths.FullPath,
                ["ancestors"] = ancestors
            });
        }
    }
}
