using Newtonsoft.Json.Linq;
using Sitecore;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="ProtectItemTool"/> and <see cref="UnprotectItemTool"/>.</summary>
    public sealed class ProtectItemArgs : ItemQueryArgs
    {
    }

    /// <summary>Marks an item read-only (protected), so it cannot be edited in the Content Editor.</summary>
    public sealed class ProtectItemTool : McpTool<ProtectItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_protect_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Protect a Sitecore item by marking it read-only, so editors cannot change it in the " +
            "Content Editor. Already-protected is a benign no-op.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ProtectItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            if (item[FieldIDs.ReadOnly] == "1")
            {
                return Result(item, true, "The item was already protected.");
            }

            ItemEditor.Edit(item, editable => editable[FieldIDs.ReadOnly] = "1");
            return Result(item, true, null);
        }

        internal static McpToolResult Result(Sitecore.Data.Items.Item item, bool isProtected, string note)
        {
            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["protected"] = isProtected
            };
            if (note != null)
            {
                result["note"] = note;
            }

            return McpToolResult.Structured(result);
        }
    }

    /// <summary>Clears an item's read-only protection.</summary>
    public sealed class UnprotectItemTool : McpTool<ProtectItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_unprotect_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a Sitecore item's read-only protection so it can be edited again. Not-protected is " +
            "a benign no-op.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ProtectItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            if (item[FieldIDs.ReadOnly] != "1")
            {
                return ProtectItemTool.Result(item, false, "The item was not protected.");
            }

            ItemEditor.Edit(item, editable => editable[FieldIDs.ReadOnly] = string.Empty);
            return ProtectItemTool.Result(item, false, null);
        }
    }
}
