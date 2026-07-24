using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>
    /// Resolves a rendering reference that may be a path, an ID, or a plain rendering name. Names are
    /// matched under the layout roots, exact-only for the same reason template writes are: a rendering
    /// name like "Container" is far from unique, so a fuzzy match could silently place the wrong
    /// component.
    /// </summary>
    public static class RenderingResolver
    {
        private static readonly string[] Roots =
        {
            "/sitecore/layout/Renderings",
            "/sitecore/layout/Sublayouts"
        };

        /// <summary>
        /// Resolves a rendering from a path, ID, or exact name. Throws <see cref="McpToolException"/>
        /// when nothing matches or a name is ambiguous, so a write never guesses which component to place.
        /// </summary>
        public static Item Resolve(Database db, string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                throw new McpToolException("No rendering was specified.");
            }

            var item = db.GetItem(reference);
            if (item != null)
            {
                return item;
            }

            var matches = Roots
                .Select(db.GetItem)
                .Where(root => root != null)
                .SelectMany(root => root.Axes.GetDescendants())
                .Where(descendant => string.Equals(descendant.Name, reference, StringComparison.OrdinalIgnoreCase))
                .GroupBy(descendant => descendant.ID)
                .Select(group => group.First())
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                var candidates = string.Join(", ", matches.Select(m => m.Paths.FullPath).Take(10));
                throw new McpToolException(
                    $"Rendering name '{reference}' is ambiguous. Matches: {candidates}. Use a full path or ID.");
            }

            throw new McpToolException($"Rendering '{reference}' was not found by path, ID, or exact name.");
        }
    }
}
