using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Protocol
{
    /// <summary>
    /// Errors raised before full validation (auth, rate limit, busy) must still echo the request id,
    /// otherwise a client cannot match the error to its pending request and waits forever.
    /// </summary>
    public class PeekIdTests
    {
        [Fact]
        public void A_numeric_id_is_read()
        {
            Assert.Equal(new JValue(42L), McpRequestEnvelope.PeekId("{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"ping\"}"));
        }

        [Fact]
        public void A_string_id_is_read()
        {
            Assert.Equal(new JValue("a-1"), McpRequestEnvelope.PeekId("{\"jsonrpc\":\"2.0\",\"id\":\"a-1\",\"method\":\"ping\"}"));
        }

        [Theory]
        [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")]
        [InlineData("{\"id\":{\"x\":1}}")]
        [InlineData("[{\"id\":1}]")]
        [InlineData("not json")]
        [InlineData("")]
        public void No_usable_id_yields_null(string body)
        {
            Assert.Null(McpRequestEnvelope.PeekId(body));
        }

        [Fact]
        public void A_failure_built_with_the_peeked_id_echoes_it()
        {
            var id = McpRequestEnvelope.PeekId("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\"}");
            var json = McpJson.Serialize(JsonRpcResponse.Failure(id, JsonRpcErrorCodes.InvalidRequest, "Rate limit exceeded."));

            Assert.Equal(7, JObject.Parse(json).Value<int>("id"));
        }
    }
}
