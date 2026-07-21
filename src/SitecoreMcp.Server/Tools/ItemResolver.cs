using Sitecore.Data;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>Resolves items from a path or ID against a call's permitted database and language.</summary>
    public static class ItemResolver
    {
        /// <summary>
        /// Resolves an item by path or ID, defaulting to the master database. Throws
        /// <see cref="McpToolException"/> when the database is disallowed or the item is not found,
        /// so the failure reaches the model rather than surfacing as null.
        /// </summary>
        public static Item Resolve(McpCallContext context, string pathOrId, string database, string language)
        {
            var db = context.ResolveDatabase(string.IsNullOrEmpty(database) ? "master" : database);
            var lang = context.ResolveLanguage(language);

            var item = ID.IsID(pathOrId)
                ? db.GetItem(ID.Parse(pathOrId), lang)
                : db.GetItem(pathOrId, lang);

            if (item == null)
            {
                throw new McpToolException($"No item found at '{pathOrId}' in database '{db.Name}' ({lang.Name}).");
            }

            return item;
        }
    }
}
