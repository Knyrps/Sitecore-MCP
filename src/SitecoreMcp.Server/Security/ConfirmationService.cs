using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Issues and redeems confirmation tokens for direct changes. A direct-change tool called without
    /// a token returns a preview plus a token bound to exactly that preview (see
    /// <see cref="ConfirmationBinding"/>); called again with the token, it runs only when the token is
    /// genuine, unexpired, unused, and bound to the same tool, editor, arguments and target state.
    /// </summary>
    /// <remarks>
    /// A token is <c>v1.{nonce}.{expiry}.{hmac}</c>, the HMAC-SHA256 over nonce, expiry and the binding
    /// digest. The signature makes tokens unforgeable and carries the binding without storing it; the
    /// in-memory store of outstanding nonces makes them single-use. Tokens are therefore valid only
    /// on the instance (and process lifetime) that issued them - fine for a confirmation that follows
    /// its preview within minutes. No Sitecore references, so it is unit-testable off-instance.
    /// </remarks>
    public sealed class ConfirmationService
    {
        private const string Version = "v1";

        private readonly byte[] _key;
        private readonly ConfirmationOptions _options;
        private readonly Func<DateTimeOffset> _clock;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _outstanding =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        /// <summary>
        /// Creates the service. Without a key a random one is generated, which is the normal case:
        /// tokens never need to outlive the process that issued them.
        /// </summary>
        public ConfirmationService(byte[] key = null, ConfirmationOptions options = null, Func<DateTimeOffset> clock = null)
        {
            if (key != null && key.Length < 32)
            {
                throw new ArgumentException("A confirmation signing key must be at least 32 bytes.", nameof(key));
            }

            _key = key ?? RandomBytes(32);
            _options = options ?? new ConfirmationOptions();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>The effective options.</summary>
        public ConfirmationOptions Options => _options;

        /// <summary>Issues a single-use token for the previewed change.</summary>
        public IssuedConfirmation Issue(ConfirmationBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            var now = _clock();
            var expires = DateTimeOffset.FromUnixTimeSeconds(now.Add(_options.Lifetime).ToUnixTimeSeconds());
            var nonce = Base64Url(RandomBytes(16));

            Prune(now);
            _outstanding[nonce] = expires;

            var expiry = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signature = Sign(nonce, expiry, ConfirmationCanonicalizer.Digest(binding));
            return new IssuedConfirmation(Version + "." + nonce + "." + expiry + "." + Base64Url(signature), expires);
        }

        /// <summary>
        /// Redeems a token for the change about to run. On success the token is spent; a refused token
        /// is left as it was, so presenting the wrong arguments does not burn a genuine confirmation.
        /// </summary>
        public ConfirmationRedemption Redeem(string token, ConfirmationBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            if (string.IsNullOrWhiteSpace(token))
            {
                return ConfirmationRedemption.Fail(ConfirmationFailure.Missing);
            }

            var parts = token.Trim().Split('.');
            if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal) ||
                parts[1].Length == 0 ||
                !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expirySeconds))
            {
                return ConfirmationRedemption.Fail(ConfirmationFailure.Malformed);
            }

            var presented = FromBase64Url(parts[3]);
            if (presented == null)
            {
                return ConfirmationRedemption.Fail(ConfirmationFailure.Malformed);
            }

            // Signature first: it proves the token is ours and that it was issued for exactly this binding.
            var expected = Sign(parts[1], parts[2], ConfirmationCanonicalizer.Digest(binding));
            if (!FixedTimeEquals(expected, presented))
            {
                return ConfirmationRedemption.Fail(ConfirmationFailure.Mismatch);
            }

            var now = _clock();
            if (now >= DateTimeOffset.FromUnixTimeSeconds(expirySeconds))
            {
                _outstanding.TryRemove(parts[1], out _);
                return ConfirmationRedemption.Fail(ConfirmationFailure.Expired);
            }

            // Atomic: of two concurrent redemptions of one token, exactly one removes it.
            if (!_outstanding.TryRemove(parts[1], out _))
            {
                return ConfirmationRedemption.Fail(ConfirmationFailure.AlreadyUsed);
            }

            return ConfirmationRedemption.Success;
        }

        /// <summary>Drops expired tokens, then the soonest-expiring ones while over the outstanding limit.</summary>
        private void Prune(DateTimeOffset now)
        {
            foreach (var entry in _outstanding)
            {
                if (now >= entry.Value)
                {
                    _outstanding.TryRemove(entry.Key, out _);
                }
            }

            var excess = _outstanding.Count - Math.Max(1, _options.MaxOutstanding) + 1;
            if (excess <= 0)
            {
                return;
            }

            foreach (var entry in _outstanding.OrderBy(e => e.Value).Take(excess).ToList())
            {
                _outstanding.TryRemove(entry.Key, out _);
            }
        }

        private byte[] Sign(string nonce, string expiry, byte[] digest)
        {
            using (var hmac = new HMACSHA256(_key))
            {
                var input = Encoding.ASCII.GetBytes(Version + "|" + nonce + "|" + expiry + "|");
                var buffer = new byte[input.Length + digest.Length];
                Buffer.BlockCopy(input, 0, buffer, 0, input.Length);
                Buffer.BlockCopy(digest, 0, buffer, input.Length, digest.Length);
                return hmac.ComputeHash(buffer);
            }
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return bytes;
        }

        private static bool FixedTimeEquals(byte[] expected, byte[] actual)
        {
            var difference = expected.Length ^ actual.Length;
            for (var i = 0; i < expected.Length; i++)
            {
                difference |= expected[i] ^ (i < actual.Length ? actual[i] : 0);
            }

            return difference == 0;
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return null;

            var base64 = segment.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
                case 1: return null;
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
