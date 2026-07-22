using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetItemTool"/>.</summary>
    public sealed class GetItemArgs : ItemQueryArgs
    {
        /// <summary>Specific fields to return; when omitted, the default view is applied.</summary>
        [McpParam(Description = "Specific field names to return (includes empty and standard fields). Omit for the default view.")]
        public string[] Fields { get; set; }

        /// <summary>Whether to include the __-prefixed standard fields in the default view.</summary>
        [McpParam(Description = "Include standard (__-prefixed) fields such as __Workflow. Default false.")]
        public bool? IncludeStandardFields { get; set; }

        /// <summary>Whether to include fields that have no value in the default view.</summary>
        [McpParam(Description = "Include fields that have no value. Default false.")]
        public bool? IncludeEmpty { get; set; }
    }

    /// <summary>Reads a single item with its fields, honouring field-level security.</summary>
    public sealed class GetItemTool : McpTool<GetItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_item";

        /// <inheritdoc />
        public override string Description =>
            "Read one Sitecore item by path or ID. By default returns only fields that have a value, " +
            "excluding standard (__) fields — an item with no content fields returns an empty map, " +
            "which is normal. To discover which fields the item's template defines, call " +
            "sitecore_get_template. For every field regardless, pass includeStandardFields and " +
            "includeEmpty, or list exact names in 'fields'.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetItemArgs args, McpCallContext context)
        {
            var includeStandard = args.IncludeStandardFields.GetValueOrDefault(false);
            var includeEmpty = args.IncludeEmpty.GetValueOrDefault(false);
            var explicitFields = args.Fields != null && args.Fields.Length > 0;

            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var projection = new ItemProjector(context).ProjectWithFields(
                item, args.Fields, includeStandard, includeEmpty);

            // When the default view returns nothing but fields do exist, tell the model what it is
            // seeing and how to get more, so an empty result is a signpost rather than a dead end.
            if (!explicitFields && !includeStandard && !includeEmpty &&
                ((JObject)projection["fields"]).Count == 0)
            {
                var stats = (JObject)projection["fieldStats"];
                if ((int)stats["empty"] > 0 || (int)stats["standard"] > 0)
                {
                    projection["hint"] =
                        $"No populated content fields on this item (template '{item.TemplateName}'). " +
                        $"{stats["empty"]} empty content field(s) and {stats["standard"]} standard field(s) exist. " +
                        "Call sitecore_get_template for the field schema, or re-read with includeEmpty/includeStandardFields.";
                }
            }

            return McpToolResult.Structured(projection);
        }
    }
}
