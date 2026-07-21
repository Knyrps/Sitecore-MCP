using SitecoreMcp.Server.Protocol;
using Xunit;

namespace SitecoreMcp.Server.Tests.Protocol
{
    public class McpRequestEnvelopeTests
    {
        [Fact]
        public void Valid_request_parses_with_id_and_params()
        {
            var ok = McpRequestEnvelope.TryParse(
                "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"x\"}}",
                out var envelope, out _);

            Assert.True(ok);
            Assert.Equal("tools/call", envelope.Method);
            Assert.False(envelope.IsNotification);
            Assert.Equal("x", envelope.Params.Value<string>("name"));
        }

        [Fact]
        public void Missing_id_is_a_notification()
        {
            McpRequestEnvelope.TryParse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                out var envelope, out _);

            Assert.True(envelope.IsNotification);
        }

        [Fact]
        public void Malformed_json_is_a_parse_error()
        {
            var ok = McpRequestEnvelope.TryParse("{ not json", out _, out var failure);

            Assert.False(ok);
            Assert.Equal(400, failure.StatusCode);
            Assert.Contains("-32700", failure.Body);
        }

        [Fact]
        public void Empty_body_is_a_parse_error()
        {
            Assert.False(McpRequestEnvelope.TryParse("   ", out _, out var failure));
            Assert.Contains("-32700", failure.Body);
        }

        [Fact]
        public void Batched_array_is_rejected()
        {
            Assert.False(McpRequestEnvelope.TryParse("[{\"jsonrpc\":\"2.0\",\"method\":\"ping\"}]", out _, out var failure));
            Assert.Contains("-32600", failure.Body);
        }

        [Fact]
        public void Wrong_jsonrpc_version_is_invalid_request()
        {
            Assert.False(McpRequestEnvelope.TryParse("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"ping\"}", out _, out var failure));
            Assert.Contains("-32600", failure.Body);
        }

        [Fact]
        public void Missing_method_is_invalid_request()
        {
            Assert.False(McpRequestEnvelope.TryParse("{\"jsonrpc\":\"2.0\",\"id\":1}", out _, out var failure));
            Assert.Contains("-32600", failure.Body);
        }
    }
}
