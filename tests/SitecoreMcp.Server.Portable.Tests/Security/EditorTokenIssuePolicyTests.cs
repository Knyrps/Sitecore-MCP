using SitecoreMcp.Server.Security;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class EditorTokenIssuePolicyTests
    {
        [Fact]
        public void With_one_editor_token_client_its_audience_is_the_default()
        {
            var policy = new EditorTokenIssuePolicy(new[] { "corepilot" }, "sitecore");

            Assert.True(policy.TryResolveAudience(null, out var audience, out _));
            Assert.Equal("corepilot", audience);
        }

        [Fact]
        public void A_requested_audience_must_belong_to_an_editor_token_client()
        {
            var policy = new EditorTokenIssuePolicy(new[] { "corepilot", "corepilot-preview" }, "sitecore");

            Assert.True(policy.TryResolveAudience("corepilot-preview", out var audience, out _));
            Assert.Equal("corepilot-preview", audience);
            Assert.False(policy.TryResolveAudience("alice", out _, out var error));
            Assert.Contains("alice", error);
        }

        [Fact]
        public void With_several_editor_token_clients_the_audience_must_be_named()
        {
            var policy = new EditorTokenIssuePolicy(new[] { "corepilot", "corepilot-preview" }, "sitecore");

            Assert.False(policy.TryResolveAudience(null, out _, out _));
        }

        [Fact]
        public void Without_editor_token_clients_nothing_is_issued()
        {
            var policy = new EditorTokenIssuePolicy(new string[0], "sitecore");

            Assert.False(policy.TryResolveAudience(null, out _, out _));
            Assert.False(policy.TryResolveAudience("corepilot", out _, out _));
        }

        [Theory]
        [InlineData("sitecore\\jane", true)]
        [InlineData("SITECORE\\jane", true)]
        [InlineData("extranet\\bob", false)]
        [InlineData("extranet\\Anonymous", false)]
        [InlineData("jane", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_users_of_the_allowed_domains_may_be_issued_a_token(string userName, bool allowed)
        {
            Assert.Equal(allowed, new EditorTokenIssuePolicy(new[] { "corepilot" }, "sitecore").IsAllowedUser(userName));
        }

        [Fact]
        public void Several_domains_may_be_allowed()
        {
            var policy = new EditorTokenIssuePolicy(new[] { "corepilot" }, "sitecore|authors");

            Assert.True(policy.IsAllowedUser("authors\\kim"));
        }
    }
}
