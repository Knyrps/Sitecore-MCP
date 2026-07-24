using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>Arguments for <see cref="ResetLayoutTool"/>.</summary>
    public sealed class ResetLayoutArgs : ItemQueryArgs
    {
        /// <summary>Whether to reset the final (per-version) layout or the shared base layout.</summary>
        [McpParam(Description = "Reset the final per-version layout (default true), so the item goes back to its inherited presentation. Set false to reset the shared base layout.")]
        public bool? FinalLayout { get; set; }
    }

    /// <summary>
    /// Resets an item's layout field to standard-values inheritance, discarding the item's own
    /// presentation overrides. The presentation-specific form of reverting a field to its default.
    /// </summary>
    public sealed class ResetLayoutTool : McpTool<ResetLayoutArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_reset_layout";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Reset a Sitecore item's presentation to what it inherits from its template's standard " +
            "values, discarding the item's own layout overrides. Resets the final per-version layout " +
            "by default. An item that already holds no local layout is reported as an unchanged no-op, " +
            "not an error.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ResetLayoutArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var finalLayout = args.FinalLayout.GetValueOrDefault(true);

            var reset = LayoutEditor.Reset(item, finalLayout);

            return McpToolResult.Structured(new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["finalLayout"] = finalLayout,
                ["reset"] = reset,
                ["note"] = reset
                    ? "Layout reset to standard-values inheritance."
                    : "The item already held no local layout on this field; nothing to reset."
            });
        }
    }
}
