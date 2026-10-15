using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Who may be issued an editor token, and for which audience. Kept free of Sitecore so the rules
    /// are unit-testable; the Sitecore-coupled issuer applies them to the logged-in user.
    /// </summary>
    public sealed class EditorTokenIssuePolicy
    {
        private readonly IReadOnlyList<string> _audiences;
        private readonly ISet<string> _domains;

        /// <summary>
        /// Creates the policy over the audiences of the configured editor-token clients and the
        /// pipe- or comma-separated Sitecore domains whose users may be issued a token.
        /// </summary>
        public EditorTokenIssuePolicy(IEnumerable<string> audiences, string allowedDomains)
        {
            _audiences = (audiences ?? Enumerable.Empty<string>()).Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.Ordinal).ToList();
            _domains = new HashSet<string>(
                (allowedDomains ?? string.Empty).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim()).Where(d => d.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the audience a token is issued for. Only audiences of configured editor-token
        /// clients are issued; none requested means the only one, when there is exactly one.
        /// </summary>
        public bool TryResolveAudience(string requested, out string audience, out string error)
        {
            audience = null;
            error = null;

            if (_audiences.Count == 0)
            {
                error = "No client is configured with requiresEditorToken=\"true\", so no editor token can be issued.";
                return false;
            }

            if (string.IsNullOrEmpty(requested))
            {
                if (_audiences.Count == 1)
                {
                    audience = _audiences[0];
                    return true;
                }

                error = "Several editor-token clients are configured; name the audience. Available: " + string.Join(", ", _audiences) + ".";
                return false;
            }

            if (_audiences.Contains(requested, StringComparer.Ordinal))
            {
                audience = requested;
                return true;
            }

            error = $"'{requested}' is not the audience of an editor-token client. Available: {string.Join(", ", _audiences)}.";
            return false;
        }

        /// <summary>Whether a fully-qualified Sitecore user name ("domain\name") belongs to an allowed domain.</summary>
        public bool IsAllowedUser(string userName)
        {
            if (string.IsNullOrEmpty(userName)) return false;

            var separator = userName.IndexOf('\\');
            if (separator <= 0 || separator == userName.Length - 1) return false;

            var domain = userName.Substring(0, separator);
            var name = userName.Substring(separator + 1);
            return _domains.Contains(domain) && !string.Equals(name, "Anonymous", StringComparison.OrdinalIgnoreCase);
        }
    }
}
