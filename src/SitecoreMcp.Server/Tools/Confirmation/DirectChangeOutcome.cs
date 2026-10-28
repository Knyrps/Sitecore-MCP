namespace SitecoreMcp.Server.Tools.Confirmation
{
    /// <summary>How one direct-change call went through <see cref="DirectChangeGate"/>, for the audit log.</summary>
    public enum DirectChangeOutcome
    {
        /// <summary>The client confirms direct changes, no token came: a preview and a token were returned, nothing changed.</summary>
        Previewed,

        /// <summary>A valid token from the preview came with the call, and the change ran.</summary>
        Confirmed,

        /// <summary>A token came with the call but was refused (expired, used, mismatched, made up); nothing changed.</summary>
        Rejected,

        /// <summary>The client does not confirm direct changes, so the change ran on the first call.</summary>
        Skipped,

        /// <summary>The client confirms direct changes but the server has no confirmation service; nothing changed.</summary>
        Unavailable
    }
}
