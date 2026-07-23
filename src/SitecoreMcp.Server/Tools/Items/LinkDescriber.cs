using Newtonsoft.Json.Linq;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Links;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>
    /// Projects Link Database entries to JSON. A link records an ID and a path captured when the link
    /// was indexed, so the item on the other end is resolved live where possible and the recorded
    /// path reported when it is not.
    /// </summary>
    public static class LinkDescriber
    {
        /// <summary>
        /// Describes an outgoing link: the field on this item that points somewhere, and where it
        /// points. <c>resolved</c> is false when the target cannot be read - which covers both a
        /// deleted target and one outside the caller's reach, since the two are indistinguishable here.
        /// </summary>
        public static JObject Reference(ItemLink link, Item source, McpCallContext context)
        {
            var target = Resolve(link.TargetDatabaseName, link.TargetItemID, context);

            return new JObject
            {
                ["sourceField"] = FieldName(source, link.SourceFieldID),
                ["targetId"] = link.TargetItemID.ToString(),
                ["targetPath"] = target?.Paths.FullPath ?? NullIfEmpty(link.TargetPath),
                ["targetDatabase"] = link.TargetDatabaseName,
                ["resolved"] = target != null
            };
        }

        /// <summary>
        /// Describes an incoming link: which item points at this one, and through which field. Returns
        /// null when the source cannot be read, so the caller can count it without disclosing it.
        /// </summary>
        public static JObject Referrer(ItemLink link, McpCallContext context)
        {
            var source = Resolve(link.SourceDatabaseName, link.SourceItemID, context);
            if (source == null)
            {
                return null;
            }

            return new JObject
            {
                ["sourceId"] = link.SourceItemID.ToString(),
                ["sourceName"] = source.Name,
                ["sourcePath"] = source.Paths.FullPath,
                ["sourceDatabase"] = link.SourceDatabaseName,
                ["sourceField"] = FieldName(source, link.SourceFieldID),
                ["sourceLanguage"] = link.SourceItemLanguage?.Name
            };
        }

        /// <summary>
        /// Resolves an item named by a link, honouring the client's permitted databases. Returns null
        /// when the database is not permitted, does not exist, or the item cannot be read.
        /// </summary>
        public static Item Resolve(string databaseName, ID itemId, McpCallContext context)
        {
            if (string.IsNullOrEmpty(databaseName) || ID.IsNullOrEmpty(itemId) ||
                !context.Client.CanUseDatabase(databaseName))
            {
                return null;
            }

            var database = Factory.GetDatabase(databaseName, false);
            return database?.GetItem(itemId);
        }

        private static string FieldName(Item item, ID fieldId)
        {
            if (ID.IsNullOrEmpty(fieldId))
            {
                return null;
            }

            // The field can be gone if the template changed since the link was indexed; the raw ID is
            // still the honest answer in that case.
            var field = item.Fields[fieldId];
            return string.IsNullOrEmpty(field?.Name) ? fieldId.ToString() : field.Name;
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
