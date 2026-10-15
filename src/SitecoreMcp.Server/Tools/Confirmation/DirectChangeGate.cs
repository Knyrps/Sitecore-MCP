using System;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Tools.Confirmation
{
    /// <summary>
    /// The server-enforced confirmation step every direct-change tool goes through. Called without a
    /// confirmation token it returns the preview plus a single-use token and changes nothing; called
    /// with the token from that preview - same tool, editor, arguments and target state - it runs the
    /// change. Anything else is refused before the change runs.
    /// </summary>
    /// <remarks>
    /// The token travels only in <c>structuredContent.confirmation</c>, never in the text content, so a
    /// client (the CorePilot App) can show the preview to the model while withholding the token until
    /// the editor confirms. Sitecore-free; the tool supplies the editor, the preview and the change.
    /// </remarks>
    public static class DirectChangeGate
    {
        /// <summary>The model-facing description of the confirmationToken argument on every direct-change tool.</summary>
        public const string TokenDescription =
            "Confirmation token from this tool's preview. Omit it: the call then returns a preview and changes " +
            "nothing. The change runs only when the same call, with identical arguments, carries the token the " +
            "editor confirmed. Tokens are single-use and expire.";

        /// <summary>Runs the confirmation step for one direct-change call.</summary>
        /// <param name="confirmations">The issuing service; null fails closed.</param>
        /// <param name="tool">The tool name.</param>
        /// <param name="editor">The Sitecore user the call runs as.</param>
        /// <param name="arguments">The call's typed or raw arguments; the confirmation token among them is ignored for binding.</param>
        /// <param name="confirmationToken">The token presented with the call, or null for a preview.</param>
        /// <param name="preview">The change as worked out now (computed on both the preview and the confirming call).</param>
        /// <param name="change">The change itself; runs only after a token is accepted.</param>
        public static McpToolResult Run(
            ConfirmationService confirmations,
            string tool,
            string editor,
            object arguments,
            string confirmationToken,
            DirectChangePreview preview,
            Func<McpToolResult> change)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (change == null) throw new ArgumentNullException(nameof(change));

            if (confirmations == null)
            {
                return McpToolResult.Failure(
                    "This is a direct change, which requires confirmation, and confirmation is not available on this server. Nothing was changed.");
            }

            var binding = new ConfirmationBinding(tool, editor, ToToken(arguments), preview.State);

            if (string.IsNullOrWhiteSpace(confirmationToken))
            {
                return PreviewResult(tool, preview, confirmations.Issue(binding));
            }

            var redemption = confirmations.Redeem(confirmationToken, binding);
            if (!redemption.Succeeded)
            {
                return RejectedResult(tool, redemption);
            }

            return change();
        }

        private static McpToolResult PreviewResult(string tool, DirectChangePreview preview, IssuedConfirmation issued)
        {
            var payload = new JObject
            {
                ["status"] = "confirmationRequired",
                ["tool"] = tool,
                ["summary"] = preview.Summary,
                ["preview"] = preview.Details,
                ["confirmation"] = new JObject
                {
                    ["token"] = issued.Token,
                    ["expiresAt"] = new JValue(issued.ExpiresAt),
                    ["argument"] = ConfirmationCanonicalizer.TokenArgument
                }
            };

            return new McpToolResult
            {
                StructuredContent = payload,
                Content =
                {
                    McpContent.FromText(
                        "Confirmation required: " + preview.Summary + "\n\n" +
                        "Nothing has been changed. This is a direct change that takes effect immediately, so the editor " +
                        "must confirm it first. Do not retry on your own: once the editor confirms, the same call is " +
                        "repeated with the confirmation token.")
                }
            };
        }

        private static McpToolResult RejectedResult(string tool, ConfirmationRedemption redemption) => new McpToolResult
        {
            IsError = true,
            StructuredContent = new JObject
            {
                ["status"] = "confirmationRejected",
                ["tool"] = tool,
                ["reason"] = Reason(redemption.Failure)
            },
            Content = { McpContent.FromText(redemption.Message + " Nothing was changed.") }
        };

        private static string Reason(ConfirmationFailure failure)
        {
            switch (failure)
            {
                case ConfirmationFailure.Missing: return "missing";
                case ConfirmationFailure.Malformed: return "malformed";
                case ConfirmationFailure.Expired: return "expired";
                case ConfirmationFailure.AlreadyUsed: return "alreadyUsed";
                default: return "mismatch";
            }
        }

        private static JToken ToToken(object arguments)
        {
            if (arguments == null) return new JObject();
            return arguments as JToken ?? JToken.FromObject(arguments);
        }
    }
}
