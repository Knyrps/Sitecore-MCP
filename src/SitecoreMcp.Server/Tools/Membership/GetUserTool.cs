using System;
using System.Linq;
using System.Web.Security;
using Newtonsoft.Json.Linq;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="GetUserTool"/>.</summary>
    public sealed class GetUserArgs
    {
        /// <summary>The exact user to look up, by name.</summary>
        [McpParam(Description = "Exact user name (domain\\name, or a bare name for the sitecore domain). Use this OR filter.")]
        public string Identity { get; set; }

        /// <summary>A substring to match user names against; ignored when identity is given.</summary>
        [McpParam(Description = "Substring to match against user names. Ignored when identity is given.")]
        public string Filter { get; set; }

        /// <summary>Whether to include each user's roles.</summary>
        [McpParam(Description = "Include the roles each user belongs to. Default true.")]
        public bool? IncludeRoles { get; set; }

        /// <summary>The maximum number of users to return for a filter.</summary>
        [McpParam(Description = "Maximum users to return for a filter (default 50, max 200).")]
        public int? Limit { get; set; }

        /// <summary>The number of users to skip, for paging a filter.</summary>
        [McpParam(Description = "Number of users to skip before returning filter results.")]
        public int? Offset { get; set; }
    }

    /// <summary>Looks up a Sitecore user by exact name, or lists users matching a name substring.</summary>
    public sealed class GetUserTool : McpTool<GetUserArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_get_user";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Look up a Sitecore user by exact name (identity), or list users whose name contains a " +
            "substring (filter). Returns identity, admin flag, profile basics, enable/lock state, and " +
            "optionally roles. Never returns a password. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetUserArgs args, McpCallContext context)
        {
            var includeRoles = args.IncludeRoles.GetValueOrDefault(true);

            if (!string.IsNullOrWhiteSpace(args.Identity))
            {
                var name = MembershipResolver.Qualify(args.Identity);
                if (!User.Exists(name))
                {
                    return McpToolResult.Structured(new JObject { ["identity"] = name, ["found"] = false });
                }

                var described = MembershipDescriber.User(User.FromName(name, false), includeRoles);
                described["found"] = true;
                return McpToolResult.Structured(described);
            }

            if (string.IsNullOrWhiteSpace(args.Filter))
            {
                throw new McpToolException("Provide either 'identity' (exact name) or 'filter' (substring).");
            }

            var matches = System.Web.Security.Membership.GetAllUsers()
                .Cast<MembershipUser>()
                .Where(m => m.UserName.IndexOf(args.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.UserName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);
            var page = new JArray(matches
                .Skip(range.Offset)
                .Take(range.Limit)
                .Select(m => (object)MembershipDescriber.User(User.FromName(m.UserName, false), includeRoles))
                .ToArray());

            return McpToolResult.Structured(Paging.Envelope("users", page, matches.Count, range));
        }
    }
}
