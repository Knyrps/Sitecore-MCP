using SitecoreMcp.Server.Transport;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Supplies the <see cref="ContentScope"/> a call runs under, once per call after authentication.
    /// The default reads config (<see cref="ConfiguredContentScopeProvider"/>); a host such as CorePilot
    /// can plug in its own, e.g. one that reads content roots, search roots and hidden fields from
    /// Sitecore items, by setting a &lt;contentScopeProvider type="..."/&gt; on the &lt;sitecoreMcp&gt; node.
    /// An implementation must never return null and should fail closed (e.g. an empty content-root
    /// list) when its source is unavailable.
    /// </summary>
    public interface IContentScopeProvider
    {
        /// <summary>The scope for calls from <paramref name="client"/>.</summary>
        ContentScope GetScope(McpClient client);
    }
}
