namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Holds the assembled pipeline once the initialize pipeline has built it. The HTTP handler
    /// reads it per request; a null pipeline means the endpoint is disabled.
    /// </summary>
    public static class McpRuntime
    {
        /// <summary>The active pipeline, or null when the endpoint is disabled or not yet initialized.</summary>
        public static McpRequestPipeline Pipeline { get; private set; }

        /// <summary>The active settings, or null when the endpoint is disabled.</summary>
        public static McpSettings Settings { get; private set; }

        /// <summary>Publishes the assembled pipeline and settings for the handler to use.</summary>
        public static void Configure(McpRequestPipeline pipeline, McpSettings settings)
        {
            Pipeline = pipeline;
            Settings = settings;
        }
    }
}
