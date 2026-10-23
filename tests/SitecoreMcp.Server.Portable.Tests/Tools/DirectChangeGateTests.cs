using System;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools.Confirmation;
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
    }
}
