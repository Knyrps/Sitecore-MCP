using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;
using Sitecore.Workflows;

namespace SitecoreMcp.Server.Tools.Workflow
{
    /// <summary>The workflow a newly created version ended up in, after <see cref="ItemWorkflow.Ensure"/>.</summary>
    public sealed class WorkflowOutcome
    {
        internal WorkflowOutcome(Item version, WorkflowStartDecision decision, bool started, WorkflowState state)
        {
            Version = version;
            Decision = decision;
            Started = started;
            State = state;
        }

        /// <summary>The version, read fresh after any start.</summary>
        public Item Version { get; }

        /// <summary>What the policy decided, and why.</summary>
        public WorkflowStartDecision Decision { get; }

        /// <summary>Whether <c>IWorkflow.Start</c> ran here (false when Sitecore had already started it, or no workflow applies).</summary>
        public bool Started { get; }

        /// <summary>The workflow state the version is in now; null when it is in none.</summary>
        public WorkflowState State { get; }

        /// <summary>
        /// The JSON tools report: the effective workflow and the state the version ended in, whether it was started here,
        /// and the reason code and sentence from <see cref="WorkflowStartPolicy"/>.
        /// </summary>
        public JObject ToJson()
        {
            var workflow = Decision.Workflow;
            return new JObject
            {
                ["id"] = workflow?.Id,
                ["name"] = workflow?.Name,
                ["state"] = WorkflowDescriber.State(State),
                ["started"] = Started,
                ["reason"] = Decision.Reason,
                ["note"] = Decision.Explanation
            };
        }
    }

    /// <summary>
    /// Leaves items and versions created through the API in the workflow state the Content Editor would: Sitecore starts
    /// a workflow on a new version (from __Default workflow) only while its WorkflowContext is enabled, which follows the
    /// context site's enableWorkflow. The Content Editor runs in the shell site (enabled); Sitecore-MCP's
    /// /sitecore/api/... endpoint resolves to a content site where it is off, so nothing started the workflow.
    /// <see cref="Run{T}"/> runs the create with the WorkflowContext enabled - Sitecore's own mechanism - and
    /// <see cref="Ensure"/> checks afterwards (<see cref="WorkflowStartPolicy"/>) and starts the workflow where needed.
    /// </summary>
    public static class ItemWorkflow
    {
        private const string InitialStateField = "Initial state";

        /// <summary>Runs a Sitecore create (add item, add version, media upload) with the WorkflowContext enabled.</summary>
        public static T Run<T>(Func<T> create)
        {
            if (create == null) throw new ArgumentNullException(nameof(create));
            using (Scope())
            {
                return create();
            }
        }

        /// <summary>
        /// Enables the WorkflowContext until disposed, as the shell site does for the Content Editor. Keep the scope to
        /// the create itself: while it is enabled, workflow-state security also applies to write access checks.
        /// </summary>
        public static IDisposable Scope() => new WorkflowContextStateSwitcher(WorkflowContextState.Enabled);

        /// <summary>
        /// Puts one newly created version in its workflow's initial state when <see cref="WorkflowStartPolicy"/> says
        /// so, and reports where it ended. Throws a tool error, naming the version, when Sitecore fails to start the
        /// workflow or the state does not stick: the version then exists without a state, which Sitecore treats as
        /// publishable, and the caller must say so rather than report success.
        /// </summary>
        public static WorkflowOutcome Ensure(Item version, ItemCreationKind kind)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));

            var decision = WorkflowStartPolicy.Decide(kind, Facts(version), id => Definition(version.Database, id));
            if (!decision.Start)
            {
                var current = Reload(version);
                return new WorkflowOutcome(current, decision, false, StateOf(current, decision.Workflow));
            }

            var workflow = version.Database.WorkflowProvider.GetWorkflow(decision.Workflow.Id);
            try
            {
                workflow.Start(version);
            }
            catch (Exception ex) when (!(ex is McpToolException))
            {
                throw new McpToolException(
                    $"'{version.Paths.FullPath}' ({version.Language.Name}, version {version.Version.Number}) was created, but Sitecore did not " +
                    $"start workflow '{decision.Workflow.Name}' on it: {ex.Message} It has no workflow state, which Sitecore treats as publishable.");
            }

            var started = Reload(version);
            var state = OwnValue(started, FieldIDs.WorkflowState);
            if (!WorkflowIds.Same(state, decision.Workflow.InitialStateId))
            {
                throw new McpToolException(
                    $"'{version.Paths.FullPath}' ({version.Language.Name}, version {version.Version.Number}) was created, but after starting workflow " +
                    $"'{decision.Workflow.Name}' it is in state '{state}' instead of the initial state {decision.Workflow.InitialStateId}.");
            }

            return new WorkflowOutcome(started, decision, true, StateOf(started, decision.Workflow));
        }

        /// <summary>
        /// <see cref="Ensure"/> for every version, in every language, of <paramref name="root"/> and (when
        /// <paramref name="deep"/>) its descendants - what the Content Editor's copy does (WorkflowContext.CopyItem resets
        /// the workflow of all versions of the copy and its subtree).
        /// </summary>
        public static IReadOnlyList<WorkflowOutcome> EnsureTree(Item root, ItemCreationKind kind, bool deep)
        {
            var outcomes = new List<WorkflowOutcome>();
            EnsureTree(root, kind, deep, outcomes);
            return outcomes;
        }

        /// <summary>
        /// The new item's statistics and owner as tools report them: who created it and when, who last updated it, and its
        /// owner. Fields hidden from the call or not readable by its user are left out.
        /// </summary>
        public static JObject Statistics(Item version, McpCallContext context)
        {
            var result = new JObject();
            Add(result, "created", version, FieldIDs.Created, context);
            Add(result, "createdBy", version, FieldIDs.CreatedBy, context);
            Add(result, "updated", version, FieldIDs.Updated, context);
            Add(result, "updatedBy", version, FieldIDs.UpdatedBy, context);
            Add(result, "owner", version, FieldIDs.Owner, context);
            return result;
        }

        /// <summary>
        /// Summarises the outcomes of a copy: how many versions are in a workflow and how many were started here, plus
        /// the root version's own outcome.
        /// </summary>
        public static JObject Summary(IReadOnlyList<WorkflowOutcome> outcomes, Item root)
        {
            var own = outcomes.FirstOrDefault(o =>
                o.Version.ID == root.ID && o.Version.Language == root.Language && o.Version.Version == root.Version)
                ?? outcomes.FirstOrDefault(o => o.Version.ID == root.ID);
            var summary = own?.ToJson() ?? new JObject();
            summary["versions"] = outcomes.Count;
            summary["inWorkflow"] = outcomes.Count(o => o.State != null);
            summary["startedVersions"] = outcomes.Count(o => o.Started);
            return summary;
        }

        private static void EnsureTree(Item item, ItemCreationKind kind, bool deep, List<WorkflowOutcome> outcomes)
        {
            foreach (var version in item.Versions.GetVersions(true))
            {
                outcomes.Add(Ensure(version, kind));
            }

            if (!deep)
            {
                return;
            }

            foreach (Item child in item.GetChildren())
            {
                EnsureTree(child, kind, true, outcomes);
            }
        }

        private static WorkflowFacts Facts(Item item)
        {
            var defaultWorkflow = item.Fields[FieldIDs.DefaultWorkflow];
            return new WorkflowFacts
            {
                // A database without a workflow provider (by default every one but master) never runs workflows. 10.3's
                // Settings.Workflows.Enabled is not consulted: it is only false for a Commerce Server standalone
                // edition, and 10.4 deprecates it (the build must stay clean on both).
                WorkflowsAvailable = item.Database.WorkflowProvider != null,
                // Sitecore's WorkflowEngine skips these two (IsStandardValuesHolder / IsMasterItem).
                IsTemplateDefinition = StandardValuesManager.IsStandardValuesHolder(item) ||
                                       item.Paths.LongID.IndexOf(ItemIDs.BranchesRoot.ToString(), StringComparison.OrdinalIgnoreCase) >= 0,
                ItemWorkflowId = OwnValue(item, FieldIDs.Workflow),
                // As WorkflowEngine reads it: the item's value (standard values included), else the template item's own.
                DefaultWorkflowId = defaultWorkflow?.InheritedValue,
                StateId = OwnValue(item, FieldIDs.WorkflowState)
            };
        }

        /// <summary>A workflow field as Sitecore's DataManager.GetWorkflowInfo reads it (standard values only when clones inherit).</summary>
        private static string OwnValue(Item item, ID fieldId)
        {
            var field = item.Fields[fieldId];
            if (field == null)
            {
                return string.Empty;
            }

            var inherit = Sitecore.Configuration.Settings.ItemCloning.InheritWorkflowData;
            return field.GetValue(inherit, inherit) ?? string.Empty;
        }

        private static WorkflowDefinition Definition(Database database, string workflowId)
        {
            var provider = database.WorkflowProvider;
            if (provider == null || !ID.IsID(workflowId))
            {
                return null;
            }

            var workflow = provider.GetWorkflow(workflowId);
            if (workflow == null)
            {
                return null;
            }

            var definition = database.GetItem(ID.Parse(workflowId));
            var finals = (workflow.GetStates() ?? new WorkflowState[0]).Where(s => s.FinalState).Select(s => s.StateID);
            var name = workflow.Appearance?.DisplayName;
            return new WorkflowDefinition(
                workflow.WorkflowID,
                string.IsNullOrEmpty(name) ? definition?.DisplayName : name,
                definition?[InitialStateField],
                finals);
        }

        private static WorkflowState StateOf(Item version, WorkflowDefinition workflow)
        {
            var stateId = OwnValue(version, FieldIDs.WorkflowState);
            if (workflow == null || string.IsNullOrEmpty(stateId))
            {
                return null;
            }

            return version.Database.WorkflowProvider?.GetWorkflow(workflow.Id)?.GetState(stateId);
        }

        private static Item Reload(Item version) =>
            version.Database.GetItem(version.ID, version.Language, version.Version) ?? version;

        private static void Add(JObject result, string key, Item version, ID fieldId, McpCallContext context)
        {
            var field = version.Fields[fieldId];
            if (field == null || context.IsHidden(field) || !AuthorizationManager.IsAllowed(field, AccessRight.FieldRead, context.User))
            {
                return;
            }

            result[key] = field.Value;
        }
    }
}
