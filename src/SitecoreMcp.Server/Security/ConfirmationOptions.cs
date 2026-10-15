using System;

namespace SitecoreMcp.Server.Security
{
    /// <summary>How long confirmation tokens live and how many may be outstanding at once.</summary>
    public sealed class ConfirmationOptions
    {
        /// <summary>How long a preview's token can be confirmed. Short: the editor confirms right after the preview.</summary>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The most unredeemed tokens kept. Beyond it the soonest-expiring are dropped, so a caller
        /// that previews in a loop cannot grow the store without bound.
        /// </summary>
        public int MaxOutstanding { get; set; } = 10000;
    }
}
