using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SitecoreMcp.Server.Tests.Tools
{
    /// <summary>
    /// Pins which tools mutate state and which require an administrator. The expected sets are
    /// written out in full on purpose: these are security decisions, so removing a gate should fail
    /// a test rather than pass quietly. Enforcement itself is one shared code path in
    /// RequestToolCatalog, verified against a live instance.
    /// </summary>
    public class ToolPermissionTests
    {
        // Schema, security, and instance-wide operations. A non-admin client never sees these.
        private static readonly string[] ExpectedAdminOnly =
        {
            "sitecore_add_base_template",
            "sitecore_add_item_acl",
            "sitecore_add_role_member",
            "sitecore_clear_item_acl",
            "sitecore_create_template",
            "sitecore_disable_user",
            "sitecore_enable_user",
            "sitecore_get_domain",
            "sitecore_get_logs",
            "sitecore_get_role",
            "sitecore_get_user",
            "sitecore_index_status",
            "sitecore_new_domain",
            "sitecore_new_role",
            "sitecore_new_user",
            "sitecore_populate_solr_schema",
            "sitecore_rebuild_index",
            "sitecore_rebuild_link_database",
            "sitecore_remove_base_template",
            "sitecore_remove_domain",
            "sitecore_remove_role",
            "sitecore_remove_role_member",
            "sitecore_remove_user",
            "sitecore_set_item_acl",
            "sitecore_test_item_acl",
            "sitecore_unlock_user"
        };

        // Anything that changes content, presentation, security, or instance state.
        private static readonly string[] ExpectedWrites =
        {
            "sitecore_add_base_template",
            "sitecore_add_item_acl",
            "sitecore_add_item_version",
            "sitecore_add_rendering",
            "sitecore_add_role_member",
            "sitecore_change_item_template",
            "sitecore_clear_item_acl",
            "sitecore_copy_item",
            "sitecore_create_item",
            "sitecore_create_template",
            "sitecore_delete_item",
            "sitecore_disable_user",
            "sitecore_enable_user",
            "sitecore_invoke_workflow",
            "sitecore_lock_item",
            "sitecore_move_item",
            "sitecore_move_rendering",
            "sitecore_new_domain",
            "sitecore_new_role",
            "sitecore_new_user",
            "sitecore_populate_solr_schema",
            "sitecore_protect_item",
            "sitecore_publish_item",
            "sitecore_rebuild_index",
            "sitecore_rebuild_link_database",
            "sitecore_remove_base_template",
            "sitecore_remove_domain",
            "sitecore_remove_item_version",
            "sitecore_remove_rendering",
            "sitecore_remove_role",
            "sitecore_remove_role_member",
            "sitecore_remove_user",
            "sitecore_rename_item",
            "sitecore_reset_item_fields",
            "sitecore_reset_layout",
            "sitecore_set_item_acl",
            "sitecore_set_layout",
            "sitecore_set_rendering",
            "sitecore_switch_rendering",
            "sitecore_unlock_item",
            "sitecore_unlock_user",
            "sitecore_unprotect_item",
            "sitecore_update_item",
            "sitecore_update_item_referrers",
            "sitecore_upload_media"
        };

        [Fact]
        public void Admin_gated_tools_are_exactly_the_expected_set()
        {
            var actual = ToolCatalog.All
                .Where(tool => tool.RequiresAdmin)
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(ExpectedAdminOnly.OrderBy(name => name, StringComparer.Ordinal).ToList(), actual);
        }

        [Fact]
        public void Write_tools_are_exactly_the_expected_set()
        {
            var actual = ToolCatalog.All
                .Where(tool => tool.RequiresWrite)
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(ExpectedWrites.OrderBy(name => name, StringComparer.Ordinal).ToList(), actual);
        }

        [Theory]
        [MemberData(nameof(AllTools))]
        public void An_admin_tool_says_so_in_its_description(ToolCase testCase)
        {
            if (!testCase.Tool.RequiresAdmin)
            {
                return;
            }

            // The description is the only signal a model gets about why a tool might be missing.
            Assert.Contains("Administrator only", testCase.Tool.Description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Reading_a_tool_never_requires_write_permission()
        {
            var readsRequiringWrite = ToolCatalog.All
                .Where(tool => tool.Name.StartsWith("sitecore_get_", StringComparison.Ordinal) && tool.RequiresWrite)
                .Select(tool => tool.Name)
                .ToList();

            Assert.True(readsRequiringWrite.Count == 0,
                $"Read tools must not require write permission: {string.Join(", ", readsRequiringWrite)}");
        }

        public static IEnumerable<object[]> AllTools => ToolCatalog.AsTheoryData();
    }
}
