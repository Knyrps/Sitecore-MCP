using Sitecore.Data;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools.Items
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
            var db = context.ResolveDatabase(database);
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

        /// <summary>
        /// Resolves an item the call is about to change, refusing it (before anything changes) when it
        /// lies outside every content root. Use for the target of every write, and for the destination
        /// a write puts an item under.
        /// </summary>
        public static Item ResolveWritable(McpCallContext context, string pathOrId, string database, string language)
        {
            var item = Resolve(context, pathOrId, database, language);
            context.EnsureWritable(item);
            return item;
        }
    }
}
