using SitecoreMcp.Server.Security;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class ContentScopeBoundaryTests
    {
        private static IContentRootBoundary Boundary(params string[] roots) =>
            new ContentScopeBoundary(new ContentScope(roots, null, null));

        [Fact]
        public void Unrestricted_scope_puts_everything_in_one_root()
        {
            Assert.Equal("/", new ContentScopeBoundary(ContentScope.Unrestricted).RootOf("master", "/sitecore/content/Home"));
            Assert.Equal("/", new ContentScopeBoundary(null).RootOf("master", "/sitecore/system"));
        }

        [Fact]
        public void Path_inside_a_root_returns_that_root()
        {
            var boundary = Boundary("/sitecore/content/SiteA", "/sitecore/content/SiteB");

            Assert.Equal("/sitecore/content/SiteB", boundary.RootOf("master", "/sitecore/content/SiteB/News/Item"));
        }

        [Fact]
        public void A_root_counts_as_inside_itself()
        {
            Assert.Equal("/sitecore/content/SiteA", Boundary("/sitecore/content/SiteA").RootOf("master", "/sitecore/content/SiteA"));
        }

        [Fact]
        public void Path_outside_every_root_returns_null()
        {
            Assert.Null(Boundary("/sitecore/content/SiteA").RootOf("master", "/sitecore/system/Settings"));
        }

        [Fact]
        public void Matching_is_segment_aware()
        {
            Assert.Null(Boundary("/sitecore/content/Site").RootOf("master", "/sitecore/content/SiteA/Home"));
        }

        [Fact]
        public void Matching_ignores_case_and_returns_the_normalized_root()
        {
            Assert.Equal("/sitecore/content/SiteA", Boundary("/sitecore/content/SiteA/").RootOf("master", "/SITECORE/content/sitea/home"));
        }

        [Fact]
        public void Nested_roots_resolve_to_the_longest_match()
        {
            var boundary = Boundary("/sitecore/content", "/sitecore/content/SiteA");

            Assert.Equal("/sitecore/content/SiteA", boundary.RootOf("master", "/sitecore/content/SiteA/Home"));
            Assert.Equal("/sitecore/content", boundary.RootOf("master", "/sitecore/content/SiteB/Home"));
        }

        [Fact]
        public void An_empty_root_list_puts_everything_outside()
        {
            Assert.Null(Boundary().RootOf("master", "/sitecore/content/Home"));
        }

        [Fact]
        public void Relocation_across_configured_roots_is_refused()
        {
            var request = new RelocationRequest
            {
                Kind = RelocationKind.Move,
                Database = "master",
                SourcePath = "/sitecore/content/SiteA/Item",
                DestinationPath = "/sitecore/content/SiteB",
                CanDeleteSource = true,
                CanCreateInDestination = true
            };

            var check = RelocationPolicy.Check(request, Boundary("/sitecore/content/SiteA", "/sitecore/content/SiteB"));

            Assert.False(check.Allowed);
            Assert.Contains("different content roots", check.Reason);
        }
    }
}
