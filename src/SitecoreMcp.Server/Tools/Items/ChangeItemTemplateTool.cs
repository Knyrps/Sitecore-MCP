using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Templates;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="ChangeItemTemplateTool"/>.</summary>
    public sealed class ChangeItemTemplateArgs : ItemQueryArgs
    {
        /// <summary>The template to change the item to, by path, ID, or exact name.</summary>
        [McpParam(Description = "The new template, by path, ID, or exact name (no partial-name matching on writes).", Required = true)]
        public string NewTemplate { get; set; }
    }

    /// <summary>
    /// Changes an item's template, with a before/after field diff so any value the change drops is
    /// reported rather than silently lost. Sitecore carries values across by matching field names;
    /// a field the new template does not define keeps no visible value.
    /// </summary>
    public sealed class ChangeItemTemplateTool : McpTool<ChangeItemTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_change_item_template";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Change a Sitecore item's template. Field values carry over where the new template defines " +
            "a field of the same name; anything else is dropped by Sitecore. The result diffs every " +
            "populated content field before and after: 'preserved' kept its value, 'changed' survived " +
            "with a different value, and 'dataLost' names each dropped field WITH its old value, so " +
            "nothing disappears silently. Consider sitecore_get_item_referrers first if other items " +
            "depend on this one's fields.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ChangeItemTemplateArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var newTemplate = TemplateResolver.Resolve(item.Database, args.NewTemplate, allowPartial: false);

            if (item.TemplateID == newTemplate.ID)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["note"] = $"The item already uses template '{newTemplate.Name}'; nothing to change."
                });
            }

            var oldTemplateName = item.TemplateName;
            var before = PopulatedContentFields(item);

            item.ChangeTemplate(new TemplateItem(newTemplate));

            // Diff against a fresh read so the report reflects what actually persisted.
            var fresh = item.Database.GetItem(item.ID, item.Language, item.Version);
            var after = fresh == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : PopulatedContentFields(fresh);

            var preserved = new JArray();
            var changed = new JArray();
            var dataLost = new JArray();

            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var newValue) || string.IsNullOrEmpty(newValue))
                {
                    dataLost.Add(new JObject { ["field"] = pair.Key, ["value"] = pair.Value });
                }
                else if (string.Equals(newValue, pair.Value, StringComparison.Ordinal))
                {
                    preserved.Add(pair.Key);
                }
                else
                {
                    changed.Add(new JObject { ["field"] = pair.Key, ["oldValue"] = pair.Value, ["newValue"] = newValue });
                }
            }

            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["oldTemplate"] = oldTemplateName,
                ["newTemplate"] = newTemplate.Name,
                ["preserved"] = preserved,
                ["changed"] = changed,
                ["dataLost"] = dataLost
            };

            if (dataLost.Count > 0)
            {
                result["warning"] = $"{dataLost.Count} field value(s) were dropped because the new template " +
                                    "does not define those fields. Their old values are listed in dataLost.";
            }

            return McpToolResult.Structured(result);
        }

        private static Dictionary<string, string> PopulatedContentFields(Item item)
        {
            item.Fields.ReadAll();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Field field in item.Fields)
            {
                if (!field.Name.StartsWith("__") && !string.IsNullOrEmpty(field.Value))
                {
                    fields[field.Name] = field.Value;
                }
            }

            return fields;
        }
    }
}
