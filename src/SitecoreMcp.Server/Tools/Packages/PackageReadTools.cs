using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>Arguments for <see cref="ListPackagesTool"/>.</summary>
    public sealed class ListPackagesArgs
    {
        /// <summary>Whether to read each package's metadata.</summary>
        [McpParam(Description = "Also read each package's metadata (name, version, author). Reads the zips, so only the 20 most recent are read. Defaults to false.")]
        public bool? IncludeMetadata { get; set; }
    }

    /// <summary>Lists the packages (.zip) and saved package projects (.xml) in the packages folder.</summary>
    public sealed class ListPackagesTool : McpTool<ListPackagesArgs>
    {
        private const int MetadataLimit = 20;

        /// <inheritdoc />
        public override string Name => "sitecore_list_packages";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "List the Sitecore packages (.zip) and saved package projects (.xml) in the packages folder (Settings.PackagePath, " +
            "usually App_Data/packages), newest first, with size and date; includeMetadata also reads name/version/author. " +
            "Packages are addressed by these file names in the other package tools. Read-only. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(ListPackagesArgs args, McpCallContext context)
        {
            var folder = PackageStore.Folder;
            if (!Directory.Exists(folder))
            {
                return McpToolResult.Structured(new JObject
                {
                    ["folder"] = folder, ["packages"] = new JArray(), ["projects"] = new JArray(),
                    ["note"] = "The packages folder does not exist yet; sitecore_create_package or sitecore_upload_package creates it."
                });
            }

            var packages = new DirectoryInfo(folder).GetFiles("*" + PackageFileName.Extension)
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            var projectFolder = PackageStore.ProjectFolder;
            var projects = Directory.Exists(projectFolder)
                ? new DirectoryInfo(projectFolder).GetFiles("*" + PackageFileName.ProjectExtension).OrderByDescending(f => f.LastWriteTimeUtc).ToList()
                : new System.Collections.Generic.List<FileInfo>();
            var withMetadata = args.IncludeMetadata.GetValueOrDefault();

            var listed = new JArray();
            for (var i = 0; i < packages.Count; i++)
            {
                var file = packages[i];
                var entry = Describe(file);
                if (withMetadata && i < MetadataLimit)
                {
                    try
                    {
                        entry["metadata"] = PackageDescriber.Metadata(PackageManifest.FromEntries(PackageStore.ReadEntries(file.FullName)).Metadata);
                    }
                    catch (McpToolException ex)
                    {
                        entry["error"] = ex.Message;
                    }
                }

                listed.Add(entry);
            }

            return McpToolResult.Structured(new JObject
            {
                ["folder"] = folder,
                ["count"] = packages.Count,
                ["packages"] = listed,
                ["projectFolder"] = projectFolder,
                ["projects"] = new JArray(projects.Select(p => (object)Describe(p)).ToArray())
            });
        }

        private static JObject Describe(FileInfo file) => new JObject
        {
            ["name"] = file.Name,
            ["sizeBytes"] = file.Length,
            ["lastWrite"] = file.LastWriteTimeUtc.ToString("o")
        };
    }

    /// <summary>Arguments for <see cref="InspectPackageTool"/>.</summary>
    public sealed class InspectPackageArgs
    {
        /// <summary>The package file name.</summary>
        [McpParam(Required = true, Description = "Package file name in the packages folder (from sitecore_list_packages); '.zip' may be omitted.")]
        public string Name { get; set; }

        /// <summary>Whether to check for existing items and files.</summary>
        [McpParam(Description = "Also check which package items and files already exist on this instance (collisions). Defaults to true.")]
        public bool? CheckCollisions { get; set; }
    }

    /// <summary>
    /// Reads a package's metadata and entries with Sitecore's PackageReader, without installing it, and
    /// says whether sitecore_install_package would accept it for this client.
    /// </summary>
    public sealed class InspectPackageTool : McpTool<InspectPackageArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_inspect_package";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Look inside a Sitecore package WITHOUT installing it: metadata (name, version, author, readme, post step), the items " +
            "(database, path, ID, versions) and files it contains, which of them already exist here, and whether " +
            "sitecore_install_package would accept it for this client (and why not). Run it before installing. Read-only. " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(InspectPackageArgs args, McpCallContext context)
        {
            var path = PackageStore.Resolve(args.Name, mustExist: true);
            var manifest = PackageManifest.FromEntries(PackageStore.ReadEntries(path));
            var file = new FileInfo(path);

            var result = new JObject
            {
                ["name"] = file.Name,
                ["sizeBytes"] = file.Length,
                ["lastWrite"] = file.LastWriteTimeUtc.ToString("o"),
                ["manifest"] = PackageDescriber.Manifest(manifest)
            };

            var collisions = new System.Collections.Generic.List<PackageItemCollision>();
            if (args.CheckCollisions.GetValueOrDefault(true))
            {
                collisions = PackageStore.ItemCollisions(manifest);
                result["collisions"] = PackageDescriber.Collisions(collisions, PackageStore.FileCollisions(manifest));
            }

            var decision = PackageInstallPolicy.Evaluate(InstallPackageTool.Request(manifest, collisions, context, runPostStep: false));
            result["install"] = new JObject
            {
                ["allowed"] = decision.Allowed,
                ["refusals"] = new JArray(decision.Refusals.Cast<object>().ToArray()),
                ["warnings"] = new JArray(decision.Warnings.Cast<object>().ToArray()),
                ["allowCode"] = context.Settings.PackagesAllowCode,
                ["writesAllowed"] = context.AllowWrites
            };

            return McpToolResult.Structured(result);
        }
    }
}
