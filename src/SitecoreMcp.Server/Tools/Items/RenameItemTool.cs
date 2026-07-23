using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="RenameItemTool"/>.</summary>
    public sealed class RenameItemArgs : ItemQueryArgs
    {
        /// <summary>The new name for the item.</summary>
        [McpParam(Description = "New item name.", Required = true)]
        public string NewName { get; set; }
    }

    /// <summary>Renames an item, validating the name and guarding against a sibling collision.</summary>
    public sealed class RenameItemTool : McpTool<RenameItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_rename_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Rename a Sitecore item. Fails if the new name is invalid or a sibling already uses it.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RenameItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            ItemHelper.ValidateName(args.NewName);

            var parent = item.Parent;
            if (parent != null)
            {
                var sibling = parent.Children[args.NewName];
                if (sibling != null && sibling.ID != item.ID)
                {
                    return McpToolResult.Failure($"'{parent.Paths.FullPath}' already has a child named '{args.NewName}'.");
                }
            }

            ItemEditor.Edit(item, editable => editable.Name = args.NewName);
            return McpToolResult.Structured(new ItemProjector(context).ProjectSummary(item));
        }
    }
}
