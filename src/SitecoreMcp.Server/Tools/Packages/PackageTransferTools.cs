using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>Arguments for <see cref="UploadPackageTool"/>.</summary>
    public sealed class UploadPackageArgs
    {
        /// <summary>The package file name.</summary>
        [McpParam(Required = true, Description = "File name to store the package under in the packages folder; '.zip' is added when missing.")]
        public string Name { get; set; }

        /// <summary>The zip as base64.</summary>
        [McpParam(Required = true, Description = "The package zip, base64-encoded. The request-size limit (Mcp.MaxRequestBytes, default 1 MB) caps it at roughly 700 KB; copy larger packages into the packages folder directly.")]
        public string ContentBase64 { get; set; }

        /// <summary>Whether to replace an existing package.</summary>
        [McpParam(Description = "Replace an existing package with the same file name. Defaults to false (refused).")]
        public bool? Overwrite { get; set; }
    }

    /// <summary>Stores an uploaded package in the packages folder after checking that Sitecore can read it. Installs nothing.</summary>
    public sealed class UploadPackageTool : McpTool<UploadPackageArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_upload_package";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Upload a Sitecore package (.zip as base64) into the packages folder, so sitecore_inspect_package and " +
            "sitecore_install_package can use it. Checks that it reads as a Sitecore package; installs nothing. The request-size " +
            "limit caps uploads at about 700 KB - copy larger packages into the folder directly. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UploadPackageArgs args, McpCallContext context)
        {
            var path = PackageStore.Resolve(args.Name, mustExist: false);
            var fileName = Path.GetFileName(path);
            if (File.Exists(path) && !args.Overwrite.GetValueOrDefault())
            {
                throw new McpToolException($"A package named '{fileName}' already exists. Pass overwrite=true to replace it, or choose another name.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(args.ContentBase64.Trim());
            }
            catch (FormatException)
            {
                throw new McpToolException("contentBase64 is not valid base64.");
            }

            if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K')
            {
                throw new McpToolException("The content is not a zip file, so it cannot be a Sitecore package.");
            }

            Directory.CreateDirectory(PackageStore.Folder);
            var temp = Path.Combine(PackageStore.Folder, Path.GetRandomFileName() + PackageFileName.Extension);
            File.WriteAllBytes(temp, bytes);

            PackageManifest manifest;
            try
            {
                manifest = PackageManifest.FromEntries(PackageStore.ReadEntries(temp));
            }
            catch
            {
                File.Delete(temp);
                throw;
            }

            if (manifest.Items.Count == 0 && manifest.Files.Count == 0 && manifest.Metadata.Name == null)
            {
                File.Delete(temp);
                throw new McpToolException("The zip holds no Sitecore package entries (no metadata, items or files). Nothing was stored.");
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            McpLog.Info($"Package uploaded: {fileName} ({bytes.Length} bytes) by {context.User?.Name}.");

            return McpToolResult.Structured(new JObject
            {
                ["name"] = fileName,
                ["sizeBytes"] = bytes.Length,
                ["manifest"] = PackageDescriber.Manifest(manifest, 50),
                ["next"] = "Inspect it with sitecore_inspect_package before installing."
            });
        }
    }

    /// <summary>Arguments for <see cref="DownloadPackageTool"/>.</summary>
    public sealed class DownloadPackageArgs
    {
        /// <summary>The package file name.</summary>
        [McpParam(Required = true, Description = "Package file name in the packages folder; '.zip' may be omitted.")]
        public string Name { get; set; }
    }

    /// <summary>Returns a package from the packages folder as base64, up to Mcp.Packages.MaxDownloadBytes.</summary>
    public sealed class DownloadPackageTool : McpTool<DownloadPackageArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_download_package";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Download a package from the packages folder as base64 (contentBase64), up to the Mcp.Packages.MaxDownloadBytes limit " +
            "(default 1 MB); larger packages must be copied from the folder directly. A package exports content and files, so this " +
            "needs write permission like the other package tools. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(DownloadPackageArgs args, McpCallContext context)
        {
            var path = PackageStore.Resolve(args.Name, mustExist: true);
            var file = new FileInfo(path);
            var limit = context.Settings.MaxPackageDownloadBytes;
            if (file.Length > limit)
            {
                throw new McpToolException($"'{file.Name}' is {file.Length} bytes, over the download limit of {limit} bytes " +
                                           $"(Mcp.Packages.MaxDownloadBytes). Copy it from {PackageStore.Folder} directly.");
            }

            var bytes = File.ReadAllBytes(path);
            McpLog.Info($"Package downloaded: {file.Name} ({bytes.Length} bytes) by {context.User?.Name}.");
            return McpToolResult.Structured(new JObject
            {
                ["name"] = file.Name,
                ["sizeBytes"] = bytes.Length,
                ["contentBase64"] = Convert.ToBase64String(bytes)
            });
        }
    }
}
