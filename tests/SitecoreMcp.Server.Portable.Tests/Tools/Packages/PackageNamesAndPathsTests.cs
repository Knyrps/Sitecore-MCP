using System.IO;
using System.Linq;
using SitecoreMcp.Server.Tools.Packages;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools.Packages
{
    /// <summary>
    /// Package file names stay inside the packages folder, and file sources stay inside the web root
    /// and away from secrets, license and Sitecore-MCP's own files.
    /// </summary>
    public class PackageNamesAndPathsTests
    {
        [Theory]
        [InlineData("My Package-1.0", "My Package-1.0.zip")]
        [InlineData("release_2.zip", "release_2.zip")]
        [InlineData("  Spaced.ZIP ", "Spaced.ZIP")]
        public void A_plain_name_becomes_a_zip_file_name(string input, string expected)
        {
            Assert.True(PackageFileName.TryNormalize(input, out var name, out var error), error);
            Assert.Equal(expected, name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("../evil.zip")]
        [InlineData("sub/evil.zip")]
        [InlineData("sub\\evil.zip")]
        [InlineData("C:evil.zip")]
        [InlineData(".hidden.zip")]
        [InlineData("bad*name.zip")]
        [InlineData("project.xml")]
        public void A_name_that_could_leave_the_packages_folder_or_is_not_a_package_is_refused(string input)
        {
            Assert.False(PackageFileName.TryNormalize(input, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void The_saved_project_sits_next_to_the_package_as_xml()
        {
            Assert.Equal("Release 1.xml", PackageFileName.ProjectFor("Release 1.zip"));
        }

        [Theory]
        [InlineData("bin/My.Module.dll", "/bin/My.Module.dll")]
        [InlineData("/App_Config/Include/My.config", "/App_Config/Include/My.config")]
        [InlineData("~/layouts\\Main.aspx", "/layouts/Main.aspx")]
        [InlineData("/Views/", "/Views")]
        public void A_web_root_relative_path_is_normalized(string input, string expected)
        {
            Assert.True(WebRootPath.TryNormalize(input, out var path, out var error), error);
            Assert.Equal(expected, path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("../outside.txt")]
        [InlineData("/bin/../../outside.txt")]
        [InlineData("C:\\inetpub\\wwwroot\\web.config")]
        [InlineData("\\\\server\\share\\x.dll")]
        public void A_path_outside_the_web_root_is_refused(string input)
        {
            Assert.False(WebRootPath.TryNormalize(input, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("/web.config")]
        [InlineData("/Views/web.config")]
        [InlineData("/App_Config/ConnectionStrings.config")]
        [InlineData("/App_Data/license.xml")]
        [InlineData("/App_Data/logs/log.20261030.txt")]
        [InlineData("/license.xml")]
        [InlineData("/certs/site.pfx")]
        [InlineData("/.env")]
        [InlineData("/bin/SitecoreMcp.Server.dll")]
        [InlineData("/App_Config/Include/SitecoreMcp/SitecoreMcp.config")]
        [InlineData("/App_Config/Include/zzzz/SitecoreMcp.Environment.config")]
        public void Secrets_license_data_and_sitecore_mcp_itself_are_forbidden(string path)
        {
            var file = PackageFilePolicy.Classify(path);

            Assert.Equal(PackageFileClass.Forbidden, file.Class);
            Assert.False(string.IsNullOrEmpty(file.Reason));
        }

        [Theory]
        [InlineData("/bin/My.Module.dll")]
        [InlineData("/App_Config/Include/My.Module.config")]
        [InlineData("/layouts/Main.aspx")]
        [InlineData("/Views/Shared/Header.cshtml")]
        [InlineData("/global.asax")]
        public void Files_that_run_code_or_change_configuration_are_code(string path)
        {
            Assert.Equal(PackageFileClass.Code, PackageFilePolicy.Classify(path).Class);
        }

        [Theory]
        [InlineData("/assets/site.css")]
        [InlineData("/scripts/app.js")]
        [InlineData("/images/logo.png")]
        public void Static_assets_are_content(string path)
        {
            Assert.Equal(PackageFileClass.Content, PackageFilePolicy.Classify(path).Class);
        }

        [Fact]
        public void A_folder_source_expands_to_its_files_and_skips_forbidden_ones()
        {
            var root = Path.Combine(Path.GetTempPath(), "scmcp-webroot-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "App_Config", "Include", "Mine"));
                File.WriteAllText(Path.Combine(root, "App_Config", "Include", "Mine", "Mine.config"), "<configuration />");
                File.WriteAllText(Path.Combine(root, "App_Config", "ConnectionStrings.config"), "secret");
                Directory.CreateDirectory(Path.Combine(root, "assets"));
                File.WriteAllText(Path.Combine(root, "assets", "site.css"), "body{}");

                var selection = PackageFileSelection.Expand(root, new[] { "/App_Config", "assets/site.css" }, maxFiles: 100);

                Assert.True(selection.Succeeded, selection.Error);
                Assert.Equal(new[] { "/App_Config/Include/Mine/Mine.config", "/assets/site.css" }, selection.Files.ToArray());
                var skipped = Assert.Single(selection.Skipped);
                Assert.Equal("/App_Config/ConnectionStrings.config", skipped.Path);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void A_file_source_naming_a_forbidden_file_is_refused_outright()
        {
            var root = Path.Combine(Path.GetTempPath(), "scmcp-webroot-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "web.config"), "<configuration />");

                var selection = PackageFileSelection.Expand(root, new[] { "/web.config" }, maxFiles: 100);

                Assert.False(selection.Succeeded);
                Assert.Contains("web.config", selection.Error);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void A_missing_file_source_is_refused()
        {
            var root = Path.Combine(Path.GetTempPath(), "scmcp-webroot-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var selection = PackageFileSelection.Expand(root, new[] { "/nope.txt" }, maxFiles: 100);

                Assert.False(selection.Succeeded);
                Assert.Contains("/nope.txt", selection.Error);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void A_selection_over_the_file_cap_is_refused()
        {
            var root = Path.Combine(Path.GetTempPath(), "scmcp-webroot-" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "assets"));
                for (var i = 0; i < 3; i++) File.WriteAllText(Path.Combine(root, "assets", i + ".css"), "x");

                var selection = PackageFileSelection.Expand(root, new[] { "/assets" }, maxFiles: 2);

                Assert.False(selection.Succeeded);
                Assert.Contains("more than 2", selection.Error);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
