using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Security.AccessControl;
using SitecoreMcp.Server.Transport;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>
    /// Turns a Sitecore item into the JSON the model sees. Applies field-level security, returns
    /// only populated fields by default, and truncates long values so a single item cannot flood
    /// the context window.
    /// </summary>
    public sealed class ItemProjector
    {
        private readonly McpCallContext _context;

        /// <summary>Creates a projector bound to the current call, whose user drives field-security checks.</summary>
        public ItemProjector(McpCallContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Projects an item's identity and metadata without probing for children. Use where the
        /// child count is irrelevant (e.g. ancestors), so no per-node HasChildren query is paid.
        /// </summary>
        public JObject ProjectReference(Item item) => new JObject
        {
            ["id"] = item.ID.ToString(),
            ["name"] = item.Name,
            ["path"] = item.Paths.FullPath,
            ["templateId"] = item.TemplateID.ToString(),
            ["templateName"] = item.TemplateName,
            ["language"] = item.Language.Name,
            ["version"] = item.Version.Number
        };

        /// <summary>Projects a reference plus whether the item has children, for contexts where drilling in matters.</summary>
        public JObject ProjectSummary(Item item)
        {
            var summary = ProjectReference(item);
            summary["hasChildren"] = item.HasChildren;
            return summary;
        }

        /// <summary>
        /// Projects an item with its fields. When <paramref name="requestedFields"/> is given, exactly
        /// those fields are returned (including empty and standard ones). Otherwise the default view is
        /// populated, non-standard fields, widened by <paramref name="includeStandardFields"/> (the
        /// __-prefixed standard fields) and <paramref name="includeEmpty"/> (fields with no value).
        /// </summary>
        public JObject ProjectWithFields(
            Item item,
            IReadOnlyCollection<string> requestedFields,
            bool includeStandardFields = false,
            bool includeEmpty = false)
        {
            var result = ProjectSummary(item);
            var fields = new JObject();

            item.Fields.ReadAll();
            var explicitRequest = requestedFields != null && requestedFields.Count > 0;
            var wanted = explicitRequest
                ? new HashSet<string>(requestedFields, System.StringComparer.OrdinalIgnoreCase)
                : null;

            int populated = 0, empty = 0, standard = 0;

            foreach (Field field in item.Fields)
            {
                if (!AuthorizationManager.IsAllowed(field, AccessRight.FieldRead, _context.User))
                {
                    // Omit rather than null, and don't count: a denied field's existence isn't disclosed.
                    continue;
                }

                var isStandard = field.Name.StartsWith("__");
                var hasValue = !string.IsNullOrEmpty(field.Value);

                // Census over everything readable, so the caller can orient even when nothing shows.
                if (isStandard) standard++;
                else if (hasValue) populated++;
                else empty++;

                bool include;
                if (wanted != null)
                {
                    include = wanted.Contains(field.Name) || wanted.Contains(field.Key);
                }
                else
                {
                    include = (includeStandardFields || !isStandard) && (includeEmpty || hasValue);
                }

                if (include)
                {
                    fields[field.Name] = Truncate(field.Value);
                }
            }

            result["fields"] = fields;
            result["fieldStats"] = new JObject
            {
                ["populated"] = populated,
                ["empty"] = empty,
                ["standard"] = standard
            };
            return result;
        }

        private string Truncate(string value)
        {
            var max = _context.Settings.MaxFieldLength;
            if (value == null || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max) + $"...[truncated, {value.Length} chars total]";
        }
    }
}
