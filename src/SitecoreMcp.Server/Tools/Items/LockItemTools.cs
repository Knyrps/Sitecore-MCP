using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="LockItemTool"/>.</summary>
    public sealed class LockItemArgs : ItemQueryArgs
    {
    }

    /// <summary>
    /// Explicitly locks an item for editing. This is distinct from the automatic lock a write tool
    /// takes and releases around a single edit: it holds the item until it is unlocked.
    /// </summary>
    public sealed class LockItemTool : McpTool<LockItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_lock_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Lock a Sitecore item for editing (a checkout that persists until unlocked), so no one " +
            "else can edit it. Refuses an item already locked by another user unless you are an " +
            "administrator. This is separate from the automatic lock write tools take around one edit.";

        /// <inheritdoc />
        protected override McpToolResult Execute(LockItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var locking = item.Locking;

            if (locking.HasLock())
            {
                return McpToolResult.Structured(new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["locked"] = true,
                    ["owner"] = locking.GetOwner(),
                    ["note"] = "The item was already locked by you."
                });
            }

            if (locking.IsLocked() && !context.IsAdministrator)
            {
                return McpToolResult.Failure($"Item is locked by '{locking.GetOwner()}' and cannot be locked by you.");
            }

            if (!locking.Lock())
            {
                return McpToolResult.Failure("The item could not be locked (the user may lack write access).");
            }

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["locked"] = true,
                ["owner"] = locking.GetOwner()
            });
        }
    }

    /// <summary>Arguments for <see cref="UnlockItemTool"/>.</summary>
    public sealed class UnlockItemArgs : ItemQueryArgs
    {
        /// <summary>Whether an administrator is deliberately overriding another user's lock.</summary>
        [McpParam(Description = "Override another user's lock. Only an administrator may do this, and it must be set explicitly. Default false.")]
        public bool? Force { get; set; }
    }

    /// <summary>Releases an item's lock, refusing to steal another user's lock unless an admin forces it.</summary>
    public sealed class UnlockItemTool : McpTool<UnlockItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_unlock_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Release a Sitecore item's edit lock. Unlocks your own lock freely; an item locked by " +
            "another user is only unlocked when you are an administrator and pass force=true, so a " +
            "lock is never stolen by accident. A non-locked item is a benign no-op.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UnlockItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var locking = item.Locking;

            if (!locking.IsLocked())
            {
                return McpToolResult.Structured(new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["locked"] = false,
                    ["note"] = "The item was not locked."
                });
            }

            if (!locking.HasLock())
            {
                var owner = locking.GetOwner();
                if (!context.IsAdministrator || !args.Force.GetValueOrDefault(false))
                {
                    return McpToolResult.Failure(
                        $"Item is locked by '{owner}'. Overriding another user's lock needs an administrator client and force=true.");
                }
            }

            locking.Unlock();
            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["locked"] = locking.IsLocked()
            });
        }
    }
}
