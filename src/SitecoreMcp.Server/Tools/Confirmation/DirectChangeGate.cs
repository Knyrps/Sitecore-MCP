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
    /// <para>
    /// Confirmation is per client (<c>confirmDirectChanges</c>). For a client that does not confirm
    /// (by default a fixed-user client, whose own tooling asks the person before a call) the change
    /// runs on the first call, any token is ignored, and the result carries the preview's summary.
    /// Every other guard lives in the tool before the gate, so it applies either way.
    /// </para>
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

        /// <summary>The marker on a result whose change ran without confirmation, in <c>directChange.confirmation</c> and the audit log.</summary>
        public const string Skipped = "skipped";

        /// <summary>Runs the confirmation step for one direct-change call, always requiring confirmation.</summary>
        public static McpToolResult Run(
            ConfirmationService confirmations,
            string tool,
            string editor,
            object arguments,
            string confirmationToken,
            DirectChangePreview preview,
            Func<McpToolResult> change) =>
            Run(true, confirmations, tool, editor, arguments, confirmationToken, preview, change);

        /// <summary>Runs the confirmation step for one direct-change call.</summary>
        /// <param name="confirmationRequired">
        /// Whether the calling client confirms direct changes. False runs the change at once and ignores
        /// <paramref name="confirmationToken"/>.
        /// </param>
        /// <param name="confirmations">The issuing service; null fails closed.</param>
        /// <param name="tool">The tool name.</param>
        /// <param name="editor">The Sitecore user the call runs as.</param>
        /// <param name="arguments">The call's typed or raw arguments; the confirmation token among them is ignored for binding.</param>
        /// <param name="confirmationToken">The token presented with the call, or null for a preview.</param>
        /// <param name="preview">The change as worked out now (computed on both the preview and the confirming call).</param>
        /// <param name="change">The change itself; runs only after a token is accepted, or at once without confirmation.</param>
        /// <param name="recordOutcome">Told how the call went (previewed, confirmed, rejected, skipped), for the audit log.</param>
        public static McpToolResult Run(
            bool confirmationRequired,
            ConfirmationService confirmations,
            string tool,
            string editor,
            object arguments,
            string confirmationToken,
            DirectChangePreview preview,
            Func<McpToolResult> change,
            Action<DirectChangeOutcome> recordOutcome = null)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (change == null) throw new ArgumentNullException(nameof(change));
            var record = recordOutcome ?? (_ => { });

            if (!confirmationRequired)
            {
                record(DirectChangeOutcome.Skipped);
                return WithSummary(change(), preview);
            }

            if (confirmations == null)
            {
                record(DirectChangeOutcome.Unavailable);
                return McpToolResult.Failure(
                    "This is a direct change, which requires confirmation, and confirmation is not available on this server. Nothing was changed.");
            }

            var binding = new ConfirmationBinding(tool, editor, ToToken(arguments), preview.State);

            if (string.IsNullOrWhiteSpace(confirmationToken))
            {
                record(DirectChangeOutcome.Previewed);
                return PreviewResult(tool, preview, confirmations.Issue(binding));
            }

            var redemption = confirmations.Redeem(confirmationToken, binding);
            if (!redemption.Succeeded)
            {
                record(DirectChangeOutcome.Rejected);
                return RejectedResult(tool, redemption);
            }

            record(DirectChangeOutcome.Confirmed);
            return change();
        }

        /// <summary>
        /// Adds the preview's summary to the result of a change that ran without confirmation, so the
        /// model can tell the person what changed. A failed change is returned as it is.
        /// </summary>
        private static McpToolResult WithSummary(McpToolResult result, DirectChangePreview preview)
        {
            if (result == null || result.IsError) return result;

            if (result.StructuredContent == null) result.StructuredContent = new JObject();
            result.StructuredContent["directChange"] = new JObject
            {
                ["summary"] = preview.Summary,
                ["confirmation"] = Skipped
            };
            result.Content.Insert(0, McpContent.FromText("Done (no confirmation required for this client): " + preview.Summary));
            return result;
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
