using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Transport;

namespace SitecoreMcp.Server.Tests.Support
{
    /// <summary>An in-memory <see cref="IMcpHttpRequest"/> so gates and auth are testable without HttpContext.</summary>
    public sealed class FakeHttpRequest : IMcpHttpRequest
    {
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string HttpMethod { get; set; } = "POST";
        public bool IsSecureConnection { get; set; } = true;
        public string RemoteAddress { get; set; } = "127.0.0.1";
        public long ContentLength { get; set; }

        public string GetHeader(string name) => _headers.TryGetValue(name, out var value) ? value : null;

        public FakeHttpRequest WithHeader(string name, string value)
        {
            _headers[name] = value;
            return this;
        }
    }
}
