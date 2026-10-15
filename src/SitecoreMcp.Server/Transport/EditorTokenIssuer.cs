using System;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Issues editor tokens for logged-in Sitecore users. This is the in-process entry point for a
    /// package that opens a panel in the Content Editor (the CorePilot package's loader): call
    /// <see cref="TryIssueForContextUser"/> while handling the editor's own request, and hand the
    /// token to the panel, which sends it as <c>X-Mcp-Editor-Token</c> on every MCP call. The
    /// <see cref="EditorTokenHttpHandler"/> endpoint wraps the same call for browser code.
    /// </summary>
    public static class EditorTokenIssuer
    {
        /// <summary>Issues a token for the current <see cref="Sitecore.Context.User"/>.</summary>
        public static bool TryIssueForContextUser(string audience, out IssuedEditorToken token, out string audienceIssued, out string error) =>
            TryIssue(Sitecore.Context.User, audience, out token, out audienceIssued, out error);

        /// <summary>
        /// Issues a token naming <paramref name="user"/> for the editor-token client whose audience is
        /// given (or the only one, when null). Refuses anonymous users, users outside the allowed
        /// domains (Mcp.EditorToken.AllowedDomains), disabled or locked-out accounts, and any request
        /// while the endpoint is disabled or no signing secret is configured.
        /// </summary>
        public static bool TryIssue(User user, string audience, out IssuedEditorToken token, out string audienceIssued, out string error)
        {
            token = null;
            audienceIssued = null;

            var tokens = McpRuntime.EditorTokens;
            var settings = McpRuntime.Settings;
            if (tokens == null || settings == null)
            {
                error = "Editor tokens are not configured on this instance (Mcp.Enabled, or the secret named by Mcp.EditorToken.SecretEnvVar).";
                return false;
            }

            var policy = new EditorTokenIssuePolicy(settings.EditorTokenAudiences, settings.EditorTokenDomains);
            if (!policy.TryResolveAudience(audience, out audienceIssued, out error))
            {
                return false;
            }

            if (user == null || !user.IsAuthenticated || !policy.IsAllowedUser(user.Name))
            {
                error = "Only a logged-in Sitecore editor can be issued an editor token.";
                audienceIssued = null;
                return false;
            }

            var membershipUser = System.Web.Security.Membership.GetUser(user.Name, false);
            if (membershipUser == null || !membershipUser.IsApproved || membershipUser.IsLockedOut)
            {
                error = "The account is disabled or locked out.";
                audienceIssued = null;
                return false;
            }

            token = tokens.Issue(user.Name, user.IsAdministrator, audienceIssued);
            McpLog.Info($"Issued editor token for '{user.Name}' (audience '{audienceIssued}', expires {token.ExpiresAt:o}).");
            return true;
        }
    }
}
