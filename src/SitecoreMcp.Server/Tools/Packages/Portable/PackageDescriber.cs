using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>Projects package manifests and collisions to JSON for tool results, with lists capped.</summary>
    public static class PackageDescriber
    {
        /// <summary>The most items, files or collisions listed in one result; counts are always complete.</summary>
        public const int MaxListed = 200;

        /// <summary>The metadata as JSON (null values dropped).</summary>
        public static JObject Metadata(PackageMetadata metadata)
        {
            var result = new JObject();
            void Add(string name, string value)
            {
                if (!string.IsNullOrEmpty(value)) result[name] = value;
            }

            Add("name", metadata.Name);
            Add("version", metadata.Version);
            Add("revision", metadata.Revision);
            Add("author", metadata.Author);
            Add("publisher", metadata.Publisher);
            Add("packageId", metadata.PackageId);
            Add("readme", metadata.Readme);
            Add("license", metadata.License);
            Add("comment", metadata.Comment);
            Add("postStep", metadata.PostStep);
            return result;
        }

        /// <summary>The manifest: metadata, counts, and the (capped) item and file lists.</summary>
        public static JObject Manifest(PackageManifest manifest, int maxListed = MaxListed)
        {
            var result = new JObject
            {
                ["metadata"] = Metadata(manifest.Metadata),
                ["counts"] = new JObject
                {
                    ["items"] = manifest.Items.Count,
                    ["itemVersions"] = manifest.ItemVersionCount,
                    ["files"] = manifest.Files.Count,
                    ["codeFiles"] = manifest.Files.Count(f => f.Class == PackageFileClass.Code),
                    ["blobs"] = manifest.BlobCount,
                    ["securityEntries"] = manifest.SecurityEntryCount
                },
                ["databases"] = new JArray(manifest.Databases.Cast<object>().ToArray()),
                ["hasProject"] = manifest.HasProject,
                ["items"] = new JArray(manifest.Items.Take(maxListed).Select(i => (object)new JObject
                {
                    ["database"] = i.Database,
                    ["path"] = i.Path,
                    ["id"] = i.Id,
                    ["versions"] = new JArray(i.Versions.Cast<object>().ToArray())
                }).ToArray()),
                ["files"] = new JArray(manifest.Files.Take(maxListed).Select(f =>
                {
                    var file = new JObject { ["path"] = f.Path, ["size"] = f.Size, ["class"] = f.Class.ToString().ToLowerInvariant() };
                    if (f.Reason != null) file["reason"] = f.Reason;
                    return (object)file;
                }).ToArray())
            };

            if (manifest.Items.Count > maxListed || manifest.Files.Count > maxListed)
            {
                result["listsTruncated"] = true;
            }

            if (manifest.EntriesWithPackageDefinedModes > 0) result["entriesWithPackageDefinedModes"] = manifest.EntriesWithPackageDefinedModes;
            if (manifest.UnsafeEntries.Count > 0) result["unsafeEntries"] = new JArray(manifest.UnsafeEntries.Take(maxListed).Cast<object>().ToArray());
            if (manifest.UnrecognizedEntries.Count > 0) result["unrecognizedEntries"] = new JArray(manifest.UnrecognizedEntries.Take(maxListed).Cast<object>().ToArray());
            return result;
        }

        /// <summary>The collisions with existing items and files.</summary>
        public static JObject Collisions(IReadOnlyList<PackageItemCollision> items, IReadOnlyList<string> files, int maxListed = MaxListed) => new JObject
        {
            ["items"] = items.Count,
            ["files"] = files.Count,
            ["itemList"] = new JArray(items.Take(maxListed).Select(c => (object)new JObject
            {
                ["database"] = c.Database,
                ["kind"] = c.Kind,
                ["packagePath"] = c.PackagePath,
                ["id"] = c.Id,
                ["existingPath"] = c.ExistingPath,
                ["existingId"] = c.ExistingId,
                ["moved"] = c.Kind == "id" && !string.Equals(c.PackagePath, c.ExistingPath, System.StringComparison.OrdinalIgnoreCase)
            }).ToArray()),
            ["fileList"] = new JArray(files.Take(maxListed).Cast<object>().ToArray())
        };
    }
}
