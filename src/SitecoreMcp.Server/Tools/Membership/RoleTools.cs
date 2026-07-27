using Newtonsoft.Json.Linq;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="NewRoleTool"/> and <see cref="RemoveRoleTool"/>.</summary>
    public sealed class RoleNameArgs
    {
        /// <summary>The role name to create or remove.</summary>
        [McpParam(Description = "Role name (domain\\name, or a bare name for the sitecore domain).", Required = true)]
        public string Name { get; set; }
    }

    /// <summary>Creates a Sitecore role.</summary>
    public sealed class NewRoleTool : McpTool<RoleNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_new_role";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description => "Create a Sitecore role. Fails if it already exists. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RoleNameArgs args, McpCallContext context)
        {
            var name = MembershipResolver.Qualify(args.Name);
            if (Role.Exists(name))
            {
                return McpToolResult.Failure($"Role '{name}' already exists.");
            }

            System.Web.Security.Roles.CreateRole(name);
            return McpToolResult.Structured(MembershipDescriber.Role(Role.FromName(name), true));
        }
    }

    /// <summary>Removes a Sitecore role.</summary>
    public sealed class RemoveRoleTool : McpTool<RoleNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_role";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a Sitecore role. Members are detached from it; the users and roles themselves are " +
            "not deleted. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RoleNameArgs args, McpCallContext context)
        {
            var name = MembershipResolver.Qualify(args.Name);
            if (!Role.Exists(name))
            {
                return McpToolResult.Failure($"Role '{name}' does not exist.");
            }

            // Do not throw when the role still has members: detaching them is the expected outcome.
            System.Web.Security.Roles.DeleteRole(name, false);
            return McpToolResult.Structured(new JObject { ["role"] = name, ["deleted"] = true });
        }
    }

    /// <summary>Arguments for <see cref="AddRoleMemberTool"/> and <see cref="RemoveRoleMemberTool"/>.</summary>
    public sealed class RoleMemberArgs
    {
        /// <summary>The role whose membership changes.</summary>
        [McpParam(Description = "The role to add to or remove from (domain\\name, or a bare name).", Required = true)]
        public string Role { get; set; }

        /// <summary>The member (a user or a role) to add or remove.</summary>
        [McpParam(Description = "The member to add or remove - a user OR a role name. Nested roles are valid in Sitecore.", Required = true)]
        public string Member { get; set; }
    }

    /// <summary>Adds a user or a role to a role.</summary>
    public sealed class AddRoleMemberTool : McpTool<RoleMemberArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_add_role_member";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Add a member to a Sitecore role. The member may be a user or another role (roles nest). " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RoleMemberArgs args, McpCallContext context)
        {
            var role = MembershipResolver.RequireRole(args.Role);
            var member = MembershipResolver.Qualify(args.Member);

            if (User.Exists(member))
            {
                System.Web.Security.Roles.AddUserToRole(member, role.Name);
                return McpToolResult.Structured(new JObject { ["role"] = role.Name, ["addedUser"] = member });
            }

            if (Role.Exists(member))
            {
                RolesInRolesManager.AddRoleToRole(Role.FromName(member), role);
                return McpToolResult.Structured(new JObject { ["role"] = role.Name, ["addedRole"] = member });
            }

            return McpToolResult.Failure($"'{member}' is neither an existing user nor an existing role.");
        }
    }

    /// <summary>Removes a user or a role from a role.</summary>
    public sealed class RemoveRoleMemberTool : McpTool<RoleMemberArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_role_member";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a member from a Sitecore role. The member may be a user or another role. " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RoleMemberArgs args, McpCallContext context)
        {
            var role = MembershipResolver.RequireRole(args.Role);
            var member = MembershipResolver.Qualify(args.Member);

            if (User.Exists(member))
            {
                System.Web.Security.Roles.RemoveUserFromRole(member, role.Name);
                return McpToolResult.Structured(new JObject { ["role"] = role.Name, ["removedUser"] = member });
            }

            if (Role.Exists(member))
            {
                RolesInRolesManager.RemoveRoleFromRole(Role.FromName(member), role);
                return McpToolResult.Structured(new JObject { ["role"] = role.Name, ["removedRole"] = member });
            }

            return McpToolResult.Failure($"'{member}' is neither an existing user nor an existing role.");
        }
    }
}
