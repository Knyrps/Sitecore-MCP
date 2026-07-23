using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using Sitecore.Jobs;
using Sitecore.Publishing;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;
using SitecoreMcp.Server.Tools.Jobs;

namespace SitecoreMcp.Server.Tools.Publishing
{
    /// <summary>Arguments for <see cref="PublishItemTool"/>.</summary>
    public sealed class PublishItemArgs : ItemQueryArgs
    {
        /// <summary>Whether to publish only what changed, or force-republish the item.</summary>
        [McpParam(
            Description = "'smart' publishes only what changed (default); 'full' republishes the item even if unchanged.",
            Enum = new[] { "smart", "full" })]
        public string Mode { get; set; }

        /// <summary>The publishing target databases; defaults to every configured target.</summary>
        [McpParam(Description = "Target database names (e.g. 'web'). Defaults to every configured publishing target.")]
        public string[] TargetDatabases { get; set; }

        /// <summary>The languages to publish; defaults to the item's language.</summary>
        [McpParam(Description = "Language codes to publish. Defaults to the item's language.")]
        public string[] Languages { get; set; }

        /// <summary>Whether to publish the item's descendants too.</summary>
        [McpParam(Description = "Publish descendants too. Defaults to false.")]
        public bool? Deep { get; set; }

        /// <summary>Whether to also publish items this item references, such as datasources and media.</summary>
        [McpParam(Description = "Also publish related items (datasources, media this item references). Defaults to false.")]
        public bool? PublishRelatedItems { get; set; }
    }

    /// <summary>
    /// Publishes an item to the configured publishing targets. Publishing runs as a background job,
    /// so this returns as soon as the job has started; the caller polls the returned handle.
    /// </summary>
    public sealed class PublishItemTool : McpTool<PublishItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_publish_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Publish a Sitecore item from its database to the publishing targets (typically web), " +
            "optionally including descendants and related items. Content written to master is not " +
            "live until it is published. This starts a background job and returns its handle " +
            "immediately — poll sitecore_get_jobs to see whether it finished. Targets are subject to " +
            "the client's permitted databases.";

        /// <inheritdoc />
        protected override McpToolResult Execute(PublishItemArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var targets = ResolveTargets(item.Database, args.TargetDatabases, context);
            var languages = ResolveLanguages(item, args.Languages, context);

            var deep = args.Deep.GetValueOrDefault(false);
            var related = args.PublishRelatedItems.GetValueOrDefault(false);

            // 'smart' compares revisions so unchanged items are skipped; 'full' forces the item out
            // even when the target already looks current.
            var full = string.Equals(args.Mode, "full", System.StringComparison.OrdinalIgnoreCase);
            var mode = full ? "full" : "smart";

            var handle = PublishManager.PublishItem(item, targets, languages, deep, !full, related);
            if (handle == null)
            {
                throw new McpToolException("Sitecore did not start a publish job for this item.");
            }

            var result = new JObject
            {
                ["handle"] = handle.ToString(),
                ["item"] = item.Paths.FullPath,
                ["mode"] = mode,
                ["deep"] = deep,
                ["publishRelatedItems"] = related,
                ["targetDatabases"] = new JArray(targets.Select(t => (object)t.Name).ToArray()),
                ["languages"] = new JArray(languages.Select(l => (object)l.Name).ToArray()),
                ["note"] = "Publishing runs in the background. Poll sitecore_get_jobs with this handle to confirm completion."
            };

            var job = JobManager.GetJob(handle);
            if (job != null)
            {
                result["job"] = JobDescriber.Describe(job);
            }

            return McpToolResult.Structured(result);
        }

        /// <summary>
        /// Resolves the target databases, defaulting to every configured publishing target. Targets
        /// go through the call context, so a client may only publish to databases it is permitted.
        /// </summary>
        private static Database[] ResolveTargets(Database source, string[] requested, McpCallContext context)
        {
            var names = requested != null && requested.Length > 0
                ? requested.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList()
                : ConfiguredTargetNames(source);

            if (names.Count == 0)
            {
                throw new McpToolException(
                    $"No publishing targets are configured for database '{source.Name}'. Name one explicitly with targetDatabases.");
            }

            return names.Select(context.ResolveDatabase).ToArray();
        }

        private static List<string> ConfiguredTargetNames(Database source)
        {
            var names = new List<string>();
            foreach (Item target in PublishManager.GetPublishingTargets(source))
            {
                var name = target["Target database"];
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            return names;
        }

        private static Language[] ResolveLanguages(Item item, string[] requested, McpCallContext context)
        {
            if (requested == null || requested.Length == 0)
            {
                return new[] { item.Language };
            }

            return requested
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(context.ResolveLanguage)
                .ToArray();
        }
    }
}
