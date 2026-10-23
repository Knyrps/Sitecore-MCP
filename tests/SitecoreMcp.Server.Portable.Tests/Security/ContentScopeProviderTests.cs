using System.Collections.Generic;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class ContentScopeProviderTests
    {
        private static readonly IDictionary<string, string> Environment = new Dictionary<string, string>
        {
            ["KEY_COREPILOT"] = "corepilot-key",
            ["KEY_IDE"] = "ide-key"
        };

        private static McpClient Client(string contentRoots = null, string searchRoots = null, string hiddenFields = null) =>
            new McpClientFactory(name => Environment.TryGetValue(name, out var value) ? value : null).Create(new McpClientDefinition
            {
                Id = "corepilot",
                KeyEnvVar = "KEY_COREPILOT",
                RequiresEditorToken = true,
                ContentRoots = contentRoots,
                SearchRoots = searchRoots,
                HiddenFields = hiddenFields
            }).Client;

        private static readonly ContentScope Global = new ContentScope(
            new[] { "/sitecore/content" },
            new[] { "/sitecore/content", "/sitecore/media library" },
            new[] { "Api Secret" });

        [Fact]
        public void A_client_without_its_own_scope_gets_the_global_scope()
        {
            var scope = new ConfiguredContentScopeProvider(Global).GetScope(Client());

            Assert.True(scope.IsInContentRoot("/sitecore/content/Site B"));
            Assert.False(scope.IsInContentRoot("/sitecore/system"));
            Assert.True(scope.IsInSearchRoot("/sitecore/media library/x"));
            Assert.True(scope.IsHiddenField("Api Secret", null));
        }

        [Fact]
        public void A_clients_content_and_search_roots_replace_the_global_ones()
        {
            var scope = new ConfiguredContentScopeProvider(Global).GetScope(
                Client(contentRoots: "/sitecore/content/Site A | /sitecore/media library/Site A", searchRoots: "/sitecore/content/Site A"));

            Assert.True(scope.IsInContentRoot("/sitecore/media library/Site A/logo"));
            Assert.False(scope.IsInContentRoot("/sitecore/content/Site B"));
            Assert.False(scope.IsInSearchRoot("/sitecore/media library/x"));
        }

        [Fact]
        public void A_clients_hidden_fields_add_to_the_global_ones()
        {
            var scope = new ConfiguredContentScopeProvider(Global).GetScope(Client(hiddenFields: "Internal Notes|{52807595-0F8F-4B20-8D2A-CB71D28C6103}"));

            Assert.True(scope.IsHiddenField("Api Secret", null));
            Assert.True(scope.IsHiddenField("Internal Notes", null));
            Assert.True(scope.IsHiddenField("__Owner", "{52807595-0F8F-4B20-8D2A-CB71D28C6103}"));
        }

        [Fact]
        public void Without_any_scope_config_nothing_is_restricted()
        {
            var scope = new ConfiguredContentScopeProvider(null).GetScope(Client());

            Assert.True(scope.IsInContentRoot("/sitecore/system"));
            Assert.True(scope.IsInSearchRoot("/sitecore/system"));
            Assert.False(scope.HasHiddenFields);
        }

        [Fact]
        public void A_field_name_with_a_comma_is_one_hidden_field()
        {
            var scope = new ConfiguredContentScopeProvider(null).GetScope(Client(hiddenFields: "Notes, internal"));

            Assert.True(scope.IsHiddenField("Notes, internal", null));
            Assert.False(scope.IsHiddenField("Notes", null));
        }
    }
}
