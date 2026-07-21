using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="MoveItemTool"/>.</summary>
    public sealed class MoveItemArgs : ItemQueryArgs
    {
        /// <summary>The destination parent's path or ID.</summary>
        [McpParam(Description = "Destination parent path or ID.", Required = true)]
        public string Destination { get; set; }
    }

    /// <summary>Moves an item under a new parent, guarding against collisions and moves into its own subtree.</summary>
    public sealed class MoveItemTool : McpTool<MoveItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_move_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Move a Sitecore item to a new parent. Fails if the destination already has a child with " +
            "the same name, or if the destination is the item itself or one of its descendants.";

        /// <inheritdoc />
        protected override McpToolResult Execute(MoveItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var destination = ItemResolver.Resolve(context, args.Destination, args.Database, args.Language);

            if (destination.Paths.LongID.StartsWith(item.Paths.LongID))
            {
                return McpToolResult.Failure("Cannot move an item into itself or one of its descendants.");
            }

            if (destination.Children[item.Name] != null)
            {
                return McpToolResult.Failure($"'{destination.Paths.FullPath}' already has a child named '{item.Name}'.");
            }

            item.MoveTo(destination);
            return McpToolResult.Structured(new ItemProjector(context).ProjectSummary(item));
        }
    }
}
