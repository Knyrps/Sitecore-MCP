using System;

namespace SitecoreMcp.Server.Security
{
    /// <summary>How editor tokens are stamped and how strictly their times are checked.</summary>
    public sealed class EditorTokenOptions
    {
        /// <summary>The issuer written into every token and required on every validated one.</summary>
        public string Issuer { get; set; } = "SitecoreMcp";

        /// <summary>How long an issued token stays valid. Short by design: the panel re-issues on open.</summary>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>Tolerance applied to expiry and not-before, for clock drift between CM instances.</summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);
    }
}
