using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Shared arguments for tools that address a single item by path or ID.</summary>
    public class ItemQueryArgs
    {
        /// <summary>The item's full path or ID (a GUID in braces).</summary>
        [McpParam(Description = "Item path (e.g. /sitecore/content/Home) or ID in braces.", Required = true)]
        public string Path { get; set; }

        /// <summary>The database to read from; defaults to master when omitted.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }

        /// <summary>The language version to read; defaults to the context language.</summary>
        [McpParam(Description = "Language code (e.g. 'en'). Defaults to the context language.")]
        public string Language { get; set; }
    }
}
