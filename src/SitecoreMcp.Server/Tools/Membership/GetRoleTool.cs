using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="GetRoleTool"/>.</summary>
    public sealed class GetRoleArgs
    {
        /// <summary>The exact role to look up, by name.</summary>
        [McpParam(Description = "Exact role name (domain\\name, or a bare name for the sitecore domain). Use this OR filter.")]
        public string Identity { get; set; }

        /// <summary>A substring to match role names against; ignored when identity is given.</summary>
        [McpParam(Description = "Substring to match against role names. Ignored when identity is given.")]
        public string Filter { get; set; }

        /// <summary>Whether to include the role's members (users and child roles).</summary>
        [McpParam(Description = "Include the role's members (users and nested roles). Default false for a filter, true for an exact lookup.")]
        public bool? IncludeMembers { get; set; }

        /// <summary>The maximum number of roles to return for a filter.</summary>
        [McpParam(Description = "Maximum roles to return for a filter (default 50, max 200).")]
        public int? Limit { get; set; }

        /// <summary>The number of roles to skip, for paging a filter.</summary>
        [McpParam(Description = "Number of roles to skip before returning filter results.")]
        public int? Offset { get; set; }
    }

    /// <summary>Looks up a Sitecore role by exact name, or lists roles matching a name substring.</summary>
    public sealed class GetRoleTool : McpTool<GetRoleArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_get_role";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Look up a Sitecore role by exact name (identity), or list roles whose name contains a " +
            "substring (filter). With includeMembers it lists the users and nested roles in the role. " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetRoleArgs args, McpCallContext context)
        {
            if (!string.IsNullOrWhiteSpace(args.Identity))
            {
                var name = MembershipResolver.Qualify(args.Identity);
                if (!Role.Exists(name))
                {
                    return McpToolResult.Structured(new JObject { ["identity"] = name, ["found"] = false });
                }

                var described = MembershipDescriber.Role(Role.FromName(name), args.IncludeMembers.GetValueOrDefault(true));
                described["found"] = true;
                return McpToolResult.Structured(described);
            }

            if (string.IsNullOrWhiteSpace(args.Filter))
            {
                throw new McpToolException("Provide either 'identity' (exact name) or 'filter' (substring).");
            }

            var includeMembers = args.IncludeMembers.GetValueOrDefault(false);
            var matches = RolesInRolesManager.GetAllRoles()
                .Where(r => r.Name.IndexOf(args.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);
            var page = new JArray(matches
                .Skip(range.Offset)
                .Take(range.Limit)
                .Select(r => (object)MembershipDescriber.Role(r, includeMembers))
                .ToArray());

            return McpToolResult.Structured(Paging.Envelope("roles", page, matches.Count, range));
        }
    }
}
