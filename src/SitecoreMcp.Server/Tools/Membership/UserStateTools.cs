using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for the user-state tools, which each act on one named user.</summary>
    public sealed class UserNameArgs
    {
        /// <summary>The user to act on.</summary>
        [McpParam(Description = "User name (domain\\name, or a bare name for the sitecore domain).", Required = true)]
        public string Name { get; set; }
    }

    /// <summary>Enables a user so the account can authenticate.</summary>
    public sealed class EnableUserTool : McpTool<UserNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_enable_user";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description => "Enable a Sitecore user so the account can log in. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UserNameArgs args, McpCallContext context) =>
            SetApproved(args.Name, true);

        internal static McpToolResult SetApproved(string name, bool approved)
        {
            var membershipUser = MembershipResolver.RequireMembershipUser(name);
            if (membershipUser.IsApproved == approved)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["user"] = membershipUser.UserName,
                    ["enabled"] = approved,
                    ["note"] = approved ? "The user was already enabled." : "The user was already disabled."
                });
            }

            membershipUser.IsApproved = approved;
            System.Web.Security.Membership.UpdateUser(membershipUser);

            return McpToolResult.Structured(new JObject
            {
                ["user"] = membershipUser.UserName,
                ["enabled"] = approved
            });
        }
    }

    /// <summary>Disables a user so the account cannot authenticate.</summary>
    public sealed class DisableUserTool : McpTool<UserNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_disable_user";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description => "Disable a Sitecore user so the account cannot log in. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UserNameArgs args, McpCallContext context) =>
            EnableUserTool.SetApproved(args.Name, false);
    }

    /// <summary>Clears a user's failed-login lockout.</summary>
    public sealed class UnlockUserTool : McpTool<UserNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_unlock_user";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Clear a Sitecore user's failed-login lockout so the account can authenticate again. This " +
            "is the membership lockout, distinct from an item lock. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UserNameArgs args, McpCallContext context)
        {
            var membershipUser = MembershipResolver.RequireMembershipUser(args.Name);
            if (!membershipUser.IsLockedOut)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["user"] = membershipUser.UserName,
                    ["lockedOut"] = false,
                    ["note"] = "The user was not locked out."
                });
            }

            var unlocked = membershipUser.UnlockUser();
            return McpToolResult.Structured(new JObject
            {
                ["user"] = membershipUser.UserName,
                ["lockedOut"] = !unlocked
            });
        }
    }
}
