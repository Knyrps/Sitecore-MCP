using SitecoreMcp.Server.Tools.Workflow;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools
{
    public class WorkflowStartPolicyTests
    {
        private const string Sample = "{A5BC37E7-ED96-4C1E-8590-A26E64DB55EA}";
        private const string Other = "{11111111-2222-3333-4444-555555555555}";
        private const string Draft = "{190B1C84-F1BE-47ED-AA41-F42193D9C8FC}";
        private const string Awaiting = "{46DA5376-10DC-4B66-B464-AFDAA29DE84F}";
        private const string Approved = "{FCA998C5-0CC3-4F91-94D8-0A4E6CAECE88}";
        private const string OtherDraft = "{99999999-8888-7777-6666-555555555555}";

        private static readonly WorkflowDefinition SampleWorkflow =
            new WorkflowDefinition(Sample, "Sample Workflow", Draft, new[] { Approved });

        private static readonly WorkflowDefinition OtherWorkflow =
            new WorkflowDefinition(Other, "Other Workflow", OtherDraft, new string[0]);

        private static WorkflowDefinition Find(string id) =>
            WorkflowIds.Same(id, Sample) ? SampleWorkflow : WorkflowIds.Same(id, Other) ? OtherWorkflow : null;

        private static WorkflowFacts Facts(string itemWorkflow = null, string defaultWorkflow = null, string state = null) =>
            new WorkflowFacts
            {
                WorkflowsAvailable = true,
                ItemWorkflowId = itemWorkflow,
                DefaultWorkflowId = defaultWorkflow,
                StateId = state,
            };

        [Fact]
        public void A_new_item_whose_template_has_a_default_workflow_and_no_state_is_started()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, Facts(defaultWorkflow: Sample), Find);

            Assert.True(decision.Start);
            Assert.Equal(Sample, decision.Workflow.Id);
            Assert.Equal(WorkflowStartReasons.NoState, decision.Reason);
        }

        [Fact]
        public void A_new_item_Sitecore_already_started_is_left_alone()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, Facts(itemWorkflow: Sample, defaultWorkflow: Sample, state: Draft), Find);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.AlreadyInitialState, decision.Reason);
            Assert.Equal(Sample, decision.Workflow.Id);
        }

        [Fact]
        public void Ids_compare_without_regard_to_case_or_braces()
        {
            var decision = WorkflowStartPolicy.Decide(
                ItemCreationKind.NewItem,
                Facts(itemWorkflow: Sample.ToLowerInvariant().Trim('{', '}'), state: Draft.ToLowerInvariant()),
                Find);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.AlreadyInitialState, decision.Reason);
        }

        [Fact]
        public void A_new_version_with_no_state_is_started_in_the_items_own_workflow()
        {
            // Sitecore's SQL provider copies every versioned field into a new version except __Workflow state, and its
            // workflow engine only restarts from __Default workflow: an item with only __Workflow is left stateless.
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewVersion, Facts(itemWorkflow: Sample), Find);

            Assert.True(decision.Start);
            Assert.Equal(Sample, decision.Workflow.Id);
            Assert.Equal(WorkflowStartReasons.NoState, decision.Reason);
        }

        [Fact]
        public void A_new_version_in_a_final_state_is_started_again()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewVersion, Facts(itemWorkflow: Sample, defaultWorkflow: Sample, state: Approved), Find);

            Assert.True(decision.Start);
            Assert.Equal(WorkflowStartReasons.FinalState, decision.Reason);
        }

        [Fact]
        public void A_copy_keeping_the_sources_state_is_started_again()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.Copy, Facts(itemWorkflow: Sample, state: Awaiting), Find);

            Assert.True(decision.Start);
            Assert.Equal(WorkflowStartReasons.OtherState, decision.Reason);
        }

        [Fact]
        public void The_items_own_workflow_wins_over_the_default_workflow()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewVersion, Facts(itemWorkflow: Other, defaultWorkflow: Sample), Find);

            Assert.True(decision.Start);
            Assert.Equal(Other, decision.Workflow.Id);
        }

        [Fact]
        public void A_state_of_another_workflow_is_not_the_initial_state()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.Copy, Facts(itemWorkflow: Other, state: Draft), Find);

            Assert.True(decision.Start);
            Assert.Equal(Other, decision.Workflow.Id);
        }

        [Fact]
        public void An_item_without_any_workflow_is_left_alone()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, Facts(), Find);

            Assert.False(decision.Start);
            Assert.Null(decision.Workflow);
            Assert.Equal(WorkflowStartReasons.NoWorkflow, decision.Reason);
        }

        [Fact]
        public void A_database_without_a_workflow_provider_never_starts_one()
        {
            var facts = Facts(defaultWorkflow: Sample);
            facts.WorkflowsAvailable = false;

            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, facts, Find);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.WorkflowsUnavailable, decision.Reason);
        }

        [Fact]
        public void Standard_values_and_branch_items_never_start_one()
        {
            var facts = Facts(defaultWorkflow: Sample);
            facts.IsTemplateDefinition = true;

            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, facts, Find);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.TemplateDefinition, decision.Reason);
        }

        [Fact]
        public void A_workflow_that_cannot_be_found_is_reported_not_started()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, Facts(defaultWorkflow: "{00000000-0000-0000-0000-00000000000A}"), Find);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.WorkflowNotFound, decision.Reason);
            Assert.Contains("{00000000-0000-0000-0000-00000000000A}", decision.Explanation);
        }

        [Fact]
        public void A_workflow_without_an_initial_state_is_reported_not_started()
        {
            var broken = new WorkflowDefinition(Sample, "Broken", null, new string[0]);

            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewItem, Facts(defaultWorkflow: Sample), _ => broken);

            Assert.False(decision.Start);
            Assert.Equal(WorkflowStartReasons.NoInitialState, decision.Reason);
            Assert.Contains("Broken", decision.Explanation);
        }

        [Fact]
        public void Every_decision_explains_itself()
        {
            var decision = WorkflowStartPolicy.Decide(ItemCreationKind.NewVersion, Facts(itemWorkflow: Sample, state: Approved), Find);

            Assert.Contains("Sample Workflow", decision.Explanation);
            Assert.Contains("final", decision.Explanation);
        }
    }
}
