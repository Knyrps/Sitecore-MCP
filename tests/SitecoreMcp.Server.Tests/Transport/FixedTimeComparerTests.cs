using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Tests.Transport
{
    public class FixedTimeComparerTests
    {
        [Fact]
        public void Equal_strings_match()
        {
            Assert.True(FixedTimeComparer.Equals("s3cr3t-key", "s3cr3t-key"));
        }

        [Fact]
        public void Different_strings_do_not_match()
        {
            Assert.False(FixedTimeComparer.Equals("s3cr3t-key", "s3cr3t-keZ"));
        }

        [Fact]
        public void Different_length_strings_do_not_match()
        {
            Assert.False(FixedTimeComparer.Equals("short", "shorter"));
        }

        [Theory]
        [InlineData(null, "x")]
        [InlineData("x", null)]
        [InlineData(null, null)]
        public void Null_arguments_never_match(string a, string b)
        {
            Assert.False(FixedTimeComparer.Equals(a, b));
        }
    }
}
