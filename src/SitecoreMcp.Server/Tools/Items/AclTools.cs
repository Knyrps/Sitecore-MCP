using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;
using Sitecore.Security.Accounts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Membership;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Shared resolution and projection for the item access-rule tools.</summary>
    internal static class AclHelper
    {
        /// <summary>Resolves a name to a user or role account, throwing when it is neither.</summary>
        public static Account ResolveAccount(string name)
        {
            var qualified = MembershipResolver.Qualify(name);
            if (User.Exists(qualified)) return User.FromName(qualified, false);
            if (Role.Exists(qualified)) return Role.FromName(qualified);
            throw new McpToolException($"'{qualified}' is neither an existing user nor an existing role.");
        }

        /// <summary>Resolves a friendly or raw access-right name to an <see cref="AccessRight"/>.</summary>
        public static AccessRight ResolveRight(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new McpToolException("No access right was specified.");
            }

            switch (name.Trim().ToLowerInvariant())
            {
                case "read": return AccessRight.ItemRead;
                case "write": return AccessRight.ItemWrite;
                case "create": return AccessRight.ItemCreate;
                case "delete": return AccessRight.ItemDelete;
                case "rename": return AccessRight.ItemRename;
                case "admin": return AccessRight.ItemAdmin;
                case "fieldread": return AccessRight.FieldRead;
                case "fieldwrite": return AccessRight.FieldWrite;
                case "any": case "*": return AccessRight.Any;
            }

            var right = AccessRight.FromName(name);
            if (right == null)
            {
                throw new McpToolException(
                    $"Unknown access right '{name}'. Use read, write, create, delete, rename, admin, fieldRead, fieldWrite, or a raw right name.");
            }

            return right;
        }

        /// <summary>Resolves 'allow' or 'deny' to an <see cref="AccessPermission"/>, defaulting to allow.</summary>
        public static AccessPermission ResolvePermission(string name)
        {
            switch ((name ?? "allow").Trim().ToLowerInvariant())
            {
                case "allow": return AccessPermission.Allow;
                case "deny": return AccessPermission.Deny;
            }

            throw new McpToolException("permission must be 'allow' or 'deny'.");
        }

        /// <summary>Resolves how a rule propagates, defaulting to the entity itself.</summary>
        public static PropagationType ResolvePropagation(string name)
        {
            switch ((name ?? "entity").Trim().ToLowerInvariant())
            {
                case "entity": return PropagationType.Entity;
                case "descendants": return PropagationType.Descendants;
                case "any": return PropagationType.Any;
            }

            throw new McpToolException("propagation must be 'entity', 'descendants', or 'any'.");
        }

        /// <summary>Reads the item's local access rules, mutates them, and writes them back.</summary>
        public static AccessRuleCollection Apply(Item item, Action<AccessRuleCollection> mutate)
        {
            var rules = AuthorizationManager.GetAccessRules(item) ?? new AccessRuleCollection();
            mutate(rules);
            AuthorizationManager.SetAccessRules(item, rules);
            return rules;
        }

        /// <summary>Projects the local access rules to JSON.</summary>
        public static JArray Describe(AccessRuleCollection rules)
        {
            if (rules == null)
            {
                return new JArray();
            }

            return new JArray(rules.Select(rule => (object)new JObject
            {
                ["account"] = rule.Account?.Name,
                ["right"] = rule.AccessRight?.Name,
                ["permission"] = rule.SecurityPermission.ToString(),
                ["propagation"] = rule.PropagationType.ToString()
            }).ToArray());
        }

        /// <summary>Builds the standard rules result for a write tool.</summary>
        public static McpToolResult RulesResult(Item item, AccessRuleCollection rules) =>
            McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["rules"] = Describe(rules)
            });
    }

    /// <summary>Arguments for <see cref="TestItemAclTool"/>.</summary>
    public sealed class TestItemAclArgs : ItemQueryArgs
    {
        /// <summary>The user or role to test.</summary>
        [McpParam(Description = "User or role name to test (domain\\name, or a bare name for the sitecore domain).", Required = true)]
        public string Account { get; set; }

        /// <summary>The access right to test.</summary>
        [McpParam(Description = "Access right: read, write, create, delete, rename, admin, fieldRead, fieldWrite (or a raw right name).", Required = true)]
        public string Right { get; set; }
    }

    /// <summary>Tests whether an account has an access right on an item, resolving inheritance and denies.</summary>
    public sealed class TestItemAclTool : McpTool<TestItemAclArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_test_item_acl";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Test whether a user or role has an access right (read, write, delete, ...) on an item, " +
            "resolving inherited rules and explicit denies. Answers 'why can account X not do Y here'. " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(TestItemAclArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var account = AclHelper.ResolveAccount(args.Account);
            var right = AclHelper.ResolveRight(args.Right);

            var allowed = AuthorizationManager.IsAllowed(item, right, account);
            var access = AuthorizationManager.GetAccess(item, account, right);

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["account"] = account.Name,
                ["right"] = right.Name,
                ["allowed"] = allowed,
                ["permission"] = access?.Permission.ToString()
            });
        }
    }

    /// <summary>Arguments for <see cref="AddItemAclTool"/> and <see cref="SetItemAclTool"/>.</summary>
    public sealed class WriteItemAclArgs : ItemQueryArgs
    {
        /// <summary>The user or role the rule applies to.</summary>
        [McpParam(Description = "User or role the rule applies to.", Required = true)]
        public string Account { get; set; }

        /// <summary>The access right the rule grants or denies.</summary>
        [McpParam(Description = "Access right: read, write, create, delete, rename, admin, fieldRead, fieldWrite (or a raw right name).", Required = true)]
        public string Right { get; set; }

        /// <summary>Whether the rule allows or denies the right.</summary>
        [McpParam(Description = "Whether the rule allows or denies the right. Default allow.", Enum = new[] { "allow", "deny" })]
        public string Permission { get; set; }

        /// <summary>How far the rule propagates.</summary>
        [McpParam(Description = "How far the rule applies: entity (this item, default), descendants, or any (both).", Enum = new[] { "entity", "descendants", "any" })]
        public string Propagation { get; set; }
    }

    /// <summary>Appends an access rule to an item.</summary>
    public sealed class AddItemAclTool : McpTool<WriteItemAclArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_add_item_acl";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Add an access rule to an item: allow or deny a right for a user or role, propagating to " +
            "the item, its descendants, or both. Appends to the existing rules. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(WriteItemAclArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var account = AclHelper.ResolveAccount(args.Account);
            var right = AclHelper.ResolveRight(args.Right);
            var permission = AclHelper.ResolvePermission(args.Permission);
            var propagation = AclHelper.ResolvePropagation(args.Propagation);

            var rules = AclHelper.Apply(item, current => current.Add(AccessRule.Create(account, right, propagation, permission)));
            return AclHelper.RulesResult(item, rules);
        }
    }

    /// <summary>Sets an account's rule for a right, replacing any existing rule for the same pair.</summary>
    public sealed class SetItemAclTool : McpTool<WriteItemAclArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_set_item_acl";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Set an account's rule for a right on an item, replacing any existing rule for that same " +
            "account and right (so allow becomes deny cleanly, without stacking). Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(WriteItemAclArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var account = AclHelper.ResolveAccount(args.Account);
            var right = AclHelper.ResolveRight(args.Right);
            var permission = AclHelper.ResolvePermission(args.Permission);
            var propagation = AclHelper.ResolvePropagation(args.Propagation);

            var rules = AclHelper.Apply(item, current =>
            {
                current.RemoveAll(rule =>
                    string.Equals(rule.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.AccessRight?.Name, right.Name, StringComparison.Ordinal));
                current.Add(AccessRule.Create(account, right, propagation, permission));
            });

            return AclHelper.RulesResult(item, rules);
        }
    }

    /// <summary>Arguments for <see cref="ClearItemAclTool"/>.</summary>
    public sealed class ClearItemAclArgs : ItemQueryArgs
    {
        /// <summary>The account whose rules to clear; omit to clear all local rules.</summary>
        [McpParam(Description = "Clear only this account's rules. Omit to clear every local access rule on the item (reverting to inherited security).")]
        public string Account { get; set; }
    }

    /// <summary>Removes an item's local access rules, all of them or for one account.</summary>
    public sealed class ClearItemAclTool : McpTool<ClearItemAclArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_clear_item_acl";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove an item's local access rules - all of them, or just one account's when 'account' " +
            "is given - so the item reverts to inherited security. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ClearItemAclArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            Account account = null;
            if (!string.IsNullOrWhiteSpace(args.Account))
            {
                account = AclHelper.ResolveAccount(args.Account);
            }

            var rules = AclHelper.Apply(item, current =>
            {
                if (account == null)
                {
                    current.Clear();
                }
                else
                {
                    current.RemoveAll(rule =>
                        string.Equals(rule.Account?.Name, account.Name, StringComparison.OrdinalIgnoreCase));
                }
            });

            return AclHelper.RulesResult(item, rules);
        }
    }
}
