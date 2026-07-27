using System;
using System.Collections.Generic;
using System.Linq;
using SitecoreMcp.Server.Tools;
using Xunit;

namespace SitecoreMcp.Server.Tests.Tools
{
    /// <summary>
    /// Keeps the shipped config and the code in step. Tools are registered through config rather
    /// than compiled in, so a new tool that nobody registers simply never appears, and a renamed
    /// class breaks the endpoint at startup. Both are cheap to catch here and expensive to catch
    /// on an instance.
    /// </summary>
    public class ToolRegistrationTests
    {
        [Fact]
        public void Every_tool_in_the_assembly_is_registered_in_config()
        {
            var configured = new HashSet<string>(ToolCatalog.ConfiguredTypeNames(), StringComparer.Ordinal);
            var missing = ToolCatalog.All
                .Select(tool => TypeName(tool))
                .Where(name => !configured.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                $"Tools exist but are not registered in SitecoreMcp.config: {string.Join(", ", missing)}");
        }

        [Fact]
        public void Config_registers_no_tool_that_does_not_exist()
        {
            var existing = new HashSet<string>(ToolCatalog.All.Select(TypeName), StringComparer.Ordinal);
            var unknown = ToolCatalog.ConfiguredTypeNames()
                .Where(name => !existing.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.True(unknown.Count == 0,
                $"SitecoreMcp.config references types that do not exist, which fails at startup: {string.Join(", ", unknown)}");
        }

        [Fact]
        public void Config_registers_each_tool_exactly_once()
        {
            var duplicates = ToolCatalog.ConfiguredTypeNames()
                .GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            // The registry rejects a duplicate name at startup, so this would take the endpoint down.
            Assert.True(duplicates.Count == 0, $"Registered more than once: {string.Join(", ", duplicates)}");
        }

        [Fact]
        public void Registry_rejects_a_duplicate_tool_name()
        {
            var registry = new McpToolRegistry();
            var first = ToolCatalog.All.First();

            registry.AddTool(first, first.RequiresAdmin);

            Assert.Throws<ArgumentException>(() => registry.AddTool(first, first.RequiresAdmin));
        }

        private static string TypeName(IMcpTool tool) =>
            tool.GetType().FullName.Substring("SitecoreMcp.Server.".Length);
    }
}
