using System;
using System.Collections.Generic;
using System.Linq;
using SitecoreMcp.Server.Tools.Packages;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools.Packages
{
    /// <summary>
    /// Whether a package may be installed for the calling client, decided before the preview: database
    /// allow-list, content roots, code files and post steps, and entries that are never installed.
    /// </summary>
    public class PackageInstallPolicyTests
    {
        private const string Id = "{110D559F-DEA5-42EA-9C1C-8A5DF7E70EF9}";

        private static PackageEntryInfo Item(string db, string path, string id = Id) =>
            new PackageEntryInfo($"items/{db}{path}/{id}/en/1/xml", 10, null);

        private static PackageEntryInfo File(string path) => new PackageEntryInfo("files" + path, 10, null);

        private static PackageEntryInfo PostStep(string type) => new PackageEntryInfo("metadata/sc_poststep.txt", type.Length, type);

        private static PackageInstallRequest Request(params PackageEntryInfo[] entries) => new PackageInstallRequest
        {
            Manifest = PackageManifest.FromEntries(entries),
            CanUseDatabase = db => db == "master" || db == "core"
        };

        private static Func<string, string, bool> Under(string root) =>
            (db, path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                          path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void A_content_package_into_permitted_databases_is_allowed()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(Item("master", "/sitecore/content/Home"), File("/assets/site.css")));

            Assert.True(decision.Allowed, string.Join("; ", decision.Refusals));
        }

        [Fact]
        public void A_database_outside_the_clients_allow_list_is_refused()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(Item("web", "/sitecore/content/Home")));

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("'web'"));
        }

        [Fact]
        public void Code_files_need_the_allow_code_setting()
        {
            var request = Request(File("/bin/My.Module.dll"));

            var refused = PackageInstallPolicy.Evaluate(request);
            request.AllowCode = true;
            var allowed = PackageInstallPolicy.Evaluate(request);

            Assert.False(refused.Allowed);
            Assert.Contains(refused.Refusals, r => r.Contains("Mcp.Packages.AllowCode") && r.Contains("/bin/My.Module.dll"));
            Assert.True(allowed.Allowed);
            Assert.Contains(allowed.Warnings, w => w.Contains("restart"));
        }

        [Fact]
        public void Forbidden_files_are_refused_even_when_code_is_allowed()
        {
            var request = Request(File("/web.config"));
            request.AllowCode = true;

            var decision = PackageInstallPolicy.Evaluate(request);

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("/web.config"));
        }

        [Fact]
        public void An_entry_that_would_write_outside_the_web_root_is_refused()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(new PackageEntryInfo("files/../evil.dll", 1, null)));

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("files/../evil.dll"));
        }

        [Fact]
        public void A_post_step_is_not_run_unless_asked_and_says_so()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(Item("master", "/sitecore/content/Home"), PostStep("My.PostStep, My")));

            Assert.True(decision.Allowed);
            Assert.False(decision.RunsPostStep);
            Assert.Contains(decision.Warnings, w => w.Contains("My.PostStep, My") && w.Contains("not run"));
        }

        [Fact]
        public void Running_a_post_step_needs_the_allow_code_setting()
        {
            var request = Request(Item("master", "/sitecore/content/Home"), PostStep("My.PostStep, My"));
            request.RunPostStep = true;

            var refused = PackageInstallPolicy.Evaluate(request);
            request.AllowCode = true;
            var allowed = PackageInstallPolicy.Evaluate(request);

            Assert.False(refused.Allowed);
            Assert.Contains(refused.Refusals, r => r.Contains("post step") && r.Contains("Mcp.Packages.AllowCode"));
            Assert.True(allowed.Allowed);
            Assert.True(allowed.RunsPostStep);
        }

        [Fact]
        public void With_content_roots_every_item_must_be_inside_them()
        {
            var request = Request(Item("master", "/sitecore/content/Site/Home"), Item("master", "/sitecore/templates/Foo", "{0A5F3F6B-1B0E-4C33-8F21-6B0F2D9E1A11}"));
            request.ContentRootsApply = true;
            request.IsInsideContentRoots = Under("/sitecore/content/Site");

            var decision = PackageInstallPolicy.Evaluate(request);

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("/sitecore/templates/Foo") && r.Contains("content root"));
            Assert.DoesNotContain(decision.Refusals, r => r.Contains("/sitecore/content/Site/Home"));
        }

        [Fact]
        public void With_content_roots_a_package_inside_them_is_allowed()
        {
            var request = Request(Item("master", "/sitecore/content/Site/Home"));
            request.ContentRootsApply = true;
            request.IsInsideContentRoots = Under("/sitecore/content/Site");

            Assert.True(PackageInstallPolicy.Evaluate(request).Allowed);
        }

        [Fact]
        public void With_content_roots_an_existing_item_elsewhere_with_the_same_id_is_refused()
        {
            // The installer would move or rewrite that item, which lies outside the roots.
            var request = Request(Item("master", "/sitecore/content/Site/Home"));
            request.ContentRootsApply = true;
            request.IsInsideContentRoots = Under("/sitecore/content/Site");
            request.Collisions = new[]
            {
                new PackageItemCollision { Database = "master", Id = Id, PackagePath = "/sitecore/content/Site/Home", Kind = "id", ExistingId = Id, ExistingPath = "/sitecore/content/Other/Home" }
            };

            var decision = PackageInstallPolicy.Evaluate(request);

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("/sitecore/content/Other/Home"));
        }

        [Fact]
        public void With_content_roots_a_package_with_files_is_refused()
        {
            var request = Request(Item("master", "/sitecore/content/Site/Home"), File("/assets/site.css"));
            request.ContentRootsApply = true;
            request.IsInsideContentRoots = Under("/sitecore/content/Site");

            var decision = PackageInstallPolicy.Evaluate(request);

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("files"));
        }

        [Fact]
        public void An_empty_package_is_refused()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(new PackageEntryInfo("metadata/sc_name.txt", 1, "x")));

            Assert.False(decision.Allowed);
            Assert.Contains(decision.Refusals, r => r.Contains("nothing to install"));
        }

        [Fact]
        public void Security_entries_and_author_set_modes_are_warned_about()
        {
            var decision = PackageInstallPolicy.Evaluate(Request(
                Item("master", "/sitecore/content/Home"),
                new PackageEntryInfo("security/users/sitecore/alice", 1, null),
                new PackageEntryInfo($"items/master/sitecore/content/Home/About/{{0A5F3F6B-1B0E-4C33-8F21-6B0F2D9E1A11}}/en/1/xml", 1, null,
                    new Dictionary<string, string> { ["id_InstallMode"] = "Overwrite" })));

            Assert.True(decision.Allowed);
            Assert.Contains(decision.Warnings, w => w.Contains("user and role"));
            Assert.Contains(decision.Warnings, w => w.Contains("collision"));
        }
    }
}
