using System.Collections.Generic;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Templates
{
    /// <summary>Arguments for <see cref="CreateTemplateTool"/>.</summary>
    public sealed class CreateTemplateArgs
    {
        /// <summary>The parent item's path or ID, under which the new template is created.</summary>
        [McpParam(Description = "Parent item path or ID (typically a folder under /sitecore/templates).", Required = true)]
        public string Parent { get; set; }

        /// <summary>The name of the new template.</summary>
        [McpParam(Description = "Name of the new template.", Required = true)]
        public string Name { get; set; }

        /// <summary>The database to create in; defaults to the parent's database (master).</summary>
        [McpParam(Description = "Database name. Defaults to the parent's database (master).")]
        public string Database { get; set; }

        /// <summary>Base templates to inherit from, by name/path/ID; defaults to the Standard Template.</summary>
        [McpParam(Description = "Base templates to inherit from, each by path, ID, or exact name (no partial-name matching, so inheritance is never a fuzzy guess). Defaults to the Standard Template when omitted.")]
        public string[] BaseTemplates { get; set; }

        /// <summary>The sections and their fields to create on the template.</summary>
        [McpParam(Description = "Sections, each containing the fields to create on the new template.")]
        public TemplateSectionDefinition[] Sections { get; set; }

        /// <summary>Whether to create a __Standard Values item for the template.</summary>
        [McpParam(Description = "Whether to create a __Standard Values item for the template (for default field values and presentation).")]
        public bool CreateStandardValues { get; set; }
    }

    /// <summary>
    /// Creates a Sitecore template with base templates, sections, and typed fields. Validates the
    /// whole definition before writing anything, and rolls back a partially built template on failure.
    /// </summary>
    public sealed class CreateTemplateTool : McpTool<CreateTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_create_template";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        // Templates define the content schema, which is developer/admin territory rather than a
        // content-editing task; an operator can waive this via the adminExemptTools config list.
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Create a new Sitecore template under a parent, with base templates (default: Standard " +
            "Template), sections, and typed fields, and optionally a __Standard Values item. Field " +
            "types must be exact (e.g. Single-Line Text). Fails if the name is invalid, a sibling of " +
            "that name exists, a field type is unknown, or a section or field name is duplicated.";

        /// <inheritdoc />
        protected override McpToolResult Execute(CreateTemplateArgs args, McpCallContext context)
        {
            var parent = ItemResolver.Resolve(context, args.Parent, args.Database, null);
            var db = parent.Database;

            ItemHelper.ValidateName(args.Name, "Invalid template name: ");

            if (parent.Children[args.Name] != null)
            {
                return McpToolResult.Failure($"'{parent.Paths.FullPath}' already has a child named '{args.Name}'.");
            }

            // Resolve base templates and validate the section/field definition before creating
            // anything, so an unknown base template, bad name, unknown field type, or duplicate
            // leaves the tree untouched.
            var baseTemplates = ResolveBaseTemplates(db, args.BaseTemplates);
            TemplateBuilder.Validate(args.Sections);

            var template = new TemplateItem(parent.Add(args.Name, new TemplateID(TemplateIDs.Template)));

            Item standardValues = null;
            try
            {
                TemplateBuilder.SetBaseTemplates(template.InnerItem, baseTemplates);
                TemplateBuilder.AddSections(template, args.Sections);
                if (args.CreateStandardValues)
                {
                    standardValues = TemplateBuilder.CreateStandardValues(template);
                }
            }
            catch
            {
                // Do not leave a half-built template behind if a later step fails.
                try { template.InnerItem.Recycle(); } catch { }
                throw;
            }

            return McpToolResult.Structured(
                TemplateDescriber.DescribeStructure(template.InnerItem, baseTemplates, standardValues));
        }

        private static IReadOnlyList<Item> ResolveBaseTemplates(Database db, string[] references)
        {
            if (references == null || references.Length == 0)
            {
                return new[] { db.GetItem(TemplateIDs.StandardTemplate) };
            }

            var resolved = new List<Item>();
            foreach (var reference in references)
            {
                resolved.Add(TemplateResolver.Resolve(db, reference, allowPartial: false));
            }

            return resolved;
        }
    }
}
