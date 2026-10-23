using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Tools.Workflow
{
    // Sitecore-free: compiled into the portable tests (and the CorePilot package's tests). Keep it that way.

    /// <summary>How an item version came to exist.</summary>
    public enum ItemCreationKind
    {
        /// <summary>The first version of a newly created item (item add, template, media upload).</summary>
        NewItem,

        /// <summary>A version added to an existing item (a copy of the latest version in that language).</summary>
        NewVersion,

        /// <summary>A version of an item made by copying another item (or a copied descendant).</summary>
        Copy
    }

    /// <summary>A workflow as the start decision needs it: its ID, name, initial state and final states.</summary>
    public sealed class WorkflowDefinition
    {
        /// <summary>Creates the definition.</summary>
        public WorkflowDefinition(string id, string name, string initialStateId, IEnumerable<string> finalStateIds)
        {
            Id = id;
            Name = string.IsNullOrEmpty(name) ? id : name;
            InitialStateId = initialStateId;
            FinalStateIds = (finalStateIds ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        /// <summary>The workflow definition item's ID.</summary>
        public string Id { get; }

        /// <summary>The workflow's display name.</summary>
        public string Name { get; }

        /// <summary>The workflow's initial state ID (its "Initial state" field), or null/empty when unset.</summary>
        public string InitialStateId { get; }

        /// <summary>The IDs of the workflow's final (publishable) states.</summary>
        public IReadOnlyList<string> FinalStateIds { get; }

        /// <summary>Whether the state is one of this workflow's final states.</summary>
        public bool IsFinal(string stateId) => FinalStateIds.Any(id => WorkflowIds.Same(id, stateId));
    }

    /// <summary>What is known about one item version right after it was created.</summary>
    public sealed class WorkflowFacts
    {
        /// <summary>
        /// Whether workflows run in the item's database at all: it has a workflow provider (by default only master
        /// does) and workflows are enabled. Sitecore never starts a workflow otherwise, so neither does this.
        /// </summary>
        public bool WorkflowsAvailable { get; set; }

        /// <summary>
        /// Whether the item is a __Standard Values item or lies under the branches root. Sitecore's workflow engine skips
        /// both: they define what new items get, they are not content.
        /// </summary>
        public bool IsTemplateDefinition { get; set; }

        /// <summary>The version's own __Workflow value (empty when unset).</summary>
        public string ItemWorkflowId { get; set; }

        /// <summary>The item's __Default workflow, usually from its template's standard values (empty when unset).</summary>
        public string DefaultWorkflowId { get; set; }

        /// <summary>The version's own __Workflow state value (empty when unset).</summary>
        public string StateId { get; set; }
    }

    /// <summary>The reason codes a <see cref="WorkflowStartDecision"/> carries (stable, reported to clients).</summary>
    public static class WorkflowStartReasons
    {
        /// <summary>The database has no workflow provider (or workflows are disabled).</summary>
        public const string WorkflowsUnavailable = "workflowsUnavailable";

        /// <summary>A standard values or branch item.</summary>
        public const string TemplateDefinition = "templateDefinition";

        /// <summary>Neither __Workflow nor __Default workflow is set.</summary>
        public const string NoWorkflow = "noWorkflow";

        /// <summary>The workflow named by the item cannot be found (or read).</summary>
        public const string WorkflowNotFound = "workflowNotFound";

        /// <summary>The workflow has no initial state, so it cannot be started.</summary>
        public const string NoInitialState = "noInitialState";

        /// <summary>The version is already in the workflow's initial state (Sitecore started it).</summary>
        public const string AlreadyInitialState = "alreadyInitialState";

        /// <summary>The version has a workflow but no state - Sitecore treats that as approved, i.e. publishable.</summary>
        public const string NoState = "noState";

        /// <summary>The version is in a final (publishable) state it inherited.</summary>
        public const string FinalState = "finalState";

        /// <summary>The version is in some other state it inherited (a copy keeps its source's state).</summary>
        public const string OtherState = "otherState";
    }

    /// <summary>Whether to start a workflow on a newly created version, and why.</summary>
    public sealed class WorkflowStartDecision
    {
        internal WorkflowStartDecision(bool start, WorkflowDefinition workflow, string reason, string explanation)
        {
            Start = start;
            Workflow = workflow;
            Reason = reason;
            Explanation = explanation;
        }

        /// <summary>Whether <c>IWorkflow.Start</c> must run on the version (putting it in the initial state).</summary>
        public bool Start { get; }

        /// <summary>The effective workflow (to start, or that the version is already in); null when there is none.</summary>
        public WorkflowDefinition Workflow { get; }

        /// <summary>One of <see cref="WorkflowStartReasons"/>.</summary>
        public string Reason { get; }

        /// <summary>A sentence saying what was decided and why.</summary>
        public string Explanation { get; }
    }

    /// <summary>
    /// Decides whether a newly created item version has to be started in its workflow, so it ends up as the Content
    /// Editor would leave it: in the workflow's initial state whenever the item (its own __Workflow) or its template
    /// (__Default workflow on the standard values) names a workflow. Sitecore only does this itself while its
    /// WorkflowContext is enabled (site enableWorkflow="true", i.e. the shell site); this is the explicit check after the
    /// operation. A version Sitecore already put in the initial state is left alone (starting again would clear its
    /// history), anything else with a workflow is started: no state at all (Sitecore treats it as approved, so
    /// publishable), a final state, or a state copied from another item.
    /// </summary>
    public static class WorkflowStartPolicy
    {
        /// <summary>Decides for one version. <paramref name="findWorkflow"/> returns null for an unknown workflow ID.</summary>
        public static WorkflowStartDecision Decide(ItemCreationKind kind, WorkflowFacts facts, Func<string, WorkflowDefinition> findWorkflow)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (findWorkflow == null) throw new ArgumentNullException(nameof(findWorkflow));

            if (!facts.WorkflowsAvailable)
            {
                return Leave(null, WorkflowStartReasons.WorkflowsUnavailable,
                    "No workflow runs in this database (it has no workflow provider), so none was started.");
            }

            if (facts.IsTemplateDefinition)
            {
                return Leave(null, WorkflowStartReasons.TemplateDefinition,
                    "Standard values and branch items are never put in a workflow.");
            }

            var workflowId = !string.IsNullOrWhiteSpace(facts.ItemWorkflowId) ? facts.ItemWorkflowId.Trim()
                : !string.IsNullOrWhiteSpace(facts.DefaultWorkflowId) ? facts.DefaultWorkflowId.Trim()
                : null;
            if (workflowId == null)
            {
                return Leave(null, WorkflowStartReasons.NoWorkflow, "Neither the item nor its template has a workflow.");
            }

            var workflow = findWorkflow(workflowId);
            if (workflow == null)
            {
                return Leave(null, WorkflowStartReasons.WorkflowNotFound,
                    $"The workflow {workflowId} the item names was not found (or cannot be read), so it was not started.");
            }

            if (string.IsNullOrWhiteSpace(workflow.InitialStateId))
            {
                return Leave(workflow, WorkflowStartReasons.NoInitialState,
                    $"Workflow '{workflow.Name}' has no initial state, so it cannot be started.");
            }

            var state = facts.StateId;
            if (WorkflowIds.Same(state, workflow.InitialStateId))
            {
                return Leave(workflow, WorkflowStartReasons.AlreadyInitialState,
                    $"The {Noun(kind)} is in the initial state of workflow '{workflow.Name}'.");
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                return Begin(workflow, WorkflowStartReasons.NoState,
                    $"The {Noun(kind)} had workflow '{workflow.Name}' but no workflow state, which Sitecore treats as approved (publishable); " +
                    "it was started in the initial state.");
            }

            if (workflow.IsFinal(state))
            {
                return Begin(workflow, WorkflowStartReasons.FinalState,
                    $"The {Noun(kind)} was in a final (publishable) state of workflow '{workflow.Name}'; it was started again in the initial state.");
            }

            return Begin(workflow, WorkflowStartReasons.OtherState,
                $"The {Noun(kind)} carried over workflow state {state}; it was started again in the initial state of workflow '{workflow.Name}'.");
        }

        private static string Noun(ItemCreationKind kind)
        {
            switch (kind)
            {
                case ItemCreationKind.NewVersion: return "new version";
                case ItemCreationKind.Copy: return "copy";
                default: return "new item";
            }
        }

        private static WorkflowStartDecision Leave(WorkflowDefinition workflow, string reason, string explanation) =>
            new WorkflowStartDecision(false, workflow, reason, explanation);

        private static WorkflowStartDecision Begin(WorkflowDefinition workflow, string reason, string explanation) =>
            new WorkflowStartDecision(true, workflow, reason, explanation);
    }

    /// <summary>Compares Sitecore IDs as stored in fields: case, braces and surrounding space do not matter.</summary>
    public static class WorkflowIds
    {
        /// <summary>Whether two ID strings name the same ID; two empty values are not "the same".</summary>
        public static bool Same(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            if (Guid.TryParse(a.Trim(), out var left) && Guid.TryParse(b.Trim(), out var right))
            {
                return left == right;
            }

            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
