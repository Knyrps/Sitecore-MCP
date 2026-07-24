using System.Collections.Generic;
using Sitecore.Data;
using Sitecore.Layouts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="AddRenderingTool"/>.</summary>
    public sealed class AddRenderingArgs : ItemQueryArgs
    {
        /// <summary>The rendering to place, by path, ID, or exact name.</summary>
        [McpParam(Description = "Rendering to add, by path, ID, or exact name (no partial-name matching on writes).", Required = true)]
        public string Rendering { get; set; }

        /// <summary>The placeholder key the rendering is placed in.</summary>
        [McpParam(Description = "Placeholder key to place the rendering in (e.g. 'main' or '/header/main-navigation').", Required = true)]
        public string Placeholder { get; set; }

        /// <summary>An optional datasource item for the rendering, by path or ID.</summary>
        [McpParam(Description = "Datasource item for the rendering, by path or ID. Optional.")]
        public string Datasource { get; set; }

        /// <summary>Optional rendering parameters as a name/value map.</summary>
        [McpParam(Description = "Rendering parameters as a name/value map. Optional.")]
        public Dictionary<string, string> Parameters { get; set; }

        /// <summary>The device to add the rendering to; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout for all versions.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>Adds a rendering to an item's presentation for a device and placeholder.</summary>
    public sealed class AddRenderingTool : McpTool<AddRenderingArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_add_rendering";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Add a rendering (component) to a Sitecore item's presentation, in a given placeholder, " +
            "with an optional datasource and parameters. Edits the final per-version layout by " +
            "default. Returns the new rendering including its unique ID, which later set/switch/remove " +
            "calls use to target it.";

        /// <inheritdoc />
        protected override McpToolResult Execute(AddRenderingArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);

            var renderingItem = RenderingResolver.Resolve(item.Database, args.Rendering);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);
            var datasourceId = ResolveDatasource(item.Database, args.Datasource);

            var uniqueId = ID.NewID.ToString();
            var definition = new RenderingDefinition
            {
                ItemID = renderingItem.ID.ToString(),
                Placeholder = args.Placeholder,
                Datasource = datasourceId,
                Parameters = LayoutEditor.EncodeParameters(args.Parameters),
                UniqueId = uniqueId
            };

            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.GetOrCreateDevice(layout, deviceItem);
                device.AddRendering(definition);
            });

            var added = PresentationDescriber.Rendering(definition, item.Database);
            return McpToolResult.Structured(
                PresentationDescriber.Result(item.Paths.FullPath, deviceItem.Name, finalLayout, added));
        }

        private static string ResolveDatasource(Database db, string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return string.Empty;
            }

            var item = db.GetItem(reference);
            if (item == null)
            {
                throw new McpToolException($"Datasource '{reference}' was not found.");
            }

            return item.ID.ToString();
        }
    }
}
