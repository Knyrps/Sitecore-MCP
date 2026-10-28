using SitecoreMcp.Server.Tools.Confirmation;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Transport
{
    public class McpAuditLineTests
    {
        [Fact]
        public void A_direct_change_run_without_confirmation_is_marked_skipped()
        {
            var line = McpAuditLine.Format("sitecore\\blickle", "sitecore-blickle", false, "tools/call", "sitecore_delete_item", 200, 12,
                DirectChangeOutcome.Skipped);

            Assert.Equal(
                "user=sitecore\\blickle client=sitecore-blickle identity=client-user method=tools/call target=sitecore_delete_item " +
                "status=200 durationMs=12 confirmed=skipped",
                line);
        }

        [Theory]
        [InlineData(DirectChangeOutcome.Previewed, "confirmed=pending")]
        [InlineData(DirectChangeOutcome.Confirmed, "confirmed=yes")]
        [InlineData(DirectChangeOutcome.Rejected, "confirmed=rejected")]
        [InlineData(DirectChangeOutcome.Unavailable, "confirmed=unavailable")]
        public void A_confirming_client_s_direct_change_records_its_confirmation_step(DirectChangeOutcome outcome, string marker)
        {
            var line = McpAuditLine.Format("sitecore\\jane", "corepilot", true, "tools/call", "sitecore_move_item", 200, 5, outcome);

            Assert.EndsWith(" " + marker, line);
            Assert.Contains("identity=editor-token", line);
        }

        [Fact]
        public void A_call_that_made_no_direct_change_has_no_marker()
        {
            var line = McpAuditLine.Format("sitecore\\blickle", null, false, "tools/list", "tools/list", 200, 3, null);

            Assert.Equal(
                "user=sitecore\\blickle client=(unnamed) identity=client-user method=tools/list target=tools/list status=200 durationMs=3",
                line);
        }
    }
}
