using Sitecore.Data;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="CopyItemTool"/>.</summary>
    public sealed class CopyItemArgs : ItemQueryArgs
    {
        /// <summary>The destination parent's path or ID.</summary>
        [McpParam(Description = "Destination parent path or ID.", Required = true)]
        public string Destination { get; set; }

        /// <summary>The name of the copy; defaults to the source item's name.</summary>
        [McpParam(Description = "Name for the copy. Defaults to the source name.")]
        public string Name { get; set; }

        /// <summary>Whether to copy the whole subtree; defaults to true.</summary>
        [McpParam(Description = "Copy descendants too. Defaults to true.")]
        public bool? Deep { get; set; }
    }

    /// <summary>Copies an item (optionally its subtree) under a new parent.</summary>
    public sealed class CopyItemTool : McpTool<CopyItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_copy_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Copy a Sitecore item to a new parent, by default including its descendants. Fails if the " +
            "destination already has a child with the target name.";

        /// <inheritdoc />
        protected override McpToolResult Execute(CopyItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var destination = ItemResolver.Resolve(context, args.Destination, args.Database, args.Language);
            var name = string.IsNullOrEmpty(args.Name) ? item.Name : args.Name;
            var deep = args.Deep.GetValueOrDefault(true);

            if (destination.Children[name] != null)
            {
                return McpToolResult.Failure($"'{destination.Paths.FullPath}' already has a child named '{name}'.");
            }

            try
            {
                var copy = item.CopyTo(destination, name, ID.NewID, deep);
                return McpToolResult.Structured(new ItemProjector(context).ProjectSummary(copy));
            }
            catch (System.Exception ex)
            {
                // A deep copy can fail partway through a subtree; say what the destination may now hold.
                var partial = destination.Children[name];
                var note = partial != null
                    ? $" A partial copy may exist at '{partial.Paths.FullPath}'."
                    : string.Empty;
                return McpToolResult.Failure($"Copy failed: {ex.Message}.{note}");
            }
        }
    }
}
