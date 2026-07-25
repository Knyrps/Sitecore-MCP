using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.SecurityModel;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Membership
{
    /// <summary>Arguments for <see cref="GetDomainTool"/>.</summary>
    public sealed class GetDomainArgs
    {
        /// <summary>The domain to describe; omit to list all domains.</summary>
        [McpParam(Description = "Domain name to describe. Omit to list every domain.")]
        public string Name { get; set; }
    }

    /// <summary>Describes a Sitecore security domain, or lists all of them.</summary>
    public sealed class GetDomainTool : McpTool<GetDomainArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_domain";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Describe a Sitecore security domain (its user count and Everyone role), or omit the name " +
            "to list every domain. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetDomainArgs args, McpCallContext context)
        {
            if (!string.IsNullOrWhiteSpace(args.Name))
            {
                var domain = DomainManager.GetDomain(args.Name.Trim());
                if (domain == null)
                {
                    return McpToolResult.Structured(new JObject { ["name"] = args.Name, ["found"] = false });
                }

                var described = MembershipDescriber.Domain(domain);
                described["found"] = true;
                return McpToolResult.Structured(described);
            }

            var domains = new JArray((DomainManager.GetDomains() ?? Enumerable.Empty<Sitecore.Security.Domains.Domain>())
                .Where(d => d != null)
                .OrderBy(d => d.Name, System.StringComparer.OrdinalIgnoreCase)
                .Select(d => (object)MembershipDescriber.Domain(d))
                .ToArray());

            return McpToolResult.Structured(new JObject
            {
                ["count"] = domains.Count,
                ["domains"] = domains
            });
        }
    }
}
