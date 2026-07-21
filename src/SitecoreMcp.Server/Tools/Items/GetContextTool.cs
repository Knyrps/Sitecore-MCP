using Newtonsoft.Json.Linq;
using Sitecore.Configuration;
using Sitecore.Data.Managers;
using Sitecore.Globalization;
using SitecoreMcp.Server.Protocol;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Empty argument set for <see cref="GetContextTool"/>.</summary>
    public sealed class GetContextArgs
    {
    }

    /// <summary>
    /// Reports the instance, the resolved user, and what this client may do. The recommended first
    /// call for an agent, and the quickest way to diagnose a bad deployment or misconfigured client.
    /// </summary>
    public sealed class GetContextTool : McpTool<GetContextArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_context";

        /// <inheritdoc />
        public override string Description =>
            "Report the current Sitecore instance, version, the user this client runs as, whether " +
            "writes are allowed, and which databases and languages are available. Call this first.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetContextArgs args, McpCallContext context)
        {
            var databases = new JArray();
            foreach (var name in context.Client.Databases)
            {
                databases.Add(name);
            }

            var languages = new JArray();
            var primary = Factory.GetDatabase(FirstDatabase(context), false);
            if (primary != null)
            {
                foreach (Language language in LanguageManager.GetLanguages(primary))
                {
                    languages.Add(language.Name);
                }
            }

            return McpToolResult.Structured(new JObject
            {
                ["instanceName"] = Settings.InstanceName,
                ["sitecoreVersion"] = About.GetVersionNumber(true),
                ["user"] = context.User.Name,
                ["isAdministrator"] = context.User.IsAdministrator,
                ["allowWrites"] = context.AllowWrites,
                ["databases"] = databases,
                ["languages"] = languages
            });
        }

        private static string FirstDatabase(McpCallContext context)
        {
            foreach (var name in context.Client.Databases)
            {
                return name;
            }

            return "master";
        }
    }
}
