using System;
using System.Collections.Generic;
using Sitecore.Configuration;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>The outcome of a field write: which fields were saved, and which did not actually persist.</summary>
    public sealed class FieldWriteResult
    {
        /// <summary>A result with nothing written and nothing dropped.</summary>
        public static readonly FieldWriteResult Empty = new FieldWriteResult(new string[0], new string[0]);

        /// <summary>Creates a result from the written and non-persisted field name lists.</summary>
        public FieldWriteResult(IReadOnlyList<string> written, IReadOnlyList<string> notPersisted)
        {
            Written = written;
            NotPersisted = notPersisted;
        }

        /// <summary>The fields whose values were changed and saved.</summary>
        public IReadOnlyList<string> Written { get; }

        /// <summary>The fields that were written but read back with their previous value (the save was dropped).</summary>
        public IReadOnlyList<string> NotPersisted { get; }
    }

    /// <summary>
    /// Applies field edits inside the required BeginEdit/EndEdit/CancelEdit shape, handling item
    /// locking for non-admin callers and surfacing a rejected save instead of silently dropping it.
    /// </summary>
    public static class ItemEditor
    {
        private struct FieldChange
        {
            public FieldChange(string name, string newValue, string oldValue)
            {
                Name = name;
                NewValue = newValue;
                OldValue = oldValue;
            }

            public string Name { get; }
            public string NewValue { get; }
            public string OldValue { get; }
        }

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
                    var locking = item.Locking;
                    if (locking.IsLocked() && !locking.HasLock())
                    {
                        throw new McpToolException(
                            $"The edit was not saved: the item is locked by '{locking.GetOwner()}'.");
                    }

                    throw new McpToolException(
                        "The edit was not saved. The item may be blocked by its workflow, or require a lock the user does not hold.");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    // We took the lock, so always try to release it (harmless if AutomaticUnlockOnSaved
                    // already did); never gate this on HasLock, which can misreport our own lock.
                    try { item.Locking.Unlock(); } catch { }
                }
            }
        }

        /// <summary>
        /// Writes a field-name/value map to an item, checking field-write permission and field
        /// existence first, then verifying the values actually persisted. Throws
        /// <see cref="McpToolException"/> for an unknown field or one the user may not write, before
        /// any change is made.
        /// </summary>
        public static FieldWriteResult WriteFields(Item item, IReadOnlyDictionary<string, string> fields, McpCallContext context)
        {
            if (fields == null || fields.Count == 0)
            {
                return FieldWriteResult.Empty;
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

            // Only write fields whose value actually differs, keeping the value each held before the
            // write. Writing a field to its current value is a no-op EndEdit reports as "nothing saved",
            // which is not a failure.
            var changes = new List<FieldChange>();
            foreach (var pair in fields)
            {
                var current = item.Fields[pair.Key].Value ?? string.Empty;
                var target = pair.Value ?? string.Empty;
                if (!string.Equals(current, target, StringComparison.Ordinal))
                {
                    changes.Add(new FieldChange(pair.Key, target, current));
                }
            }

            if (changes.Count == 0)
            {
                return FieldWriteResult.Empty;
            }

            var written = new List<string>();
            Edit(item, editable =>
            {
                foreach (var change in changes)
                {
                    editable.Fields[change.Name].Value = change.NewValue;
                    written.Add(change.Name);
                }
            });

            // Verify persistence with a fresh read. A field "did not persist" when its saved value is
            // still exactly what it was before (the change was silently dropped, e.g. by field security
            // or a save handler) rather than merely server-normalized (which differs from the old value).
            var notPersisted = new List<string>();
            var fresh = item.Database.GetItem(item.ID, item.Language, item.Version);
            if (fresh != null)
            {
                fresh.Fields.ReadAll();
                foreach (var change in changes)
                {
                    var actual = fresh.Fields[change.Name]?.Value ?? string.Empty;
                    if (string.Equals(actual, change.OldValue, StringComparison.Ordinal) &&
                        !string.Equals(actual, change.NewValue, StringComparison.Ordinal))
                    {
                        notPersisted.Add(change.Name);
                    }
                }
            }

            return new FieldWriteResult(written, notPersisted);
        }

        /// <summary>
        /// Ensures the current user can edit the item, locking it when the instance requires a lock
        /// and the caller is not an admin. Returns true when this call acquired the lock, so the
        /// caller can release it afterward. Throws when another user holds the lock.
        /// </summary>
        private static bool EnsureEditable(Item item)
        {
            // Admins bypass the lock requirement, and when the instance does not require a lock we let
            // EndEdit (and AutomaticLockOnSave, if set) handle it. No proactive locking in either case.
            if (Sitecore.Context.User.IsAdministrator || !Settings.RequireLockBeforeEditing)
            {
                return false;
            }

            var locking = item.Locking;

            if (locking.HasLock())
            {
                return false;
            }

            // Lock() is the authoritative test: it succeeds for an unlocked item (and re-taking our own
            // lock), and only fails when the item genuinely cannot be locked. Do not pre-judge on
            // IsLocked, which can disagree with what Lock() will actually allow.
            if (locking.Lock())
            {
                return true;
            }

            if (locking.IsLocked())
            {
                throw new McpToolException(
                    $"Item is locked by '{locking.GetOwner()}' and cannot be edited until it is released.");
            }

            throw new McpToolException("The item could not be locked for editing (the user may lack write access).");
        }
    }
}
