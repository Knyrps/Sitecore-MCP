using System.Linq;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetItemReferencesTool"/>.</summary>
    public sealed class GetItemReferencesArgs : ItemQueryArgs
    {
        /// <summary>The maximum number of references to return in one call.</summary>
        [McpParam(Description = "Maximum references to return (default 50, max 500).")]
        public int? Limit { get; set; }

        /// <summary>The number of references to skip, for paging.</summary>
        [McpParam(Description = "Number of references to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Lists what an item points at: every outgoing link recorded in the Link Database, with the
    /// field it comes from. The counterpart to sitecore_get_item_referrers.
    /// </summary>
    public sealed class GetItemReferencesTool : McpTool<GetItemReferencesArgs>
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 500;

        /// <inheritdoc />
        public override string Name => "sitecore_get_item_references";

        /// <inheritdoc />
        public override string Description =>
            "List what a Sitecore item points AT — its outgoing links (datasources, link and " +
            "reference fields, media), each with the field it comes from. Use it to see what an item " +
            "depends on. Results come from the Link Database, so they are only as fresh as its last " +
            "update. A reference with resolved=false is either a deleted target or one you cannot " +
            "read — those are indistinguishable here.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetItemReferencesArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var links = Sitecore.Globals.LinkDatabase.GetReferences(item) ?? new Sitecore.Links.ItemLink[0];
            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);

            var page = new JArray(links
                .Skip(range.Offset)
                .Take(range.Limit)
                .Select(link => (object)LinkDescriber.Reference(link, item, context))
                .ToArray());

            var result = Paging.Envelope("references", page, links.Length, range);
            result["item"] = item.Paths.FullPath;

            var unresolved = page.Count(entry => entry["resolved"]?.Value<bool>() == false);
            if (unresolved > 0)
            {
                result["unresolved"] = unresolved;
                result["hint"] = "Some targets could not be read. They may have been deleted, or they " +
                                 "may sit in a database or branch this client cannot access.";
            }

            return McpToolResult.Structured(result);
        }
    }
}
