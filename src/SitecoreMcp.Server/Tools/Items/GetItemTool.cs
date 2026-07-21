using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetItemTool"/>.</summary>
    public sealed class GetItemArgs : ItemQueryArgs
    {
        /// <summary>Specific fields to return; when omitted, all populated non-standard fields are returned.</summary>
        [McpParam(Description = "Optional list of field names to return. Omit to return all populated fields.")]
        public string[] Fields { get; set; }
    }

    /// <summary>Reads a single item with its populated fields, honouring field-level security.</summary>
    public sealed class GetItemTool : McpTool<GetItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_item";

        /// <inheritdoc />
        public override string Description =>
            "Read one Sitecore item by path or ID, returning its metadata and populated fields. " +
            "Pass 'fields' to request specific fields, including empty ones.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var projection = new ItemProjector(context).ProjectWithFields(item, args.Fields);
            return McpToolResult.Structured(projection);
        }
    }
}
