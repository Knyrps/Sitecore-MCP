using System.Web;
using System.Web.Routing;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>Supplies the MCP handler for the registered endpoint route.</summary>
    public sealed class McpRouteHandler : IRouteHandler
    {
        /// <inheritdoc />
        public IHttpHandler GetHttpHandler(RequestContext requestContext) => new McpHttpHandler();
    }

    /// <summary>Supplies the editor-token issuing handler for its route.</summary>
    public sealed class EditorTokenRouteHandler : IRouteHandler
    {
        /// <inheritdoc />
        public IHttpHandler GetHttpHandler(RequestContext requestContext) => new EditorTokenHttpHandler();
    }
}
