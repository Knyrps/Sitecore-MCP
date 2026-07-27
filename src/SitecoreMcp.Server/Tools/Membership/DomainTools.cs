using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sitecore.SecurityModel;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="NewDomainTool"/> and <see cref="RemoveDomainTool"/>.</summary>
    public sealed class DomainNameArgs
    {
        /// <summary>The domain name to create or remove.</summary>
        [McpParam(Description = "Domain name.", Required = true)]
        public string Name { get; set; }
    }

    /// <summary>Creates a Sitecore security domain.</summary>
    public sealed class NewDomainTool : McpTool<DomainNameArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_new_domain";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description => "Create a Sitecore security domain. Fails if it already exists. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(DomainNameArgs args, McpCallContext context)
        {
            var name = args.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return McpToolResult.Failure("No domain name was specified.");
            }

            if (DomainManager.DomainExists(name))
            {
                return McpToolResult.Failure($"Domain '{name}' already exists.");
            }

            DomainManager.AddDomain(name);
            return McpToolResult.Structured(MembershipDescriber.Domain(DomainManager.GetDomain(name)));
        }
    }

    /// <summary>Removes a Sitecore security domain, refusing the built-in ones.</summary>
    public sealed class RemoveDomainTool : McpTool<DomainNameArgs>
    {
        // The domains Sitecore ships and relies on; removing one would break authentication.
        private static readonly HashSet<string> BuiltIn =
            new HashSet<string>(new[] { "sitecore", "extranet", "default" }, StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override string Name => "sitecore_remove_domain";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove a Sitecore security domain. Refuses the built-in domains (sitecore, extranet, " +
            "default). Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(DomainNameArgs args, McpCallContext context)
        {
            var name = args.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return McpToolResult.Failure("No domain name was specified.");
            }

            if (BuiltIn.Contains(name))
            {
                return McpToolResult.Failure($"Refusing to remove the built-in domain '{name}'.");
            }

            if (!DomainManager.DomainExists(name))
            {
                return McpToolResult.Failure($"Domain '{name}' does not exist.");
            }

            DomainManager.RemoveDomain(name);
            return McpToolResult.Structured(new JObject { ["domain"] = name, ["removed"] = true });
        }
    }
}
