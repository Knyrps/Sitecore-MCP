using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Workflows;

namespace SitecoreMcp.Server.Tools.Workflow
{
    /// <summary>Projects workflow states, commands, and history events to JSON.</summary>
    public static class WorkflowDescriber
    {
        /// <summary>Describes a workflow state: its ID, name, and whether it is final.</summary>
        public static JObject State(WorkflowState state)
        {
            if (state == null)
            {
                return null;
            }

            return new JObject
            {
                ["id"] = state.StateID,
                ["displayName"] = state.DisplayName,
                ["final"] = state.FinalState
            };
        }

        /// <summary>Describes the commands available on an item in its current state.</summary>
        public static JArray Commands(WorkflowCommand[] commands)
        {
            if (commands == null)
            {
                return new JArray();
            }

            return new JArray(commands.Select(command => (object)new JObject
            {
                ["id"] = command.CommandID,
                ["displayName"] = command.DisplayName
            }).ToArray());
        }

        /// <summary>
        /// Describes the workflow history: who moved the item between which states, when, and why.
        /// Events store raw state IDs, so each is resolved to its display name through the workflow.
        /// </summary>
        public static JArray History(IWorkflow workflow, WorkflowEvent[] events)
        {
            if (events == null)
            {
                return new JArray();
            }

            return new JArray(events.Select(entry =>
            {
                var comments = entry.CommentFields?.Keys
                    .Cast<string>()
                    .Select(key => entry.CommentFields[key])
                    .Where(value => !string.IsNullOrEmpty(value))
                    .ToList();

                return (object)new JObject
                {
                    ["date"] = entry.Date.ToUniversalTime().ToString("o"),
                    ["user"] = entry.User,
                    ["oldState"] = StateName(workflow, entry.OldState),
                    ["newState"] = StateName(workflow, entry.NewState),
                    ["comments"] = comments != null && comments.Count > 0 ? string.Join(" | ", comments) : null
                };
            }).ToArray());
        }

        private static string StateName(IWorkflow workflow, string stateId)
        {
            if (string.IsNullOrEmpty(stateId))
            {
                return null;
            }

            var name = workflow?.GetState(stateId)?.DisplayName;
            return string.IsNullOrEmpty(name) ? stateId : name;
        }
    }
}
