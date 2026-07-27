using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Layouts;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="MoveRenderingTool"/>.</summary>
    public sealed class MoveRenderingArgs : ItemQueryArgs
    {
        /// <summary>The unique ID of the rendering instance to move.</summary>
        [McpParam(Description = "Unique ID of the rendering instance to move, as returned by sitecore_get_renderings.", Required = true)]
        public string UniqueId { get; set; }

        /// <summary>The 1-based position to move it to among the device's renderings.</summary>
        [McpParam(Description = "1-based target position among the device's renderings (the order sitecore_get_renderings lists them in).", Required = true)]
        public int Position { get; set; }

        /// <summary>The device the rendering is on; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Moves a rendering to a different position in the device's rendering order, which controls the
    /// order components render in within a placeholder.
    /// </summary>
    public sealed class MoveRenderingTool : McpTool<MoveRenderingArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_move_rendering";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Move a rendering to a different 1-based position in the device's rendering order (the " +
            "order sitecore_get_renderings lists, which is the order components render in within a " +
            "placeholder). Identify the instance by unique ID. Edits the final per-version layout by default.";

        /// <inheritdoc />
        protected override McpToolResult Execute(MoveRenderingArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);

            JArray order = null;
            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.Device(layout, deviceItem);
                var rendering = LayoutEditor.RequireRendering(device, args.UniqueId);

                device.Renderings.Remove(rendering);
                var position = Math.Max(0, Math.Min(device.Renderings.Count, args.Position - 1));
                device.Renderings.Insert(position, rendering);

                order = new JArray(device.Renderings
                    .Cast<RenderingDefinition>()
                    .Select((r, i) => (object)$"{i + 1}: {r.UniqueId}")
                    .ToArray());
            });

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["device"] = deviceItem.Name,
                ["finalLayout"] = finalLayout,
                ["movedTo"] = args.Position,
                ["order"] = order
            });
        }
    }
}
