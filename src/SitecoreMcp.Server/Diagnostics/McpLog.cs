using System;
using log4net;

namespace SitecoreMcp.Server.Diagnostics
{
    /// <summary>
    /// The single entry point for all MCP logging. Everything is written through one dedicated
    /// log4net logger so it lands in mcp.log and never in the main Sitecore log.
    /// </summary>
    public static class McpLog
    {
        private static readonly ILog Log = LogManager.GetLogger("SitecoreMcp");

        /// <summary>Writes an informational message.</summary>
        public static void Info(string message) => Log.Info(message);

        /// <summary>Writes a warning.</summary>
        public static void Warn(string message) => Log.Warn(message);

        /// <summary>Writes an error, with the exception detail when one is supplied.</summary>
        public static void Error(string message, Exception exception = null)
        {
            if (exception == null) Log.Error(message);
            else Log.Error(message, exception);
        }

        /// <summary>Writes an audit entry, tagged so the forensic trail is greppable within mcp.log.</summary>
        public static void Audit(string message) => Log.Info("AUDIT " + message);
    }
}
