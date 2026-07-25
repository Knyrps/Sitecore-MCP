using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Fields;
using Sitecore.Security.AccessControl;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="ResetItemFieldsTool"/>.</summary>
    public sealed class ResetItemFieldsArgs : ItemQueryArgs
    {
        /// <summary>The fields to reset; omit to reset every locally-set content field.</summary>
        [McpParam(Description = "Field names to reset to their template's standard-values inheritance. Omit to reset every locally-set content field (skips __-prefixed standard fields, which you must name explicitly).")]
        public string[] Fields { get; set; }
    }

    /// <summary>
    /// Resets fields to their template's standard-values inheritance, discarding the item's own
    /// values. This is the un-override that update_item cannot do: update writes a value, while reset
    /// removes the local value so the field inherits again.
    /// </summary>
    public sealed class ResetItemFieldsTool : McpTool<ResetItemFieldsArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_reset_item_fields";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Reset an item's fields to their template's standard-values inheritance, discarding the " +
            "item's own values (the opposite of update_item, which sets a value). Name the fields, or " +
            "omit them to reset every locally-set content field. A named field that is already " +
            "inherited is left alone. After saving it verifies each reset actually took: a field that " +
            "reads back with a local value (re-applied by a save handler) is listed in notPersisted.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ResetItemFieldsArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            item.Fields.ReadAll();

            var targets = args.Fields != null && args.Fields.Length > 0
                ? Named(item, args.Fields, context)
                : AllLocallySet(item, context);

            // Only fields that actually hold a local value need resetting; a named-but-inherited field
            // is a benign no-op, and entering an edit that changes nothing would fail to save.
            var toReset = targets.Where(HasLocalValue).Select(f => f.Name).ToList();
            var alreadyInherited = targets.Where(f => !HasLocalValue(f)).Select(f => f.Name).ToList();

            if (toReset.Count == 0)
            {
                var noop = new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["reset"] = new JArray(),
                    ["hint"] = args.Fields != null && args.Fields.Length > 0
                        ? "None of the named fields hold a local value; they already inherit from standard values."
                        : "No locally-set content fields to reset."
                };
                if (alreadyInherited.Count > 0)
                {
                    noop["alreadyInherited"] = new JArray(alreadyInherited.Cast<object>().ToArray());
                }

                return McpToolResult.Structured(noop);
            }

            ItemEditor.Edit(item, editable =>
            {
                foreach (var name in toReset)
                {
                    editable.Fields[name].Reset();
                }
            });

            // Verify with a fresh read: a field that still holds a local value did not actually reset.
            var notPersisted = new List<string>();
            var fresh = item.Database.GetItem(item.ID, item.Language, item.Version);
            if (fresh != null)
            {
                fresh.Fields.ReadAll();
                foreach (var name in toReset)
                {
                    if (HasLocalValue(fresh.Fields[name]))
                    {
                        notPersisted.Add(name);
                    }
                }
            }

            var reset = toReset.Where(name => !notPersisted.Contains(name)).ToList();
            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["reset"] = new JArray(reset.Cast<object>().ToArray())
            };
            if (alreadyInherited.Count > 0)
            {
                result["alreadyInherited"] = new JArray(alreadyInherited.Cast<object>().ToArray());
            }
            if (notPersisted.Count > 0)
            {
                result["notPersisted"] = new JArray(notPersisted.Cast<object>().ToArray());
                result["warning"] = "These fields still hold a local value after reset, so the reset did " +
                                    "not persist (likely re-applied by a save handler): " + string.Join(", ", notPersisted) + ".";
            }

            return McpToolResult.Structured(result);
        }

        /// <summary>Resolves and validates explicitly named fields, rejecting an unknown or unwritable one before anything changes.</summary>
        private static List<Field> Named(Sitecore.Data.Items.Item item, IEnumerable<string> names, McpCallContext context)
        {
            var fields = new List<Field>();
            foreach (var name in names)
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

                fields.Add(field);
            }

            return fields;
        }

        /// <summary>
        /// Collects every locally-set, writable content field. Standard (__-prefixed) fields are
        /// skipped, since a blanket reset of presentation, workflow, or security fields would be a
        /// footgun; name those explicitly to reset them.
        /// </summary>
        private static List<Field> AllLocallySet(Sitecore.Data.Items.Item item, McpCallContext context)
        {
            var fields = new List<Field>();
            foreach (Field field in item.Fields)
            {
                if (field.Name.StartsWith("__") || !HasLocalValue(field))
                {
                    continue;
                }

                if (AuthorizationManager.IsAllowed(field, AccessRight.FieldWrite, context.User))
                {
                    fields.Add(field);
                }
            }

            return fields;
        }

        // A field is locally set when it has a stored value of its own, i.e. one that does not fall
        // back to standard values.
        private static bool HasLocalValue(Field field) =>
            field != null && !string.IsNullOrEmpty(field.GetValue(false, false));
    }
}
