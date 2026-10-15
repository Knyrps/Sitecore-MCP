using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Tools.Confirmation
{
    /// <summary>
    /// What a direct change will do, worked out before anything changes: a sentence for the editor,
    /// the machine-readable detail behind it, and a fingerprint of the target as it looks now.
    /// </summary>
    public sealed class DirectChangePreview
    {
        /// <summary>Creates the preview.</summary>
        /// <param name="summary">One human-readable sentence, e.g. "Delete '/sitecore/content/Home/About' and its 3 descendants."</param>
        /// <param name="details">Structured detail for the confirmation UI (paths, counts, before/after values).</param>
        /// <param name="state">
        /// A fingerprint of the target (e.g. item ID, path and revision). The confirmation token is bound
        /// to it, so a change made to the target after the preview invalidates the token.
        /// </param>
        public DirectChangePreview(string summary, JObject details, string state)
        {
            Summary = summary ?? string.Empty;
            Details = details ?? new JObject();
            State = state ?? string.Empty;
        }

        /// <summary>The human-readable sentence describing the change.</summary>
        public string Summary { get; }

        /// <summary>The structured detail of the change.</summary>
        public JObject Details { get; }

        /// <summary>The target's fingerprint at preview time.</summary>
        public string State { get; }
    }
}
