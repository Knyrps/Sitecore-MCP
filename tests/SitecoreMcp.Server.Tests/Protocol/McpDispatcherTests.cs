using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tests.Support;
using Xunit;

namespace SitecoreMcp.Server.Tests.Protocol
{
    public class McpDispatcherTests
    {
        private static McpDispatcher NewDispatcher(IToolCatalog catalog = null) =>
            new McpDispatcher(catalog ?? new FakeToolCatalog(), "TestServer", "1.0.0");

        private static McpRequestEnvelope Parse(string body)
        {
            Assert.True(McpRequestEnvelope.TryParse(body, out var envelope, out _));
            return envelope;
        }

        [Fact]
        public void Initialize_echoes_supported_version_and_advertises_tools()
        {
            var outcome = NewDispatcher().Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\"}}"));

            var result = JObject.Parse(outcome.Body)["result"];
            Assert.Equal(200, outcome.StatusCode);
            Assert.Equal("2025-03-26", result["protocolVersion"]);
            Assert.NotNull(result["capabilities"]["tools"]);
            Assert.Equal("TestServer", result["serverInfo"]["name"]);
        }

        [Fact]
        public void Initialize_falls_back_to_preferred_version_when_unsupported()
        {
            var outcome = NewDispatcher().Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"1999-01-01\"}}"));

            Assert.Equal(McpProtocolVersions.Preferred, JObject.Parse(outcome.Body)["result"]["protocolVersion"]);
        }

        [Fact]
        public void Ping_returns_empty_result()
        {
            var outcome = NewDispatcher().Dispatch(Parse("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"ping\"}"));

            var body = JObject.Parse(outcome.Body);
            Assert.Equal(7, body["id"]);
            Assert.Empty((JObject)body["result"]);
        }

        [Fact]
        public void ToolsList_returns_registered_tools()
        {
            var catalog = new FakeToolCatalog().Add("sitecore_get_item", _ => McpToolResult.Text("ok"));

            var outcome = NewDispatcher(catalog).Dispatch(Parse("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));

            var tools = (JArray)JObject.Parse(outcome.Body)["result"]["tools"];
            Assert.Single(tools);
            Assert.Equal("sitecore_get_item", tools[0]["name"]);
        }

        [Fact]
        public void ToolsCall_invokes_the_named_tool()
        {
            var catalog = new FakeToolCatalog().Add("echo", args => McpToolResult.Text(args.Value<string>("text")));

            var outcome = NewDispatcher(catalog).Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"arguments\":{\"text\":\"hi\"}}}"));

            var content = (JArray)JObject.Parse(outcome.Body)["result"]["content"];
            Assert.Equal("hi", content[0]["text"]);
        }

        [Fact]
        public void ToolsCall_on_unknown_tool_is_invalid_params()
        {
            var outcome = NewDispatcher().Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"missing\"}}"));

            Assert.Equal(JsonRpcErrorCodes.InvalidParams, (int)JObject.Parse(outcome.Body)["error"]["code"]);
        }

        [Fact]
        public void ToolsCall_surfaces_tool_failure_as_isError_not_protocol_error()
        {
            var catalog = new FakeToolCatalog().Add("boom", _ => McpToolResult.Failure("nope"));

            var outcome = NewDispatcher(catalog).Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"boom\"}}"));

            var result = JObject.Parse(outcome.Body)["result"];
            Assert.Null(JObject.Parse(outcome.Body)["error"]);
            Assert.True((bool)result["isError"]);
        }

        [Fact]
        public void ToolsCall_swallows_an_escaped_tool_exception_into_isError()
        {
            var catalog = new FakeToolCatalog().Add("throws", _ => throw new System.InvalidOperationException("kaboom"));

            var outcome = NewDispatcher(catalog).Dispatch(Parse(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"throws\"}}"));

            Assert.Equal(200, outcome.StatusCode);
            Assert.True((bool)JObject.Parse(outcome.Body)["result"]["isError"]);
        }

        [Fact]
        public void Unknown_method_is_method_not_found()
        {
            var outcome = NewDispatcher().Dispatch(Parse("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"does/not/exist\"}"));

            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, (int)JObject.Parse(outcome.Body)["error"]["code"]);
        }

        [Fact]
        public void Notification_is_accepted_with_no_body()
        {
            var outcome = NewDispatcher().Dispatch(Parse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));

            Assert.Equal(202, outcome.StatusCode);
            Assert.Null(outcome.Body);
        }

        [Fact]
        public void Unknown_notification_is_still_accepted_with_no_body()
        {
            var outcome = NewDispatcher().Dispatch(Parse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/somethingNew\"}"));

            Assert.Equal(202, outcome.StatusCode);
            Assert.Null(outcome.Body);
        }
    }
}
