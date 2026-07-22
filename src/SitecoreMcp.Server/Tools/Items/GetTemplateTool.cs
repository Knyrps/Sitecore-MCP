using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetTemplateTool"/>.</summary>
    public sealed class GetTemplateArgs : ItemQueryArgs
    {
    }

    /// <summary>
    /// Returns the template behind an item (or a template itself): its fields, own and inherited,
    /// with type and section. This is how a caller discovers valid field names before reading or
    /// writing, instead of guessing them.
    /// </summary>
    public sealed class GetTemplateTool : McpTool<GetTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_template";

        /// <inheritdoc />
        public override string Description =>
            "Describe the template of a Sitecore item (or of a template item directly): its base " +
            "templates and every field it defines or inherits, with field type and section. Use this " +
            "to learn valid field names before reading specific fields or creating or updating items.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetTemplateArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            // If the resolved item is itself a template definition, describe it; otherwise describe
            // the template the item is based on.
            var template = item.TemplateID == Sitecore.TemplateIDs.Template
                ? new TemplateItem(item)
                : item.Template;

            var ownFieldIds = new HashSet<string>();
            foreach (var own in template.OwnFields)
            {
                ownFieldIds.Add(own.ID.ToString());
            }

            var fields = new JArray();
            foreach (var field in template.Fields)
            {
                // Skip the standard template's own __ fields to keep the list to meaningful content
                // fields; they are numerous and rarely what the caller is after.
                if (field.Name.StartsWith("__"))
                {
                    continue;
                }

                fields.Add(new JObject
                {
                    ["name"] = field.Name,
                    ["type"] = field.Type,
                    ["section"] = field.Section?.Name,
                    ["shared"] = field.IsShared,
                    ["unversioned"] = field.IsUnversioned,
                    ["source"] = string.IsNullOrEmpty(field.Source) ? null : field.Source,
                    ["inherited"] = !ownFieldIds.Contains(field.ID.ToString())
                });
            }

            var baseTemplates = new JArray();
            foreach (var baseTemplate in template.BaseTemplates)
            {
                baseTemplates.Add(new JObject
                {
                    ["id"] = baseTemplate.ID.ToString(),
                    ["name"] = baseTemplate.Name,
                    ["path"] = baseTemplate.InnerItem.Paths.FullPath
                });
            }

            return McpToolResult.Structured(new JObject
            {
                ["id"] = template.ID.ToString(),
                ["name"] = template.Name,
                ["path"] = template.InnerItem.Paths.FullPath,
                ["forItem"] = item.Paths.FullPath,
                ["baseTemplates"] = baseTemplates,
                ["fieldCount"] = fields.Count,
                ["fields"] = fields
            });
        }
    }
}
