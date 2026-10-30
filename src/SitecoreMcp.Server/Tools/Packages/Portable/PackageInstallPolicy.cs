using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>A package item that meets an item already in the database.</summary>
    public sealed class PackageItemCollision
    {
        /// <summary>The database.</summary>
        public string Database { get; set; }
        /// <summary>The package item's ID.</summary>
        public string Id { get; set; }
        /// <summary>The package item's path.</summary>
        public string PackagePath { get; set; }
        /// <summary>"id" (same ID exists, possibly elsewhere) or "path" (another item already has that path).</summary>
        public string Kind { get; set; }
        /// <summary>The existing item's ID.</summary>
        public string ExistingId { get; set; }
        /// <summary>The existing item's path.</summary>
        public string ExistingPath { get; set; }
    }

    /// <summary>Everything the install decision depends on.</summary>
    public sealed class PackageInstallRequest
    {
        /// <summary>The package's content.</summary>
        public PackageManifest Manifest { get; set; }

        /// <summary>Whether the client may use a database.</summary>
        public Func<string, bool> CanUseDatabase { get; set; } = _ => false;

        /// <summary>Whether content roots restrict the calling client.</summary>
        public bool ContentRootsApply { get; set; }

        /// <summary>Whether an item path (database, path) is inside the client's content roots.</summary>
        public Func<string, string, bool> IsInsideContentRoots { get; set; } = (_, __) => true;

        /// <summary>The package items that meet existing items.</summary>
        public IReadOnlyList<PackageItemCollision> Collisions { get; set; } = new PackageItemCollision[0];

        /// <summary>Mcp.Packages.AllowCode: code files and post steps may be installed.</summary>
        public bool AllowCode { get; set; }

        /// <summary>The caller asked for the package's post step to run.</summary>
        public bool RunPostStep { get; set; }
    }

    /// <summary>The install decision.</summary>
    public sealed class PackageInstallDecision
    {
        /// <summary>Whether the install may go ahead (to the preview and confirmation).</summary>
        public bool Allowed => Refusals.Count == 0;
        /// <summary>Why it may not.</summary>
        public IReadOnlyList<string> Refusals { get; set; } = new string[0];
        /// <summary>What the person should know before confirming.</summary>
        public IReadOnlyList<string> Warnings { get; set; } = new string[0];
        /// <summary>Whether the post step will run.</summary>
        public bool RunsPostStep { get; set; }
    }

    /// <summary>
    /// Decides whether a package may be installed for the calling client. The rules, all checked before
    /// the preview so a refused package is never offered for confirmation:
    /// <list type="bullet">
    /// <item>Entries that would write outside the web root, and forbidden files (web.config, connection
    /// strings, license, App_Data, keys, Sitecore-MCP's own files), are always refused.</item>
    /// <item>Code (files under /bin or App_Config, *.dll, *.config, *.aspx, *.cshtml, ...) and running a post
    /// step need <c>Mcp.Packages.AllowCode</c>; a post step also has to be asked for.</item>
    /// <item>Every item's database must be on the client's allow-list.</item>
    /// <item>When content roots restrict the client, every package item must be inside them, so must any
    /// existing item the installer would touch (same ID elsewhere), and the package may hold no files.</item>
    /// </list>
    /// </summary>
    public static class PackageInstallPolicy
    {
        private const int MaxListed = 10;

        /// <summary>Evaluates the request.</summary>
        public static PackageInstallDecision Evaluate(PackageInstallRequest request)
        {
            if (request?.Manifest == null) throw new ArgumentNullException(nameof(request));
            var manifest = request.Manifest;
            var refusals = new List<string>();
            var warnings = new List<string>();

            if (manifest.UnsafeEntries.Count > 0)
            {
                refusals.Add($"The package has entries that would write outside the web root or cannot be read safely: {List(manifest.UnsafeEntries)}.");
            }

            var forbidden = manifest.Files.Where(f => f.Class == PackageFileClass.Forbidden).ToList();
            if (forbidden.Count > 0)
            {
                refusals.Add("The package contains files that are never installed: " +
                             List(forbidden.Select(f => $"{f.Path} ({f.Reason})")) + ".");
            }

            var code = manifest.Files.Where(f => f.Class == PackageFileClass.Code).ToList();
            if (code.Count > 0)
            {
                if (!request.AllowCode)
                {
                    refusals.Add($"The package contains {code.Count} code or configuration file(s) ({List(code.Select(f => f.Path))}). " +
                                 "Installing code is off on this instance: an administrator can enable it with the Mcp.Packages.AllowCode setting.");
                }
                else
                {
                    warnings.Add($"The package installs {code.Count} code or configuration file(s) ({List(code.Select(f => f.Path))}); " +
                                 "the site will restart after the install, and that code runs in the site.");
                }
            }

            var postStep = manifest.Metadata.PostStep;
            var runsPostStep = false;
            if (!string.IsNullOrWhiteSpace(postStep))
            {
                if (!request.RunPostStep)
                {
                    warnings.Add($"The package has a post step ('{postStep}') that will not run. Pass runPostStep=true to run it " +
                                 "(needs Mcp.Packages.AllowCode), or run its steps by hand.");
                }
                else if (!request.AllowCode)
                {
                    refusals.Add($"The package's post step ('{postStep}') runs code, and installing code is off on this instance " +
                                 "(Mcp.Packages.AllowCode). Install without runPostStep, or have an administrator enable it.");
                }
                else
                {
                    runsPostStep = true;
                    warnings.Add($"The package's post step ('{postStep}') will run after the install.");
                }
            }

            var blockedDatabases = manifest.Databases.Where(db => !request.CanUseDatabase(db)).ToList();
            if (blockedDatabases.Count > 0)
            {
                refusals.Add("The package installs into database(s) this client may not use: " +
                             string.Join(", ", blockedDatabases.Select(db => "'" + db + "'")) + ".");
            }

            if (request.ContentRootsApply)
            {
                var outside = manifest.Items.Where(i => !request.IsInsideContentRoots(i.Database, i.Path)).Select(i => $"{i.Database}:{i.Path}").ToList();
                if (outside.Count > 0)
                {
                    refusals.Add($"Content roots apply to this client and {outside.Count} package item(s) lie outside every content root: {List(outside)}.");
                }

                var touched = (request.Collisions ?? new PackageItemCollision[0])
                    .Where(c => !string.IsNullOrEmpty(c.ExistingPath) && !request.IsInsideContentRoots(c.Database, c.ExistingPath))
                    .Select(c => $"{c.Database}:{c.ExistingPath}")
                    .Distinct()
                    .ToList();
                if (touched.Count > 0)
                {
                    refusals.Add($"Content roots apply to this client and the install would change existing item(s) outside every content root: {List(touched)}.");
                }

                if (manifest.Files.Count > 0)
                {
                    refusals.Add($"Content roots apply to this client, so packages with files are refused (this one has {manifest.Files.Count} files).");
                }
            }

            if (manifest.Items.Count == 0 && manifest.Files.Count == 0 && manifest.BlobCount == 0)
            {
                refusals.Add("The package has nothing to install (no items and no files).");
            }

            if (manifest.SecurityEntryCount > 0)
            {
                warnings.Add($"The package contains {manifest.SecurityEntryCount} user and role entries; they are not installed by this tool.");
            }

            if (manifest.EntriesWithPackageDefinedModes > 0)
            {
                warnings.Add($"{manifest.EntriesWithPackageDefinedModes} entries carry a collision mode set by the package author; " +
                             "it is ignored and the itemCollision argument applies instead.");
            }

            return new PackageInstallDecision { Refusals = refusals, Warnings = warnings, RunsPostStep = runsPostStep };
        }

        private static string List(IEnumerable<string> values)
        {
            var all = values.ToList();
            var shown = string.Join(", ", all.Take(MaxListed));
            return all.Count > MaxListed ? $"{shown} and {all.Count - MaxListed} more" : shown;
        }
    }
}
