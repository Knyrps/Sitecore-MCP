using System;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// A tool failure the model should see and can act on, such as a missing item or a disallowed
    /// database. Carries a clean message; the catalog turns it into an isError result rather than
    /// a protocol fault.
    /// </summary>
    public sealed class McpToolException : Exception
    {
        /// <summary>Creates the exception with a message written for the model.</summary>
        public McpToolException(string message) : base(message)
        {
        }
    }
}
