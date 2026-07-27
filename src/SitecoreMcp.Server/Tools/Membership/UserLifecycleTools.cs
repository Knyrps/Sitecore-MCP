using Newtonsoft.Json.Linq;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="NewUserTool"/>.</summary>
    public sealed class NewUserArgs
    {
        /// <summary>The name of the user to create.</summary>
        [McpParam(Description = "User name (domain\\name, or a bare name for the sitecore domain).", Required = true)]
        public string Name { get; set; }

        /// <summary>The initial password for the account.</summary>
        [McpParam(Description = "Initial password for the account.", Required = true)]
        public string Password { get; set; }

        /// <summary>An email address for the profile.</summary>
        [McpParam(Description = "Email address for the profile. Optional.")]
        public string Email { get; set; }

        /// <summary>A display/full name for the profile.</summary>
        [McpParam(Description = "Full name for the profile. Optional.")]
        public string FullName { get; set; }

        /// <summary>Whether the user is a Sitecore administrator.</summary>
        [McpParam(Description = "Make the user a Sitecore administrator. Default false.")]
        public bool? IsAdministrator { get; set; }
    }

    /// <summary>Creates a Sitecore user with an initial password and optional profile.</summary>
    public sealed class NewUserTool : McpTool<NewUserArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_new_user";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Create a Sitecore user with an initial password, and optionally an email, full name, and " +
            "administrator flag. Fails if the user already exists. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(NewUserArgs args, McpCallContext context)
        {
            var name = MembershipResolver.Qualify(args.Name);
            if (User.Exists(name))
            {
                return McpToolResult.Failure($"User '{name}' already exists.");
            }

            var user = User.Create(name, args.Password);

            try
            {
                var profile = user.Profile;
                if (!string.IsNullOrEmpty(args.Email)) profile.Email = args.Email;
                if (!string.IsNullOrEmpty(args.FullName)) profile.FullName = args.FullName;
                if (args.IsAdministrator.GetValueOrDefault(false)) profile.IsAdministrator = true;
                profile.Save();
            }
            catch
            {
                // The account exists even if the profile could not be fully written; the describe below
                // will show what actually stuck.
            }

            return McpToolResult.Structured(MembershipDescriber.User(User.FromName(name, false), true));
        }
    }

    /// <summary>Arguments for <see cref="RemoveUserTool"/>.</summary>
    public sealed class RemoveUserArgs
    {
        /// <summary>The name of the user to delete.</summary>
        [McpParam(Description = "User name to delete (domain\\name, or a bare name for the sitecore domain).", Required = true)]
        public string Name { get; set; }
    }

    /// <summary>Deletes a Sitecore user, refusing the built-in administrator and the calling user.</summary>
    public sealed class RemoveUserTool : McpTool<RemoveUserArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_user";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Delete a Sitecore user. Refuses to delete the built-in administrator or the user this " +
            "client runs as. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RemoveUserArgs args, McpCallContext context)
        {
            var name = MembershipResolver.Qualify(args.Name);

            if (name.Equals(context.User?.Name, System.StringComparison.OrdinalIgnoreCase))
            {
                return McpToolResult.Failure("Refusing to delete the user this client runs as.");
            }

            if (name.Equals("sitecore\\Admin", System.StringComparison.OrdinalIgnoreCase))
            {
                return McpToolResult.Failure("Refusing to delete the built-in administrator.");
            }

            if (!User.Exists(name))
            {
                return McpToolResult.Failure($"User '{name}' does not exist.");
            }

            User.FromName(name, false).Delete();
            return McpToolResult.Structured(new JObject { ["user"] = name, ["deleted"] = true });
        }
    }
}
