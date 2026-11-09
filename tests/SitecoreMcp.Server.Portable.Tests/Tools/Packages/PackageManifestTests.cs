using System.Collections.Generic;
using System.Linq;
using SitecoreMcp.Server.Tools.Packages;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools.Packages
{
    /// <summary>
    /// A package's entries (as Sitecore's PackageReader yields them) become a manifest: metadata, the
    /// items with their database, path, ID and versions, the files, and what the installer would not
    /// handle or the package author decided.
    /// </summary>
    public class PackageManifestTests
    {
        private const string HomeId = "{110D559F-DEA5-42EA-9C1C-8A5DF7E70EF9}";
        private const string AboutId = "{0A5F3F6B-1B0E-4C33-8F21-6B0F2D9E1A11}";

        private static PackageEntryInfo Meta(string name, string text) =>
            new PackageEntryInfo("metadata/" + name, text.Length, text);

        private static PackageEntryInfo Entry(string key, long size = 10, IDictionary<string, string> properties = null) =>
            new PackageEntryInfo(key, size, null, properties);

        private static PackageManifest Sample() => PackageManifest.FromEntries(new[]
        {
            Entry("installer/version"),
            Entry("installer/project"),
            Meta("sc_name.txt", "My Module"),
            Meta("sc_version.txt", "1.2"),
            Meta("sc_author.txt", "Alice"),
            Meta("sc_publisher.txt", "Contoso"),
            Meta("sc_readme.txt", "Read me"),
            Meta("sc_poststep.txt", "My.Module.PostStep, My.Module"),
            Entry($"items/master/sitecore/content/Home/{HomeId}/en/1/xml"),
            Entry($"items/master/sitecore/content/Home/{HomeId}/en/2/xml"),
            Entry($"items/master/sitecore/content/Home/{HomeId}/de-DE/1/xml"),
            Entry($"items/master/sitecore/content/Home/About/{AboutId}/invariant/0/xml",
                properties: new Dictionary<string, string> { ["id_InstallMode"] = "Overwrite" }),
            Entry("files/bin/My.Module.dll", 4096),
            Entry("files/assets/site.css", 120),
            Entry("files/assets/.directory", 0),
            Entry("blob/master/{8E1E1F2E-1111-2222-3333-444455556666}", 2048),
            Entry("security/users/sitecore/alice")
        });

        [Fact]
        public void Metadata_is_read_from_the_metadata_entries()
        {
            var manifest = Sample();

            Assert.Equal("My Module", manifest.Metadata.Name);
            Assert.Equal("1.2", manifest.Metadata.Version);
            Assert.Equal("Alice", manifest.Metadata.Author);
            Assert.Equal("Contoso", manifest.Metadata.Publisher);
            Assert.Equal("Read me", manifest.Metadata.Readme);
            Assert.Equal("My.Module.PostStep, My.Module", manifest.Metadata.PostStep);
            Assert.True(manifest.HasProject);
        }

        [Fact]
        public void Items_are_grouped_by_database_and_id_with_their_versions()
        {
            var manifest = Sample();

            Assert.Equal(2, manifest.Items.Count);
            var home = manifest.Items.Single(i => i.Id == HomeId);
            Assert.Equal("master", home.Database);
            Assert.Equal("/sitecore/content/Home", home.Path);
            Assert.Equal(new[] { "de-DE#1", "en#1", "en#2" }, home.Versions.ToArray());
            var about = manifest.Items.Single(i => i.Id == AboutId);
            Assert.Equal("/sitecore/content/Home/About", about.Path);
            Assert.Equal(4, manifest.ItemVersionCount);
        }

        [Fact]
        public void Files_are_listed_with_size_and_class_and_directory_markers_are_not_files()
        {
            var manifest = Sample();

            Assert.Equal(new[] { "/assets/site.css", "/bin/My.Module.dll" }, manifest.Files.Select(f => f.Path).ToArray());
            Assert.Equal(PackageFileClass.Code, manifest.Files.Single(f => f.Path == "/bin/My.Module.dll").Class);
            Assert.Equal(4096, manifest.Files.Single(f => f.Path == "/bin/My.Module.dll").Size);
        }

        [Fact]
        public void Blobs_security_accounts_and_author_set_collision_modes_are_counted()
        {
            var manifest = Sample();

            Assert.Equal(1, manifest.BlobCount);
            Assert.Equal(1, manifest.SecurityEntryCount);
            Assert.Equal(1, manifest.EntriesWithPackageDefinedModes);
            Assert.Equal(new[] { "master" }, manifest.Databases.ToArray());
        }

        [Fact]
        public void A_file_key_that_climbs_out_of_the_web_root_is_flagged_as_unsafe()
        {
            var manifest = PackageManifest.FromEntries(new[] { Entry("files/../outside/evil.dll") });

            Assert.Empty(manifest.Files);
            Assert.Equal(new[] { "files/../outside/evil.dll" }, manifest.UnsafeEntries.ToArray());
        }

        [Fact]
        public void An_unrecognized_entry_is_listed_rather_than_dropped()
        {
            var manifest = PackageManifest.FromEntries(new[] { Entry("something/else") });

            Assert.Equal(new[] { "something/else" }, manifest.UnrecognizedEntries.ToArray());
        }

        [Theory]
        [InlineData(null, "skip")]
        [InlineData("skip", "skip")]
        [InlineData("merge", "merge")]
        [InlineData("Overwrite", "overwrite")]
        [InlineData("mergeClear", "mergeClear")]
        public void Item_collision_modes_parse_with_skip_as_the_default(string input, string expected)
        {
            Assert.True(PackageCollisionModes.TryParseItemMode(input, out var mode, out _));
            Assert.Equal(expected, mode.Name);
        }

        [Fact]
        public void Item_collision_modes_map_to_the_wizards_install_and_merge_modes()
        {
            PackageCollisionModes.TryParseItemMode("mergeAppend", out var append, out _);
            PackageCollisionModes.TryParseItemMode("overwrite", out var overwrite, out _);

            Assert.Equal("Merge", append.InstallMode);
            Assert.Equal("Append", append.MergeMode);
            Assert.Equal("Overwrite", overwrite.InstallMode);
            Assert.Equal("Undefined", overwrite.MergeMode);
        }

        [Fact]
        public void An_unknown_collision_mode_is_refused_listing_the_choices()
        {
            Assert.False(PackageCollisionModes.TryParseItemMode("replace", out _, out var error));
            Assert.Contains("skip", error);
            Assert.Contains("overwrite", error);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("skip", false)]
        [InlineData("overwrite", true)]
        public void File_collision_modes_parse_with_skip_as_the_default(string input, bool overwrite)
        {
            Assert.True(PackageCollisionModes.TryParseFileMode(input, out var parsed, out _));
            Assert.Equal(overwrite, parsed);
        }

        [Theory]
        [InlineData("id_InstallMode", true)]
        [InlineData("path_VersionMergeMode", true)]
        [InlineData("InstallMode", true)]
        [InlineData("SomethingElse", false)]
        public void Author_set_behaviour_properties_are_recognized(string key, bool expected)
        {
            Assert.Equal(expected, PackageCollisionModes.IsPackageDefinedBehaviour(key));
        }
    }
}
