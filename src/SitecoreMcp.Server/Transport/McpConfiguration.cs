using System;
using System.Collections.Generic;
using System.Xml;
using Sitecore.Configuration;
using Sitecore.Xml;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Security;
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
        private List<string> _contentRoots;
        private List<string> _searchRoots;
        private List<string> _hiddenFields;

        /// <summary>
        /// Adds a client from its raw config node, resolving the key from the named environment variable.
        /// The rules (one identity source per client, no keyless client) live in <see cref="McpClientFactory"/>.
        /// </summary>
        public void AddClient(XmlNode node)
        {
            var definition = new McpClientDefinition
            {
                Id = XmlUtil.GetAttribute("id", node),
                KeyEnvVar = XmlUtil.GetAttribute("keyEnvVar", node),
                User = XmlUtil.GetAttribute("user", node),
                RequiresEditorToken = IsTrue(XmlUtil.GetAttribute("requiresEditorToken", node)),
                Audience = XmlUtil.GetAttribute("audience", node),
                AllowWrites = IsTrue(XmlUtil.GetAttribute("allowWrites", node)),
                Databases = XmlUtil.GetAttribute("databases", node),
                ContentRoots = XmlUtil.GetAttribute("contentRoots", node),
                SearchRoots = XmlUtil.GetAttribute("searchRoots", node),
                HiddenFields = XmlUtil.GetAttribute("hiddenFields", node),
                ConfirmDirectChanges = OptionalBool(XmlUtil.GetAttribute("confirmDirectChanges", node), "confirmDirectChanges")
            };

            // Mcp.ConfirmDirectChanges is the default for fixed-user clients only; editor-token clients always confirm.
            var creation = new McpClientFactory(Environment.GetEnvironmentVariable,
                Settings.GetBoolSetting("Mcp.ConfirmDirectChanges", false)).Create(definition);
            if (!creation.Succeeded)
            {
                McpLog.Warn(creation.Error);
                return;
            }

            _clients.Add(creation.Client);
        }

        /// <summary>
        /// Registers a tool from its raw config node: creates it from the type attribute and reads an
        /// optional admin attribute. Config can only ADD an admin requirement, never remove one, so a
        /// tool that requires an administrator in code stays that way whatever config says.
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
            var configRequiresAdmin = string.Equals(adminAttribute, "true", StringComparison.OrdinalIgnoreCase);

            // A code-level admin requirement is a safety net against a config mistake exposing a
            // privileged tool, so config may tighten the gate but never loosen it.
            var requiresAdmin = tool.RequiresAdmin || configRequiresAdmin;
            if (tool.RequiresAdmin && !configRequiresAdmin && !string.IsNullOrEmpty(adminAttribute))
            {
                McpLog.Warn($"Tool '{tool.Name}' requires an administrator in code; the admin=\"{adminAttribute}\" config value is ignored, because config can add an admin requirement but not remove one.");
            }

            _registry.AddTool(tool, requiresAdmin);
        }

        /// <summary>Adds a permitted request Origin.</summary>
        public void AddOrigin(string origin) => AddTrimmed(_allowedOrigins, origin);

        /// <summary>Adds a permitted client address.</summary>
        public void AddAddress(string address) => AddTrimmed(_allowedAddresses, address);

        /// <summary>Adds a proxy address whose X-Forwarded-For header is trusted.</summary>
        public void AddProxy(string proxy) => AddTrimmed(_trustedProxies, proxy);

        /// <summary>Adds a global content root: a path under which items may be changed.</summary>
        public void AddContentRoot(string path) => AddTrimmed(_contentRoots = _contentRoots ?? new List<string>(), path);

        /// <summary>Adds a global search root: a path under which search looks.</summary>
        public void AddSearchRoot(string path) => AddTrimmed(_searchRoots = _searchRoots ?? new List<string>(), path);

        /// <summary>Adds a global hidden field, by name or ID.</summary>
        public void AddHiddenField(string nameOrId) => AddTrimmed(_hiddenFields = _hiddenFields ?? new List<string>(), nameOrId);

        /// <summary>
        /// The source of each call's content scope. Defaults to <see cref="ConfiguredContentScopeProvider"/>
        /// over the global lists and the client's attributes; set a &lt;contentScopeProvider type="..."/&gt;
        /// child node to supply scope another way (e.g. from Sitecore items).
        /// </summary>
        public IContentScopeProvider ContentScopeProvider { get; set; }

        /// <summary>
        /// The editor-token service assembled by <see cref="Build"/>, or null when no signing secret is
        /// configured. Shared by the authenticator (validation) and the issuing endpoint/helper.
        /// </summary>
        public EditorTokenService EditorTokens { get; private set; }

        /// <summary>Reads settings, assembles the pipeline, and returns it with the effective settings.</summary>
        public (McpRequestPipeline Pipeline, McpSettings Settings) Build()
        {
            var settings = LoadSettings();

            foreach (var origin in _allowedOrigins) settings.AllowedOrigins.Add(origin);
            foreach (var address in _allowedAddresses) settings.AllowedAddresses.Add(address);
            foreach (var proxy in _trustedProxies) settings.TrustedProxies.Add(proxy);

            var gates = GateChain.FromSettings(settings);
            EditorTokens = LoadEditorTokens(settings);
            var authenticator = new McpAuthenticator(_clients, EditorTokens);
            var rateLimiter = new RateLimiter(
                Settings.GetIntSetting("Mcp.RateLimit.Capacity", 30),
                Settings.GetDoubleSetting("Mcp.RateLimit.RefillPerSecond", 1.0));

            // A list element present but empty (e.g. <contentRoots/> with no children) is never called,
            // so an unconfigured list stays null and restricts nothing.
            var scopeProvider = ContentScopeProvider ??
                new ConfiguredContentScopeProvider(new ContentScope(_contentRoots, _searchRoots, _hiddenFields));

            // Confirmation tokens are signed with a per-process random key and kept in memory: a direct
            // change is confirmed within minutes of its preview, on the instance that previewed it.
            var confirmations = new ConfirmationService(options: new ConfirmationOptions
            {
                Lifetime = TimeSpan.FromSeconds(Math.Max(30, Settings.GetIntSetting("Mcp.Confirmation.LifetimeSeconds", 300)))
            });

            var pipeline = new McpRequestPipeline(settings, gates, authenticator, rateLimiter, _registry, scopeProvider, confirmations);
            return (pipeline, settings);
        }

        /// <summary>
        /// Builds the editor-token service from the secret in the environment variable named by
        /// Mcp.EditorToken.SecretEnvVar, and records the audiences of the clients that require one.
        /// Returns null when no secret (or too short a one) is set: every editor-token client is then
        /// refused and no token can be issued - fail closed, never a fallback identity.
        /// </summary>
        private EditorTokenService LoadEditorTokens(McpSettings settings)
        {
            foreach (var client in _clients)
            {
                if (client.RequiresEditorToken) settings.EditorTokenAudiences.Add(client.EditorTokenAudience);
            }

            var secretEnvVar = Settings.GetSetting("Mcp.EditorToken.SecretEnvVar", "SITECORE_MCP_EDITOR_TOKEN_SECRET");
            var secret = string.IsNullOrEmpty(secretEnvVar) ? null : Environment.GetEnvironmentVariable(secretEnvVar);
            if (string.IsNullOrEmpty(secret))
            {
                if (settings.EditorTokenAudiences.Count > 0)
                {
                    McpLog.Warn($"Environment variable '{secretEnvVar}' is not set; editor tokens cannot be issued or accepted, so every requiresEditorToken client is refused.");
                }

                return null;
            }

            if (secret.Length < EditorTokenService.MinimumSecretLength)
            {
                McpLog.Error($"The editor-token secret in '{secretEnvVar}' is shorter than {EditorTokenService.MinimumSecretLength} characters; editor tokens are disabled.");
                return null;
            }

            return new EditorTokenService(secret, new EditorTokenOptions
            {
                Issuer = Settings.GetSetting("Mcp.EditorToken.Issuer", "SitecoreMcp"),
                Lifetime = TimeSpan.FromSeconds(Math.Max(30, Settings.GetIntSetting("Mcp.EditorToken.LifetimeSeconds", 900))),
                ClockSkew = TimeSpan.FromSeconds(Math.Max(0, Settings.GetIntSetting("Mcp.EditorToken.ClockSkewSeconds", 60)))
            });
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
            MaxImageDimension = Settings.GetIntSetting("Mcp.Images.MaxDimension", 1024),
            MaxImageBytes = Settings.GetLongSetting("Mcp.Images.MaxBytes", 1024 * 1024),
            VerboseErrors = Settings.GetBoolSetting("Mcp.VerboseErrors", false),
            ServerName = Settings.GetSetting("Mcp.ServerName", "SitecoreMcp"),
            ServerVersion = Settings.GetSetting("Mcp.ServerVersion", "0.1.0"),
            EditorTokenEndpointPath = Settings.GetSetting("Mcp.EditorToken.EndpointPath", "sitecore/api/sitecore-mcp/editor-token"),
            EditorTokenDomains = Settings.GetSetting("Mcp.EditorToken.AllowedDomains", "sitecore")
        };

        /// <summary>
        /// Reads an optional true/false attribute: null when absent. Any other value is logged and read as
        /// true, so a typo never switches a safety check off.
        /// </summary>
        private static bool? OptionalBool(string value, string attribute)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                McpLog.Warn($"{attribute}=\"{value}\" is neither true nor false; reading it as true.");
            }

            return true;
        }

        private static bool IsTrue(string value) =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        private static void AddTrimmed(List<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
        }
    }
}
