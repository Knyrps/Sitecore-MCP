using SitecoreMcp.Server.Transport;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// The config-driven scope: the global &lt;contentRoots&gt;, &lt;searchRoots&gt; and &lt;hiddenFields&gt;,
    /// combined with the calling client's own attributes. A client's content roots and search roots
    /// replace the global lists (so a client can be narrowed or widened deliberately); its hidden
    /// fields are added to the global ones, because hiding is a safety rule a client must not undo.
    /// </summary>
    public sealed class ConfiguredContentScopeProvider : IContentScopeProvider
    {
        private readonly ContentScope _global;

        /// <summary>Creates the provider over the global scope (null meaning none configured).</summary>
        public ConfiguredContentScopeProvider(ContentScope global)
        {
            _global = global ?? ContentScope.Unrestricted;
        }

        /// <inheritdoc />
        public ContentScope GetScope(McpClient client) =>
            ContentScope.Combine(_global, client?.Scope ?? ContentScope.Unrestricted);
    }
}
