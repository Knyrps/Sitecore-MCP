using System;

namespace SitecoreMcp.Server.Security
{
    /// <summary>A freshly signed editor token and the instant it stops being accepted.</summary>
    public sealed class IssuedEditorToken
    {
        /// <summary>Creates the value.</summary>
        public IssuedEditorToken(string token, DateTimeOffset expiresAt)
        {
            Token = token;
            ExpiresAt = expiresAt;
        }

        /// <summary>The compact token (JWT, HS256) the caller sends in the editor-token header.</summary>
        public string Token { get; }

        /// <summary>When the token expires (before clock-skew tolerance).</summary>
        public DateTimeOffset ExpiresAt { get; }
    }
}
