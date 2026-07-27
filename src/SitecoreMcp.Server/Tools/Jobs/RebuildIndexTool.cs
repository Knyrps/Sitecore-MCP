using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Maintenance;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>Arguments for <see cref="RebuildIndexTool"/>.</summary>
    public sealed class RebuildIndexArgs
    {
        /// <summary>The index to rebuild fully.</summary>
        [McpParam(Description = "Index name to rebuild fully (e.g. sitecore_master_index). Use this OR rootPath.")]
        public string Name { get; set; }

        /// <summary>A subtree to refresh across the indexes that cover it, instead of a full rebuild.</summary>
        [McpParam(Description = "Path or ID of a subtree to refresh in the indexes covering it - much cheaper than a full rebuild after editing a branch. Use this OR name.")]
        public string RootPath { get; set; }

        /// <summary>The database rootPath is resolved in; defaults to master.</summary>
        [McpParam(Description = "Database for rootPath. Defaults to 'master'.")]
        public string Database { get; set; }
    }

    /// <summary>
    /// Rebuilds a search index in the background: a full rebuild of one index, or a scoped refresh of
    /// a subtree. Returns job handles to poll rather than blocking on work that can take minutes.
    /// </summary>
    public sealed class RebuildIndexTool : McpTool<RebuildIndexArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_rebuild_index";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Rebuild a search index in the background: 'name' fully rebuilds one index; 'rootPath' " +
            "refreshes just that subtree in every index covering it (much cheaper - prefer it after " +
            "editing a branch). Returns job handle(s) immediately; poll sitecore_get_jobs for " +
            "progress, and check sitecore_index_status afterwards. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RebuildIndexArgs args, McpCallContext context)
        {
            var byName = !string.IsNullOrWhiteSpace(args.Name);
            var byRoot = !string.IsNullOrWhiteSpace(args.RootPath);
            if (byName == byRoot)
            {
                throw new McpToolException("Provide exactly one of 'name' (full rebuild) or 'rootPath' (scoped refresh).");
            }

            if (byName)
            {
                var index = ContentSearchManager.GetIndex(args.Name.Trim());
                if (index == null)
                {
                    var known = string.Join(", ", ContentSearchManager.Indexes.Select(i => i.Name));
                    throw new McpToolException($"Search index '{args.Name}' does not exist. Indexes: {known}.");
                }

                // The second argument is 'start': false hands back an unstarted job that never runs.
                var job = IndexCustodian.FullRebuild(index, true);
                return McpToolResult.Structured(new JObject
                {
                    ["index"] = index.Name,
                    ["mode"] = "fullRebuild",
                    ["job"] = JobDescriber.Describe(job),
                    ["note"] = "The rebuild runs in the background. Poll sitecore_get_jobs with the job handle; " +
                               "sitecore_index_status shows the result once finished."
                });
            }

            var root = ItemResolver.Resolve(context, args.RootPath, args.Database, null);
            var jobs = IndexCustodian.RefreshTree(new SitecoreIndexableItem(root)) ?? Enumerable.Empty<Sitecore.Abstractions.BaseJob>();

            return McpToolResult.Structured(new JObject
            {
                ["root"] = root.Paths.FullPath,
                ["mode"] = "refreshTree",
                ["jobs"] = new JArray(jobs.Select(job => (object)JobDescriber.Describe(job)).ToArray()),
                ["note"] = "Each covering index refreshes the subtree in the background. Poll sitecore_get_jobs."
            });
        }
    }
}
