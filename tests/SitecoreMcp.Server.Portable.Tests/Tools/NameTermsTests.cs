using SitecoreMcp.Server.Tools.Search;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools
{
    public class NameTermsTests
    {
        [Fact]
        public void A_single_word_is_one_term()
        {
            Assert.Equal(new[] { "Bulk" }, NameTerms.Split("Bulk"));
        }

        [Fact]
        public void Several_words_are_separate_terms_so_every_one_must_be_in_the_name()
        {
            Assert.Equal(new[] { "Bulk", "A" }, NameTerms.Split("Bulk A"));
        }

        [Fact]
        public void Extra_whitespace_and_tabs_make_no_empty_terms()
        {
            Assert.Equal(new[] { "Test", "Page", "2" }, NameTerms.Split("  Test \t Page   2 "));
        }

        [Fact]
        public void The_same_word_twice_counts_once_ignoring_case()
        {
            Assert.Equal(new[] { "Page", "two" }, NameTerms.Split("Page page two"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Nothing_to_search_for_gives_no_terms(string value)
        {
            Assert.Empty(NameTerms.Split(value));
        }
    }
}
