using Sitecore.Data.Items;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>
    /// Applies <see cref="RelocationPolicy"/> to real items: gathers the editor's delete, rename and
    /// create rights and the items' paths, and throws <see cref="McpToolException"/> before anything
    /// changes when the move, copy or rename is not allowed.
    /// </summary>
    public static class RelocationGuard
    {
        /// <summary>Refuses the relocation unless the editor holds the rights and it stays inside one content root.</summary>
        public static void Ensure(McpCallContext context, RelocationKind kind, Item source, Item destination = null)
        {
            var request = new RelocationRequest
            {
                Kind = kind,
                Database = source.Database.Name,
                SourcePath = source.Paths.FullPath,
                DestinationPath = destination?.Paths.FullPath,
                CanDeleteSource = kind == RelocationKind.Move && source.Access.CanDelete(),
                CanRenameSource = kind == RelocationKind.Rename && source.Access.CanRename(),
                CanCreateInDestination = destination != null && destination.Access.CanCreate()
            };

            var check = RelocationPolicy.Check(request, context.ContentRoots);
            if (!check.Allowed)
            {
                throw new McpToolException(check.Reason + " Nothing was changed.");
            }
        }

        /// <summary>Refuses a change to an item outside every content root, or to a content root itself.</summary>
        public static void EnsureInsideContentRoots(McpCallContext context, Item item, string action)
        {
            var roots = context.ContentRoots ?? UnrestrictedContentRoots.Instance;
            var path = item.Paths.FullPath;
            var root = roots.RootOf(item.Database.Name, path);
            if (root == null)
            {
                throw new McpToolException($"Cannot {action} '{path}': it is outside the content roots CorePilot may change. Nothing was changed.");
            }

            if (string.Equals(root, path, System.StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException($"Cannot {action} '{path}': it is a content root. Nothing was changed.");
            }
        }
    }
}
