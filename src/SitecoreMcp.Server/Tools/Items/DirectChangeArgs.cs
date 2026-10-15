using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>
    /// Arguments for a tool that makes a direct change to one item: the item address plus the
    /// confirmation token that lets the previewed change run.
    /// </summary>
    public class DirectChangeArgs : ItemQueryArgs
    {
        /// <summary>The token from this tool's preview; omitted to request the preview.</summary>
        [McpParam(Description = DirectChangeGate.TokenDescription)]
        public string ConfirmationToken { get; set; }
    }
}
