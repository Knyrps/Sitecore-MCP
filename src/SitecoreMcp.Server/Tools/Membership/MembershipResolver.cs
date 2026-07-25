namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>
    /// Normalises account names. Sitecore user and role names are domain-qualified (domain\name); a
    /// bare name is assumed to be in the default domain so a caller can pass "editor" for
    /// "sitecore\editor".
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
    }
}
