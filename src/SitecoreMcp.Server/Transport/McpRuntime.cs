using SitecoreMcp.Server.Security;

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

        /// <summary>The editor-token service, or null when the endpoint is disabled or no signing secret is set.</summary>
        public static EditorTokenService EditorTokens { get; private set; }

        /// <summary>Publishes the assembled pipeline, settings, and editor-token service for the handlers to use.</summary>
        public static void Configure(McpRequestPipeline pipeline, McpSettings settings, EditorTokenService editorTokens = null)
        {
            Pipeline = pipeline;
            Settings = settings;
            EditorTokens = editorTokens;
        }
    }
}
