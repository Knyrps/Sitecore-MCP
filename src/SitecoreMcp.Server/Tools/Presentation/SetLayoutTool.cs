using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="SetLayoutTool"/>.</summary>
    public sealed class SetLayoutArgs : ItemQueryArgs
    {
        /// <summary>The layout to assign, by path, ID, or exact name.</summary>
        [McpParam(Description = "The layout item to assign (under /sitecore/layout/Layouts), by path, ID, or exact name. Pass an empty string to clear the device's layout.", Required = true)]
        public string Layout { get; set; }

        /// <summary>The device to assign the layout on; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Assigns which layout (the outer MVC view) a device uses for an item - the one presentation
    /// property the rendering tools do not touch.
    /// </summary>
    public sealed class SetLayoutTool : McpTool<SetLayoutArgs>
    {
        private const string LayoutsRoot = "/sitecore/layout/Layouts";

        /// <inheritdoc />
        public override string Name => "sitecore_set_layout";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Assign the layout (the outer view an item renders with) for a device - by path, ID, or " +
            "exact name under /sitecore/layout/Layouts; an empty string clears it. This is separate " +
            "from the renderings placed INTO the layout's placeholders. Edits the final per-version " +
            "layout by default.";

        /// <inheritdoc />
        protected override McpToolResult Execute(SetLayoutArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);

            Item layoutItem = null;
            if (!string.IsNullOrEmpty(args.Layout))
            {
                layoutItem = ResolveLayout(item.Database, args.Layout);
            }

            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.GetOrCreateDevice(layout, deviceItem);
                device.Layout = layoutItem?.ID.ToString() ?? string.Empty;
            });

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["device"] = deviceItem.Name,
                ["finalLayout"] = finalLayout,
                ["layout"] = layoutItem == null ? null : (Newtonsoft.Json.Linq.JToken)new JObject
                {
                    ["id"] = layoutItem.ID.ToString(),
                    ["name"] = layoutItem.Name,
                    ["path"] = layoutItem.Paths.FullPath
                }
            });
        }

        /// <summary>Resolves a layout by path, ID, or exact name under the Layouts root — exact-only, like every write.</summary>
        private static Item ResolveLayout(Database db, string reference)
        {
            var item = db.GetItem(reference);
            if (item != null)
            {
                return item;
            }

            var root = db.GetItem(LayoutsRoot);
            var matches = root == null
                ? new System.Collections.Generic.List<Item>()
                : root.Axes.GetDescendants()
                    .Where(descendant => string.Equals(descendant.Name, reference, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                var candidates = string.Join(", ", matches.Select(m => m.Paths.FullPath).Take(10));
                throw new McpToolException($"Layout name '{reference}' is ambiguous. Matches: {candidates}. Use a full path or ID.");
            }

            throw new McpToolException($"Layout '{reference}' was not found by path, ID, or exact name under {LayoutsRoot}.");
        }
    }
}
