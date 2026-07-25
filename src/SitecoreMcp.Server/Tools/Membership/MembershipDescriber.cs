using System;
using System.Linq;
using System.Web.Security;
using Newtonsoft.Json.Linq;
using Sitecore.Security.Accounts;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>
    /// Projects security accounts to JSON. User enable/lock state comes from the membership provider,
    /// while identity and roles come from Sitecore's account model; both are read defensively so a
    /// describer never throws on a partially provisioned account.
    /// </summary>
    public static class MembershipDescriber
    {
        /// <summary>Describes a user: identity, admin flag, profile basics, enable/lock state, and optionally roles.</summary>
        public static JObject User(User user, bool includeRoles)
        {
            var result = new JObject
            {
                ["name"] = user.Name,
                ["localName"] = user.LocalName,
                ["domain"] = user.Domain?.Name,
                ["displayName"] = user.DisplayName,
                ["isAdministrator"] = user.IsAdministrator
            };

            // The profile is a separate provider read and never carries the password; guard it so a
            // missing profile does not fail the whole describe.
            try
            {
                var profile = user.Profile;
                result["email"] = NullIfEmpty(profile?.Email);
                result["fullName"] = NullIfEmpty(profile?.FullName);
                result["comment"] = NullIfEmpty(profile?.Comment);
            }
            catch
            {
                // Leave the profile fields absent when the provider cannot load it.
            }

            var membership = SafeMembershipUser(user.Name);
            if (membership != null)
            {
                result["enabled"] = membership.IsApproved;
                result["lockedOut"] = membership.IsLockedOut;
                result["lastLogin"] = membership.LastLoginDate == default(DateTime)
                    ? null
                    : (JToken)membership.LastLoginDate.ToUniversalTime().ToString("o");
            }

            if (includeRoles)
            {
                result["roles"] = Names(RolesInRolesManager.GetRolesForUser(user, false).Select(r => r.Name));
            }

            return result;
        }

        /// <summary>Describes a role: identity, its kind, and either a member count or the members themselves.</summary>
        public static JObject Role(Role role, bool includeMembers)
        {
            var result = new JObject
            {
                ["name"] = role.Name,
                ["localName"] = role.LocalName,
                ["domain"] = role.Domain?.Name,
                ["isEveryone"] = role.IsEveryone,
                ["isGlobal"] = role.IsGlobal
            };

            if (includeMembers)
            {
                result["memberUsers"] = Names(RolesInRolesManager.GetUsersInRole(role, false).Select(u => u.Name));
                result["memberRoles"] = Names(RolesInRolesManager.GetRolesInRole(role, false).Select(r => r.Name));
            }
            else
            {
                result["memberUserCount"] = RolesInRolesManager.GetUsersInRole(role, false).Count();
            }

            return result;
        }

        /// <summary>Describes a domain: its name, user count, and Everyone role.</summary>
        public static JObject Domain(Sitecore.Security.Domains.Domain domain)
        {
            var result = new JObject { ["name"] = domain.Name };

            try { result["userCount"] = domain.GetUserCount(); } catch { }
            try { result["everyoneRole"] = domain.GetEveryoneRole()?.Name; } catch { }

            return result;
        }

        private static MembershipUser SafeMembershipUser(string fullName)
        {
            try
            {
                return System.Web.Security.Membership.GetUser(fullName, false);
            }
            catch
            {
                return null;
            }
        }

        private static JArray Names(System.Collections.Generic.IEnumerable<string> names) =>
            new JArray(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());

        private static JToken NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : (JToken)value;
    }
}
