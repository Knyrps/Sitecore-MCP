using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Workflow
{
    /// <summary>Arguments for <see cref="GetWorkflowHistoryTool"/>.</summary>
    public sealed class GetWorkflowHistoryArgs : ItemQueryArgs
    {
    }

    /// <summary>
    /// Reports an item's workflow situation in one call: which workflow it is in, its current state,
    /// the commands available right now, and the full history of who moved it when.
    /// </summary>
    public sealed class GetWorkflowHistoryTool : McpTool<GetWorkflowHistoryArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_workflow_history";

        /// <inheritdoc />
        public override string Description =>
            "Report a Sitecore item's workflow: which workflow it is in, its current state, the " +
            "commands available in that state (what sitecore_invoke_workflow could execute), and the " +
            "history of transitions (who, when, old and new state, comments). An item not in a " +
            "workflow reports that plainly.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetWorkflowHistoryArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var workflow = item.State.GetWorkflow();
            if (workflow == null)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["workflow"] = null,
                    ["hint"] = "This item is not in a workflow (its __Workflow field is empty)."
                });
            }

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["workflow"] = workflow.WorkflowID,
                ["state"] = WorkflowDescriber.State(workflow.GetState(item)),
                ["availableCommands"] = WorkflowDescriber.Commands(workflow.GetCommands(item)),
                ["history"] = WorkflowDescriber.History(workflow, workflow.GetHistory(item))
            });
        }
    }
}
