using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="SwitchRenderingTool"/>.</summary>
    public sealed class SwitchRenderingArgs : ItemQueryArgs
    {
        /// <summary>The unique ID of the rendering instance to switch.</summary>
        [McpParam(Description = "Unique ID of the rendering instance to switch, as returned by sitecore_get_renderings.", Required = true)]
        public string UniqueId { get; set; }

        /// <summary>The rendering to switch to, by path, ID, or exact name.</summary>
        [McpParam(Description = "Rendering to switch to, by path, ID, or exact name (no partial-name matching on writes).", Required = true)]
        public string NewRendering { get; set; }

        /// <summary>The device the rendering is on; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Swaps the component of an existing rendering instance in place, keeping its placeholder,
    /// datasource, parameters, and position. Atomic, so a placeholder is never left empty the way
    /// a remove-then-add could if the second step failed.
    /// </summary>
    public sealed class SwitchRenderingTool : McpTool<SwitchRenderingArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_switch_rendering";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Replace the component of an existing rendering instance with a different one, keeping its " +
            "placeholder, datasource, parameters, and position. Identify the instance by its unique " +
            "ID and give the new rendering by path, ID, or exact name. Safer than remove-then-add " +
            "because it never leaves the placeholder empty. Edits the final per-version layout by default.";

        /// <inheritdoc />
        protected override McpToolResult Execute(SwitchRenderingArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);
            var newRenderingItem = RenderingResolver.Resolve(item.Database, args.NewRendering);

            JObject switched = null;
            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.Device(layout, deviceItem);
                var rendering = LayoutEditor.RequireRendering(device, args.UniqueId);
                rendering.ItemID = newRenderingItem.ID.ToString();
                switched = PresentationDescriber.Rendering(rendering, item.Database);
            });

            return McpToolResult.Structured(
                PresentationDescriber.Result(item.Paths.FullPath, deviceItem.Name, finalLayout, switched));
        }
    }
}
