using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>
    /// Formats the AUDIT line written for every authenticated call. Sitecore-free so the format,
    /// including the confirmation marker on direct changes, is unit-testable.
    /// </summary>
    public static class McpAuditLine
    {
        /// <summary>Builds the audit line for one call.</summary>
        /// <param name="userName">The effective Sitecore user.</param>
        /// <param name="clientId">The client's config id; null for a client built in code.</param>
        /// <param name="isEditor">Whether the call ran as the editor named by an editor token.</param>
        /// <param name="method">The JSON-RPC method.</param>
        /// <param name="target">The tool name for tools/call, else the method.</param>
        /// <param name="status">The HTTP status returned.</param>
        /// <param name="durationMs">How long dispatch took.</param>
        /// <param name="directChange">
        /// How a direct change went through confirmation, or null when the call made none. Appended as
        /// <c>confirmed=pending|yes|rejected|skipped|unavailable</c>; <c>skipped</c> means the client does
        /// not confirm direct changes and the change ran without any confirmation.
        /// </param>
        public static string Format(string userName, string clientId, bool isEditor, string method, string target,
            int status, long durationMs, DirectChangeOutcome? directChange)
        {
            var line =
                $"user={userName} client={clientId ?? "(unnamed)"} " +
                $"identity={(isEditor ? "editor-token" : "client-user")} " +
                $"method={method} target={target} " +
                $"status={status} durationMs={durationMs}";

            return directChange.HasValue ? line + " confirmed=" + Marker(directChange.Value) : line;
        }

        private static string Marker(DirectChangeOutcome outcome)
        {
            switch (outcome)
            {
                case DirectChangeOutcome.Previewed: return "pending";
                case DirectChangeOutcome.Confirmed: return "yes";
                case DirectChangeOutcome.Rejected: return "rejected";
                case DirectChangeOutcome.Skipped: return DirectChangeGate.Skipped;
                default: return "unavailable";
            }
        }
    }
}
