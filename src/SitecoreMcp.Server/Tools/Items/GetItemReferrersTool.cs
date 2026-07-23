using System.Linq;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetItemReferrersTool"/>.</summary>
    public sealed class GetItemReferrersArgs : ItemQueryArgs
    {
        /// <summary>The maximum number of referrers to return in one call.</summary>
        [McpParam(Description = "Maximum referrers to return (default 50, max 500).")]
        public int? Limit { get; set; }

        /// <summary>The number of referrers to skip, for paging.</summary>
        [McpParam(Description = "Number of referrers to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Lists what points at an item: every incoming link recorded in the Link Database. This is the
    /// impact check to run before deleting, moving, or renaming something.
    /// </summary>
    public sealed class GetItemReferrersTool : McpTool<GetItemReferrersArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 500;

        /// <inheritdoc />
        public override string Name => "sitecore_get_item_referrers";

        /// <inheritdoc />
        public override string Description =>
            "List what points AT a Sitecore item — its incoming links, each with the item and field " +
            "referencing it. Run this before delete_item, move_item, or rename_item to see what would " +
            "break. 'total' counts every referrer the Link Database knows about; referrers this client " +
            "cannot read are counted as 'unreadable' rather than listed, so the total stays a truthful " +
            "impact signal. Results are only as fresh as the Link Database's last update.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetItemReferrersArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var links = Sitecore.Globals.LinkDatabase.GetReferrers(item) ?? new Sitecore.Links.ItemLink[0];
            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);

            // Only the page is resolved: the total comes straight from the link count, so the impact
            // signal stays cheap even for an item referenced thousands of times.
            var slice = links.Skip(range.Offset).Take(range.Limit).ToList();
            var described = slice
                .Select(link => LinkDescriber.Referrer(link, context))
                .Where(entry => entry != null)
                .ToArray();

            // Bespoke envelope: unreadable sources are counted but not listed, so the listed count is
            // not the page size and hasMore has to be measured against the slice instead.
            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["total"] = links.Length,
                ["offset"] = range.Offset,
                ["count"] = described.Length,
                ["hasMore"] = range.Offset + slice.Count < links.Length,
                ["referrers"] = new JArray(described.Cast<object>().ToArray())
            };

            var unreadable = slice.Count - described.Length;
            if (unreadable > 0)
            {
                result["unreadable"] = unreadable;
                result["hint"] = "Some referrers are not readable by this client and are counted but " +
                                 "not listed. They still reference this item, so treat them as impact.";
            }

            if (links.Length == 0)
            {
                result["hint"] = "Nothing references this item according to the Link Database. If the " +
                                 "database is stale (a bulk import, or link tracking disabled), that is " +
                                 "not proof: rebuild it before relying on an empty result.";
            }

            return McpToolResult.Structured(result);
        }
    }
}
