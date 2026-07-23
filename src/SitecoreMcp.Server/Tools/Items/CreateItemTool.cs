using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="CreateItemTool"/>.</summary>
    public sealed class CreateItemArgs
    {
        /// <summary>The parent item's path or ID, under which the new item is created.</summary>
        [McpParam(Description = "Parent item path or ID.", Required = true)]
        public string Parent { get; set; }

        /// <summary>The name of the new item.</summary>
        [McpParam(Description = "Name of the new item.", Required = true)]
        public string Name { get; set; }

        /// <summary>The template to base the new item on, by path, ID, or name.</summary>
        [McpParam(Description = "Template for the new item, by path, ID, or exact name (no partial-name matching on writes).", Required = true)]
        public string Template { get; set; }

        /// <summary>The database to create in; defaults to master.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }

        /// <summary>The language version to write initial fields into; defaults to the context language.</summary>
        [McpParam(Description = "Language code. Defaults to the context language.")]
        public string Language { get; set; }

        /// <summary>Optional initial field values for the new item.</summary>
        [McpParam(Description = "Optional map of field name to value to set on the new item.")]
        public Dictionary<string, string> Fields { get; set; }
    }

    /// <summary>Creates a child item from a template, validating the name and guarding against collisions.</summary>
    public sealed class CreateItemTool : McpTool<CreateItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_create_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Create a new Sitecore item under a parent, from a template. Optionally set initial field " +
            "values. Fails if the name is invalid or a sibling with that name already exists.";

        /// <inheritdoc />
        protected override McpToolResult Execute(CreateItemArgs args, McpCallContext context)
        {
            var parent = ItemResolver.Resolve(context, args.Parent, args.Database, args.Language);

            ItemHelper.ValidateName(args.Name);

            if (parent.Children[args.Name] != null)
            {
                return McpToolResult.Failure($"'{parent.Paths.FullPath}' already has a child named '{args.Name}'.");
            }

            var templateItem = TemplateResolver.Resolve(parent.Database, args.Template, allowPartial: false);
            var created = parent.Add(args.Name, new TemplateID(templateItem.ID));
            FieldWriteResult write = null;
            if (args.Fields != null && args.Fields.Count > 0)
            {
                write = ItemEditor.WriteFields(created, args.Fields, context);
            }

            var projection = new ItemProjector(context).ProjectWithFields(created, null);
            if (write != null && write.NotPersisted.Count > 0)
            {
                projection["notPersisted"] = new JArray(write.NotPersisted);
            }
            return McpToolResult.Structured(projection);
        }
    }
}
