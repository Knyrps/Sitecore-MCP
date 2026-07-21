using System;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Tests.Transport
{
    public class RateLimiterTests
    {
        [Fact]
        public void Allows_up_to_capacity_then_rejects()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var limiter = new RateLimiter(capacity: 3, refillPerSecond: 1, clock: () => now);

            Assert.True(limiter.TryAcquire("k").Allowed);
            Assert.True(limiter.TryAcquire("k").Allowed);
            Assert.True(limiter.TryAcquire("k").Allowed);

            var rejected = limiter.TryAcquire("k");
            Assert.False(rejected.Allowed);
            Assert.True(rejected.RetryAfterSeconds >= 1);
        }

        [Fact]
        public void Refills_over_time()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var limiter = new RateLimiter(capacity: 1, refillPerSecond: 1, clock: () => now);

            Assert.True(limiter.TryAcquire("k").Allowed);
            Assert.False(limiter.TryAcquire("k").Allowed);

            now = now.AddSeconds(1);
            Assert.True(limiter.TryAcquire("k").Allowed);
        }

        [Fact]
        public void Keys_are_independent()
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var limiter = new RateLimiter(capacity: 1, refillPerSecond: 1, clock: () => now);

            Assert.True(limiter.TryAcquire("a").Allowed);
            Assert.True(limiter.TryAcquire("b").Allowed);
            Assert.False(limiter.TryAcquire("a").Allowed);
        }
    }
}
