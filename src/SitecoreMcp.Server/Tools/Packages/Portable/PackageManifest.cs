using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>One entry of a package as read by Sitecore's PackageReader, reduced to what the manifest needs.</summary>
    public sealed class PackageEntryInfo
    {
        /// <summary>Creates the entry.</summary>
        public PackageEntryInfo(string key, long size, string text, IDictionary<string, string> properties = null)
        {
            Key = key ?? string.Empty;
            Size = size;
            Text = text;
            Properties = properties ?? new Dictionary<string, string>();
        }

        /// <summary>The entry key, e.g. "items/master/sitecore/content/Home/{ID}/en/1/xml" or "files/bin/My.dll".</summary>
        public string Key { get; }

        /// <summary>The entry's data length in bytes.</summary>
        public long Size { get; }

        /// <summary>The entry's text, read for metadata entries only.</summary>
        public string Text { get; }

        /// <summary>The entry's properties (author-set install options live here).</summary>
        public IDictionary<string, string> Properties { get; }
    }

    /// <summary>The package metadata (the Package Designer's "Metadata" step).</summary>
    public sealed class PackageMetadata
    {
        /// <summary>Package name.</summary>
        public string Name { get; set; }
        /// <summary>Version.</summary>
        public string Version { get; set; }
        /// <summary>Revision.</summary>
        public string Revision { get; set; }
        /// <summary>Author.</summary>
        public string Author { get; set; }
        /// <summary>Publisher.</summary>
        public string Publisher { get; set; }
        /// <summary>Readme.</summary>
        public string Readme { get; set; }
        /// <summary>License text.</summary>
        public string License { get; set; }
        /// <summary>Comment.</summary>
        public string Comment { get; set; }
        /// <summary>Package ID.</summary>
        public string PackageId { get; set; }
        /// <summary>The post step: a type name or URL the installer runs after installing.</summary>
        public string PostStep { get; set; }
    }

    /// <summary>One item in a package, across its versions.</summary>
    public sealed class PackageItem
    {
        /// <summary>The database the item installs into.</summary>
        public string Database { get; set; }
        /// <summary>The item's path in the package.</summary>
        public string Path { get; set; }
        /// <summary>The item ID, braced.</summary>
        public string Id { get; set; }
        /// <summary>Versions as "language#number", sorted.</summary>
        public IReadOnlyList<string> Versions { get; set; } = new string[0];
    }

    /// <summary>One file in a package.</summary>
    public sealed class PackageFile
    {
        /// <summary>The web-root path the file installs to.</summary>
        public string Path { get; set; }
        /// <summary>Size in bytes.</summary>
        public long Size { get; set; }
        /// <summary>Content, code or forbidden.</summary>
        public PackageFileClass Class { get; set; }
        /// <summary>Why it is code or forbidden.</summary>
        public string Reason { get; set; }
    }

    /// <summary>
    /// What a package contains, built from its entries without installing anything. Entry keys follow
    /// Sitecore.Install (decompiled 10.3.0 and 10.4.0): "items/{db}{path}/{id}/{language}/{version}/xml",
    /// "files{virtualPath}" (a ".directory" suffix marks a folder), "metadata/sc_*.txt", "blob/...",
    /// "security/...", "installer/version" and "installer/project".
    /// </summary>
    public sealed class PackageManifest
    {
        /// <summary>The metadata.</summary>
        public PackageMetadata Metadata { get; private set; } = new PackageMetadata();
        /// <summary>The items, sorted by database and path.</summary>
        public IReadOnlyList<PackageItem> Items { get; private set; } = new PackageItem[0];
        /// <summary>The number of item versions (entries).</summary>
        public int ItemVersionCount { get; private set; }
        /// <summary>The files, sorted by path.</summary>
        public IReadOnlyList<PackageFile> Files { get; private set; } = new PackageFile[0];
        /// <summary>The databases items install into.</summary>
        public IReadOnlyList<string> Databases { get; private set; } = new string[0];
        /// <summary>Media blobs.</summary>
        public int BlobCount { get; private set; }
        /// <summary>Security (user and role) entries; the installer does not install them through this tool.</summary>
        public int SecurityEntryCount { get; private set; }
        /// <summary>Whether the package carries its project (it can be reopened in the Package Designer).</summary>
        public bool HasProject { get; private set; }
        /// <summary>Entries whose author fixed a collision mode (overridden by the install tool).</summary>
        public int EntriesWithPackageDefinedModes { get; private set; }
        /// <summary>Entries whose key would write outside the web root or could not be parsed safely.</summary>
        public IReadOnlyList<string> UnsafeEntries { get; private set; } = new string[0];
        /// <summary>Entries of a kind this tool does not recognize.</summary>
        public IReadOnlyList<string> UnrecognizedEntries { get; private set; } = new string[0];
        /// <summary>The total size of all entries in bytes.</summary>
        public long TotalSize { get; private set; }

        /// <summary>Builds the manifest.</summary>
        public static PackageManifest FromEntries(IEnumerable<PackageEntryInfo> entries)
        {
            var manifest = new PackageManifest();
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var items = new Dictionary<string, (PackageItem Item, SortedSet<string> Versions)>(StringComparer.OrdinalIgnoreCase);
            var files = new List<PackageFile>();
            var unsafeEntries = new List<string>();
            var unrecognized = new List<string>();

            foreach (var entry in entries ?? Enumerable.Empty<PackageEntryInfo>())
            {
                manifest.TotalSize += Math.Max(0, entry.Size);
                if (entry.Properties.Keys.Any(PackageCollisionModes.IsPackageDefinedBehaviour)) manifest.EntriesWithPackageDefinedModes++;

                var key = entry.Key;
                var prefix = key.Split('/')[0].ToLowerInvariant();
                switch (prefix)
                {
                    case "metadata":
                        metadata[key.Substring("metadata/".Length)] = entry.Text ?? string.Empty;
                        break;
                    case "installer":
                        if (key.Equals("installer/project", StringComparison.OrdinalIgnoreCase)) manifest.HasProject = true;
                        break;
                    case "blob":
                        manifest.BlobCount++;
                        break;
                    case "security":
                        manifest.SecurityEntryCount++;
                        break;
                    case "items":
                        if (!TryParseItem(key, out var database, out var path, out var id, out var version))
                        {
                            unsafeEntries.Add(key);
                            break;
                        }

                        var itemKey = database + "|" + id;
                        if (!items.TryGetValue(itemKey, out var known))
                        {
                            known = (new PackageItem { Database = database, Path = path, Id = id }, new SortedSet<string>(StringComparer.Ordinal));
                            items[itemKey] = known;
                        }

                        known.Versions.Add(version);
                        manifest.ItemVersionCount++;
                        break;
                    case "files":
                        var virtualPath = key.Substring("files".Length);
                        if (virtualPath.EndsWith("/.directory", StringComparison.OrdinalIgnoreCase)) break;
                        if (!WebRootPath.TryNormalize(virtualPath, out var normalized, out _))
                        {
                            unsafeEntries.Add(key);
                            break;
                        }

                        var verdict = PackageFilePolicy.Classify(normalized);
                        files.Add(new PackageFile { Path = normalized, Size = entry.Size, Class = verdict.Class, Reason = verdict.Reason });
                        break;
                    default:
                        unrecognized.Add(key);
                        break;
                }
            }

            manifest.Metadata = new PackageMetadata
            {
                Name = Get(metadata, "sc_name.txt"),
                Version = Get(metadata, "sc_version.txt"),
                Revision = Get(metadata, "sc_revision.txt"),
                Author = Get(metadata, "sc_author.txt"),
                Publisher = Get(metadata, "sc_publisher.txt"),
                Readme = Get(metadata, "sc_readme.txt"),
                License = Get(metadata, "sc_license.txt"),
                Comment = Get(metadata, "sc_comment.txt"),
                PackageId = Get(metadata, "sc_packageid.txt"),
                PostStep = Get(metadata, "sc_poststep.txt")
            };

            manifest.Items = items.Values
                .Select(v => { v.Item.Versions = v.Versions.ToList(); return v.Item; })
                .OrderBy(i => i.Database, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            manifest.Databases = manifest.Items.Select(i => i.Database).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            manifest.Files = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            manifest.UnsafeEntries = unsafeEntries;
            manifest.UnrecognizedEntries = unrecognized;
            return manifest;
        }

        private static string Get(IDictionary<string, string> metadata, string key) =>
            metadata.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;

        /// <summary>Parses "items/{db}/{path...}/{id}/{language}/{version}/xml".</summary>
        private static bool TryParseItem(string key, out string database, out string path, out string id, out string version)
        {
            database = path = id = version = null;
            var parts = key.Split('/');
            // items, db, at least one path segment, id, language, version, xml
            if (parts.Length < 7 || !parts[parts.Length - 1].Equals("xml", StringComparison.OrdinalIgnoreCase)) return false;

            var pathSegments = parts.Skip(2).Take(parts.Length - 6).ToArray();
            if (pathSegments.Any(s => s.Length == 0 || s == "." || s == "..")) return false;
            if (!Guid.TryParse(parts[parts.Length - 4], out var guid)) return false;

            database = parts[1];
            path = "/" + string.Join("/", pathSegments);
            id = guid.ToString("B").ToUpperInvariant();
            version = parts[parts.Length - 3] + "#" + parts[parts.Length - 2];
            return database.Length > 0;
        }
    }

    /// <summary>A collision mode for items, as the Installation Wizard offers them.</summary>
    public sealed class ItemCollisionMode
    {
        /// <summary>Creates the mode.</summary>
        public ItemCollisionMode(string name, string installMode, string mergeMode, string description)
        {
            Name = name;
            InstallMode = installMode;
            MergeMode = mergeMode;
            Description = description;
        }

        /// <summary>The argument value.</summary>
        public string Name { get; }
        /// <summary>The Sitecore.Install.Utils.InstallMode name.</summary>
        public string InstallMode { get; }
        /// <summary>The Sitecore.Install.Utils.MergeMode name.</summary>
        public string MergeMode { get; }
        /// <summary>What it does to an item that already exists.</summary>
        public string Description { get; }
    }

    /// <summary>
    /// The collision choices of the Installation Wizard, by name. The default is skip: an item or file
    /// that already exists is left exactly as it is, and only new ones are installed.
    /// </summary>
    public static class PackageCollisionModes
    {
        private static readonly ItemCollisionMode[] ItemModes =
        {
            new ItemCollisionMode("skip", "Skip", "Undefined", "existing items are left untouched; only new items are installed"),
            new ItemCollisionMode("merge", "Merge", "Merge", "existing items keep their other versions; versions with the same number are replaced by the package's"),
            new ItemCollisionMode("mergeAppend", "Merge", "Append", "the package's versions are added as new versions after the existing ones"),
            new ItemCollisionMode("mergeClear", "Merge", "Clear", "every existing version of the item is removed and replaced by the package's"),
            new ItemCollisionMode("overwrite", "Overwrite", "Undefined",
                "the existing item is replaced, and its existing children that are not in the package are DELETED"),
            new ItemCollisionMode("sideBySide", "SideBySide", "Undefined", "the package's item is added next to the existing one under a new ID")
        };

        /// <summary>The item mode names, for descriptions and errors.</summary>
        public static IReadOnlyList<string> ItemModeNames => ItemModes.Select(m => m.Name).ToList();

        /// <summary>Parses an item collision mode; null or empty means skip.</summary>
        public static bool TryParseItemMode(string value, out ItemCollisionMode mode, out string error)
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? "skip" : value.Trim();
            mode = ItemModes.FirstOrDefault(m => m.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            error = mode == null ? $"'{value}' is not an item collision mode. Use one of: {string.Join(", ", ItemModeNames)}." : null;
            return mode != null;
        }

        /// <summary>Parses a file collision mode ("skip" or "overwrite"; null means skip). True result means overwrite.</summary>
        public static bool TryParseFileMode(string value, out bool overwrite, out string error)
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? "skip" : value.Trim();
            overwrite = wanted.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
            var valid = overwrite || wanted.Equals("skip", StringComparison.OrdinalIgnoreCase);
            error = valid ? null : $"'{value}' is not a file collision mode. Use skip or overwrite.";
            return valid;
        }

        /// <summary>
        /// Whether an entry property is an install option the package author fixed (InstallMode or
        /// VersionMergeMode, optionally prefixed id_/path_). The installer prefers these over the
        /// caller's choice, so the install tool strips them.
        /// </summary>
        public static bool IsPackageDefinedBehaviour(string propertyKey) =>
            propertyKey != null &&
            (propertyKey.EndsWith("InstallMode", StringComparison.OrdinalIgnoreCase) ||
             propertyKey.EndsWith("VersionMergeMode", StringComparison.OrdinalIgnoreCase));
    }
}
