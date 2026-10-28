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
    
        [Fact]
        public void An_editor_token_client_confirms_direct_changes_by_default()
        {
            var client = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true
            }).Client;

            Assert.True(client.ConfirmDirectChanges);
        }

        [Fact]
        public void An_editor_token_client_cannot_turn_confirmation_off_and_is_skipped()
        {
            var result = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true, ConfirmDirectChanges = false
            });

            Assert.False(result.Succeeded);
            Assert.Contains("confirmDirectChanges", result.Error);
            Assert.Contains("corepilot", result.Error);
        }

        [Fact]
        public void An_editor_token_client_may_state_confirmation_explicitly()
        {
            var result = Factory().Create(new McpClientDefinition
            {
                Id = "corepilot", KeyEnvVar = "KEY_COREPILOT", RequiresEditorToken = true, ConfirmDirectChanges = true
            });

            Assert.True(result.Succeeded);
            Assert.True(result.Client.ConfirmDirectChanges);
        }

        [Fact]
        public void A_fixed_user_client_runs_direct_changes_without_confirmation_by_default()
        {
            var client = Factory().Create(new McpClientDefinition { Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice" }).Client;

            Assert.False(client.ConfirmDirectChanges);
        }

        [Fact]
        public void A_fixed_user_client_can_opt_in_to_confirmation()
        {
            var client = Factory().Create(new McpClientDefinition
            {
                Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice", ConfirmDirectChanges = true
            }).Client;

            Assert.True(client.ConfirmDirectChanges);
        }

        [Fact]
        public void The_global_default_applies_to_fixed_user_clients_that_do_not_say()
        {
            var factory = new McpClientFactory(name => Environment.TryGetValue(name, out var value) ? value : null, confirmDirectChangesByDefault: true);

            Assert.True(factory.Create(new McpClientDefinition { Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice" }).Client.ConfirmDirectChanges);
            Assert.False(factory.Create(new McpClientDefinition
            {
                Id = "alice", KeyEnvVar = "KEY_ALICE", User = "sitecore\\alice", ConfirmDirectChanges = false
            }).Client.ConfirmDirectChanges);
        }

        [Fact]
        public void A_client_built_in_code_for_a_user_does_not_confirm_and_one_for_editor_tokens_does()
        {
            Assert.False(McpClient.ForUser("alice", "k", "sitecore\\alice", true, null).ConfirmDirectChanges);
            Assert.True(McpClient.ForEditorToken("corepilot", "k", "corepilot", true, null).ConfirmDirectChanges);
        }
}
}
