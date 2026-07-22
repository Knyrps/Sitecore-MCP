using System.IO;
using System.Text;
using System.Web;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// The endpoint's HTTP handler. Reads the body within the configured limit, runs the pipeline,
    /// and writes the response. Holds no per-request state, so it is safe to reuse.
    /// </summary>
    public sealed class McpHttpHandler : IHttpHandler
    {
        /// <inheritdoc />
        public bool IsReusable => true;

        /// <inheritdoc />
        public void ProcessRequest(HttpContext context)
        {
            var pipeline = McpRuntime.Pipeline;
            var settings = McpRuntime.Settings;
            if (pipeline == null || settings == null)
            {
                Write(context, new McpResponse(404, null));
                return;
            }

            if (!TryReadBody(context.Request, settings.MaxRequestBytes, out var body))
            {
                Write(context, new McpResponse(413, null));
                return;
            }

            var response = pipeline.Handle(new HttpContextRequest(context.Request), body);
            Write(context, response);
        }

        private static bool TryReadBody(HttpRequest request, long maxBytes, out string body)
        {
            body = null;

            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[8192];
                long total = 0;
                int read;
                while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        return false;
                    }

                    buffer.Write(chunk, 0, read);
                }

                body = (request.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
                return true;
            }
        }

        private static void Write(HttpContext context, McpResponse response)
        {
            var http = context.Response;
            http.TrySkipIisCustomErrors = true;
            // Stop ASP.NET Forms Authentication from turning our 401 into a 302 -> /login.aspx.
            http.SuppressFormsAuthenticationRedirect = true;
            http.StatusCode = response.StatusCode;
            http.ContentType = "application/json";

            foreach (var header in response.Headers)
            {
                http.Headers[header.Key] = header.Value;
            }

            if (!string.IsNullOrEmpty(response.Body))
            {
                http.Write(response.Body);
            }
        }
    }
}
