using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Issues and validates editor tokens: short-lived HS256 JWTs that name the Sitecore editor a
    /// call should run as. Sitecore issues one when the CorePilot panel opens; the client sends it on
    /// every call and the server runs the call as that editor.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than built on System.IdentityModel.Tokens.Jwt, for the same reason the
    /// protocol is: that package's dependency graph collides with Sitecore's binding redirects. The
    /// format is a standard compact JWT, so any JWT library can read it. Only HS256 is accepted; the
    /// header's alg is checked before anything else, so "none" or an algorithm swap is refused.
    /// No Sitecore references, so it is unit-testable off-instance.
    /// </remarks>
    public sealed class EditorTokenService
    {
        /// <summary>The shortest signing secret accepted, in characters.</summary>
        public const int MinimumSecretLength = 32;

        private const string Algorithm = "HS256";
        private const string AdminClaim = "admin";

        private readonly byte[] _key;
        private readonly EditorTokenOptions _options;
        private readonly Func<DateTimeOffset> _clock;

        /// <summary>Creates the service over a signing secret. Throws when the secret is missing or too short.</summary>
        public EditorTokenService(string secret, EditorTokenOptions options = null, Func<DateTimeOffset> clock = null)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < MinimumSecretLength)
            {
                throw new ArgumentException(
                    $"The editor-token secret must be at least {MinimumSecretLength} characters.", nameof(secret));
            }

            _key = Encoding.UTF8.GetBytes(secret);
            _options = options ?? new EditorTokenOptions();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>The effective options, e.g. for reporting the lifetime to the issuing endpoint.</summary>
        public EditorTokenOptions Options => _options;

        /// <summary>Issues a token naming the editor, valid only for the client whose audience is given.</summary>
        public IssuedEditorToken Issue(string userName, bool isAdmin, string audience)
        {
            if (string.IsNullOrEmpty(userName)) throw new ArgumentException("A user name is required.", nameof(userName));
            if (string.IsNullOrEmpty(audience)) throw new ArgumentException("An audience is required.", nameof(audience));

            var now = _clock();
            var expires = now.Add(_options.Lifetime);

            var header = new JObject { ["alg"] = Algorithm, ["typ"] = "JWT" };
            var payload = new JObject
            {
                ["iss"] = _options.Issuer,
                ["aud"] = audience,
                ["sub"] = userName,
                [AdminClaim] = isAdmin,
                ["iat"] = now.ToUnixTimeSeconds(),
                ["nbf"] = now.ToUnixTimeSeconds(),
                ["exp"] = expires.ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N")
            };

            var signingInput = Encode(header) + "." + Encode(payload);
            var token = signingInput + "." + Base64Url(Sign(signingInput));
            return new IssuedEditorToken(token, DateTimeOffset.FromUnixTimeSeconds(expires.ToUnixTimeSeconds()));
        }

        /// <summary>Validates a token for the given client audience, returning the editor or the refusal reason.</summary>
        public EditorTokenValidation Validate(string token, string expectedAudience)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Missing);
            }

            var parts = token.Trim().Split('.');
            if (parts.Length != 3)
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Malformed);
            }

            var header = DecodeObject(parts[0]);
            if (header == null || !string.Equals(Text(header, "alg"), Algorithm, StringComparison.Ordinal))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Malformed);
            }

            var signature = FromBase64Url(parts[2]);
            if (signature == null)
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Malformed);
            }

            // Signature before any claim is trusted: an unsigned payload says nothing.
            if (!FixedTimeEquals(Sign(parts[0] + "." + parts[1]), signature))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.InvalidSignature);
            }

            var payload = DecodeObject(parts[1]);
            var subject = payload == null ? null : Text(payload, "sub");
            var expires = payload == null ? null : Seconds(payload, "exp");
            if (string.IsNullOrEmpty(subject) || expires == null)
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Malformed);
            }

            if (!string.Equals(Text(payload, "iss"), _options.Issuer, StringComparison.Ordinal))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.WrongIssuer);
            }

            if (string.IsNullOrEmpty(expectedAudience) ||
                !string.Equals(Text(payload, "aud"), expectedAudience, StringComparison.Ordinal))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.WrongAudience);
            }

            var now = _clock();
            if (now - _options.ClockSkew >= DateTimeOffset.FromUnixTimeSeconds(expires.Value))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.Expired);
            }

            var notBefore = Seconds(payload, "nbf");
            if (notBefore != null && now + _options.ClockSkew < DateTimeOffset.FromUnixTimeSeconds(notBefore.Value))
            {
                return EditorTokenValidation.Fail(EditorTokenFailure.NotYetValid);
            }

            var isAdmin = payload[AdminClaim]?.Type == JTokenType.Boolean && payload.Value<bool>(AdminClaim);
            return EditorTokenValidation.Success(subject, isAdmin);
        }

        private byte[] Sign(string signingInput)
        {
            using (var hmac = new HMACSHA256(_key))
            {
                return hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
            }
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

        private static string Encode(JObject value) =>
            Base64Url(Encoding.UTF8.GetBytes(value.ToString(Formatting.None)));

        private static JObject DecodeObject(string segment)
        {
            var bytes = FromBase64Url(segment);
            if (bytes == null) return null;

            try
            {
                return JToken.Parse(Encoding.UTF8.GetString(bytes)) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JObject value, string name) =>
            value[name]?.Type == JTokenType.String ? value.Value<string>(name) : null;

        private static long? Seconds(JObject value, string name) =>
            value[name]?.Type == JTokenType.Integer ? value.Value<long>(name) : (long?)null;

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
