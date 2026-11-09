using System.Linq;
using System.Xml;
using SitecoreMcp.Server.Tools.Configuration;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools.Configuration
{
    /// <summary>
    /// sitecore_get_config reads the merged configuration the way showconfig.aspx shows it, through an
    /// XPath, with patch sources, size limits and secret redaction.
    /// </summary>
    public class ConfigQueryTests
    {
        private const string Merged =
            "<sitecore xmlns:patch=\"http://www.sitecore.net/xmlconfig/\">" +
            "<settings>" +
            "<setting name=\"Mcp.Enabled\" value=\"true\" patch:source=\"SitecoreMcp.Environment.config\" />" +
            "<setting name=\"Payment.ApiKey\" value=\"abc123\" patch:source=\"Payment.config\" />" +
            "<setting name=\"MailServerPassword\" value=\"hunter2\" />" +
            "</settings>" +
            "<pipelines><httpRequestBegin patch:source=\"Sitecore.config\">" +
            "<processor type=\"A.First, A\" />" +
            "<processor type=\"B.Second, B\" patch:source=\"B.config\"><param desc=\"x\"><inner>deep</inner></param></processor>" +
            "</httpRequestBegin></pipelines>" +
            "<databases><database id=\"master\" connectionStringName=\"master\" authToken=\"t0ken\" /></databases>" +
            "</sitecore>";

        private static XmlDocument Doc()
        {
            var doc = new XmlDocument();
            doc.LoadXml(Merged);
            return doc;
        }

        private static ConfigQueryResult Run(string xpath, System.Action<ConfigQueryOptions> configure = null)
        {
            var options = new ConfigQueryOptions { XPath = xpath };
            configure?.Invoke(options);
            return ConfigQuery.Run(Doc(), options);
        }

        [Fact]
        public void A_setting_is_found_by_name_with_its_value_and_patch_source()
        {
            var result = Run("/sitecore/settings/setting[@name='Mcp.Enabled']");

            Assert.True(result.Succeeded);
            var node = Assert.Single(result.Nodes);
            Assert.Equal("/sitecore/settings/setting[@name='Mcp.Enabled']", node.Path);
            Assert.Equal("true", node.Attributes["value"]);
            Assert.Equal(new[] { "SitecoreMcp.Environment.config" }, node.PatchSources);
            Assert.Contains("patch:source=\"SitecoreMcp.Environment.config\"", node.Xml);
        }

        [Fact]
        public void Patch_sources_can_be_stripped_from_the_xml_and_attributes()
        {
            var node = Run("/sitecore/settings/setting[@name='Mcp.Enabled']", o => o.IncludePatchSource = false).Nodes.Single();

            Assert.DoesNotContain("patch:source", node.Xml);
            Assert.DoesNotContain("xmlns:patch", node.Xml);
            Assert.False(node.Attributes.ContainsKey("patch:source"));
            Assert.Empty(node.PatchSources);
        }

        [Fact]
        public void A_descendant_axis_finds_a_pipeline_anywhere()
        {
            var node = Run("//pipelines/httpRequestBegin").Nodes.Single();

            Assert.Equal("/sitecore/pipelines/httpRequestBegin", node.Path);
            Assert.Contains("A.First, A", node.Xml);
            Assert.Equal(2, node.ChildElements);
        }

        [Fact]
        public void Node_only_returns_the_element_and_its_attributes_without_children()
        {
            var node = Run("//pipelines/httpRequestBegin", o => o.NodeOnly = true).Nodes.Single();

            Assert.DoesNotContain("processor", node.Xml);
            Assert.Equal(2, node.ChildElements);
            Assert.True(node.ChildrenOmitted);
        }

        [Fact]
        public void A_depth_limit_prunes_deeper_elements_and_says_so()
        {
            var node = Run("//pipelines/httpRequestBegin", o => o.Depth = 1).Nodes.Single();

            Assert.Contains("B.Second, B", node.Xml);
            Assert.DoesNotContain("<param", node.Xml);
            Assert.Contains("omitted", node.Xml);
            Assert.True(node.ChildrenOmitted);
        }

        [Fact]
        public void Siblings_are_told_apart_in_the_path_by_an_identifying_attribute()
        {
            var nodes = Run("//httpRequestBegin/processor").Nodes;

            Assert.Equal(
                new[]
                {
                    "/sitecore/pipelines/httpRequestBegin/processor[@type='A.First, A']",
                    "/sitecore/pipelines/httpRequestBegin/processor[@type='B.Second, B']"
                },
                nodes.Select(n => n.Path).ToArray());
        }

        [Fact]
        public void An_invalid_xpath_is_reported_clearly()
        {
            var result = Run("/sitecore/settings/setting[@name=");

            Assert.False(result.Succeeded);
            Assert.Contains("not a valid XPath", result.Error);
        }

        [Fact]
        public void An_xpath_that_matches_nothing_says_so()
        {
            var result = Run("/sitecore/settings/setting[@name='Nope']");

            Assert.False(result.Succeeded);
            Assert.Equal(0, result.Matched);
            Assert.Contains("matched nothing", result.Error);
        }

        [Fact]
        public void The_value_of_a_setting_whose_name_looks_secret_is_redacted()
        {
            var nodes = Run("/sitecore/settings/setting").Nodes;

            var apiKey = nodes.Single(n => n.Attributes["name"] == "Payment.ApiKey");
            Assert.Equal(ConfigRedaction.Redacted, apiKey.Attributes["value"]);
            Assert.DoesNotContain("abc123", apiKey.Xml);
            Assert.Equal(ConfigRedaction.Redacted, nodes.Single(n => n.Attributes["name"] == "MailServerPassword").Attributes["value"]);
            Assert.Equal("true", nodes.Single(n => n.Attributes["name"] == "Mcp.Enabled").Attributes["value"]);
        }

        [Fact]
        public void An_attribute_whose_own_name_looks_secret_is_redacted_but_names_of_secrets_are_kept()
        {
            var result = Run("//databases/database");
            var node = result.Nodes.Single();

            Assert.Equal(ConfigRedaction.Redacted, node.Attributes["authToken"]);
            Assert.DoesNotContain("t0ken", node.Xml);
            // connectionStringName names a connection string; it holds no secret.
            Assert.Equal("master", node.Attributes["connectionStringName"]);
            Assert.Equal(1, result.RedactedValues);
        }

        [Fact]
        public void Secrets_inside_a_returned_subtree_are_redacted_too()
        {
            var node = Run("/sitecore/settings").Nodes.Single();

            Assert.DoesNotContain("abc123", node.Xml);
            Assert.DoesNotContain("hunter2", node.Xml);
            Assert.Contains("Mcp.Enabled", node.Xml);
        }

        [Fact]
        public void Matches_beyond_the_node_cap_are_truncated_and_flagged()
        {
            var result = Run("//setting", o => o.MaxNodes = 2);

            Assert.True(result.Truncated);
            Assert.Equal(3, result.Matched);
            Assert.Equal(2, result.Nodes.Count);
        }

        [Fact]
        public void Output_beyond_the_character_cap_is_truncated_and_flagged()
        {
            var result = Run("//setting", o => o.MaxChars = 150);

            Assert.True(result.Truncated);
            Assert.True(result.Nodes.Count < 3);
        }

        [Fact]
        public void An_attribute_can_be_selected_directly()
        {
            var node = Run("/sitecore/settings/setting[@name='Mcp.Enabled']/@value").Nodes.Single();

            Assert.Equal("attribute", node.Kind);
            Assert.Equal("/sitecore/settings/setting[@name='Mcp.Enabled']/@value", node.Path);
            Assert.Equal("true", node.Value);
        }

        [Fact]
        public void A_secret_attribute_selected_directly_is_redacted()
        {
            var node = Run("/sitecore/settings/setting[@name='Payment.ApiKey']/@value").Nodes.Single();

            Assert.Equal(ConfigRedaction.Redacted, node.Value);
        }

        [Fact]
        public void The_live_document_is_never_changed()
        {
            var doc = Doc();
            ConfigQuery.Run(doc, new ConfigQueryOptions { XPath = "/sitecore", Depth = 1, IncludePatchSource = false });

            Assert.Equal(Doc().OuterXml, doc.OuterXml);
        }

        [Theory]
        [InlineData("Payment.ApiKey", true)]
        [InlineData("SMTP.Password", true)]
        [InlineData("clientSecret", true)]
        [InlineData("authToken", true)]
        [InlineData("ConnectionString", true)]
        [InlineData("connectionStringName", false)]
        [InlineData("keyEnvVar", false)]
        [InlineData("type", false)]
        [InlineData("Mcp.Enabled", false)]
        public void Sensitive_names(string name, bool sensitive)
        {
            Assert.Equal(sensitive, ConfigRedaction.IsSensitiveName(name));
        }
    }
}
