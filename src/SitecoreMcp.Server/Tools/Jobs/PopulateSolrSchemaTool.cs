using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.Pipelines;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>Arguments for <see cref="PopulateSolrSchemaTool"/>.</summary>
    public sealed class PopulateSolrSchemaArgs
    {
        /// <summary>The index whose Solr core schema to populate; omit for every index.</summary>
        [McpParam(Description = "Index name to populate the Solr managed schema for. Omit to populate every index.")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Runs Sitecore's populate-Solr-managed-schema pipeline (the Control Panel operation) for one or
    /// all indexes. Solr-specific by nature, so the provider is bound by reflection and a non-Solr
    /// instance reports "not applicable" instead of failing to load.
    /// </summary>
    public sealed class PopulateSolrSchemaTool : McpTool<PopulateSolrSchemaArgs>
    {
        private const string PipelineName = "contentSearch.PopulateSolrSchema";
        private const string ArgsTypeName =
            "Sitecore.ContentSearch.SolrProvider.Pipelines.PopulateSolrSchema.PopulateManagedSchemaArgs, Sitecore.ContentSearch.SolrProvider";

        /// <inheritdoc />
        public override string Name => "sitecore_populate_solr_schema";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Populate the Solr managed schema for one index or all of them (the Control Panel " +
            "'Populate Solr Managed Schema' operation). Run it after adding indexed or computed " +
            "fields, BEFORE sitecore_rebuild_index - a rebuild cannot index a field the schema does " +
            "not know. On a non-Solr instance this reports notApplicable. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(PopulateSolrSchemaArgs args, McpCallContext context)
        {
            // Bound at runtime so the server assembly loads on instances without the Solr provider.
            var argsType = Type.GetType(ArgsTypeName, false);
            if (argsType == null)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["notApplicable"] = true,
                    ["reason"] = "The Solr provider is not present on this instance, so there is no managed schema to populate."
                });
            }

            var indexes = string.IsNullOrWhiteSpace(args.Name)
                ? ContentSearchManager.Indexes.ToList()
                : ContentSearchManager.Indexes.Where(i => string.Equals(i.Name, args.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

            if (indexes.Count == 0)
            {
                var known = string.Join(", ", ContentSearchManager.Indexes.Select(i => i.Name));
                throw new McpToolException($"Search index '{args.Name}' does not exist. Indexes: {known}.");
            }

            var results = new JArray();
            var failures = 0;
            foreach (var index in indexes)
            {
                try
                {
                    var pipelineArgs = (PipelineArgs)Activator.CreateInstance(argsType, index);
                    CorePipeline.Run(PipelineName, pipelineArgs);
                    results.Add(new JObject { ["index"] = index.Name, ["populated"] = true });
                }
                catch (Exception ex)
                {
                    failures++;
                    results.Add(new JObject
                    {
                        ["index"] = index.Name,
                        ["populated"] = false,
                        ["error"] = ex.InnerException?.Message ?? ex.Message
                    });
                }
            }

            var result = new JObject
            {
                ["count"] = results.Count,
                ["indexes"] = results
            };
            if (failures > 0)
            {
                result["warning"] = $"{failures} index(es) failed to populate; see each entry's error.";
            }

            return McpToolResult.Structured(result);
        }
    }
}
