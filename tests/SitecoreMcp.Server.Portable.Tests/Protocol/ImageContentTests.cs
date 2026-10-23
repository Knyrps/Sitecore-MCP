using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Protocol
{
    public class ImageContentTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static JObject Serialized(McpToolResult result) => JObject.Parse(McpJson.Serialize(result));

        [Fact]
        public void An_image_result_carries_an_mcp_image_content_block()
        {
            var result = McpToolResult.Image(Png, "image/png", new JObject { ["width"] = 1 });

            var block = (JObject)Serialized(result)["content"][0];

            Assert.Equal(new JObject
            {
                ["type"] = "image",
                ["data"] = "iVBORw0KGgo=",
                ["mimeType"] = "image/png"
            }, block);
        }

        [Fact]
        public void An_image_result_also_describes_the_image_as_structured_content_and_text()
        {
            var metadata = new JObject { ["path"] = "/sitecore/media library/logo", ["width"] = 640 };

            var json = Serialized(McpToolResult.Image(Png, "image/png", metadata));

            Assert.Equal(metadata, json["structuredContent"]);
            var text = (JObject)json["content"][1];
            Assert.Equal("text", (string)text["type"]);
            Assert.Equal(metadata, JObject.Parse((string)text["text"]));
            Assert.False((bool)json["isError"]);
        }

        [Fact]
        public void A_text_block_carries_no_image_members()
        {
            var block = (JObject)Serialized(McpToolResult.Text("hello"))["content"][0];

            Assert.Equal(new JObject { ["type"] = "text", ["text"] = "hello" }, block);
        }

        [Fact]
        public void Image_members_are_omitted_even_with_a_serializer_that_writes_nulls()
        {
            var block = JObject.FromObject(McpContent.FromImage(Png, "image/jpeg"));

            Assert.Null(block["text"]);
            Assert.Equal("image/jpeg", (string)block["mimeType"]);
            Assert.Null(JObject.FromObject(McpContent.FromText("x"))["data"]);
        }
    }
}
