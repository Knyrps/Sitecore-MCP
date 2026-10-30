using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Install;
using Sitecore.Install.Files;
using Sitecore.Install.Framework;
using Sitecore.Install.Zip;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>
    /// The packages folder (<c>Settings.PackagePath</c>, by default <c>$(dataFolder)/packages</c>) and
    /// reading packages with Sitecore's own <see cref="PackageReader"/>. Every package a tool touches is
    /// addressed by file name inside this folder; nothing else on disk is reachable.
    /// </summary>
    internal static class PackageStore
    {
        /// <summary>The physical packages folder, mapped the way the Installation Wizard maps it.</summary>
        public static string Folder => PathUtils.MapPath(Settings.PackagePath);

        /// <summary>The physical web root.</summary>
        public static string WebRoot => PathUtils.MapPath("/");

        /// <summary>The folder the Package Designer saves projects to (PackageProjectsFolder under the packages folder).</summary>
        public static string ProjectFolder
        {
            get
            {
                var sub = Settings.GetSetting("PackageProjectsFolder");
                return string.IsNullOrEmpty(sub) ? Folder : Path.Combine(Folder, sub);
            }
        }

        /// <summary>Resolves a package name to its full path in the packages folder; throws when invalid or (if required) missing.</summary>
        public static string Resolve(string name, bool mustExist)
        {
            if (!PackageFileName.TryNormalize(name, out var fileName, out var error)) throw new McpToolException(error);

            var folder = Folder;
            var path = Path.GetFullPath(Path.Combine(folder, fileName));
            if (!path.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                throw new McpToolException($"'{name}' does not resolve inside the packages folder.");
            }

            if (mustExist && !File.Exists(path))
            {
                throw new McpToolException($"No package named '{fileName}' in the packages folder ({folder}). " +
                                           "sitecore_list_packages lists what is there.");
            }

            return path;
        }

        /// <summary>Reads every entry of a package without installing anything.</summary>
        public static List<PackageEntryInfo> ReadEntries(string packagePath)
        {
            var recorder = new RecordingSink();
            recorder.Initialize(Installer.CreatePreviewContext());
            try
            {
                new PackageReader(packagePath).Populate(recorder);
            }
            catch (Exception ex)
            {
                throw new McpToolException($"'{Path.GetFileName(packagePath)}' could not be read as a Sitecore package: {ex.Message}");
            }

            return recorder.Entries;
        }

        /// <summary>
        /// The package items that meet items already in their database: by ID (the installer then updates,
        /// and may move, that item) or by path (another item already has that path).
        /// </summary>
        public static List<PackageItemCollision> ItemCollisions(PackageManifest manifest)
        {
            var collisions = new List<PackageItemCollision>();
            foreach (var item in manifest.Items)
            {
                var database = Factory.GetDatabase(item.Database, false);
                if (database == null) continue;

                var existing = database.GetItem(ID.Parse(item.Id));
                if (existing != null)
                {
                    collisions.Add(new PackageItemCollision
                    {
                        Database = item.Database, Id = item.Id, PackagePath = item.Path, Kind = "id",
                        ExistingId = existing.ID.ToString(), ExistingPath = existing.Paths.FullPath
                    });
                    continue;
                }

                var atPath = database.GetItem(item.Path);
                if (atPath != null)
                {
                    collisions.Add(new PackageItemCollision
                    {
                        Database = item.Database, Id = item.Id, PackagePath = item.Path, Kind = "path",
                        ExistingId = atPath.ID.ToString(), ExistingPath = atPath.Paths.FullPath
                    });
                }
            }

            return collisions;
        }

        /// <summary>The package files that already exist in the web root.</summary>
        public static List<string> FileCollisions(PackageManifest manifest)
        {
            var root = WebRoot;
            return manifest.Files.Where(f => File.Exists(WebRootPath.ToPhysical(root, f.Path))).Select(f => f.Path).ToList();
        }

        /// <summary>Collects entry keys, sizes, properties and metadata text; keeps no entry data.</summary>
        private sealed class RecordingSink : ISink<PackageEntry>
        {
            public List<PackageEntryInfo> Entries { get; } = new List<PackageEntryInfo>();

            public bool IsInitialized => ProcessingContext != null;

            public IProcessingContext ProcessingContext { get; private set; }

            public void Initialize(IProcessingContext context) => ProcessingContext = context;

            public void Put(PackageEntry entry)
            {
                long size = 0;
                string text = null;
                using (var holder = entry.GetStream())
                {
                    var stream = holder?.Stream;
                    if (stream != null)
                    {
                        try
                        {
                            size = stream.CanSeek ? stream.Length : 0;
                        }
                        catch (NotSupportedException)
                        {
                        }

                        if (entry.Key.StartsWith(Constants.MetadataPrefix + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                text = reader.ReadToEnd();
                            }
                        }
                    }
                }

                Entries.Add(new PackageEntryInfo(entry.Key, size, text, new Dictionary<string, string>(entry.Properties)));
            }

            public void Flush()
            {
            }

            public void Finish()
            {
            }
        }
    }
}
