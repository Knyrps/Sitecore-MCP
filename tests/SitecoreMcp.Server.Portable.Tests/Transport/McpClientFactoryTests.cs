using System.Collections.Generic;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Transport
{
    public class McpClientFactoryTests
    {
        private static readonly IDictionary<string, string> Environment = new Dictionary<string, string>
        {
            ["KEY_ALICE"] = "alice-key",
            ["KEY_COREPILOT"] = "corepilot-key"
        };

        private static McpClientFactory Factory() =>
            new McpClientFactory(name => Environment.TryGetValue(name, out var value) ? value : null);

        [Fact]
        public void A_fixed_user_client_is_created_as_today()
        {
            var result = Factory().Create(new McpClientDefinition
            {
                Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice", AllowWrites = true, Databases = "master|web"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("sitecore\\alice", result.Client.UserName);
            Assert.False(result.Client.RequiresEditorToken);
            Assert.True(result.Client.AllowWrites);
            Assert.True(result.Client.CanUseDatabase("web"));
        }

        [Fact]
        public void A_client_without_databases_defaults_to_master()
        {
            var client = Factory().Create(new McpClientDefinition { Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice" }).Client;

            Assert.True(client.CanUseDatabase("master"));
            Assert.False(client.CanUseDatabase("web"));
        }

        [Fact]
        public void An_editor_token_client_has_no_fixed_user_and_takes_its_id_as_audience()
        {
            var result = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true, AllowWrites = true
            });

            Assert.True(result.Succeeded);
            Assert.True(result.Client.RequiresEditorToken);
            Assert.Null(result.Client.UserName);
            Assert.Equal("corepilot", result.Client.EditorTokenAudience);
        }

        [Fact]
        public void An_editor_token_client_may_name_its_audience()
        {
            var client = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true, Audience = "corepilot-prod"
            }).Client;

            Assert.Equal("corepilot-prod", client.EditorTokenAudience);
        }

        [Fact]
        public void An_editor_token_client_cannot_be_configured_with_a_fixed_user()
        {
            var result = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true, User = "sitecore\\admin"
            });

            Assert.False(result.Succeeded);
            Assert.Null(result.Client);
            Assert.Contains("fixed user", result.Error);
        }

        [Fact]
        public void An_editor_token_client_needs_an_id_or_audience()
        {
            var result = Factory().Create(new McpClientDefinition { KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true });

            Assert.False(result.Succeeded);
        }

        [Fact]
        public void A_client_with_neither_user_nor_editor_token_is_refused()
        {
            Assert.False(Factory().Create(new McpClientDefinition { Id = "x", KeyEnvVar = "KEY_ALICE" }).Succeeded);
        }

        [Fact]
        public void A_client_whose_key_variable_is_unset_is_disabled()
        {
            var result = Factory().Create(new McpClientDefinition { Id = "bob", KeyEnvVar = "KEY_BOB", User = "sitecore\\bob" });

            Assert.False(result.Succeeded);
            Assert.Contains("KEY_BOB", result.Error);
        }
    }
}
