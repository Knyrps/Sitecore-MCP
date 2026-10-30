using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using Sitecore.Configuration;
using Sitecore.DependencyInjection;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Configuration
{
    /// <summary>Arguments for <see cref="GetConfigTool"/>.</summary>
    public sealed class GetConfigArgs
    {
        /// <summary>The XPath selecting the nodes.</summary>
        [McpParam(Required = true, Description =
            "XPath into the merged <sitecore> configuration, e.g. /sitecore/settings/setting[@name='Mcp.Enabled'], " +
            "//pipelines/httpRequestBegin, //sites/site[@name='website']. Element names are case-sensitive; " +
            "the patch: prefix is bound to the patch namespace.")]
        public string Xpath { get; set; }

        /// <summary>Return matched elements with their attributes only.</summary>
        [McpParam(Description = "Return each matched element with its attributes only, no children (childElements still says how many). Defaults to false.")]
        public bool? NodeOnly { get; set; }

        /// <summary>How many levels of descendants to include.</summary>
        [McpParam(Description = "Levels of descendants to include under each matched element (default 3, max 20); deeper elements are replaced by an 'omitted' comment.")]
        public int? Depth { get; set; }

        /// <summary>Whether to keep patch:source attributes.</summary>
        [McpParam(Description = "Keep the patch:source attributes that name the include file that set each node (default true). False strips them for a cleaner read.")]
        public bool? IncludePatchSource { get; set; }

        /// <summary>Rule overrides for a role view.</summary>
        [McpParam(Description =
            "Optional rule-based view, as showconfig.aspx offers: rule name to value, e.g. {\"role\": \"ContentDelivery\", \"search\": \"solr\"}. " +
            "Rules you do not name keep this instance's value. Omit (with layers) to read the live configuration.")]
        public Dictionary<string, string> Rules { get; set; }

        /// <summary>Layers to include.</summary>
        [McpParam(Description = "Optional pipe-separated configuration layers to include (e.g. 'Sitecore|Modules|Custom|Environment'); omit for all. Builds a fresh view like showconfig.aspx?layer=...")]
        public string Layers { get; set; }

        /// <summary>The most nodes returned.</summary>
        [McpParam(Description = "Maximum matched nodes to return (default 20, max 200). 'truncated' says when more matched.")]
        public int? MaxNodes { get; set; }
    }

    /// <summary>
    /// Reads the effective Sitecore configuration as /sitecore/admin/showconfig.aspx shows it: the
    /// merged <c>&lt;sitecore&gt;</c> document with <c>patch:source</c> attributes. The live view is
    /// <c>Factory.GetConfiguration()</c>; a role/layer view is rebuilt with Sitecore's own
    /// <c>RuleBasedConfigReader</c> over the chosen layers' include files, exactly as showconfig does
    /// (decompiled Sitecore.Client 10.3.0 and 10.4.0, identical). Secret-looking values are redacted.
    /// </summary>
    public sealed class GetConfigTool : McpTool<GetConfigArgs>
    {
        private const int MaxNodesCap = 200;
        private const int MaxChars = 60000;

        /// <inheritdoc />
        public override string Name => "sitecore_get_config";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Read the effective (merged) Sitecore configuration, as /sitecore/admin/showconfig.aspx shows it, by XPath: " +
            "settings, pipelines, sites, events, scheduling. Returns each matched node as XML plus its path, attributes and " +
            "patch:source (which include file set it). Limit the size with nodeOnly/depth/maxNodes; pass rules (e.g. role) or " +
            "layers for the configuration another role would get. Values whose names look secret (password, secret, key, token, " +
            "connectionString) are always redacted. Read-only. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetConfigArgs args, McpCallContext context)
        {
            var view = ConfigViewRules.Build(args.Rules, args.Layers, RuleNames(out var rules), name => rules?.GetRuleDefinitionValue(name));
            if (!view.Succeeded) throw new McpToolException(view.Error);

            var document = view.IsLiveConfiguration ? Factory.GetConfiguration() : BuildView(view);

            var result = ConfigQuery.Run(document, new ConfigQueryOptions
            {
                XPath = args.Xpath,
                NodeOnly = args.NodeOnly.GetValueOrDefault(),
                Depth = args.Depth ?? ConfigQueryOptions.DefaultDepth,
                IncludePatchSource = args.IncludePatchSource.GetValueOrDefault(true),
                MaxNodes = Paging.Clamp(args.MaxNodes.GetValueOrDefault(20), 1, MaxNodesCap),
                MaxChars = MaxChars
            });

            if (!result.Succeeded) throw new McpToolException(result.Error);

            var payload = new JObject
            {
                ["xpath"] = args.Xpath,
                ["view"] = view.IsLiveConfiguration
                    ? new JObject { ["kind"] = "live" }
                    : new JObject
                    {
                        ["kind"] = "ruleBased",
                        ["rules"] = JObject.FromObject(view.RuleDefinitions.ToDictionary(p => p.Key.Replace(ConfigViewRules.DefineSuffix, string.Empty), p => p.Value)),
                        ["layers"] = view.Layers.Count == 0 ? (JToken)"all" : new JArray(view.Layers.Cast<object>().ToArray())
                    },
                ["matched"] = result.Matched,
                ["returned"] = result.Nodes.Count,
                ["truncated"] = result.Truncated,
                ["redactedValues"] = result.RedactedValues,
                ["nodes"] = new JArray(result.Nodes.Select(Describe).Cast<object>().ToArray())
            };

            if (result.Truncated)
            {
                payload["hint"] = "More matched than was returned. Narrow the XPath, lower depth, or use nodeOnly.";
            }

            return McpToolResult.Structured(payload);
        }

        private static JObject Describe(ConfigNodeView node)
        {
            var described = new JObject { ["kind"] = node.Kind, ["path"] = node.Path };
            if (node.Kind == "element")
            {
                described["attributes"] = JObject.FromObject(node.Attributes);
                described["patchSources"] = new JArray(node.PatchSources.Cast<object>().ToArray());
                described["childElements"] = node.ChildElements;
                described["childrenOmitted"] = node.ChildrenOmitted;
                described["xml"] = node.Xml;
            }
            else
            {
                described["value"] = node.Value;
            }

            return described;
        }

        private static IEnumerable<string> RuleNames(out IConfigurationRulesContext rules)
        {
            rules = ServiceLocator.ServiceProvider?.GetService(typeof(IConfigurationRulesContext)) as IConfigurationRulesContext;
            return rules?.GetRuleDefinitionNames() ?? new string[0];
        }

        private static XmlDocument BuildView(ConfigView view)
        {
            var layered = new LayeredConfigurationFiles();
            var layers = layered.ConfigurationLayerProviders.SelectMany(provider => provider.GetLayers()).ToList();

            var unknown = view.Layers.Where(name => !layers.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0)
            {
                throw new McpToolException($"Unknown configuration layer(s): {string.Join(", ", unknown)}. " +
                                           $"Layers on this instance: {string.Join(", ", layers.Select(l => l.Name))}.");
            }

            var chosen = view.Layers.Count == 0
                ? layers
                : layers.Where(l => view.Layers.Any(name => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))).ToList();
            var includeFiles = chosen.SelectMany(l => l.GetConfigurationFiles()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var rules = new NameValueCollection();
            foreach (var pair in view.RuleDefinitions) rules.Add(pair.Key, pair.Value);

            try
            {
                return new RuleBasedConfigView(includeFiles, rules).Build();
            }
            catch (Exception ex) when (!(ex is McpToolException))
            {
                throw new McpToolException("Sitecore could not build the configuration for that view: " + ex.Message);
            }
        }

        /// <summary>
        /// Sitecore's rule-based reader, opened up to build a document for given rules and include files
        /// the way showconfig.aspx does (it calls the protected internal DoGetConfiguration). Builds a new
        /// document; the live configuration is untouched.
        /// </summary>
        private sealed class RuleBasedConfigView : RuleBasedConfigReader
        {
            public RuleBasedConfigView(IEnumerable<string> includeFiles, NameValueCollection rules)
                : base(includeFiles, rules)
            {
            }

            public XmlDocument Build() => DoGetConfiguration();
        }
    }
}
