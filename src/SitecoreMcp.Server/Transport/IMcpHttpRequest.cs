namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// The slice of an incoming HTTP request the transport gates need. Abstracted away from
    /// System.Web so the gates stay unit-testable without an HttpContext.
    /// </summary>
    public interface IMcpHttpRequest
    {
        /// <summary>The HTTP method, uppercased (e.g. "POST").</summary>
        string HttpMethod { get; }

        /// <summary>Whether the request arrived over HTTPS.</summary>
        bool IsSecureConnection { get; }

        /// <summary>The immediate peer's IP address, before any proxy header is considered.</summary>
        string RemoteAddress { get; }

        /// <summary>The client-declared body length in bytes, or -1 when not provided.</summary>
        long ContentLength { get; }

        /// <summary>Returns the named header's value, or null when the header is absent.</summary>
        string GetHeader(string name);
    }
}
