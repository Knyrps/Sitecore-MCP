using System;
using System.Web.Routing;
using Sitecore.Configuration;
using Sitecore.Pipelines;
using SitecoreMcp.Server.Diagnostics;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Builds the pipeline from config and registers the endpoint route during Sitecore
    /// initialization. When the endpoint is disabled, it registers nothing, so a disabled instance
    /// is indistinguishable from one without the module installed.
    /// </summary>
    public sealed class McpInitializeProcessor
    {
        /// <summary>
        /// Assembles and publishes the pipeline, then maps its route, unless the module is disabled.
        /// A failure here disables the endpoint but must never crash Sitecore startup, so everything
        /// is caught: a bad MCP config leaves the endpoint unregistered, not the site down.
        /// </summary>
        public void Process(PipelineArgs args)
        {
            try
            {
                var configuration = Factory.CreateObject("sitecoreMcp", true) as McpConfiguration;
                if (configuration == null)
                {
                    McpLog.Warn("No <sitecoreMcp> configuration found; endpoint not registered.");
                    return;
                }

                var (pipeline, settings) = configuration.Build();
                if (!settings.Enabled)
                {
                    McpLog.Info("Endpoint disabled (Mcp.Enabled=false); not registering route.");
                    return;
                }

                McpRuntime.Configure(pipeline, settings, configuration.EditorTokens);
                RegisterRoute("SitecoreMcp", settings.EndpointPath, new McpRouteHandler());
                McpLog.Info($"Endpoint registered at '{settings.EndpointPath}'.");

                // The issuing endpoint exists only when a client needs editor tokens and they can be signed.
                if (configuration.EditorTokens != null && settings.EditorTokenAudiences.Count > 0)
                {
                    RegisterRoute("SitecoreMcp.EditorToken", settings.EditorTokenEndpointPath, new EditorTokenRouteHandler());
                    McpLog.Info($"Editor-token endpoint registered at '{settings.EditorTokenEndpointPath}'.");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error("Failed to initialize the MCP endpoint; it will be unavailable. Sitecore startup continues.", ex);
            }
        }

        private static void RegisterRoute(string name, string path, IRouteHandler handler)
        {
            using (RouteTable.Routes.GetWriteLock())
            {
                if (RouteTable.Routes[name] != null)
                {
                    return;
                }

                RouteTable.Routes.Add(name, new Route(path, handler));
            }
        }
    }
}
