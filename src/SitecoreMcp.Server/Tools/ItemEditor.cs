using System;
using System.Collections.Generic;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Applies field edits inside the required BeginEdit/EndEdit/CancelEdit shape, so a failure part
    /// way through never leaves an item stuck in an open editing state.
    /// </summary>
    public static class ItemEditor
    {
        /// <summary>Runs <paramref name="mutate"/> inside an edit, cancelling and rethrowing on any failure.</summary>
        public static void Edit(Item item, Action<Item> mutate)
        {
            item.Editing.BeginEdit();
            try
            {
                mutate(item);
                item.Editing.EndEdit();
            }
            catch
            {
                item.Editing.CancelEdit();
                throw;
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
    }
}
