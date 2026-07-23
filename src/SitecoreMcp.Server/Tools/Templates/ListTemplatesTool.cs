using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Managers;
using Sitecore.Data.Templates;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Templates
{
    /// <summary>Arguments for <see cref="ListTemplatesTool"/>.</summary>
    public sealed class ListTemplatesArgs
    {
        /// <summary>Case-insensitive substring to match against a template's full name.</summary>
        [McpParam(Description = "Filter to templates whose full name contains this text (case-insensitive).")]
        public string NameContains { get; set; }

        /// <summary>The database to enumerate templates from; defaults to master.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }

        /// <summary>The maximum number of templates to return in one call.</summary>
        [McpParam(Description = "Maximum templates to return (default 50, max 200).")]
        public int? Limit { get; set; }

        /// <summary>The number of templates to skip, for paging.</summary>
        [McpParam(Description = "Number of templates to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Lists templates in a database, filtered by name and paged. Use it to find a template's path
    /// or ID before creating an item, then sitecore_get_template to see its fields.
    /// </summary>
    public sealed class ListTemplatesTool : McpTool<ListTemplatesArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_list_templates";

        /// <inheritdoc />
        public override string Description =>
            "List Sitecore templates in a database, optionally filtered by a substring of their full " +
            "name, paged. Use this to discover a template before creating an item.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ListTemplatesArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);

            var filter = args.NameContains;
            var matches = TemplateManager.GetTemplates(db).Values
                .Where(t => string.IsNullOrEmpty(filter) ||
                            t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);

            var page = new JArray();
            for (int i = range.Offset, taken = 0; i < matches.Count && taken < range.Limit; i++, taken++)
            {
                page.Add(Describe(db, matches[i]));
            }

            var result = Paging.Envelope("templates", page, matches.Count, range);
            result["database"] = db.Name;
            return McpToolResult.Structured(result);
        }

        private static JObject Describe(Sitecore.Data.Database db, Template template)
        {
            // Resolve the item only for the returned page; skip the path if the caller cannot read it.
            var item = db.GetItem(template.ID);
            return new JObject
            {
                ["id"] = template.ID.ToString(),
                ["name"] = template.Name,
                ["fullName"] = template.FullName,
                ["path"] = item?.Paths.FullPath
            };
        }
    }
}
