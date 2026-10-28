using System;
using System.Collections.Generic;
using System.Linq;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Confirmation;
using Xunit;

namespace SitecoreMcp.Server.Tests.Tools
{
    /// <summary>
    /// The confirmation contract the CorePilot App relies on: every tool that can make a direct change
    /// accepts an optional confirmationToken argument (the preview comes back when it is omitted).
    /// The preview/token behaviour itself is pinned in the portable DirectChangeGate tests.
    /// </summary>
    public class DirectChangeContractTests
    {
        public static IEnumerable<object[]> DirectChangeTools => new[]
        {
            "sitecore_delete_item",
            "sitecore_move_item",
            "sitecore_rename_item",
            "sitecore_publish_item",
            "sitecore_invoke_workflow",
            "sitecore_update_item",
            "sitecore_reset_item_fields"
        }.Select(name => new object[] { name });

        [Theory]
        [MemberData(nameof(DirectChangeTools))]
        public void Direct_change_tools_accept_an_optional_confirmation_token(string toolName)
        {
            var tool = ToolCatalog.All.Single(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
            var schema = JsonSchemaGenerator.Generate(tool.ArgumentType);

            Assert.NotNull(schema["properties"]?["confirmationToken"]);
            var required = schema["required"]?.Select(token => (string)token) ?? Enumerable.Empty<string>();
            Assert.DoesNotContain("confirmationToken", required);
        }
    
        [Theory]
        [MemberData(nameof(DirectChangeTools))]
        public void Direct_change_tools_describe_themselves_to_clients_without_confirmation(string toolName)
        {
            var tool = ToolCatalog.All.Single(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));

            var described = Assert.IsAssignableFrom<IDirectChangeDescription>(tool);
            Assert.False(string.IsNullOrWhiteSpace(described.DescriptionWithoutConfirmation));
            Assert.DoesNotContain("confirm", described.DescriptionWithoutConfirmation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preview", described.DescriptionWithoutConfirmation, StringComparison.OrdinalIgnoreCase);
        }
}
}
