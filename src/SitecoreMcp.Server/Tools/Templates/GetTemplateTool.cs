using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Templates
{
    /// <summary>Arguments for <see cref="GetTemplateTool"/>.</summary>
    public sealed class GetTemplateArgs : ItemQueryArgs
    {
    }

    /// <summary>
    /// Returns the template behind an item (or a template itself): its fields, own and inherited,
    /// with type and section. This is how a caller discovers valid field names before reading or
    /// writing, instead of guessing them.
    /// </summary>
    public sealed class GetTemplateTool : McpTool<GetTemplateArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_template";

        /// <inheritdoc />
        public override string Description =>
            "Describe the template of a Sitecore item (or of a template item directly): its base " +
            "templates and every field it defines or inherits, with field type and section. Use this " +
            "to learn valid field names before reading specific fields or creating or updating items.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetTemplateArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            // If the resolved item is itself a template definition, describe it; otherwise describe
            // the template the item is based on.
            var template = item.TemplateID == Sitecore.TemplateIDs.Template
                ? new TemplateItem(item)
                : item.Template;

            return McpToolResult.Structured(TemplateDescriber.Describe(template, item));
        }
    }
}
