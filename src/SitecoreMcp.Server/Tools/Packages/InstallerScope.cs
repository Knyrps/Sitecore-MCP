using System;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Sites;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>
    /// The context Sitecore's install API expects: the shell site with the core database, as in the Package
    /// Designer and the Installation Wizard. <c>Sitecore.Install.Constants.Database</c> is <c>Context.Database</c>,
    /// and <c>InstallerObjectFactory</c> (behind <c>IOUtils</c> and the package writer) reads its type registry from
    /// <c>/sitecore/system/modules/installer/objects</c> there in a static constructor. On <c>/sitecore/api/mcp</c>
    /// the context site is a content site, so that read returns nothing and the type initializer fails - and stays
    /// failed for the life of the app domain.
    /// </summary>
    internal sealed class InstallerScope : IDisposable
    {
        /// <summary>The site whose context the install API runs in.</summary>
        public const string SiteName = "shell";

        private readonly SiteContextSwitcher _site;
        private readonly DatabaseSwitcher _database;

        private InstallerScope()
        {
            var shell = SiteContextFactory.GetSiteContext(SiteName);
            if (shell != null) _site = new SiteContextSwitcher(shell);

            // Explicit, because Context.Database prefers a database set on the request over the site's.
            var core = Factory.GetDatabase("core", false);
            if (core == null) throw new McpToolException("The core database is not available, and Sitecore's install API needs it.");
            _database = new DatabaseSwitcher(core);
        }

        /// <summary>Enters the install API's context until disposed.</summary>
        public static InstallerScope Enter() => new InstallerScope();

        /// <summary>
        /// Turns the cached failure of an install API type initializer (from a call made before this fix, or a
        /// core database without the installer items) into an actionable message.
        /// </summary>
        public static Exception Explain(Exception ex)
        {
            for (var inner = ex; inner != null; inner = inner.InnerException)
            {
                if (inner is TypeInitializationException init && init.TypeName != null && init.TypeName.StartsWith("Sitecore.Install", StringComparison.Ordinal))
                {
                    return new McpToolException(
                        $"Sitecore's install API failed to initialize ({init.TypeName}). That failure is cached until the app " +
                        "domain restarts: recycle the CM's app pool once, then try again. If it fails again, check that the core " +
                        "database has /sitecore/system/Modules/Installer/Objects.");
                }
            }

            return null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _database.Dispose();
            _site?.Dispose();
        }
    }
}
