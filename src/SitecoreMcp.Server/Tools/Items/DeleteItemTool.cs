using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="DeleteItemTool"/>.</summary>
    public sealed class DeleteItemArgs : ItemQueryArgs
    {
        /// <summary>Whether to destroy the item permanently rather than recycling it.</summary>
        [McpParam(Description = "Delete permanently instead of recycling. Defaults to false (recycle).")]
        public bool? Permanent { get; set; }
    }

    /// <summary>Deletes an item, recycling it by default so the delete is reversible unless made permanent.</summary>
    public sealed class DeleteItemTool : McpTool<DeleteItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_delete_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Delete a Sitecore item. By default it is recycled (recoverable); pass permanent=true to " +
            "destroy it and its descendants irreversibly.";

        /// <inheritdoc />
        protected override McpToolResult Execute(DeleteItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var permanent = args.Permanent.GetValueOrDefault(false);

            var summary = new JObject
            {
                ["id"] = item.ID.ToString(),
                ["path"] = item.Paths.FullPath,
                ["name"] = item.Name,
                ["permanent"] = permanent
            };

            if (permanent)
            {
                item.Delete();
                summary["deleted"] = true;
            }
            else
            {
                item.Recycle();
                summary["recycled"] = true;
            }

            return McpToolResult.Structured(summary);
        }
    }
}
