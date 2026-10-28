using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="MoveItemTool"/>.</summary>
    public sealed class MoveItemArgs : DirectChangeArgs
    {
        /// <summary>The destination parent's path or ID.</summary>
        [McpParam(Description = "Destination parent path or ID.", Required = true)]
        public string Destination { get; set; }
    }

    /// <summary>
    /// Moves an item under a new parent, guarding against collisions, moves into its own subtree,
    /// missing rights and crossing content roots. A direct change: it runs only once confirmed.
    /// </summary>
    public sealed class MoveItemTool : McpTool<MoveItemArgs>, IDirectChangeDescription
    {
        /// <inheritdoc />
        public override string Name => "sitecore_move_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Move a Sitecore item to a new parent. Fails if the destination already has a child with " +
            "the same name, if the destination is the item itself or one of its descendants, if the " +
            "move would cross content roots, or if the editor may not delete the item or create under " +
            "the destination. This is a direct change: called without a confirmationToken it only " +
            "returns a preview and changes nothing; the move runs once the editor has confirmed.";

        /// <inheritdoc />
        public string DescriptionWithoutConfirmation =>
            "Move a Sitecore item to a new parent. Fails if the destination already has a child with " +
            "the same name, if the destination is the item itself or one of its descendants, if the " +
            "move would cross content roots, or if the user may not delete the item or create under " +
            "the destination. The move runs on the first call; the result's directChange.summary says " +
            "where the item went.";

        /// <inheritdoc />
        protected override McpToolResult Execute(MoveItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.ResolveWritable(context, args.Path, args.Database, args.Language);
            var destination = ItemResolver.ResolveWritable(context, args.Destination, args.Database, args.Language);

            if (destination.Paths.LongID.StartsWith(item.Paths.LongID))
            {
                return McpToolResult.Failure("Cannot move an item into itself or one of its descendants.");
            }

            if (destination.Children[item.Name] != null)
            {
                return McpToolResult.Failure($"'{destination.Paths.FullPath}' already has a child named '{item.Name}'.");
            }

            RelocationGuard.Ensure(context, RelocationKind.Move, item, destination);

            var from = item.Paths.FullPath;
            var to = destination.Paths.FullPath.TrimEnd('/') + "/" + item.Name;
            var preview = new DirectChangePreview(
                $"Move '{from}' (with its descendants) under '{destination.Paths.FullPath}', so its path becomes '{to}'. " +
                "Links stored by ID keep working; anything addressing it by path does not.",
                new JObject
                {
                    ["id"] = item.ID.ToString(),
                    ["from"] = from,
                    ["to"] = to,
                    ["fromParent"] = item.Parent?.Paths.FullPath,
                    ["toParent"] = destination.Paths.FullPath,
                    ["database"] = item.Database.Name
                },
                ItemFingerprint.Of(item) + "|" + ItemFingerprint.Of(destination));

            return context.ConfirmDirectChange(Name, args, args.ConfirmationToken, preview, () =>
            {
                item.MoveTo(destination);
                return McpToolResult.Structured(new ItemProjector(context).ProjectSummary(item));
            });
        }
    }
}
