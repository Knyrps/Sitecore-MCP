using System;
using System.Collections.Concurrent;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>The outcome of a rate-limit check: whether the call is allowed and, if not, how long to wait.</summary>
    public struct RateLimitDecision
    {
        /// <summary>Whether a token was available and has been consumed.</summary>
        public bool Allowed { get; }

        /// <summary>When rejected, the seconds a caller should wait before retrying; zero when allowed.</summary>
        public int RetryAfterSeconds { get; }

        /// <summary>Creates a decision with the given allow flag and retry hint.</summary>
        public RateLimitDecision(bool allowed, int retryAfterSeconds)
        {
            Allowed = allowed;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }

    /// <summary>
    /// A per-key token-bucket limiter. Each key refills at a steady rate up to a burst capacity,
    /// so a caller may spike briefly but not sustain more than the configured average rate.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly int _capacity;
        private readonly double _refillPerSecond;
        private readonly Func<DateTime> _clock;
        private readonly ConcurrentDictionary<string, Bucket> _buckets =
            new ConcurrentDictionary<string, Bucket>(StringComparer.Ordinal);

        /// <summary>
        /// Creates a limiter allowing bursts up to <paramref name="capacity"/> and a steady
        /// <paramref name="refillPerSecond"/> tokens per second. The clock is injectable for testing.
        /// </summary>
        public RateLimiter(int capacity, double refillPerSecond, Func<DateTime> clock = null)
        {
            _capacity = Math.Max(1, capacity);
            _refillPerSecond = refillPerSecond > 0 ? refillPerSecond : 1;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>Attempts to consume one token for the given key, refilling it first based on elapsed time.</summary>
        public RateLimitDecision TryAcquire(string key)
        {
            var bucket = _buckets.GetOrAdd(key ?? string.Empty, _ => new Bucket(_capacity, _clock()));

            lock (bucket.SyncRoot)
            {
                var now = _clock();
                var elapsedSeconds = (now - bucket.LastRefill).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    bucket.Tokens = Math.Min(_capacity, bucket.Tokens + elapsedSeconds * _refillPerSecond);
                    bucket.LastRefill = now;
                }

                if (bucket.Tokens >= 1)
                {
                    bucket.Tokens -= 1;
                    return new RateLimitDecision(true, 0);
                }

                var secondsUntilToken = (int)Math.Ceiling((1 - bucket.Tokens) / _refillPerSecond);
                return new RateLimitDecision(false, Math.Max(1, secondsUntilToken));
            }
        }

        private sealed class Bucket
        {
            public Bucket(double tokens, DateTime lastRefill)
            {
                Tokens = tokens;
                LastRefill = lastRefill;
            }

            public readonly object SyncRoot = new object();
            public double Tokens;
            public DateTime LastRefill;
        }
    }
}
