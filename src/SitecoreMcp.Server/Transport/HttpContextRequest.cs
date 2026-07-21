using System.Web;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>Adapts a System.Web request to <see cref="IMcpHttpRequest"/> for the gates and pipeline.</summary>
    public sealed class HttpContextRequest : IMcpHttpRequest
    {
        private readonly HttpRequest _request;

        /// <summary>Wraps the given ASP.NET request.</summary>
        public HttpContextRequest(HttpRequest request)
        {
            _request = request;
        }

        /// <inheritdoc />
        public string HttpMethod => _request.HttpMethod?.ToUpperInvariant();

        /// <inheritdoc />
        public bool IsSecureConnection => _request.IsSecureConnection;

        /// <inheritdoc />
        public string RemoteAddress => _request.UserHostAddress;

        /// <inheritdoc />
        public long ContentLength => _request.ContentLength;

        /// <inheritdoc />
        public string GetHeader(string name) => _request.Headers[name];
    }
}
