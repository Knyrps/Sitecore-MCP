using Sitecore.Security.Accounts;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>
    /// Normalises account names and resolves existing accounts. Sitecore user and role names are
    /// domain-qualified (domain\name); a bare name is assumed to be in the default domain so a caller
    /// can pass "editor" for "sitecore\editor".
    /// </summary>
    public static class MembershipResolver
    {
        /// <summary>The domain assumed when a name is given without one.</summary>
        public const string DefaultDomain = "sitecore";

        /// <summary>Returns the name domain-qualified, prefixing the default domain when none is given.</summary>
        public static string Qualify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpToolException("No account name was specified.");
            }

            var trimmed = name.Trim();
            return trimmed.Contains("\\") ? trimmed : $"{DefaultDomain}\\{trimmed}";
        }

        /// <summary>Resolves an existing user by name, throwing <see cref="McpToolException"/> when it does not exist.</summary>
        public static User RequireUser(string name)
        {
            var qualified = Qualify(name);
            if (!User.Exists(qualified))
            {
                throw new McpToolException($"User '{qualified}' does not exist.");
            }

            return User.FromName(qualified, false);
        }

        /// <summary>Resolves an existing role by name, throwing <see cref="McpToolException"/> when it does not exist.</summary>
        public static Role RequireRole(string name)
        {
            var qualified = Qualify(name);
            if (!Role.Exists(qualified))
            {
                throw new McpToolException($"Role '{qualified}' does not exist.");
            }

            return Role.FromName(qualified);
        }

        /// <summary>
        /// Resolves the membership-provider record for a user, which carries the enable and lock state
        /// the Sitecore account model does not. Throws when the provider has no such user.
        /// </summary>
        public static System.Web.Security.MembershipUser RequireMembershipUser(string name)
        {
            var qualified = Qualify(name);
            var membershipUser = System.Web.Security.Membership.GetUser(qualified, false);
            if (membershipUser == null)
            {
                throw new McpToolException($"User '{qualified}' does not exist in the membership provider.");
            }

            return membershipUser;
        }
    }
}
