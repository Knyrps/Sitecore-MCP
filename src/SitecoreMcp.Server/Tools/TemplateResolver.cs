using System;
using System.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>Resolves a template reference that may be a path, an ID, or a plain template name.</summary>
    public static class TemplateResolver
    {
        /// <summary>
        /// Resolves a template from a path, ID, or name. A name is matched against template names in
        /// the database. Throws <see cref="McpToolException"/> when nothing matches, the match is
        /// ambiguous, or the reference points at a non-template item, so a model that thinks in names
        /// does not need a separate lookup first.
        /// </summary>
        public static Item Resolve(Database db, string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                throw new McpToolException("No template was specified.");
            }

            // A path or ID resolves an item directly; require that it actually be a template.
            var item = db.GetItem(reference);
            if (item != null)
            {
                if (item.TemplateID == Sitecore.TemplateIDs.Template)
                {
                    return item;
                }

                throw new McpToolException($"'{item.Paths.FullPath}' is not a template.");
            }

            // Otherwise treat the reference as a template name: an exact name match wins; failing
            // that, a unique partial match resolves (so "Local Datasource" finds "Local Datasource
            // Folder"), and anything ambiguous is reported with its candidates.
            var templates = TemplateManager.GetTemplates(db).Values.ToList();

            var exact = templates
                .Where(t => string.Equals(t.Name, reference, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (TryResolveSingle(db, exact, out var exactItem))
            {
                return exactItem;
            }
            if (exact.Count > 1)
            {
                throw Ambiguous(reference, exact);
            }

            var partial = templates
                .Where(t => t.Name.IndexOf(reference, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (TryResolveSingle(db, partial, out var partialItem))
            {
                return partialItem;
            }
            if (partial.Count > 1)
            {
                throw Ambiguous(reference, partial);
            }

            throw new McpToolException($"Template '{reference}' was not found by path, ID, or name.");
        }

        private static bool TryResolveSingle(Database db, System.Collections.Generic.List<Sitecore.Data.Templates.Template> matches, out Item item)
        {
            item = null;
            if (matches.Count != 1)
            {
                return false;
            }

            item = db.GetItem(matches[0].ID);
            return item != null;
        }

        private static McpToolException Ambiguous(string reference, System.Collections.Generic.List<Sitecore.Data.Templates.Template> matches)
        {
            var candidates = string.Join(", ", matches.Select(t => t.FullName).Take(10));
            return new McpToolException(
                $"Template name '{reference}' is ambiguous. Matches: {candidates}. Use a full path or ID.");
        }
    }
}
