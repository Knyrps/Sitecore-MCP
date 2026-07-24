using System;
using System.Collections.Generic;
using System.Xml;
using Sitecore.Configuration;
using Sitecore.Xml;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// The composition root, bound from the &lt;sitecoreMcp&gt; config node. It reads scalar
    /// settings, collects clients and tools, resolves each client's key from its environment
    /// variable, and assembles the request pipeline.
    /// </summary>
    public sealed class McpConfiguration
    {
        private readonly List<McpClient> _clients = new List<McpClient>();
        private readonly McpToolRegistry _registry = new McpToolRegistry();
        private readonly List<string> _allowedOrigins = new List<string>();
        private readonly List<string> _allowedAddresses = new List<string>();
        private readonly List<string> _trustedProxies = new List<string>();

        /// <summary>Adds a client from its raw config node, resolving the key from the named environment variable.</summary>
        public void AddClient(XmlNode node)
        {
            var keyEnvVar = XmlUtil.GetAttribute("keyEnvVar", node);
            var user = XmlUtil.GetAttribute("user", node);
            var allowWrites = string.Equals(XmlUtil.GetAttribute("allowWrites", node), "true", StringComparison.OrdinalIgnoreCase);
            var databases = SplitList(XmlUtil.GetAttribute("databases", node));

            if (string.IsNullOrEmpty(keyEnvVar) || string.IsNullOrEmpty(user))
            {
                McpLog.Warn("Skipping client with missing keyEnvVar or user.");
                return;
            }

            var key = Environment.GetEnvironmentVariable(keyEnvVar);
            if (string.IsNullOrEmpty(key))
            {
                // Never register a keyless client: a null key would risk matching an empty presented key.
                McpLog.Warn($"Environment variable '{keyEnvVar}' is not set; client for '{user}' disabled.");
                return;
            }

            if (databases.Count == 0)
            {
                databases.Add("master");
            }

            _clients.Add(new McpClient(key, user, allowWrites, databases));
        }

        /// <summary>
        /// Registers a tool from its raw config node: creates it from the type attribute and reads an
        /// optional admin attribute. The attribute overrides the tool's own default, so which tools
        /// require an administrator can be tightened or loosened by a config patch on the element.
        /// </summary>
        public void AddTool(XmlNode node)
        {
            var tool = Factory.CreateObject(node, true) as IMcpTool;
            if (tool == null)
            {
                McpLog.Warn("Skipping a tool whose type did not resolve to an IMcpTool.");
                return;
            }

            var adminAttribute = XmlUtil.GetAttribute("admin", node);
            var requiresAdmin = string.IsNullOrEmpty(adminAttribute)
                ? tool.RequiresAdmin
                : string.Equals(adminAttribute, "true", StringComparison.OrdinalIgnoreCase);

            _registry.AddTool(tool, requiresAdmin);
        }

        /// <summary>Adds a permitted request Origin.</summary>
        public void AddOrigin(string origin) => AddTrimmed(_allowedOrigins, origin);

        /// <summary>Adds a permitted client address.</summary>
        public void AddAddress(string address) => AddTrimmed(_allowedAddresses, address);

        /// <summary>Adds a proxy address whose X-Forwarded-For header is trusted.</summary>
        public void AddProxy(string proxy) => AddTrimmed(_trustedProxies, proxy);

        /// <summary>Reads settings, assembles the pipeline, and returns it with the effective settings.</summary>
        public (McpRequestPipeline Pipeline, McpSettings Settings) Build()
        {
            var settings = LoadSettings();

            foreach (var origin in _allowedOrigins) settings.AllowedOrigins.Add(origin);
            foreach (var address in _allowedAddresses) settings.AllowedAddresses.Add(address);
            foreach (var proxy in _trustedProxies) settings.TrustedProxies.Add(proxy);

            var gates = GateChain.FromSettings(settings);
            var authenticator = new McpAuthenticator(_clients);
            var rateLimiter = new RateLimiter(
                Settings.GetIntSetting("Mcp.RateLimit.Capacity", 30),
                Settings.GetDoubleSetting("Mcp.RateLimit.RefillPerSecond", 1.0));

            var pipeline = new McpRequestPipeline(settings, gates, authenticator, rateLimiter, _registry);
            return (pipeline, settings);
        }

        private static McpSettings LoadSettings() => new McpSettings
        {
            Enabled = Settings.GetBoolSetting("Mcp.Enabled", false),
            EndpointPath = Settings.GetSetting("Mcp.EndpointPath", "sitecore/api/mcp"),
            RequireHttps = Settings.GetBoolSetting("Mcp.RequireHttps", true),
            AllowWrites = Settings.GetBoolSetting("Mcp.AllowWrites", false),
            MaxRequestBytes = Settings.GetLongSetting("Mcp.MaxRequestBytes", 1024 * 1024),
            MaxConcurrentCalls = Settings.GetIntSetting("Mcp.MaxConcurrentCalls", 4),
            MaxFieldLength = Settings.GetIntSetting("Mcp.MaxFieldLength", 2000),
            VerboseErrors = Settings.GetBoolSetting("Mcp.VerboseErrors", false),
            ServerName = Settings.GetSetting("Mcp.ServerName", "SitecoreMcp"),
            ServerVersion = Settings.GetSetting("Mcp.ServerVersion", "0.1.0")
        };

        private static List<string> SplitList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value)) return result;

            foreach (var part in value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }

            return result;
        }

        private static void AddTrimmed(List<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
        }
    }
}
