using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Exactly what a confirmation token stands for: one tool, called by one editor, with one set of
    /// arguments, against the target as it looked when the preview was made. A token redeems only
    /// for a binding equal to the one it was issued for.
    /// </summary>
    public sealed class ConfirmationBinding
    {
        /// <summary>Creates the binding.</summary>
        /// <param name="tool">The tool name, e.g. "sitecore_delete_item".</param>
        /// <param name="editor">The Sitecore user the call runs as, e.g. "sitecore\jane". Compared case-insensitively.</param>
        /// <param name="arguments">The call's arguments; any confirmation token among them is ignored.</param>
        /// <param name="state">
        /// A fingerprint of the target as previewed (e.g. item ID, path and revision), so a token is
        /// refused if the item changed between preview and confirmation. May be empty.
        /// </param>
        public ConfirmationBinding(string tool, string editor, JToken arguments, string state)
        {
            Tool = tool ?? string.Empty;
            Editor = editor ?? string.Empty;
            Arguments = arguments;
            State = state ?? string.Empty;
        }

        /// <summary>The tool name.</summary>
        public string Tool { get; }

        /// <summary>The editor the call runs as.</summary>
        public string Editor { get; }

        /// <summary>The call's arguments.</summary>
        public JToken Arguments { get; }

        /// <summary>The previewed target's fingerprint.</summary>
        public string State { get; }
    }
}
