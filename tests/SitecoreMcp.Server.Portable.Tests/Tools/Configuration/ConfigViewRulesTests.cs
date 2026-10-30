using System.Collections.Generic;
using SitecoreMcp.Server.Tools.Configuration;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools.Configuration
{
    /// <summary>
    /// The role/layer view mirrors showconfig.aspx: requested rules become "name:define" entries for
    /// a RuleBasedConfigReader, rules not asked for keep the instance's current value, and no rules
    /// and no layers means the live merged configuration.
    /// </summary>
    public class ConfigViewRulesTests
    {
        private static readonly string[] Known = { "role", "search", "env" };

        private static string[] Current(string name)
        {
            switch (name)
            {
                case "role": return new[] { "contentmanagement", "indexing" };
                case "search": return new[] { "solr" };
                case "env": return new[] { string.Empty };
                default: return null;
            }
        }

        [Fact]
        public void No_rules_and_no_layers_is_the_live_configuration()
        {
            var view = ConfigViewRules.Build(null, null, Known, Current);

            Assert.True(view.Succeeded);
            Assert.True(view.IsLiveConfiguration);
        }

        [Fact]
        public void A_requested_role_replaces_the_current_one_and_other_rules_keep_their_value()
        {
            var view = ConfigViewRules.Build(new Dictionary<string, string> { ["Role"] = "ContentDelivery" }, null, Known, Current);

            Assert.True(view.Succeeded);
            Assert.False(view.IsLiveConfiguration);
            Assert.Equal("ContentDelivery", view.RuleDefinitions["role:define"]);
            Assert.Equal("solr", view.RuleDefinitions["search:define"]);
            // A rule with no non-empty current value is left to the reader's own default, as showconfig does.
            Assert.False(view.RuleDefinitions.ContainsKey("env:define"));
        }

        [Fact]
        public void Layers_alone_build_a_rule_based_view_with_current_rules()
        {
            var view = ConfigViewRules.Build(null, "Sitecore| Modules ", Known, Current);

            Assert.False(view.IsLiveConfiguration);
            Assert.Equal(new[] { "Sitecore", "Modules" }, view.Layers);
            Assert.Equal("contentmanagement", view.RuleDefinitions["role:define"]);
        }

        [Fact]
        public void An_unknown_rule_is_refused_naming_the_known_ones()
        {
            var view = ConfigViewRules.Build(new Dictionary<string, string> { ["colour"] = "blue" }, null, Known, Current);

            Assert.False(view.Succeeded);
            Assert.Contains("colour", view.Error);
            Assert.Contains("role, search, env", view.Error);
        }
    }
}
