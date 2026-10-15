using Newtonsoft.Json.Linq;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="DeleteItemTool"/>.</summary>
    public sealed class DeleteItemArgs : DirectChangeArgs
    {
        /// <summary>Whether to destroy the item permanently rather than recycling it.</summary>
        [McpParam(Description = "Delete permanently instead of recycling. Defaults to false (recycle).")]
        public bool? Permanent { get; set; }
    }

    /// <summary>
    /// Deletes an item, recycling it by default so the delete is reversible unless made permanent.
    /// A direct change: the first call returns a preview and a confirmation token, and the delete runs
    /// only when the call is repeated with that token.
    /// </summary>
    public sealed class DeleteItemTool : McpTool<DeleteItemArgs>
    {
        private const int DescendantCountLimit = 1000;

        /// <inheritdoc />
        public override string Name => "sitecore_delete_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Delete a Sitecore item. By default it is recycled (recoverable); pass permanent=true to " +
            "destroy it and its descendants irreversibly. This is a direct change: called without a " +
            "confirmationToken it only returns a preview (what would be deleted, how many descendants " +
            "and referrers) and changes nothing; the delete runs once the editor has confirmed.";

        /// <inheritdoc />
        protected override McpToolResult Execute(DeleteItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var permanent = args.Permanent.GetValueOrDefault(false);

            if (!item.Access.CanDelete())
            {
                return McpToolResult.Failure($"Not permitted to delete '{item.Paths.FullPath}'. Nothing was changed.");
            }

            RelocationGuard.EnsureInsideContentRoots(context, item, "delete");

            return context.ConfirmDirectChange(Name, args, args.ConfirmationToken, Preview(item, permanent), () =>
            {
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
            });
        }

        private static DirectChangePreview Preview(Item item, bool permanent)
        {
            var descendants = CountDescendants(item, DescendantCountLimit, out var capped);
            var referrers = (Sitecore.Globals.LinkDatabase.GetReferrers(item) ?? new Sitecore.Links.ItemLink[0]).Length;
            var path = item.Paths.FullPath;

            var scope = descendants == 0
                ? $"'{path}'"
                : $"'{path}' and its {(capped ? "more than " : string.Empty)}{descendants} descendant{(descendants == 1 ? string.Empty : "s")}";
            var how = permanent ? "permanently (cannot be restored)" : "to the recycle bin (restorable)";
            var links = referrers == 0
                ? string.Empty
                : referrers == 1
                    ? " 1 link from another item points to it and would break."
                    : $" {referrers} links from other items point to it and would break.";
            var summary = $"Delete {scope} {how}.{links}";

            var details = new JObject
            {
                ["id"] = item.ID.ToString(),
                ["path"] = path,
                ["name"] = item.Name,
                ["database"] = item.Database.Name,
                ["permanent"] = permanent,
                ["descendants"] = descendants,
                ["descendantsCapped"] = capped,
                ["referrers"] = referrers
            };

            return new DirectChangePreview(summary, details, ItemFingerprint.Of(item));
        }

        private static int CountDescendants(Item item, int limit, out bool capped)
        {
            var count = 0;
            var pending = new System.Collections.Generic.Stack<Item>();
            pending.Push(item);
            while (pending.Count > 0)
            {
                foreach (Item child in pending.Pop().GetChildren())
                {
                    if (++count >= limit)
                    {
                        capped = true;
                        return count;
                    }

                    pending.Push(child);
                }
            }

            capped = false;
            return count;
        }
    }
}
