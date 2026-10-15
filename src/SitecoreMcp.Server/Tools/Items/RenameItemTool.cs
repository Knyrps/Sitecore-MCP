using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="RenameItemTool"/>.</summary>
    public sealed class RenameItemArgs : DirectChangeArgs
    {
        /// <summary>The new name for the item.</summary>
        [McpParam(Description = "New item name.", Required = true)]
        public string NewName { get; set; }
    }

    /// <summary>
    /// Renames an item, validating the name and guarding against a sibling collision, missing rename
    /// rights and content-root boundaries. A direct change: it runs only once confirmed.
    /// </summary>
    public sealed class RenameItemTool : McpTool<RenameItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_rename_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Rename a Sitecore item. Fails if the new name is invalid, a sibling already uses it, the " +
            "editor may not rename the item, or the item is outside (or is itself) a content root. This " +
            "is a direct change: called without a confirmationToken it only returns a preview and " +
            "changes nothing; the rename runs once the editor has confirmed.";

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

            RelocationGuard.Ensure(context, RelocationKind.Rename, item);

            var from = item.Paths.FullPath;
            var to = parent == null ? "/" + args.NewName : parent.Paths.FullPath.TrimEnd('/') + "/" + args.NewName;
            var preview = new DirectChangePreview(
                $"Rename '{from}' from '{item.Name}' to '{args.NewName}', so its path (and its descendants' paths) becomes '{to}'. " +
                "The name is shared by every version and language, and the item's URL changes.",
                new JObject
                {
                    ["id"] = item.ID.ToString(),
                    ["oldName"] = item.Name,
                    ["newName"] = args.NewName,
                    ["from"] = from,
                    ["to"] = to,
                    ["database"] = item.Database.Name
                },
                ItemFingerprint.Of(item));

            return context.ConfirmDirectChange(Name, args, args.ConfirmationToken, preview, () =>
            {
                ItemEditor.Edit(item, editable => editable.Name = args.NewName);
                return McpToolResult.Structured(new ItemProjector(context).ProjectSummary(item));
            });
        }
    }
}
