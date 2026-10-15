namespace SitecoreMcp.Server.Security
{
    /// <summary>Why an editor token was refused. Safe to report to the caller: none of these leak the secret.</summary>
    public enum EditorTokenFailure
    {
        /// <summary>The token was accepted.</summary>
        None = 0,

        /// <summary>No token was presented.</summary>
        Missing,

        /// <summary>The token is not a well-formed HS256 JWT carrying the required claims.</summary>
        Malformed,

        /// <summary>The signature does not match: the token was tampered with or signed with another secret.</summary>
        InvalidSignature,

        /// <summary>The token was issued by someone else.</summary>
        WrongIssuer,

        /// <summary>The token was issued for a different client.</summary>
        WrongAudience,

        /// <summary>The token's lifetime has passed.</summary>
        Expired,

        /// <summary>The token is not valid yet.</summary>
        NotYetValid,

        /// <summary>No signing secret is configured, so no token can be trusted.</summary>
        NotConfigured
    }

    /// <summary>The outcome of validating an editor token: the editor it names, or why it was refused.</summary>
    public sealed class EditorTokenValidation
    {
        private EditorTokenValidation(EditorTokenFailure failure, string userName, bool isAdmin)
        {
            Failure = failure;
            UserName = userName;
            IsAdmin = isAdmin;
        }

        /// <summary>Whether the token was accepted.</summary>
        public bool Succeeded => Failure == EditorTokenFailure.None;

        /// <summary>Why the token was refused, or <see cref="EditorTokenFailure.None"/>.</summary>
        public EditorTokenFailure Failure { get; }

        /// <summary>The Sitecore user name the token was issued for (e.g. "sitecore\jane"); null on failure.</summary>
        public string UserName { get; }

        /// <summary>
        /// The admin claim as issued. Informational only: the server takes the admin decision from the
        /// live Sitecore user at call time, so a demoted editor loses admin tools before the token expires.
        /// </summary>
        public bool IsAdmin { get; }

        internal static EditorTokenValidation Success(string userName, bool isAdmin) =>
            new EditorTokenValidation(EditorTokenFailure.None, userName, isAdmin);

        internal static EditorTokenValidation Fail(EditorTokenFailure failure) =>
            new EditorTokenValidation(failure, null, false);
    }
}
