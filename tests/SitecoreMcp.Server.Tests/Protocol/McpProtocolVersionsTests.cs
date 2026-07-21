using SitecoreMcp.Server.Protocol;
using Xunit;

namespace SitecoreMcp.Server.Tests.Protocol
{
    public class McpProtocolVersionsTests
    {
        [Theory]
        [InlineData("2025-06-18", true)]
        [InlineData("2025-03-26", true)]
        [InlineData("2024-01-01", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSupported_matches_the_known_versions(string version, bool expected)
        {
            Assert.Equal(expected, McpProtocolVersions.IsSupported(version));
        }

        [Fact]
        public void Negotiate_echoes_a_supported_version()
        {
            Assert.Equal("2025-03-26", McpProtocolVersions.Negotiate("2025-03-26"));
        }

        [Fact]
        public void Negotiate_falls_back_to_preferred_for_an_unsupported_version()
        {
            Assert.Equal(McpProtocolVersions.Preferred, McpProtocolVersions.Negotiate("1999-01-01"));
        }
    }
}
