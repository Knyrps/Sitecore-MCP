using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Reduces a <see cref="ConfirmationBinding"/> to a stable digest. Arguments are canonicalized
    /// first - object keys sorted ordinally, null-valued properties dropped, the confirmation token
    /// itself removed - so the same change always hashes the same however its JSON was laid out.
    /// </summary>
    public static class ConfirmationCanonicalizer
    {
        /// <summary>The argument name the confirmation token travels under.</summary>
        public const string TokenArgument = "confirmationToken";

        /// <summary>Returns the canonical compact JSON for a call's arguments, without the confirmation token.</summary>
        public static string CanonicalArguments(JToken arguments)
        {
            if (arguments == null || arguments.Type == JTokenType.Null)
            {
                return "{}";
            }

            var canonical = Canonicalize(arguments, topLevel: true);
            return canonical.ToString(Formatting.None);
        }

        /// <summary>Returns the SHA-256 digest of the binding: tool, editor, canonical arguments and state.</summary>
        public static byte[] Digest(ConfirmationBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            // Length-prefixed fields, so no choice of values can make two bindings collide by shifting
            // characters across a field boundary.
            var builder = new StringBuilder();
            Append(builder, binding.Tool);
            Append(builder, binding.Editor.ToLowerInvariant());
            Append(builder, CanonicalArguments(binding.Arguments));
            Append(builder, binding.State);

            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            }
        }

        private static void Append(StringBuilder builder, string value)
        {
            builder.Append(value.Length).Append(':').Append(value).Append('\n');
        }

        private static JToken Canonicalize(JToken token, bool topLevel)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var result = new JObject();
                    var properties = ((JObject)token).Properties()
                        .Where(p => p.Value.Type != JTokenType.Null && p.Value.Type != JTokenType.Undefined)
                        .Where(p => !(topLevel && string.Equals(p.Name, TokenArgument, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(p => p.Name, StringComparer.Ordinal);
                    foreach (var property in properties)
                    {
                        result.Add(property.Name, Canonicalize(property.Value, topLevel: false));
                    }

                    return result;

                case JTokenType.Array:
                    return new JArray(((JArray)token).Select(item => Canonicalize(item, topLevel: false)));

                default:
                    return token.DeepClone();
            }
        }
    }
}
