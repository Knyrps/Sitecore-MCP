using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Tests.Support
{
    /// <summary>A scripted <see cref="IToolCatalog"/> for exercising the dispatcher without Sitecore.</summary>
    public sealed class FakeToolCatalog : IToolCatalog
    {
        private readonly Dictionary<string, Func<JObject, McpToolResult>> _tools =
            new Dictionary<string, Func<JObject, McpToolResult>>(StringComparer.Ordinal);

        public FakeToolCatalog Add(string name, Func<JObject, McpToolResult> handler)
        {
            _tools[name] = handler;
            return this;
        }

        public IReadOnlyList<ToolDescriptor> List()
        {
            var list = new List<ToolDescriptor>();
            foreach (var name in _tools.Keys)
            {
                list.Add(new ToolDescriptor { Name = name, Description = name, InputSchema = new JObject() });
            }

            return list;
        }

        public bool Contains(string name) => _tools.ContainsKey(name);

        public McpToolResult Invoke(string name, JObject arguments) => _tools[name](arguments);
    }
}
