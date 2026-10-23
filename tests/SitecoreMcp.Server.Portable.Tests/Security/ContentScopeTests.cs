using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tools;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class ContentScopeTests
    {
        private static ContentScope Roots(params string[] contentRoots) =>
            new ContentScope(contentRoots, null, null);

        [Fact]
        public void A_write_outside_every_content_root_is_refused()
        {
            var scope = Roots("/sitecore/content/Site A", "/sitecore/media library/Site A");

            var ex = Assert.Throws<McpToolException>(() => scope.EnsureWritable("/sitecore/content/Site B/Home"));
            Assert.Contains("/sitecore/content/Site B/Home", ex.Message);
            Assert.Contains("content root", ex.Message);
        }

        [Theory]
        [InlineData("/sitecore/content/Site A")]
        [InlineData("/sitecore/content/Site A/Home/News")]
        [InlineData("/SITECORE/content/site a/home")]
        [InlineData("/sitecore/media library/Site A/logo")]
        public void A_write_inside_a_content_root_is_allowed(string path)
        {
            var scope = Roots("/sitecore/content/Site A/", "/sitecore/media library/Site A");

            Assert.True(scope.IsInContentRoot(path));
            scope.EnsureWritable(path);
        }

        [Fact]
        public void A_sibling_whose_name_starts_with_the_root_name_is_outside_it()
        {
            Assert.False(Roots("/sitecore/content/Site").IsInContentRoot("/sitecore/content/Site B/Home"));
        }

        [Fact]
        public void Without_configured_content_roots_writes_are_not_restricted()
        {
            Assert.True(ContentScope.Unrestricted.IsInContentRoot("/sitecore/system/Settings"));
            ContentScope.Unrestricted.EnsureWritable("/sitecore/system/Settings");
        }

        [Fact]
        public void An_empty_content_root_list_makes_everything_read_only()
        {
            Assert.Throws<McpToolException>(() => Roots().EnsureWritable("/sitecore/content/Home"));
        }

        [Fact]
        public void Search_results_outside_every_search_root_are_excluded()
        {
            var scope = new ContentScope(null, new[] { "/sitecore/content/Site A" }, null);

            Assert.True(scope.IsInSearchRoot("/sitecore/content/Site A/Home"));
            Assert.False(scope.IsInSearchRoot("/sitecore/content/Site B/Home"));
            Assert.False(scope.IsInSearchRoot("/sitecore/system/Settings"));
        }

        [Fact]
        public void Search_roots_are_independent_of_content_roots()
        {
            var scope = new ContentScope(new[] { "/sitecore/content/Site A" }, new[] { "/sitecore/content" }, null);

            Assert.True(scope.IsInSearchRoot("/sitecore/content/Site B/Home"));
            Assert.False(scope.IsInContentRoot("/sitecore/content/Site B/Home"));
        }

        [Fact]
        public void Without_a_requested_root_search_starts_at_every_search_root()
        {
            var scope = new ContentScope(null, new[] { "/sitecore/content/Site A", "/sitecore/media library/Site A" }, null);

            Assert.Equal(new[] { "/sitecore/content/Site A", "/sitecore/media library/Site A" }, scope.SearchStartPaths(null));
        }

        [Fact]
        public void A_requested_root_inside_a_search_root_is_searched_as_asked()
        {
            var scope = new ContentScope(null, new[] { "/sitecore/content/Site A" }, null);

            Assert.Equal(new[] { "/sitecore/content/Site A/News" }, scope.SearchStartPaths("/sitecore/content/Site A/News"));
        }

        [Fact]
        public void A_requested_root_above_the_search_roots_narrows_to_the_search_roots_below_it()
        {
            var scope = new ContentScope(null, new[] { "/sitecore/content/Site A", "/sitecore/media library/Site A" }, null);

            Assert.Equal(new[] { "/sitecore/content/Site A" }, scope.SearchStartPaths("/sitecore/content"));
        }

        [Fact]
        public void A_requested_root_outside_every_search_root_searches_nowhere()
        {
            var scope = new ContentScope(null, new[] { "/sitecore/content/Site A" }, null);

            Assert.Empty(scope.SearchStartPaths("/sitecore/content/Site B"));
        }

        [Fact]
        public void Without_configured_search_roots_search_starts_where_asked()
        {
            Assert.Equal(new[] { "/sitecore/content/Site B" }, ContentScope.Unrestricted.SearchStartPaths("/sitecore/content/Site B"));
            Assert.Equal(new string[] { null }, ContentScope.Unrestricted.SearchStartPaths(null));
            Assert.True(ContentScope.Unrestricted.IsInSearchRoot("/sitecore/system"));
        }

        private sealed class FakeField
        {
            public FakeField(string name, string id) { Name = name; Id = id; }
            public string Name { get; }
            public string Id { get; }
        }

        private static readonly FakeField Title = new FakeField("Title", "{75577384-3C97-45DA-A847-81B00500E250}");
        private static readonly FakeField ApiSecret = new FakeField("Api Secret", "{0E3F2B8A-6B2D-4C55-9C0B-6A0C7D5E1F11}");
        private static readonly FakeField Owner = new FakeField("__Owner", "{52807595-0F8F-4B20-8D2A-CB71D28C6103}");
        private static readonly FakeField[] Fields = { Title, ApiSecret, Owner };

        private static FakeField Lookup(string nameOrId)
        {
            foreach (var field in Fields)
            {
                if (string.Equals(field.Name, nameOrId, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(field.Id, nameOrId, System.StringComparison.OrdinalIgnoreCase)) return field;
            }

            return null;
        }

        private static ContentScope Hiding(params string[] hidden) => new ContentScope(null, null, hidden);

        [Fact]
        public void A_hidden_field_is_matched_by_name_ignoring_case_or_by_id_with_or_without_braces()
        {
            var scope = Hiding("api secret", "52807595-0f8f-4b20-8d2a-cb71d28c6103");

            Assert.True(scope.IsHiddenField(ApiSecret.Name, ApiSecret.Id));
            Assert.True(scope.IsHiddenField(Owner.Name, Owner.Id));
            Assert.False(scope.IsHiddenField(Title.Name, Title.Id));
        }

        [Fact]
        public void Hidden_fields_are_absent_from_reads()
        {
            var visible = Hiding("Api Secret").VisibleFields(Fields, f => f.Name, f => f.Id);

            Assert.Equal(new[] { Title, Owner }, visible);
        }

        [Fact]
        public void A_hidden_field_cannot_be_found_by_name_or_by_id()
        {
            var scope = Hiding("{0E3F2B8A-6B2D-4C55-9C0B-6A0C7D5E1F11}");

            Assert.Null(scope.FindVisibleField("api secret", Lookup, f => f.Name, f => f.Id));
            Assert.Null(scope.FindVisibleField(ApiSecret.Id, Lookup, f => f.Name, f => f.Id));
            Assert.Same(Title, scope.FindVisibleField("title", Lookup, f => f.Name, f => f.Id));
        }

        [Fact]
        public void Writing_a_hidden_field_reports_field_not_found_exactly_like_a_missing_field()
        {
            var scope = Hiding("Api Secret");

            var hidden = Assert.Throws<McpToolException>(() =>
                scope.RequireVisibleField("Api Secret", Lookup, f => f.Name, f => f.Id, "Settings"));
            var missing = Assert.Throws<McpToolException>(() =>
                scope.RequireVisibleField("No Such Field", Lookup, f => f.Name, f => f.Id, "Settings"));

            Assert.Equal("Field 'Api Secret' does not exist on template 'Settings'.", hidden.Message);
            Assert.Equal("Field 'No Such Field' does not exist on template 'Settings'.", missing.Message);
        }

        [Fact]
        public void Without_configured_hidden_fields_every_field_is_visible()
        {
            Assert.False(ContentScope.Unrestricted.IsHiddenField(ApiSecret.Name, ApiSecret.Id));
            Assert.Same(ApiSecret, ContentScope.Unrestricted.RequireVisibleField("Api Secret", Lookup, f => f.Name, f => f.Id, "Settings"));
        }

        [Fact]
        public void Hidden_field_names_can_be_checked_without_an_id_for_index_and_link_data()
        {
            var scope = Hiding("Api Secret", Owner.Id);

            Assert.True(scope.IsHiddenField("API SECRET", null));
            Assert.True(scope.IsHiddenField(null, "{52807595-0F8F-4B20-8D2A-CB71D28C6103}"));
            Assert.False(scope.IsHiddenField("Title", null));
            Assert.False(scope.IsHiddenField(null, null));
        }

        [Theory]
        [InlineData("api_secret")]
        [InlineData("API_SECRET")]
        [InlineData("api secret")]
        [InlineData("api_secret_t")]
        [InlineData("api_secret_sm")]
        [InlineData("api_secret_t_en")]
        public void A_hidden_field_is_recognised_under_its_index_field_names(string indexFieldName)
        {
            Assert.True(Hiding("Api Secret").IsHiddenIndexField(indexFieldName));
        }

        [Theory]
        [InlineData("title_t")]
        [InlineData("api_secretary")]
        [InlineData("api_secret_backup_copy")]
        [InlineData("")]
        public void Other_index_fields_are_not_hidden(string indexFieldName)
        {
            Assert.False(Hiding("Api Secret").IsHiddenIndexField(indexFieldName));
        }

        [Theory]
        [InlineData("/sitecore/content//*[@Api Secret='x']")]
        [InlineData("/sitecore/content//*[@api secret!='']")]
        [InlineData("fast:/sitecore/content//*[@#Api Secret#='%x%']")]
        public void A_query_that_tests_a_hidden_field_is_recognised(string query)
        {
            Assert.True(Hiding("Api Secret").MentionsHiddenField(query));
        }

        [Fact]
        public void A_query_without_hidden_fields_is_not_flagged()
        {
            Assert.False(Hiding("Api Secret").MentionsHiddenField("/sitecore/content//*[@Title='x']"));
            Assert.False(ContentScope.Unrestricted.MentionsHiddenField("/sitecore/content//*[@Api Secret='x']"));
        }

        [Fact]
        public void A_free_text_hit_counts_only_when_every_term_is_in_a_visible_value()
        {
            var visible = new[] { "Quarterly results", "Our revenue grew" };

            Assert.True(ContentScope.TextFoundIn("revenue QUARTERLY", visible));
            Assert.False(ContentScope.TextFoundIn("revenue password", visible));
            Assert.False(ContentScope.TextFoundIn("anything", new string[0]));
        }

        [Fact]
        public void Hidden_field_ids_can_be_expanded_with_their_names()
        {
            var scope = Hiding(Owner.Id, "Api Secret");

            Assert.Equal(new[] { "{52807595-0F8F-4B20-8D2A-CB71D28C6103}" }, scope.HiddenFieldIds);

            var expanded = scope.WithHiddenFieldNames(new[] { "__Owner" });
            Assert.True(expanded.IsHiddenIndexField("__owner"));
            Assert.True(expanded.IsHiddenField("Api Secret", null));
            Assert.True(expanded.IsHiddenField(null, Owner.Id));
        }
    }
}
