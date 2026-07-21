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
        /// <summary>Assembles and publishes the pipeline, then maps its route, unless the module is disabled.</summary>
        public void Process(PipelineArgs args)
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

            McpRuntime.Configure(pipeline, settings);
            RegisterRoute(settings.EndpointPath);
            McpLog.Info($"Endpoint registered at '{settings.EndpointPath}'.");
        }

        private static void RegisterRoute(string endpointPath)
        {
            using (RouteTable.Routes.GetWriteLock())
            {
                if (RouteTable.Routes["SitecoreMcp"] != null)
                {
                    return;
                }

                var route = new Route(endpointPath, new McpRouteHandler());
                RouteTable.Routes.Add("SitecoreMcp", route);
            }
        }
    }
}
