using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Templates
{
    /// <summary>Arguments for the base-template tools.</summary>
    public sealed class BaseTemplateArgs : ItemQueryArgs
    {
        /// <summary>The base template to add or remove, by path, ID, or exact name.</summary>
        [McpParam(Description = "The base template to add or remove, by path, ID, or exact name.", Required = true)]
        public string BaseTemplate { get; set; }
    }

    /// <summary>Shared plumbing for editing a template's inheritance list.</summary>
    internal static class BaseTemplateHelper
    {
        /// <summary>Resolves the target as a template item, rejecting anything else.</summary>
        public static Item RequireTemplate(McpCallContext context, ItemQueryArgs args)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            if (item.TemplateID != TemplateIDs.Template)
            {
                throw new McpToolException($"'{item.Paths.FullPath}' is not a template.");
            }

            return item;
        }

        /// <summary>Reads the template's base-template IDs from its __Base template field.</summary>
        public static List<ID> ReadBases(Item template) =>
            (template[FieldIDs.BaseTemplate] ?? string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(ID.IsID)
                .Select(ID.Parse)
                .ToList();

        /// <summary>Writes the base list back and returns the resolved bases for the result.</summary>
        public static McpToolResult Write(Item template, List<ID> bases)
        {
            ItemEditor.Edit(template, editable =>
                editable[FieldIDs.BaseTemplate] = string.Join("|", bases.Select(id => id.ToString())));

            var resolved = new JArray(bases
                .Select(id => template.Database.GetItem(id))
                .Where(baseItem => baseItem != null)
                .Select(baseItem => (object)new JObject
                {
                    ["id"] = baseItem.ID.ToString(),
                    ["name"] = baseItem.Name,
                    ["path"] = baseItem.Paths.FullPath
                }).ToArray());

            return McpToolResult.Structured(new JObject
            {
                ["template"] = template.Paths.FullPath,
                ["baseTemplates"] = resolved
            });
        }
    }

    /// <summary>Adds a base template to an existing template's inheritance.</summary>
    public sealed class AddBaseTemplateTool : McpTool<BaseTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_add_base_template";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Add a base template to an existing template, so it inherits that template's fields. " +
            "Existing bases are untouched. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(BaseTemplateArgs args, McpCallContext context)
        {
            var template = BaseTemplateHelper.RequireTemplate(context, args);
            var baseTemplate = TemplateResolver.Resolve(template.Database, args.BaseTemplate, allowPartial: false);

            if (baseTemplate.ID == template.ID)
            {
                return McpToolResult.Failure("A template cannot inherit from itself.");
            }

            var bases = BaseTemplateHelper.ReadBases(template);
            if (bases.Contains(baseTemplate.ID))
            {
                return McpToolResult.Failure($"'{baseTemplate.Name}' is already a base template of '{template.Name}'.");
            }

            bases.Add(baseTemplate.ID);
            return BaseTemplateHelper.Write(template, bases);
        }
    }

    /// <summary>Removes a base template from an existing template's inheritance.</summary>
    public sealed class RemoveBaseTemplateTool : McpTool<BaseTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_base_template";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a base template from an existing template. Items keep no values for the fields " +
            "that stop being inherited, but no stored data is deleted - re-adding the base restores " +
            "them. Fails clearly when the target is not currently a base. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(BaseTemplateArgs args, McpCallContext context)
        {
            var template = BaseTemplateHelper.RequireTemplate(context, args);
            var baseTemplate = TemplateResolver.Resolve(template.Database, args.BaseTemplate, allowPartial: false);

            var bases = BaseTemplateHelper.ReadBases(template);
            if (!bases.Remove(baseTemplate.ID))
            {
                var current = string.Join(", ", bases
                    .Select(id => template.Database.GetItem(id)?.Name)
                    .Where(name => name != null));
                return McpToolResult.Failure(
                    $"'{baseTemplate.Name}' is not a base template of '{template.Name}'. Current bases: {current}.");
            }

            return BaseTemplateHelper.Write(template, bases);
        }
    }
}
