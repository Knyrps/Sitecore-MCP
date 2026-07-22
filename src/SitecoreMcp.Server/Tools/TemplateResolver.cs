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

            // Otherwise treat the reference as a template name.
            var byName = TemplateManager.GetTemplates(db).Values
                .Where(t => string.Equals(t.Name, reference, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byName.Count == 1)
            {
                var resolved = db.GetItem(byName[0].ID);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            if (byName.Count > 1)
            {
                var candidates = string.Join(", ", byName.Select(t => t.FullName).Take(10));
                throw new McpToolException(
                    $"Template name '{reference}' is ambiguous. Matches: {candidates}. Use a full path or ID.");
            }

            throw new McpToolException($"Template '{reference}' was not found by path, ID, or name.");
        }
    }
}
