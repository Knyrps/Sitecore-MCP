using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Workflow
{
    /// <summary>Arguments for <see cref="InvokeWorkflowTool"/>.</summary>
    public sealed class InvokeWorkflowArgs : ItemQueryArgs
    {
        /// <summary>The workflow command to execute, by display name or ID.</summary>
        [McpParam(Description = "The workflow command to execute, by display name (e.g. 'Submit', 'Approve') or command ID.", Required = true)]
        public string Command { get; set; }

        /// <summary>An optional comment recorded in the workflow history.</summary>
        [McpParam(Description = "Comment recorded in the workflow history. Optional.")]
        public string Comments { get; set; }
    }

    /// <summary>
    /// Executes a workflow command on an item (submit, approve, reject, ...), moving it through its
    /// workflow. Only commands available in the item's current state to the calling user can run.
    /// </summary>
    public sealed class InvokeWorkflowTool : McpTool<InvokeWorkflowArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_invoke_workflow";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Execute a workflow command on a Sitecore item (e.g. Submit, Approve, Reject), moving it " +
            "to its next workflow state. The command must be available in the item's current state to " +
            "this client's user - workflow security applies. An unavailable command is refused with " +
            "the commands that ARE available; check them first with sitecore_get_workflow_history.";

        /// <inheritdoc />
        protected override McpToolResult Execute(InvokeWorkflowArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var workflow = item.State.GetWorkflow();
            if (workflow == null)
            {
                throw new McpToolException("This item is not in a workflow, so no command can be executed.");
            }

            var before = workflow.GetState(item);

            // GetCommands(item) is already filtered to the current state and the calling user's
            // workflow security, so matching against it enforces both at once.
            var available = workflow.GetCommands(item) ?? new Sitecore.Workflows.WorkflowCommand[0];
            var command = available.FirstOrDefault(c =>
                string.Equals(c.CommandID, args.Command, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DisplayName, args.Command, StringComparison.OrdinalIgnoreCase));

            if (command == null)
            {
                var names = string.Join(", ", available.Select(c => c.DisplayName));
                throw new McpToolException(
                    $"Command '{args.Command}' is not available on this item in state " +
                    $"'{before?.DisplayName}' for this user." +
                    (names.Length > 0 ? $" Available: {names}." : " No commands are available."));
            }

            var result = workflow.Execute(command.CommandID, item, args.Comments ?? string.Empty, false, new object[0]);

            // Re-read so the reported state is what actually stuck, not what the transition intended.
            var fresh = item.Database.GetItem(item.ID, item.Language, item.Version) ?? item;
            var after = workflow.GetState(fresh);

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["command"] = command.DisplayName,
                ["succeeded"] = result?.Succeeded ?? false,
                ["message"] = string.IsNullOrEmpty(result?.Message) ? null : result.Message,
                ["oldState"] = WorkflowDescriber.State(before),
                ["newState"] = WorkflowDescriber.State(after)
            });
        }
    }
}
