using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Tests.Tools
{
    /// <summary>
    /// Discovers the registered tools for the catalogue tests. Sitecore.Kernel is not present in the
    /// test host, so enumerating the assembly's types partially fails; the tool classes themselves
    /// load and construct fine, which is all these tests need.
    /// </summary>
    public static class ToolCatalog
    {
        private static readonly Lazy<IReadOnlyList<IMcpTool>> Instances =
            new Lazy<IReadOnlyList<IMcpTool>>(Discover);

        /// <summary>Every tool implementation in the server assembly.</summary>
        public static IReadOnlyList<IMcpTool> All => Instances.Value;

        /// <summary>Each tool as a single xunit theory case, so every tool is its own test result.</summary>
        public static IEnumerable<object[]> AsTheoryData() => All.Select(tool => new object[] { new ToolCase(tool) });

        /// <summary>The tool type names referenced by the shipped config, in registration order.</summary>
        public static IReadOnlyList<string> ConfiguredTypeNames()
        {
            var config = File.ReadAllText(ConfigPath());
            return System.Text.RegularExpressions.Regex
                .Matches(config, @"<tool\s+type=""SitecoreMcp\.Server\.(?<type>[^,]+),")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Groups["type"].Value)
                .ToList();
        }

        /// <summary>Locates the shipped config by walking up from the test assembly to the repo root.</summary>
        public static string ConfigPath()
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(new Uri(typeof(ToolCatalog).Assembly.CodeBase).LocalPath));
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    @"src\SitecoreMcp.Server\App_Config\Include\SitecoreMcp\SitecoreMcp.config");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate SitecoreMcp.config above the test assembly.");
        }

        private static IReadOnlyList<IMcpTool> Discover()
        {
            Type[] types;
            try
            {
                types = typeof(IMcpTool).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Expected: types whose signatures reference Sitecore.Kernel cannot load here.
                types = ex.Types.Where(type => type != null).ToArray();
            }

            return types
                .Where(type => !type.IsAbstract && typeof(IMcpTool).IsAssignableFrom(type))
                .Select(type => (IMcpTool)Activator.CreateInstance(type))
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// Wraps a tool so xunit prints the tool name as the test case label rather than the type name.
    /// </summary>
    public sealed class ToolCase
    {
        public ToolCase(IMcpTool tool) => Tool = tool;

        public IMcpTool Tool { get; }

        public override string ToString() => Tool.Name;
    }
}
