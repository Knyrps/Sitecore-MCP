using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Install;
using Sitecore.Install.Files;
using Sitecore.Install.Framework;
using Sitecore.Install.Items;
using Sitecore.Install.Serialization;
using Sitecore.Install.Utils;
using Sitecore.Install.Zip;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>One item source of a new package.</summary>
    public sealed class PackageItemSourceArgs
    {
        /// <summary>The item.</summary>
        [McpParam(Required = true, Description = "Item path or ID.")]
        public string Path { get; set; }

        /// <summary>The database.</summary>
        [McpParam(Description = "Database of the item (default master); must be permitted for this client.")]
        public string Database { get; set; }

        /// <summary>Whether to include the descendants.</summary>
        [McpParam(Description = "Include the item's descendants too (the Package Designer's 'with subitems'). Defaults to false.")]
        public bool? Subitems { get; set; }
    }

    /// <summary>Arguments for <see cref="CreatePackageTool"/>.</summary>
    public sealed class CreatePackageArgs
    {
        /// <summary>The package file name.</summary>
        [McpParam(Required = true, Description = "Package file name to write in the packages folder; '.zip' is added when missing. Letters, digits, space, '.', '-', '_' only.")]
        public string Name { get; set; }

        /// <summary>The package name in its metadata.</summary>
        [McpParam(Description = "Package name in the metadata (shown by the Installation Wizard). Defaults to the file name without extension.")]
        public string PackageName { get; set; }

        /// <summary>Version.</summary>
        [McpParam(Description = "Package version, e.g. 1.0.")]
        public string Version { get; set; }

        /// <summary>Author.</summary>
        [McpParam(Description = "Package author. Defaults to the calling Sitecore user.")]
        public string Author { get; set; }

        /// <summary>Publisher.</summary>
        [McpParam(Description = "Package publisher.")]
        public string Publisher { get; set; }

        /// <summary>Readme.</summary>
        [McpParam(Description = "Readme text shown before installing (what it contains, post-install steps).")]
        public string Readme { get; set; }

        /// <summary>Item sources.</summary>
        [McpParam(Description = "Items to include: each { path, database?, subitems? }. All versions and languages of each item are included.")]
        public List<PackageItemSourceArgs> Items { get; set; }

        /// <summary>File sources.</summary>
        [McpParam(Description =
            "Files or folders to include, web-root relative (e.g. /bin/My.Module.dll, /App_Config/Include/My.Module). Folders are expanded; " +
            "web.config, connection strings, license, App_Data, keys and Sitecore-MCP's own files are never included.")]
        public List<string> Files { get; set; }

        /// <summary>Whether to save the package project.</summary>
        [McpParam(Description = "Also save the package project XML next to it (the Package Designer can open and rebuild it). Defaults to false.")]
        public bool? SaveProject { get; set; }

        /// <summary>Whether to replace an existing package of that name.</summary>
        [McpParam(Description = "Replace an existing package with the same file name. Defaults to false (refused).")]
        public bool? Overwrite { get; set; }
    }

    /// <summary>
    /// Designs and builds a package with Sitecore's own Sitecore.Install API - a <see cref="PackageProject"/>
    /// with a static item source and a static file source, built by <see cref="PackageGenerator"/> into a
    /// <see cref="PackageWriter"/> - so the zip is the same as the Package Designer would produce.
    /// Items are read as the calling user, so only what that user can read is packaged.
    /// </summary>
    public sealed class CreatePackageTool : McpTool<CreatePackageArgs>
    {
        private const int MaxFiles = 5000;
        private const int MaxItemSources = 200;

        /// <inheritdoc />
        public override string Name => "sitecore_create_package";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Build a Sitecore package (.zip, the Package Designer's format) in the packages folder from metadata (name, version, " +
            "author, publisher, readme), items (path or ID per database, optionally with subitems) and web-root files or folders " +
            "(secrets, license and App_Data are never included). Returns the file name, size and a manifest summary; optionally " +
            "saves the package project XML. Installs nothing. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(CreatePackageArgs args, McpCallContext context)
        {
            var path = PackageStore.Resolve(args.Name, mustExist: false);
            var fileName = System.IO.Path.GetFileName(path);
            if (File.Exists(path) && !args.Overwrite.GetValueOrDefault())
            {
                throw new McpToolException($"A package named '{fileName}' already exists. Pass overwrite=true to replace it, or choose another name.");
            }

            var itemSources = args.Items ?? new List<PackageItemSourceArgs>();
            var fileSources = args.Files ?? new List<string>();
            if (itemSources.Count == 0 && fileSources.Count == 0) throw new McpToolException("Name at least one item or file to package.");
            if (itemSources.Count > MaxItemSources) throw new McpToolException($"At most {MaxItemSources} item sources per package.");

            // Resolve everything before writing anything.
            var items = itemSources.Select(source =>
            {
                if (string.IsNullOrWhiteSpace(source?.Path)) throw new McpToolException("Every item source needs a path or ID.");
                return (Item: ItemResolver.Resolve(context, source.Path, source.Database, null), Subitems: source.Subitems.GetValueOrDefault());
            }).ToList();

            var selection = PackageFileSelection.Expand(PackageStore.WebRoot, fileSources, MaxFiles);
            if (!selection.Succeeded) throw new McpToolException(selection.Error);

            var project = new PackageProject();
            project.Metadata.PackageName = string.IsNullOrWhiteSpace(args.PackageName) ? System.IO.Path.GetFileNameWithoutExtension(fileName) : args.PackageName.Trim();
            project.Metadata.Version = args.Version ?? string.Empty;
            project.Metadata.Author = args.Author ?? context.User?.Name ?? string.Empty;
            project.Metadata.Publisher = args.Publisher ?? string.Empty;
            project.Metadata.Readme = args.Readme ?? string.Empty;

            if (items.Count > 0) project.Sources.Add(StaticItemSource(items));
            if (selection.Files.Count > 0)
            {
                var files = new ExplicitFileSource { Name = "Files" };
                foreach (var file in selection.Files) files.Entries.Add(file);
                project.Sources.Add(files);
            }

            Directory.CreateDirectory(PackageStore.Folder);
            var temp = System.IO.Path.Combine(PackageStore.Folder, System.IO.Path.GetRandomFileName() + PackageFileName.Extension);
            try
            {
                using (var writer = new PackageWriter(temp))
                {
                    writer.Initialize(Installer.CreateInstallationContext());
                    PackageGenerator.GeneratePackage(project, writer);
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex) when (!(ex is McpToolException))
            {
                TryDelete(temp);
                throw new McpToolException("Building the package failed: " + ex.Message + " Nothing was written.");
            }

            string projectFile = null;
            if (args.SaveProject.GetValueOrDefault())
            {
                Directory.CreateDirectory(PackageStore.ProjectFolder);
                projectFile = System.IO.Path.Combine(PackageStore.ProjectFolder, PackageFileName.ProjectFor(fileName));
                File.WriteAllText(projectFile, IOUtils.StoreSolution(project));
            }

            // Report what was actually written, read back with Sitecore's reader.
            var manifest = PackageManifest.FromEntries(PackageStore.ReadEntries(path));
            McpLog.Info($"Package created: {fileName} by {context.User?.Name}: {manifest.Items.Count} items, {manifest.Files.Count} files.");

            var result = new JObject
            {
                ["name"] = fileName,
                ["path"] = path,
                ["sizeBytes"] = new FileInfo(path).Length,
                ["manifest"] = PackageDescriber.Manifest(manifest),
                ["projectFile"] = projectFile
            };
            if (selection.Skipped.Count > 0)
            {
                result["skippedFiles"] = new JArray(selection.Skipped.Select(s => (object)new JObject { ["path"] = s.Path, ["reason"] = s.Reason }).ToArray());
            }

            return McpToolResult.Structured(result);
        }

        /// <summary>
        /// One static item source holding every chosen item, built the way the Package Designer's "Add items
        /// statically" dialog builds it: single items as exact references, subtrees expanded through an
        /// ItemSource, then collected (sorted, de-duplicated) into an ExplicitItemSource.
        /// </summary>
        private static ExplicitItemSource StaticItemSource(IEnumerable<(Sitecore.Data.Items.Item Item, bool Subitems)> items)
        {
            var collection = new SourceCollection<PackageEntry>();
            var single = new ExplicitItemSource();
            collection.Add(single);
            foreach (var (item, subitems) in items)
            {
                if (subitems)
                {
                    collection.Add(new ItemSource { SkipVersions = true, Database = item.Database.Name, Root = item.ID.ToString() });
                }
                else
                {
                    single.Entries.Add(new ItemReference(item.Uri, false).ToString());
                }
            }

            var builder = new ExplicitItemSource.Builder(new ExplicitItemSource { Name = "Items" });
            builder.Initialize(new SimpleProcessingContext());
            new EntrySorter(collection).Populate(new Uniq(builder));
            builder.Finish();
            return builder.Source;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
