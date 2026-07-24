using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="RemoveRenderingTool"/>.</summary>
    public sealed class RemoveRenderingArgs : ItemQueryArgs
    {
        /// <summary>The unique ID of the rendering instance to remove.</summary>
        [McpParam(Description = "Unique ID of the rendering instance to remove, as returned by sitecore_get_renderings.", Required = true)]
        public string UniqueId { get; set; }

        /// <summary>The device the rendering is on; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>Removes a rendering from an item's presentation, identified by its unique ID.</summary>
    public sealed class RemoveRenderingTool : McpTool<RemoveRenderingArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_rendering";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a rendering (component) from a Sitecore item's presentation, identified by its " +
            "unique ID. Edits the final per-version layout by default.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RemoveRenderingArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);

            JObject removed = null;
            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.Device(layout, deviceItem);
                var rendering = LayoutEditor.RequireRendering(device, args.UniqueId);
                removed = PresentationDescriber.Rendering(rendering, item.Database);
                device.Renderings.Remove(rendering);
            });

            return McpToolResult.Structured(
                PresentationDescriber.Result(item.Paths.FullPath, deviceItem.Name, finalLayout, removed));
        }
    }
}
