using Sitecore.Data;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Workflow;

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

    /// <summary>
    /// Copies an item (optionally its subtree) under a new parent, refusing before anything is created
    /// when the editor may not create there or the copy would cross content roots.
    /// </summary>
    public sealed class CopyItemTool : McpTool<CopyItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_copy_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Copy a Sitecore item to a new parent, by default including its descendants. Fails if the " +
            "destination already has a child with the target name, if the destination is in another content " +
            "root, or if the editor may not create items under the destination. As in the Content Editor, " +
            "every version of the copy (and its copied descendants) that has a workflow is reset to the " +
            "workflow's initial state, so a copy of approved content is not publishable; see 'workflow' in the result.";

        /// <inheritdoc />
        protected override McpToolResult Execute(CopyItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var destination = ItemResolver.ResolveWritable(context, args.Destination, args.Database, args.Language);
            var name = string.IsNullOrEmpty(args.Name) ? item.Name : args.Name;
            var deep = args.Deep.GetValueOrDefault(true);

            if (destination.Children[name] != null)
            {
                return McpToolResult.Failure($"'{destination.Paths.FullPath}' already has a child named '{name}'.");
            }

            RelocationGuard.Ensure(context, RelocationKind.Copy, item, destination);

            Sitecore.Data.Items.Item copy;
            try
            {
                copy = item.CopyTo(destination, name, ID.NewID, deep);
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

            // A copy keeps its source's workflow state (an approved source gives a publishable copy). The Content
            // Editor's copy (WorkflowContext.CopyItem) restarts the workflow of every version of the copy and its
            // subtree; do the same.
            var outcomes = ItemWorkflow.EnsureTree(copy, ItemCreationKind.Copy, deep);
            var result = new ItemProjector(context).ProjectSummary(copy);
            result["childCount"] = copy.Children.Count;
            result["deep"] = deep;
            result["workflow"] = ItemWorkflow.Summary(outcomes, copy);
            result["statistics"] = ItemWorkflow.Statistics(copy, context);
            return McpToolResult.Structured(result);
        }
    }
}
