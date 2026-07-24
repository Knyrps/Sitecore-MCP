using System.Collections.Generic;
using System.Web;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="SetRenderingTool"/>.</summary>
    public sealed class SetRenderingArgs : ItemQueryArgs
    {
        /// <summary>The unique ID of the rendering instance to update.</summary>
        [McpParam(Description = "Unique ID of the rendering instance to change, as returned by sitecore_get_renderings.", Required = true)]
        public string UniqueId { get; set; }

        /// <summary>A new placeholder for the rendering; omit to leave it unchanged.</summary>
        [McpParam(Description = "New placeholder key. Omit to leave it unchanged.")]
        public string Placeholder { get; set; }

        /// <summary>A new datasource by path or ID; omit to leave it, empty string to clear it.</summary>
        [McpParam(Description = "New datasource by path or ID. Omit to leave it unchanged; pass an empty string to clear it.")]
        public string Datasource { get; set; }

        /// <summary>Parameters to set; only supplied keys change, and a null value removes that parameter.</summary>
        [McpParam(Description = "Rendering parameters to set, as a name/value map. Only the keys you pass change; a null value removes that parameter. Omit to leave all parameters unchanged.")]
        public Dictionary<string, string> Parameters { get; set; }

        /// <summary>The device the rendering is on; defaults to "Default".</summary>
        [McpParam(Description = "Device name. Defaults to Default.")]
        public string Device { get; set; }

        /// <summary>Whether to edit the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Edit the final per-version layout (default true). Set false to change the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Updates an existing rendering instance's datasource, placeholder, and parameters, changing only
    /// what is passed. This is also how rendering parameters are read-modified-written.
    /// </summary>
    public sealed class SetRenderingTool : McpTool<SetRenderingArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_set_rendering";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Change an existing rendering on a Sitecore item, identified by its unique ID: its " +
            "datasource, placeholder, or parameters. Only the parts you pass change. For parameters, " +
            "only the keys you supply are touched and a null value removes that key, so you never have " +
            "to resend the whole set. Edits the final per-version layout by default.";

        /// <inheritdoc />
        protected override McpToolResult Execute(SetRenderingArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);
            var deviceItem = LayoutEditor.ResolveDevice(item, args.Device);

            var datasourceId = ResolveDatasource(item, args.Datasource);

            JObject changed = null;
            LayoutEditor.Edit(item, finalLayout, layout =>
            {
                var device = LayoutEditor.Device(layout, deviceItem);
                var rendering = LayoutEditor.RequireRendering(device, args.UniqueId);

                if (args.Placeholder != null)
                {
                    rendering.Placeholder = args.Placeholder;
                }

                if (datasourceId != null)
                {
                    rendering.Datasource = datasourceId;
                }

                if (args.Parameters != null && args.Parameters.Count > 0)
                {
                    rendering.Parameters = MergeParameters(rendering.Parameters, args.Parameters);
                }

                changed = PresentationDescriber.Rendering(rendering, item.Database);
            });

            return McpToolResult.Structured(
                PresentationDescriber.Result(item.Paths.FullPath, deviceItem.Name, finalLayout, changed));
        }

        /// <summary>
        /// Merges supplied parameter changes into the existing blob: a present key is set, a null value
        /// removes the key, and untouched keys are preserved.
        /// </summary>
        private static string MergeParameters(string existing, IReadOnlyDictionary<string, string> changes)
        {
            var merged = HttpUtility.ParseQueryString(existing ?? string.Empty);
            foreach (var change in changes)
            {
                if (change.Value == null)
                {
                    merged.Remove(change.Key);
                }
                else
                {
                    merged[change.Key] = change.Value;
                }
            }

            return merged.ToString();
        }

        // Returns null to mean "leave unchanged" and empty string to mean "clear", distinguishing an
        // omitted argument from an explicit clear.
        private static string ResolveDatasource(Sitecore.Data.Items.Item context, string reference)
        {
            if (reference == null)
            {
                return null;
            }

            if (reference.Length == 0)
            {
                return string.Empty;
            }

            var item = context.Database.GetItem(reference);
            if (item == null)
            {
                throw new McpToolException($"Datasource '{reference}' was not found.");
            }

            return item.ID.ToString();
        }
    }
}
