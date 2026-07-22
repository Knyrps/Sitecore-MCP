using System;
using System.Collections.Generic;
using Sitecore.Configuration;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Applies field edits inside the required BeginEdit/EndEdit/CancelEdit shape, handling item
    /// locking for non-admin callers and surfacing a rejected save instead of silently dropping it.
    /// </summary>
    public static class ItemEditor
    {
        /// <summary>
        /// Runs <paramref name="mutate"/> inside an edit. Acquires a lock first when the instance
        /// requires one and the caller is not an admin, restores the prior lock state afterward, and
        /// throws <see cref="McpToolException"/> if the item is locked by someone else or the save is
        /// rejected (so a locked or workflow-blocked write never reports a silent success).
        /// </summary>
        public static void Edit(Item item, Action<Item> mutate)
        {
            var lockAcquired = EnsureEditable(item);
            try
            {
                item.Editing.BeginEdit();
                bool saved;
                try
                {
                    mutate(item);
                    saved = item.Editing.EndEdit();
                }
                catch
                {
                    item.Editing.CancelEdit();
                    throw;
                }

                if (!saved)
                {
                    throw new McpToolException(
                        "The edit was not saved. The item may require a lock, be locked by another user, " +
                        "or be blocked by its workflow.");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    RestoreUnlocked(item);
                }
            }
        }

        /// <summary>
        /// Writes a field-name/value map to an item, checking field-write permission and field
        /// existence first. Returns the names actually written. Throws <see cref="McpToolException"/>
        /// for an unknown field or one the user may not write, before any change is made.
        /// </summary>
        public static IReadOnlyList<string> WriteFields(Item item, IReadOnlyDictionary<string, string> fields, McpCallContext context)
        {
            if (fields == null || fields.Count == 0)
            {
                return new string[0];
            }

            item.Fields.ReadAll();

            // Validate every field up front so a bad field in the set changes nothing.
            foreach (var name in fields.Keys)
            {
                var field = item.Fields[name];
                if (field == null)
                {
                    throw new McpToolException($"Field '{name}' does not exist on template '{item.TemplateName}'.");
                }

                if (!AuthorizationManager.IsAllowed(field, AccessRight.FieldWrite, context.User))
                {
                    throw new McpToolException($"Not permitted to write field '{name}'.");
                }
            }

            var written = new List<string>();
            Edit(item, editable =>
            {
                foreach (var pair in fields)
                {
                    editable.Fields[pair.Key].Value = pair.Value ?? string.Empty;
                    written.Add(pair.Key);
                }
            });

            return written;
        }

        /// <summary>
        /// Ensures the current user can edit the item, locking it when the instance requires a lock
        /// and the caller is not an admin. Returns true when this call acquired the lock, so the
        /// caller can release it afterward. Throws when another user holds the lock.
        /// </summary>
        private static bool EnsureEditable(Item item)
        {
            var locking = item.Locking;

            if (locking.HasLock())
            {
                return false;
            }

            if (locking.IsLocked())
            {
                throw new McpToolException(
                    $"Item is locked by '{locking.GetOwner()}'. It must be unlocked or checked in by that user first.");
            }

            // Admins bypass the lock requirement; otherwise honor RequireLockBeforeEditing.
            if (Sitecore.Context.User.IsAdministrator || !Settings.RequireLockBeforeEditing)
            {
                return false;
            }

            if (!locking.CanLock() || !locking.Lock())
            {
                throw new McpToolException("The item requires a lock before editing and could not be locked for the current user.");
            }

            return true;
        }

        private static void RestoreUnlocked(Item item)
        {
            // Best-effort: only unlock if we still hold it (AutomaticUnlockOnSaved may have released
            // it already), and never let a restore failure mask the edit outcome.
            try
            {
                if (item.Locking.HasLock())
                {
                    item.Locking.Unlock();
                }
            }
            catch
            {
            }
        }
    }
}
