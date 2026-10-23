using System;
using System.Web;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// <c>POST {Mcp.EditorToken.EndpointPath}[?audience=...]</c>: issues an editor token for the
    /// Sitecore user logged in on this browser session. Meant for the panel loader running inside the
    /// Content Editor, so it authenticates by the Sitecore login cookie, not an API key.
    /// </summary>
    /// <remarks>
    /// Issuing has no side effects and the response carries no CORS headers, so another origin cannot
    /// read a token. As defence in depth the endpoint is POST-only, honours Mcp.RequireHttps, and
    /// refuses a request whose Origin is neither this host nor an allowed origin. The response is
    /// marked no-store so no cache keeps a token.
    /// </remarks>
    public sealed class EditorTokenHttpHandler : IHttpHandler
    {
        /// <inheritdoc />
        public bool IsReusable => true;

        /// <inheritdoc />
        public void ProcessRequest(HttpContext context)
        {
            var request = context.Request;
            var settings = McpRuntime.Settings;
            if (settings == null || McpRuntime.EditorTokens == null)
            {
                Write(context, 404, Problem("Editor tokens are not enabled on this instance."));
                return;
            }

            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["Allow"] = "POST";
                Write(context, 405, Problem("Only POST is supported."));
                return;
            }

            if (settings.RequireHttps && !request.IsSecureConnection)
            {
                Write(context, 403, Problem("HTTPS is required."));
                return;
            }

            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && !IsSameOrigin(origin, request.Url) && !settings.AllowedOrigins.Contains(origin))
            {
                Write(context, 403, Problem("Origin is not allowed."));
                return;
            }

            if (!EditorTokenIssuer.TryIssueForContextUser(request.QueryString["audience"], out var token, out var audience, out var error))
            {
                var status = Sitecore.Context.User != null && Sitecore.Context.User.IsAuthenticated ? 403 : 401;
                Write(context, status, Problem(error));
                return;
            }

            Write(context, 200, new JObject
            {
                ["token"] = token.Token,
                ["expiresAt"] = token.ExpiresAt.ToString("o"),
                ["audience"] = audience,
                ["user"] = Sitecore.Context.User.Name,
                ["header"] = McpHeaders.EditorToken
            });
        }

        private static bool IsSameOrigin(string origin, Uri requestUrl) =>
            Uri.TryCreate(origin, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.GetLeftPart(UriPartial.Authority), requestUrl.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

        private static JObject Problem(string detail) => new JObject { ["error"] = detail };

        private static void Write(HttpContext context, int status, JObject body)
        {
            var http = context.Response;
            http.TrySkipIisCustomErrors = true;
            http.SuppressFormsAuthenticationRedirect = true;
            http.StatusCode = status;
            http.ContentType = "application/json";
            http.Cache.SetCacheability(HttpCacheability.NoCache);
            http.Cache.SetNoStore();
            http.Write(body.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
