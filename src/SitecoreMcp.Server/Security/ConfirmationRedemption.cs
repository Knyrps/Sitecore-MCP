using System;

namespace SitecoreMcp.Server.Security
{
    /// <summary>Why a confirmation token was refused. Safe to report: none of these leak the signing key.</summary>
    public enum ConfirmationFailure
    {
        /// <summary>The token was accepted and is now spent.</summary>
        None = 0,

        /// <summary>No token was presented.</summary>
        Missing,

        /// <summary>The token is not in the confirmation-token format.</summary>
        Malformed,

        /// <summary>
        /// The token was issued for a different change - other arguments, another tool or editor, or
        /// the target changed since the preview - or was not issued by this server.
        /// </summary>
        Mismatch,

        /// <summary>The token's lifetime has passed.</summary>
        Expired,

        /// <summary>The token was already used (or dropped from the store); tokens are single-use.</summary>
        AlreadyUsed
    }

    /// <summary>A freshly issued confirmation token and the instant it stops being accepted.</summary>
    public sealed class IssuedConfirmation
    {
        /// <summary>Creates the value.</summary>
        public IssuedConfirmation(string token, DateTimeOffset expiresAt)
        {
            Token = token;
            ExpiresAt = expiresAt;
        }

        /// <summary>The opaque token the caller sends back as the confirmationToken argument.</summary>
        public string Token { get; }

        /// <summary>When the token expires.</summary>
        public DateTimeOffset ExpiresAt { get; }
    }

    /// <summary>The outcome of redeeming a confirmation token.</summary>
    public sealed class ConfirmationRedemption
    {
        private ConfirmationRedemption(ConfirmationFailure failure)
        {
            Failure = failure;
        }

        /// <summary>Whether the token was accepted (and consumed).</summary>
        public bool Succeeded => Failure == ConfirmationFailure.None;

        /// <summary>Why the token was refused, or <see cref="ConfirmationFailure.None"/>.</summary>
        public ConfirmationFailure Failure { get; }

        /// <summary>A sentence for the caller explaining the refusal; null on success.</summary>
        public string Message
        {
            get
            {
                switch (Failure)
                {
                    case ConfirmationFailure.None: return null;
                    case ConfirmationFailure.Missing: return "No confirmation token was presented.";
                    case ConfirmationFailure.Malformed: return "The confirmation token is malformed.";
                    case ConfirmationFailure.Expired: return "The confirmation token has expired. Request a new preview and confirm again.";
                    case ConfirmationFailure.AlreadyUsed: return "The confirmation token was already used. Each token confirms one change once; request a new preview.";
                    default:
                        return "The confirmation token does not match this change: the arguments, the editor or the target " +
                               "differ from the preview it was issued for. Request a new preview and confirm again.";
                }
            }
        }

        internal static readonly ConfirmationRedemption Success = new ConfirmationRedemption(ConfirmationFailure.None);

        internal static ConfirmationRedemption Fail(ConfirmationFailure failure) => new ConfirmationRedemption(failure);
    }
}
