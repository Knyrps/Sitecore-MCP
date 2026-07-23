using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sitecore;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Projects a template to JSON for tool results. Two views: <see cref="Describe"/> reads the
    /// template engine (own and inherited fields, for get_template), and <see cref="DescribeStructure"/>
    /// walks the item tree directly (authoritative for what a create or update just wrote, with no
    /// dependence on template-engine cache timing).
    /// </summary>
    public static class TemplateDescriber
    {
        /// <summary>
        /// Describes a template through the template engine: its base templates and every field it
        /// defines or inherits, with type, section, and whether the field is inherited. Standard
        /// "__" fields are omitted to keep the list to meaningful content fields.
        /// </summary>
        public static JObject Describe(TemplateItem template, Item forItem)
        {
            var ownFieldIds = new HashSet<string>();
            foreach (var own in template.OwnFields)
            {
                ownFieldIds.Add(own.ID.ToString());
            }

            var fields = new JArray();
            foreach (var field in template.Fields)
            {
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

            return new JObject
            {
                ["id"] = template.ID.ToString(),
                ["name"] = template.Name,
                ["path"] = template.InnerItem.Paths.FullPath,
                ["forItem"] = forItem.Paths.FullPath,
                ["baseTemplates"] = BaseTemplates(template.BaseTemplates),
                ["fieldCount"] = fields.Count,
                ["fields"] = fields
            };
        }

        /// <summary>
        /// Describes a template from its own item tree: the sections and fields directly under it,
        /// with each field's type and flags read from the field item. Reports the resolved base
        /// templates and whether a standard-values item exists. Used to confirm a create or update.
        /// </summary>
        public static JObject DescribeStructure(Item template, IReadOnlyList<Item> baseTemplates, Item standardValues)
        {
            var sections = new JArray();
            var fieldCount = 0;

            foreach (Item sectionItem in template.Children)
            {
                if (sectionItem.TemplateID != TemplateIDs.TemplateSection)
                {
                    continue;
                }

                var fields = new JArray();
                foreach (Item fieldItem in sectionItem.Children)
                {
                    if (fieldItem.TemplateID != TemplateIDs.TemplateField)
                    {
                        continue;
                    }

                    fields.Add(new JObject
                    {
                        ["name"] = fieldItem.Name,
                        ["type"] = fieldItem["Type"],
                        ["shared"] = fieldItem["Shared"] == "1",
                        ["unversioned"] = fieldItem["Unversioned"] == "1",
                        ["source"] = string.IsNullOrEmpty(fieldItem["Source"]) ? null : fieldItem["Source"]
                    });
                    fieldCount++;
                }

                sections.Add(new JObject
                {
                    ["name"] = sectionItem.Name,
                    ["fields"] = fields
                });
            }

            return new JObject
            {
                ["id"] = template.ID.ToString(),
                ["name"] = template.Name,
                ["path"] = template.Paths.FullPath,
                ["baseTemplates"] = BaseTemplates(baseTemplates),
                ["sectionCount"] = sections.Count,
                ["fieldCount"] = fieldCount,
                ["sections"] = sections,
                ["standardValues"] = standardValues == null ? null : (JToken)standardValues.Paths.FullPath
            };
        }

        private static JArray BaseTemplates(IEnumerable<TemplateItem> templates)
        {
            var result = new JArray();
            foreach (var baseTemplate in templates)
            {
                result.Add(new JObject
                {
                    ["id"] = baseTemplate.ID.ToString(),
                    ["name"] = baseTemplate.Name,
                    ["path"] = baseTemplate.InnerItem.Paths.FullPath
                });
            }

            return result;
        }

        private static JArray BaseTemplates(IEnumerable<Item> templates)
        {
            var result = new JArray();
            foreach (var baseTemplate in templates)
            {
                result.Add(new JObject
                {
                    ["id"] = baseTemplate.ID.ToString(),
                    ["name"] = baseTemplate.Name,
                    ["path"] = baseTemplate.Paths.FullPath
                });
            }

            return result;
        }
    }
}
