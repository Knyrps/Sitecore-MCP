using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="GetRenderingsTool"/>.</summary>
    public sealed class GetRenderingsArgs : ItemQueryArgs
    {
        /// <summary>The device whose renderings to read; defaults to "Default".</summary>
        [McpParam(Description = "Device name (e.g. Default, Print). Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to read the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Read the final per-version layout (default true, what actually renders). Set false to read the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Lists the renderings placed on an item for a device: what component each is, its placeholder,
    /// datasource, and parameters. This is how a caller sees what a page is built from before changing it.
    /// </summary>
    public sealed class GetRenderingsTool : McpTool<GetRenderingsArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_renderings";

        /// <inheritdoc />
        public override string Description =>
            "List the renderings (components) placed on a Sitecore item for a device, each with its " +
            "placeholder, datasource, parameters, and unique ID. Reads the effective final layout by " +
            "default (what actually renders, resolving inheritance and the page's own overrides). An " +
            "empty list is normal for an item with no presentation. Use the unique IDs to target " +
            "sitecore_set_rendering, sitecore_remove_rendering, or sitecore_switch_rendering.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetRenderingsArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);

            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);
            var layout = LayoutEditor.Read(item, finalLayout);
            var device = LayoutEditor.Device(layout, deviceItem);

            var renderings = PresentationDescriber.Renderings(device, item.Database);

            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["device"] = deviceItem.Name,
                ["finalLayout"] = finalLayout,
                ["count"] = renderings.Count,
                ["renderings"] = renderings
            };

            if (renderings.Count == 0)
            {
                result["hint"] = "No renderings on this device. The item may have no presentation, or " +
                                 "it may be defined on a different device or on the shared layout " +
                                 "(finalLayout=false).";
            }

            return McpToolResult.Structured(result);
        }
    }
}
