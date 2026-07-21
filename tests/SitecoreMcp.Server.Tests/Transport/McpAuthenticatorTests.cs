using System.Collections.Generic;
using SitecoreMcp.Server.Tests.Support;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Tests.Transport
{
    public class McpAuthenticatorTests
    {
        private static McpAuthenticator Authenticator() => new McpAuthenticator(new List<McpClient>
        {
            new McpClient("key-one", "sitecore\\one", allowWrites: false, databases: new[] { "master" }),
            new McpClient("key-two", "sitecore\\two", allowWrites: true, databases: new[] { "master", "web" })
        });

        [Fact]
        public void Resolves_a_client_from_a_bearer_token()
        {
            var request = new FakeHttpRequest().WithHeader("Authorization", "Bearer key-two");
            var client = Authenticator().Authenticate(request);

            Assert.NotNull(client);
            Assert.Equal("sitecore\\two", client.UserName);
        }

        [Fact]
        public void Resolves_a_client_from_the_x_mcp_key_header()
        {
            var request = new FakeHttpRequest().WithHeader("X-Mcp-Key", "key-one");
            Assert.Equal("sitecore\\one", Authenticator().Authenticate(request).UserName);
        }

        [Fact]
        public void Returns_null_for_a_wrong_key()
        {
            var request = new FakeHttpRequest().WithHeader("Authorization", "Bearer nope");
            Assert.Null(Authenticator().Authenticate(request));
        }

        [Fact]
        public void Returns_null_when_no_key_is_present()
        {
            Assert.Null(Authenticator().Authenticate(new FakeHttpRequest()));
        }

        [Fact]
        public void Database_allow_list_is_enforced_on_the_resolved_client()
        {
            var request = new FakeHttpRequest().WithHeader("Authorization", "Bearer key-one");
            var client = Authenticator().Authenticate(request);

            Assert.True(client.CanUseDatabase("master"));
            Assert.False(client.CanUseDatabase("web"));
        }
    }
}
