using System;
using System.Linq;
using SitecoreMcp.Server.Security;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class RelocationPolicyTests
    {
        /// <summary>Two sites' content roots, as an installation might configure them.</summary>
        private sealed class TwoSiteRoots : IContentRootBoundary
        {
            private static readonly string[] Roots = { "/sitecore/content/SiteA", "/sitecore/content/SiteB" };

            public string RootOf(string database, string path) =>
                Roots.FirstOrDefault(root =>
                    string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
        }

        private static RelocationRequest Move(string source = "/sitecore/content/SiteA/News/Item", string destination = "/sitecore/content/SiteA/Archive",
            bool canDelete = true, bool canCreate = true) =>
            new RelocationRequest
            {
                Kind = RelocationKind.Move,
                Database = "master",
                SourcePath = source,
                DestinationPath = destination,
                CanDeleteSource = canDelete,
                CanCreateInDestination = canCreate
            };

        private static RelocationCheck Check(RelocationRequest request) => RelocationPolicy.Check(request, new TwoSiteRoots());

        [Fact]
        public void A_move_inside_one_content_root_with_rights_is_allowed()
        {
            var check = Check(Move());

            Assert.True(check.Allowed);
            Assert.Null(check.Reason);
        }

        [Fact]
        public void A_move_across_content_roots_is_refused()
        {
            var check = Check(Move(destination: "/sitecore/content/SiteB/Archive"));

            Assert.False(check.Allowed);
            Assert.Contains("content root", check.Reason);
        }

        [Fact]
        public void A_move_from_outside_every_content_root_is_refused()
        {
            Assert.False(Check(Move(source: "/sitecore/system/Settings/Thing")).Allowed);
        }

        [Fact]
        public void A_move_to_outside_every_content_root_is_refused()
        {
            Assert.False(Check(Move(destination: "/sitecore/templates/User Defined")).Allowed);
        }

        [Fact]
        public void A_root_with_a_shared_name_prefix_is_not_the_same_root()
        {
            // "/sitecore/content/SiteAB" is not inside "/sitecore/content/SiteA".
            Assert.False(Check(Move(destination: "/sitecore/content/SiteAB/Archive")).Allowed);
        }

        [Fact]
        public void A_move_without_delete_rights_on_the_source_is_refused()
        {
            var check = Check(Move(canDelete: false));

            Assert.False(check.Allowed);
            Assert.Contains("delete", check.Reason);
        }

        [Fact]
        public void A_move_without_create_rights_on_the_destination_is_refused()
        {
            var check = Check(Move(canCreate: false));

            Assert.False(check.Allowed);
            Assert.Contains("create", check.Reason);
        }

        [Fact]
        public void A_copy_needs_create_rights_on_the_destination_but_leaves_the_source_alone()
        {
            var copy = Move(canDelete: false);
            copy.Kind = RelocationKind.Copy;
            Assert.True(Check(copy).Allowed);

            copy.CanCreateInDestination = false;
            Assert.False(Check(copy).Allowed);
        }

        [Fact]
        public void A_copy_across_content_roots_is_refused()
        {
            var copy = Move(destination: "/sitecore/content/SiteB");
            copy.Kind = RelocationKind.Copy;

            Assert.False(Check(copy).Allowed);
        }

        [Fact]
        public void A_rename_needs_rename_rights()
        {
            var rename = new RelocationRequest { Kind = RelocationKind.Rename, Database = "master", SourcePath = "/sitecore/content/SiteA/News", CanRenameSource = true };
            Assert.True(Check(rename).Allowed);

            rename.CanRenameSource = false;
            var refused = Check(rename);
            Assert.False(refused.Allowed);
            Assert.Contains("rename", refused.Reason);
        }

        [Fact]
        public void A_rename_outside_every_content_root_is_refused()
        {
            var rename = new RelocationRequest { Kind = RelocationKind.Rename, Database = "master", SourcePath = "/sitecore/layout/Renderings/Hero", CanRenameSource = true };

            Assert.False(Check(rename).Allowed);
        }

        [Fact]
        public void Renaming_a_content_root_itself_is_refused()
        {
            // The root is configured by path; renaming it would silently move the boundary.
            var rename = new RelocationRequest { Kind = RelocationKind.Rename, Database = "master", SourcePath = "/sitecore/content/SiteA", CanRenameSource = true };

            Assert.False(Check(rename).Allowed);
        }

        [Fact]
        public void Moving_a_content_root_itself_is_refused()
        {
            Assert.False(Check(Move(source: "/sitecore/content/SiteA", destination: "/sitecore/content/SiteA/Archive")).Allowed);
        }

        [Fact]
        public void Copying_a_content_root_within_itself_is_allowed()
        {
            var copy = Move(source: "/sitecore/content/SiteA", destination: "/sitecore/content/SiteA/Archive");
            copy.Kind = RelocationKind.Copy;

            Assert.True(Check(copy).Allowed);
        }

        [Fact]
        public void Without_configured_roots_only_rights_are_checked()
        {
            var move = Move(source: "/sitecore/system/A", destination: "/sitecore/templates/B");

            Assert.True(RelocationPolicy.Check(move, null).Allowed);
            Assert.True(RelocationPolicy.Check(move, UnrestrictedContentRoots.Instance).Allowed);
            move.CanDeleteSource = false;
            Assert.False(RelocationPolicy.Check(move, null).Allowed);
        }
    }
}
