using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tests.Support;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Protocol
{
    /// <summary>
    /// The instructions sent at initialize match the calling client: a client that confirms direct
    /// changes is told to show the preview and stop; one that does not is told the change runs at once.
    /// </summary>
    public class ServerInstructionsTests
    {
        private const string StopAndAsk = "Tell the user what the preview says and stop";

        [Fact]
        public void A_confirming_client_is_told_to_stop_at_the_preview()
        {
            var text = McpServerInstructions.For(confirmDirectChanges: true);

            Assert.Contains(StopAndAsk, text);
            Assert.Equal(McpServerInstructions.Text, text);
        }

        [Fact]
        public void A_client_without_confirmation_is_not_told_to_stop_and_ask()
        {
            var text = McpServerInstructions.For(confirmDirectChanges: false);

            Assert.DoesNotContain(StopAndAsk, text);
            Assert.DoesNotContain("change NOTHING", text);
            Assert.Contains("take effect on the first call", text);
            // The rest of the guidance is the same for both.
            Assert.Contains("Call sitecore_get_context first", text);
            Assert.Contains("move/copy/rename also refuse up front", text);
            Assert.Contains("Prefer typed filters", text);
        }

        [Theory]
        [InlineData(true, StopAndAsk)]
        [InlineData(false, "take effect on the first call")]
        public void Initialize_returns_the_instructions_it_was_built_with(bool confirm, string expected)
        {
            var dispatcher = new McpDispatcher(new FakeToolCatalog(), "SitecoreMcp", "test", McpServerInstructions.For(confirm));

            var outcome = dispatcher.Dispatch(Envelope("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}"));

            Assert.Contains(expected, (string)JObject.Parse(outcome.Body)["result"]["instructions"]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_client_is_told_that_package_installs_always_stop_at_a_preview(bool confirm)
        {
            var text = McpServerInstructions.For(confirm);

            Assert.Contains("sitecore_install_package ALWAYS returns a preview first", text);
            Assert.Contains("sitecore_get_config", text);
            Assert.Contains("redacted", text);
        }

        [Fact]
        public void A_dispatcher_built_without_instructions_sends_the_confirming_text()
        {
            var dispatcher = new McpDispatcher(new FakeToolCatalog(), "SitecoreMcp", "test");

            var outcome = dispatcher.Dispatch(Envelope("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}"));

            Assert.Equal(McpServerInstructions.Text, (string)JObject.Parse(outcome.Body)["result"]["instructions"]);
        }

        private static McpRequestEnvelope Envelope(string json)
        {
            Assert.True(McpRequestEnvelope.TryParse(json, out var envelope, out _));
            return envelope;
        }
    }
}
