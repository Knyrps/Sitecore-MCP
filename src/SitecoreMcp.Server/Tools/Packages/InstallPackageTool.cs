using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Events;
using Sitecore.Install;
using Sitecore.Install.Events;
using Sitecore.Install.Files;
using Sitecore.Install.Framework;
using Sitecore.Install.Items;
using Sitecore.Install.Utils;
using Sitecore.Install.Zip;
using Sitecore.Jobs;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Confirmation;
using SitecoreMcp.Server.Tools.Jobs;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>Arguments for <see cref="InstallPackageTool"/>.</summary>
    public sealed class InstallPackageArgs
    {
        /// <summary>The package file name.</summary>
        [McpParam(Required = true, Description = "Package file name in the packages folder (from sitecore_list_packages); '.zip' may be omitted.")]
        public string Name { get; set; }

        /// <summary>What happens to items that already exist.</summary>
        [McpParam(Description =
            "What happens to an item that already exists (same ID, or another item at the same path), as in the Installation Wizard: " +
            "skip (default: leave it untouched, install only new items), merge (replace versions with the same number), mergeAppend " +
            "(add the package's versions as new versions), mergeClear (replace all versions), overwrite (replace the item and DELETE " +
            "its children that are not in the package), sideBySide (add the package's item next to it).",
            Enum = new[] { "skip", "merge", "mergeAppend", "mergeClear", "overwrite", "sideBySide" })]
        public string ItemCollision { get; set; }

        /// <summary>What happens to files that already exist.</summary>
        [McpParam(Description = "What happens to a file that already exists: skip (default, keep the existing file) or overwrite.", Enum = new[] { "skip", "overwrite" })]
        public string FileCollision { get; set; }

        /// <summary>Whether to run the package's post step.</summary>
        [McpParam(Description = "Run the package's post step after installing (it executes code; needs the Mcp.Packages.AllowCode setting). Defaults to false.")]
        public bool? RunPostStep { get; set; }

        /// <summary>The token from this tool's preview.</summary>
        [McpParam(Description =
            "Confirmation token from this tool's preview. Package installs ALWAYS need confirmation, for every client: omit it to get " +
            "the preview (what would be installed, what already exists) and change nothing; the install runs only when the same call, " +
            "with identical arguments, carries the token a person confirmed. Tokens are single-use and expire.")]
        public string ConfirmationToken { get; set; }
    }

    /// <summary>
    /// Installs a package from the packages folder with Sitecore's own <see cref="Installer"/>, as the
    /// Installation Wizard does, but with the collision behaviour decided up front (default skip) and
    /// author-set collision modes stripped. Always a confirmed direct change: an install can create and
    /// replace items anywhere the package says and, with Mcp.Packages.AllowCode, deploy code and restart
    /// the site, so a client's confirmDirectChanges="false" does not apply to it.
    /// </summary>
    public sealed class InstallPackageTool : McpTool<InstallPackageArgs>, IAlwaysConfirmedDirectChange
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        /// <inheritdoc />
        public override string Name => "sitecore_install_package";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Install a Sitecore package from the packages folder, as the Installation Wizard does. Existing items and files are " +
            "skipped unless itemCollision/fileCollision say otherwise. ALWAYS a confirmed direct change, for every client: called " +
            "without confirmationToken it validates the package (databases, content roots, code files, post step) and returns a " +
            "preview with what already exists, changing nothing; the install runs once a person has confirmed. Long installs " +
            "continue as a background job (poll sitecore_get_jobs). Run sitecore_inspect_package first. Administrator only.";

        /// <summary>Builds the policy request for a package and this call's client.</summary>
        internal static PackageInstallRequest Request(PackageManifest manifest, IReadOnlyList<PackageItemCollision> collisions,
            McpCallContext context, bool runPostStep) => new PackageInstallRequest
        {
            Manifest = manifest,
            CanUseDatabase = context.Client.CanUseDatabase,
            ContentRootsApply = context.Scope.ContentRoots != null,
            IsInsideContentRoots = (database, path) => context.ContentRoots.RootOf(database, path) != null,
            Collisions = collisions,
            AllowCode = context.Settings.PackagesAllowCode,
            RunPostStep = runPostStep
        };

        /// <inheritdoc />
        protected override McpToolResult Execute(InstallPackageArgs args, McpCallContext context)
        {
            if (!PackageCollisionModes.TryParseItemMode(args.ItemCollision, out var itemMode, out var error)) throw new McpToolException(error);
            if (!PackageCollisionModes.TryParseFileMode(args.FileCollision, out var overwriteFiles, out error)) throw new McpToolException(error);

            var path = PackageStore.Resolve(args.Name, mustExist: true);
            var file = new FileInfo(path);
            var manifest = PackageManifest.FromEntries(PackageStore.ReadEntries(path));
            var itemCollisions = PackageStore.ItemCollisions(manifest);
            var fileCollisions = PackageStore.FileCollisions(manifest);

            var decision = PackageInstallPolicy.Evaluate(Request(manifest, itemCollisions, context, args.RunPostStep.GetValueOrDefault()));
            if (!decision.Allowed)
            {
                throw new McpToolException($"'{file.Name}' cannot be installed for this client. Nothing was changed.\n- " +
                                           string.Join("\n- ", decision.Refusals));
            }

            var preview = Preview(file, manifest, itemCollisions, fileCollisions, itemMode, overwriteFiles, decision);

            return context.ConfirmDirectChangeAlways(Name, args, args.ConfirmationToken, preview, () =>
                RunInstall(context, file, manifest, itemMode, overwriteFiles, decision));
        }

        private static DirectChangePreview Preview(FileInfo file, PackageManifest manifest, IReadOnlyList<PackageItemCollision> items,
            IReadOnlyList<string> files, ItemCollisionMode itemMode, bool overwriteFiles, PackageInstallDecision decision)
        {
            var label = manifest.Metadata.Name ?? file.Name;
            if (!string.IsNullOrEmpty(manifest.Metadata.Version)) label += " " + manifest.Metadata.Version;

            var parts = new List<string>();
            if (manifest.Items.Count > 0)
            {
                parts.Add($"{manifest.Items.Count} item(s) into {string.Join(", ", manifest.Databases)}" +
                          (items.Count > 0 ? $" ({items.Count} already exist: {itemMode.Name} - {itemMode.Description})" : string.Empty));
            }

            if (manifest.Files.Count > 0)
            {
                parts.Add($"{manifest.Files.Count} file(s)" +
                          (files.Count > 0 ? $" ({files.Count} already exist: {(overwriteFiles ? "overwritten" : "kept")})" : string.Empty));
            }

            if (manifest.BlobCount > 0) parts.Add($"{manifest.BlobCount} media blob(s)");

            var summary = $"Install package '{label}' ({file.Name}): {string.Join(", ", parts)}." +
                          (decision.RunsPostStep ? " The post step runs afterwards." : string.Empty) +
                          (manifest.Files.Any(f => f.Class == PackageFileClass.Code) ? " Code files are installed and the site will restart." : string.Empty);

            var details = new JObject
            {
                ["package"] = file.Name,
                ["sizeBytes"] = file.Length,
                ["itemCollision"] = new JObject { ["mode"] = itemMode.Name, ["meaning"] = itemMode.Description },
                ["fileCollision"] = overwriteFiles ? "overwrite" : "skip",
                ["runsPostStep"] = decision.RunsPostStep,
                ["manifest"] = PackageDescriber.Manifest(manifest),
                ["collisions"] = PackageDescriber.Collisions(items, files),
                ["warnings"] = new JArray(decision.Warnings.Cast<object>().ToArray())
            };

            // Bound to the package file as it is now: replacing the zip after the preview invalidates the token.
            var state = $"{file.Name}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            return new DirectChangePreview(summary, details, state);
        }

        private static McpToolResult RunInstall(McpCallContext context, FileInfo file, PackageManifest manifest,
            ItemCollisionMode itemMode, bool overwriteFiles, PackageInstallDecision decision)
        {
            var runner = new Runner(file.FullName, itemMode, overwriteFiles, decision.RunsPostStep);
            var options = new DefaultJobOptions(
                "MCP_InstallPackage_" + file.Name,
                "packageInstall",
                Sitecore.Context.Site?.Name ?? "shell",
                runner,
                nameof(Runner.Run))
            {
                // Run as the calling administrator with security on: no SecurityDisabler, unlike the wizard.
                ContextUser = context.User,
                EnableSecurity = true,
                AfterLife = TimeSpan.FromMinutes(30)
            };

            McpLog.Info($"Package install started: {file.Name} ({file.Length} bytes) by {context.User?.Name}, items={itemMode.Name}, " +
                        $"files={(overwriteFiles ? "overwrite" : "skip")}, postStep={decision.RunsPostStep}.");

            var job = JobManager.Start(options);
            var deadline = DateTime.UtcNow + Wait;
            while (!job.IsDone && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
            }

            var result = new JObject
            {
                ["package"] = file.Name,
                ["job"] = JobDescriber.Describe(job),
                ["finished"] = job.IsDone,
                ["itemCollision"] = itemMode.Name,
                ["fileCollision"] = overwriteFiles ? "overwrite" : "skip"
            };

            if (!job.IsDone)
            {
                result["note"] = "The install is still running as a background job. Poll sitecore_get_jobs with the job handle; " +
                                 "when it is done, check the items (sitecore_get_item) and the Sitecore log.";
                result["postInstall"] = PostInstall(manifest, decision);
                return McpToolResult.Structured(result);
            }

            if (runner.Failure != null)
            {
                McpLog.Warn($"Package install failed: {file.Name}: {runner.Failure.Message}");
                return new McpToolResult
                {
                    IsError = true,
                    StructuredContent = result,
                    Content =
                    {
                        McpContent.FromText($"Installing '{file.Name}' failed: {runner.Failure.Message}. Part of the package may already be " +
                                            "installed; check the items and the Sitecore log.")
                    }
                };
            }

            result["installed"] = new JObject
            {
                ["itemEntries"] = runner.ItemEntries,
                ["fileEntries"] = runner.FileEntries,
                ["blobEntries"] = runner.BlobEntries,
                ["items"] = manifest.Items.Count,
                ["files"] = manifest.Files.Count
            };
            result["postStepRan"] = runner.PostStepRan;
            result["postInstall"] = PostInstall(manifest, decision);
            McpLog.Info($"Package install finished: {file.Name}: {runner.ItemEntries} item and {runner.FileEntries} file entries processed.");
            return McpToolResult.Structured(result);
        }

        private static JArray PostInstall(PackageManifest manifest, PackageInstallDecision decision)
        {
            var steps = new List<string>();
            if (manifest.Items.Any(i => i.Database.Equals("master", StringComparison.OrdinalIgnoreCase)))
                steps.Add("The items were installed into master: publish them (sitecore_publish_item) to make them live.");
            if (manifest.Items.Count > 0)
                steps.Add("Refresh the search indexes for the installed paths if search should find them (sitecore_rebuild_index with rootPath).");
            if (manifest.Files.Any(f => f.Class == PackageFileClass.Code))
                steps.Add("Code or configuration files were installed: the site restarts (expect a short outage) and must be checked afterwards.");
            if (!string.IsNullOrWhiteSpace(manifest.Metadata.PostStep) && !decision.RunsPostStep)
                steps.Add($"The package's post step ('{manifest.Metadata.PostStep}') did not run; run its steps by hand if they are needed.");
            if (manifest.SecurityEntryCount > 0)
                steps.Add("The package's users and roles were not installed; create them with the membership tools if needed.");
            if (!string.IsNullOrWhiteSpace(manifest.Metadata.Readme))
                steps.Add("Read the package readme (manifest.metadata.readme in sitecore_inspect_package) for the author's instructions.");
            return new JArray(steps.Cast<object>().ToArray());
        }

        /// <summary>Runs the install on the job thread, recording what happened for the result.</summary>
        public sealed class Runner
        {
            private readonly string _path;
            private readonly ItemCollisionMode _itemMode;
            private readonly bool _overwriteFiles;
            private readonly bool _runPostStep;

            /// <summary>Creates the runner.</summary>
            public Runner(string path, ItemCollisionMode itemMode, bool overwriteFiles, bool runPostStep)
            {
                _path = path;
                _itemMode = itemMode;
                _overwriteFiles = overwriteFiles;
                _runPostStep = runPostStep;
            }

            /// <summary>The exception that stopped the install, if any.</summary>
            public Exception Failure { get; private set; }

            /// <summary>Item version entries handed to the installer.</summary>
            public int ItemEntries { get; private set; }

            /// <summary>File entries handed to the installer.</summary>
            public int FileEntries { get; private set; }

            /// <summary>Blob entries handed to the installer.</summary>
            public int BlobEntries { get; private set; }

            /// <summary>Whether the post step ran.</summary>
            public bool PostStepRan { get; private set; }

            /// <summary>Invoked by the job engine.</summary>
            public void Run()
            {
                try
                {
                    var installMode = (InstallMode)Enum.Parse(typeof(InstallMode), _itemMode.InstallMode);
                    var mergeMode = (MergeMode)Enum.Parse(typeof(MergeMode), _itemMode.MergeMode);

                    var context = Installer.CreateInstallationContext();
                    context.AddAspect<IItemInstallerEvents>(new DefaultItemInstallerEvents(new BehaviourOptions(installMode, mergeMode)));
                    context.AddAspect<IFileInstallerEvents>(new DefaultFileInstallerEvents(_overwriteFiles));

                    var installer = new Installer();
                    installer.InstallPackage(_path, true, new CallerModesOnly(new PackageReader(_path), this), context);

                    if (_runPostStep)
                    {
                        // ExecutePostStep raises packageinstall:poststep:* and packageinstall:ended itself.
                        installer.ExecutePostStep(Installer.GetPostStep(context), context);
                        PostStepRan = true;
                    }
                    else
                    {
                        // The wizard raises this when it finishes; listeners (cache clearing) expect it.
                        Event.RaiseEvent("packageinstall:ended",
                            new InstallationEventArgs(new List<ItemUri>(), new List<FileCopyInfo>(), "packageinstall:ended"));
                    }
                }
                catch (Exception ex)
                {
                    Failure = ex;
                    Sitecore.Diagnostics.Log.Error("Sitecore-MCP package install failed: " + _path, ex, this);
                }
            }

            private void Count(string key)
            {
                if (key.StartsWith(Constants.ItemsPrefix, StringComparison.OrdinalIgnoreCase)) ItemEntries++;
                else if (key.StartsWith(Constants.FilesPrefix, StringComparison.OrdinalIgnoreCase)) FileEntries++;
                else if (key.StartsWith(Constants.BlobDataPrefix, StringComparison.OrdinalIgnoreCase)) BlobEntries++;
            }

            /// <summary>
            /// Hands the package's entries to the installer without the collision modes the package author
            /// may have fixed on them (the installer prefers those over the caller's choice), counting them.
            /// </summary>
            private sealed class CallerModesOnly : ISource<PackageEntry>
            {
                private readonly ISource<PackageEntry> _inner;
                private readonly Runner _runner;

                public CallerModesOnly(ISource<PackageEntry> inner, Runner runner)
                {
                    _inner = inner;
                    _runner = runner;
                }

                public void Populate(ISink<PackageEntry> sink) => _inner.Populate(new Sink(sink, _runner));

                private sealed class Sink : ISink<PackageEntry>
                {
                    private readonly ISink<PackageEntry> _next;
                    private readonly Runner _runner;

                    public Sink(ISink<PackageEntry> next, Runner runner)
                    {
                        _next = next;
                        _runner = runner;
                    }

                    public bool IsInitialized => _next.IsInitialized;

                    public IProcessingContext ProcessingContext => _next.ProcessingContext;

                    public void Initialize(IProcessingContext context) => _next.Initialize(context);

                    public void Put(PackageEntry entry)
                    {
                        foreach (var key in entry.Properties.Keys.Where(PackageCollisionModes.IsPackageDefinedBehaviour).ToList())
                        {
                            entry.Properties.Remove(key);
                        }

                        _runner.Count(entry.Key);
                        _next.Put(entry);
                    }

                    public void Flush() => _next.Flush();

                    public void Finish() => _next.Finish();
                }
            }
        }
    }
}
