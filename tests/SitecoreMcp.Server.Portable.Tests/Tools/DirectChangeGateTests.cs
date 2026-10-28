using System;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Confirmation;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools
{
    public class DirectChangeGateTests
    {
        private const string Tool = "sitecore_delete_item";
        private const string Editor = "sitecore\\jane";
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        private sealed class DeleteArgs
        {
            public string Path { get; set; }
            public bool? Permanent { get; set; }
            public string ConfirmationToken { get; set; }
        }

        private static DirectChangePreview Preview(string state = "{1}|/sitecore/content/Home/About|rev-1") =>
            new DirectChangePreview(
                "Delete '/sitecore/content/Home/About' and its 3 descendants (moved to the recycle bin).",
                new JObject { ["path"] = "/sitecore/content/Home/About", ["descendants"] = 3 },
                state);

        private sealed class Change
        {
            public int Runs;
            public McpToolResult Run()
            {
                Runs++;
                return McpToolResult.Text("deleted");
            }
        }

        private static McpToolResult Call(ConfirmationService confirmations, DeleteArgs args, Change change, DirectChangePreview preview = null) =>
            DirectChangeGate.Run(confirmations, Tool, Editor, args, args.ConfirmationToken, preview ?? Preview(), change.Run);

        private static string TokenFrom(McpToolResult preview) =>
            (string)preview.StructuredContent["confirmation"]["token"];

        [Fact]
        public void Without_a_token_it_returns_a_preview_and_changes_nothing()
        {
            var change = new Change();

            var result = Call(new ConfirmationService(clock: () => Now), new DeleteArgs { Path = "/sitecore/content/Home/About" }, change);

            Assert.Equal(0, change.Runs);
            Assert.False(result.IsError);
            Assert.Equal("confirmationRequired", (string)result.StructuredContent["status"]);
            Assert.Equal(Tool, (string)result.StructuredContent["tool"]);
            Assert.Equal(3, (int)result.StructuredContent["preview"]["descendants"]);
            Assert.False(string.IsNullOrEmpty(TokenFrom(result)));
            Assert.Equal(Now.AddMinutes(5), (DateTimeOffset)result.StructuredContent["confirmation"]["expiresAt"]);
        }

        [Fact]
        public void The_preview_reads_as_a_human_summary_and_keeps_the_token_out_of_the_text()
        {
            var result = Call(new ConfirmationService(clock: () => Now), new DeleteArgs { Path = "/sitecore/content/Home/About" }, new Change());

            var text = string.Concat(result.Content.ConvertAll(c => c.Text));
            Assert.Contains("Delete '/sitecore/content/Home/About' and its 3 descendants", text);
            Assert.Contains("Nothing has been changed", text);
            // The App withholds the token from the model, which sees the text; the token lives only in structured content.
            Assert.DoesNotContain(TokenFrom(result), text);
            Assert.Equal("Delete '/sitecore/content/Home/About' and its 3 descendants (moved to the recycle bin).",
                (string)result.StructuredContent["summary"]);
        }

        [Fact]
        public void With_the_previews_token_and_the_same_arguments_the_change_runs()
        {
            var confirmations = new ConfirmationService(clock: () => Now);
            var change = new Change();
            var preview = Call(confirmations, new DeleteArgs { Path = "/sitecore/content/Home/About" }, change);

            var result = Call(confirmations, new DeleteArgs { Path = "/sitecore/content/Home/About", ConfirmationToken = TokenFrom(preview) }, change);

            Assert.Equal(1, change.Runs);
            Assert.Equal("deleted", result.Content[0].Text);
        }

        [Fact]
        public void A_token_used_with_different_arguments_is_rejected_and_nothing_runs()
        {
            var confirmations = new ConfirmationService(clock: () => Now);
            var change = new Change();
            var preview = Call(confirmations, new DeleteArgs { Path = "/sitecore/content/Home/About" }, change);

            var result = Call(confirmations, new DeleteArgs { Path = "/sitecore/content/Home/About", Permanent = true, ConfirmationToken = TokenFrom(preview) }, change);

            Assert.Equal(0, change.Runs);
            Assert.True(result.IsError);
            Assert.Equal("confirmationRejected", (string)result.StructuredContent["status"]);
            Assert.Equal("mismatch", (string)result.StructuredContent["reason"]);
        }

        [Fact]
        public void A_token_is_rejected_when_the_target_changed_after_the_preview()
        {
            var confirmations = new ConfirmationService(clock: () => Now);
            var change = new Change();
            var preview = Call(confirmations, new DeleteArgs { Path = "/a" }, change, Preview(state: "rev-1"));

            var result = Call(confirmations, new DeleteArgs { Path = "/a", ConfirmationToken = TokenFrom(preview) }, change, Preview(state: "rev-2"));

            Assert.Equal(0, change.Runs);
            Assert.True(result.IsError);
        }

        [Fact]
        public void A_token_confirms_only_once()
        {
            var confirmations = new ConfirmationService(clock: () => Now);
            var change = new Change();
            var token = TokenFrom(Call(confirmations, new DeleteArgs { Path = "/a" }, change));

            Call(confirmations, new DeleteArgs { Path = "/a", ConfirmationToken = token }, change);
            var replay = Call(confirmations, new DeleteArgs { Path = "/a", ConfirmationToken = token }, change);

            Assert.Equal(1, change.Runs);
            Assert.True(replay.IsError);
            Assert.Equal("alreadyUsed", (string)replay.StructuredContent["reason"]);
        }

        [Fact]
        public void An_expired_token_is_rejected()
        {
            var now = Now;
            var confirmations = new ConfirmationService(clock: () => now);
            var change = new Change();
            var token = TokenFrom(Call(confirmations, new DeleteArgs { Path = "/a" }, change));

            now = Now.AddHours(1);
            var result = Call(confirmations, new DeleteArgs { Path = "/a", ConfirmationToken = token }, change);

            Assert.Equal(0, change.Runs);
            Assert.Equal("expired", (string)result.StructuredContent["reason"]);
        }

        [Fact]
        public void A_made_up_token_is_rejected()
        {
            var change = new Change();

            var result = Call(new ConfirmationService(clock: () => Now), new DeleteArgs { Path = "/a", ConfirmationToken = "yes-please" }, change);

            Assert.Equal(0, change.Runs);
            Assert.True(result.IsError);
        }

        [Fact]
        public void Without_a_confirmation_service_direct_changes_fail_closed()
        {
            var change = new Change();

            var result = DirectChangeGate.Run(null, Tool, Editor, new DeleteArgs { Path = "/a" }, null, Preview(), change.Run);

            Assert.Equal(0, change.Runs);
            Assert.True(result.IsError);
        }
    
        // Per-client confirmation: a fixed-user client (an IDE agent with its own permission prompts)
        // runs direct changes on the first call; an editor-token client (CorePilot) always confirms.

        public static TheoryData<string> DirectChangeTools => new TheoryData<string>
        {
            "sitecore_delete_item", "sitecore_move_item", "sitecore_rename_item", "sitecore_publish_item",
            "sitecore_invoke_workflow", "sitecore_update_item", "sitecore_reset_item_fields"
        };

        private static McpClient FixedUserClient(bool? confirmDirectChanges) =>
            new McpClientFactory(name => "key").Create(new McpClientDefinition
            {
                Id = "sitecore-blickle", KeyEnvVar = "KEY", User = "sitecore\\blickle", AllowWrites = true,
                ConfirmDirectChanges = confirmDirectChanges
            }).Client;

        private static McpToolResult CallAs(McpClient client, string tool, DeleteArgs args, Change change,
            Action<DirectChangeOutcome> outcome = null) =>
            DirectChangeGate.Run(client.ConfirmDirectChanges, new ConfirmationService(clock: () => Now), tool, Editor, args,
                args.ConfirmationToken, Preview(), change.Run, outcome);

        [Theory]
        [MemberData(nameof(DirectChangeTools))]
        public void A_fixed_user_clients_direct_change_runs_on_the_first_call(string tool)
        {
            var change = new Change();

            var result = CallAs(FixedUserClient(null), tool, new DeleteArgs { Path = "/sitecore/content/Home/About" }, change);

            Assert.Equal(1, change.Runs);
            Assert.False(result.IsError);
            Assert.NotEqual("confirmationRequired", (string)result.StructuredContent?["status"]);
        }

        [Theory]
        [MemberData(nameof(DirectChangeTools))]
        public void A_fixed_user_client_that_opted_in_gets_a_preview_first(string tool)
        {
            var change = new Change();

            var result = CallAs(FixedUserClient(true), tool, new DeleteArgs { Path = "/sitecore/content/Home/About" }, change);

            Assert.Equal(0, change.Runs);
            Assert.Equal("confirmationRequired", (string)result.StructuredContent["status"]);
        }

        [Fact]
        public void An_editor_token_client_gets_a_preview_first()
        {
            var change = new Change();
            var corepilot = new McpClientFactory(name => "key").Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY", RequiresEditorToken = true, AllowWrites = true
            }).Client;

            var result = CallAs(corepilot, Tool, new DeleteArgs { Path = "/a" }, change);

            Assert.Equal(0, change.Runs);
            Assert.Equal("confirmationRequired", (string)result.StructuredContent["status"]);
        }

        [Fact]
        public void Without_confirmation_the_result_still_says_what_changed()
        {
            var result = CallAs(FixedUserClient(false), Tool, new DeleteArgs { Path = "/sitecore/content/Home/About" }, new Change());

            Assert.Equal("Delete '/sitecore/content/Home/About' and its 3 descendants (moved to the recycle bin).",
                (string)result.StructuredContent["directChange"]["summary"]);
            Assert.Equal("skipped", (string)result.StructuredContent["directChange"]["confirmation"]);
            var text = string.Concat(result.Content.ConvertAll(c => c.Text));
            Assert.Contains("Delete '/sitecore/content/Home/About' and its 3 descendants", text);
            Assert.Contains("deleted", text);
        }

        [Fact]
        public void Without_confirmation_a_passed_token_is_ignored()
        {
            var change = new Change();

            var result = CallAs(FixedUserClient(false), Tool, new DeleteArgs { Path = "/a", ConfirmationToken = "made-up" }, change);

            Assert.Equal(1, change.Runs);
            Assert.False(result.IsError);
        }

        [Fact]
        public void Without_confirmation_no_confirmation_service_is_needed()
        {
            var change = new Change();

            var result = DirectChangeGate.Run(false, null, Tool, Editor, new DeleteArgs { Path = "/a" }, null, Preview(), change.Run);

            Assert.Equal(1, change.Runs);
            Assert.False(result.IsError);
        }

        [Fact]
        public void A_failed_change_without_confirmation_is_reported_as_it_failed()
        {
            var result = DirectChangeGate.Run(false, null, Tool, Editor, new DeleteArgs { Path = "/a" }, null, Preview(),
                () => McpToolResult.Failure("Not permitted."));

            Assert.True(result.IsError);
            Assert.Equal("Not permitted.", result.Content[0].Text);
        }

        [Fact]
        public void The_gate_reports_how_each_call_was_confirmed()
        {
            var outcomes = new System.Collections.Generic.List<DirectChangeOutcome>();
            var confirmations = new ConfirmationService(clock: () => Now);
            McpToolResult Run(bool required, string token) => DirectChangeGate.Run(required, confirmations, Tool, Editor,
                new DeleteArgs { Path = "/a", ConfirmationToken = token }, token, Preview(), new Change().Run, outcomes.Add);

            Run(false, null);
            var token = TokenFrom(Run(true, null));
            Run(true, token);
            Run(true, token);

            Assert.Equal(new[] { DirectChangeOutcome.Skipped, DirectChangeOutcome.Previewed, DirectChangeOutcome.Confirmed, DirectChangeOutcome.Rejected },
                outcomes);
        }
}
}
