using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tools.Confirmation;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools
{
    /// <summary>
    /// tools/list matches the calling client: a client without confirmation is not told that direct
    /// changes stop at a preview. The schemas stay the same for every client (CorePilot derives which
    /// tools are direct changes from the confirmationToken argument), only descriptions differ.
    /// </summary>
    public class DirectChangeDescriptionTests
    {
        private sealed class DeleteTool : IDirectChangeDescription
        {
            public string DescriptionWithoutConfirmation => "Delete a Sitecore item. Runs on the first call.";
        }

        private static ToolDescriptor Delete() => new ToolDescriptor
        {
            Name = "sitecore_delete_item",
            Description = "Delete a Sitecore item. This is a direct change: called without a confirmationToken it only returns a preview.",
            InputSchema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["path"] = new JObject { ["type"] = "string", ["description"] = "Item path or ID." },
                    ["confirmationToken"] = new JObject { ["type"] = "string", ["description"] = DirectChangeGate.TokenDescription }
                },
                ["required"] = new JArray("path")
            }
        };

        [Fact]
        public void A_confirming_client_sees_the_descriptions_unchanged()
        {
            var listed = DirectChangeDescriptions.ForClient(Delete(), new DeleteTool(), confirmDirectChanges: true);

            Assert.True(JToken.DeepEquals(JObject.FromObject(Delete()), JObject.FromObject(listed)));
        }

        [Fact]
        public void A_client_without_confirmation_sees_the_tool_described_as_running_at_once()
        {
            var listed = DirectChangeDescriptions.ForClient(Delete(), new DeleteTool(), confirmDirectChanges: false);

            Assert.Equal("Delete a Sitecore item. Runs on the first call.", listed.Description);
            var token = (string)listed.InputSchema["properties"]["confirmationToken"]["description"];
            Assert.DoesNotContain("preview", token);
            Assert.Contains("ignored", token);
        }

        [Fact]
        public void A_client_without_confirmation_keeps_the_same_schema_shape()
        {
            var listed = DirectChangeDescriptions.ForClient(Delete(), new DeleteTool(), confirmDirectChanges: false);

            Assert.True(JToken.DeepEquals(WithoutDescriptions(Delete().InputSchema), WithoutDescriptions(listed.InputSchema)));
            Assert.Equal("sitecore_delete_item", listed.Name);
        }

        [Fact]
        public void Listing_for_one_client_does_not_change_the_descriptor_for_the_next()
        {
            var original = Delete();

            DirectChangeDescriptions.ForClient(original, new DeleteTool(), confirmDirectChanges: false);

            Assert.True(JToken.DeepEquals(JObject.FromObject(Delete()), JObject.FromObject(original)));
        }

        [Fact]
        public void A_tool_that_is_not_a_direct_change_is_listed_unchanged()
        {
            var read = new ToolDescriptor
            {
                Name = "sitecore_get_item", Description = "Read an item.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() }
            };

            var listed = DirectChangeDescriptions.ForClient(read, new object(), confirmDirectChanges: false);

            Assert.True(JToken.DeepEquals(JObject.FromObject(read), JObject.FromObject(listed)));
        }

        private static JToken WithoutDescriptions(JObject schema)
        {
            var copy = (JObject)schema.DeepClone();
            foreach (var property in ((JObject)copy["properties"]).Properties())
            {
                ((JObject)property.Value).Remove("description");
            }

            return copy;
        }
    }
}
