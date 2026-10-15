using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Sitecore-typed shortcuts over <see cref="McpCallContext.Scope"/>, so every tool applies the
    /// content-root and hidden-field rules the same way. The rules themselves live in the Sitecore-free
    /// <see cref="Security.ContentScope"/>.
    /// </summary>
    public static class ContentScopeExtensions
    {
        /// <summary>Refuses the write when <paramref name="item"/> is outside every content root.</summary>
        public static void EnsureWritable(this McpCallContext context, Item item) =>
            context.Scope.EnsureWritable(item.Paths.FullPath);

        /// <summary>Refuses the write when <paramref name="path"/> (e.g. an item about to be created) is outside every content root.</summary>
        public static void EnsureWritablePath(this McpCallContext context, string path) =>
            context.Scope.EnsureWritable(path);

        /// <summary>Whether the field is hidden: to the caller it does not exist.</summary>
        public static bool IsHidden(this McpCallContext context, Field field) =>
            field != null && context.Scope.IsHiddenField(field.Name, field.ID.ToString());

        /// <summary>Whether the template field is hidden.</summary>
        public static bool IsHidden(this McpCallContext context, TemplateFieldItem field) =>
            field != null && context.Scope.IsHiddenField(field.Name, field.ID.ToString());

        /// <summary>
        /// Finds a field by name or ID, returning null when it does not exist or is hidden.
        /// </summary>
        public static Field FindVisibleField(this McpCallContext context, Item item, string nameOrId) =>
            context.Scope.FindVisibleField(nameOrId, key => item.Fields[key], f => f.Name, f => f.ID.ToString());

        /// <summary>
        /// Finds a field by name or ID, throwing the same "field not found" error for a hidden field as
        /// for one the template does not define.
        /// </summary>
        public static Field RequireVisibleField(this McpCallContext context, Item item, string nameOrId) =>
            context.Scope.RequireVisibleField(nameOrId, key => item.Fields[key], f => f.Name, f => f.ID.ToString(), item.TemplateName);

        /// <summary>The item's fields minus hidden ones. Call item.Fields.ReadAll() first for the full set.</summary>
        public static IEnumerable<Field> VisibleFields(this McpCallContext context, Item item) =>
            context.Scope.VisibleFields(item.Fields.Cast<Field>(), f => f.Name, f => f.ID.ToString());

        /// <summary>
        /// Whether a link recorded against <paramref name="fieldId"/> in <paramref name="databaseName"/>
        /// comes from a hidden field. Resolves the field definition item to learn its name, so a field
        /// hidden by name is caught too; definitions are cached per call context.
        /// </summary>
        public static bool IsHiddenLinkField(this McpCallContext context, string databaseName, ID fieldId)
        {
            if (!context.Scope.HasHiddenFields || ID.IsNullOrEmpty(fieldId)) return false;
            if (context.Scope.IsHiddenField(null, fieldId.ToString())) return true;

            var key = (databaseName ?? string.Empty) + "|" + fieldId;
            return LinkFieldCache(context).GetOrAdd(key, _ =>
            {
                // Read as the calling user (SecurityDisabler is never used). A definition the user
                // cannot read only matters for a field hidden by name; one hidden by ID is caught above.
                var database = string.IsNullOrEmpty(databaseName) ? null : Factory.GetDatabase(databaseName, false);
                var definition = database?.GetItem(fieldId);
                return definition != null && context.Scope.IsHiddenField(definition.Name, null);
            });
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<McpCallContext, ConcurrentDictionary<string, bool>> Caches =
            new System.Runtime.CompilerServices.ConditionalWeakTable<McpCallContext, ConcurrentDictionary<string, bool>>();

        private static ConcurrentDictionary<string, bool> LinkFieldCache(McpCallContext context) =>
            Caches.GetValue(context, _ => new ConcurrentDictionary<string, bool>());
    }
}
