using System.Linq;
using SitecoreMcp.Server.Tools.Confirmation;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Tools
{
    public class SharedFieldWriteTests
    {
        private static PlannedFieldChange Versioned(string name, string from, string to) =>
            new PlannedFieldChange(name, shared: false, unversioned: false, oldValue: from, newValue: to);

        private static PlannedFieldChange Shared(string name, string from, string to) =>
            new PlannedFieldChange(name, shared: true, unversioned: false, oldValue: from, newValue: to);

        private static PlannedFieldChange Unversioned(string name, string from, string to) =>
            new PlannedFieldChange(name, shared: false, unversioned: true, oldValue: from, newValue: to);

        [Fact]
        public void Writing_only_versioned_fields_needs_no_confirmation()
        {
            Assert.Empty(SharedFieldWrites.NeedingConfirmation(new[] { Versioned("Title", "Old", "New") }));
        }

        [Fact]
        public void Writing_a_shared_field_needs_confirmation()
        {
            var needing = SharedFieldWrites.NeedingConfirmation(new[] { Versioned("Title", "a", "b"), Shared("Navigation Title", "x", "y") });

            Assert.Equal(new[] { "Navigation Title" }, needing.Select(c => c.Name));
        }

        [Fact]
        public void Writing_an_unversioned_field_needs_confirmation()
        {
            Assert.Single(SharedFieldWrites.NeedingConfirmation(new[] { Unversioned("Teaser", "x", "y") }));
        }

        [Fact]
        public void A_shared_field_set_to_its_current_value_is_not_a_change()
        {
            Assert.Empty(SharedFieldWrites.NeedingConfirmation(new[] { Shared("Navigation Title", "same", "same") }));
        }

        [Fact]
        public void The_preview_names_the_item_the_fields_and_their_reach()
        {
            var changes = new[] { Versioned("Title", "a", "b"), Shared("Navigation Title", "Old nav", "New nav"), Unversioned("Teaser", "t1", "t2") };

            var preview = SharedFieldWrites.Preview("/sitecore/content/Home", "en", changes, "rev-1");

            Assert.Contains("/sitecore/content/Home", preview.Summary);
            Assert.Contains("'Navigation Title'", preview.Summary);
            Assert.Contains("'Teaser'", preview.Summary);
            Assert.Contains("every version and language", preview.Summary);
            Assert.Contains("every version in 'en'", preview.Summary);
            Assert.Equal("rev-1", preview.State);

            var fields = preview.Details["fields"];
            var nav = fields.Single(f => (string)f["name"] == "Navigation Title");
            Assert.Equal("shared", (string)nav["scope"]);
            Assert.Equal("Old nav", (string)nav["from"]);
            Assert.Equal("New nav", (string)nav["to"]);
            Assert.Equal("unversioned", (string)fields.Single(f => (string)f["name"] == "Teaser")["scope"]);
            Assert.Equal("versioned", (string)fields.Single(f => (string)f["name"] == "Title")["scope"]);
        }

        [Fact]
        public void Long_values_are_shortened_in_the_preview()
        {
            var preview = SharedFieldWrites.Preview("/a", "en", new[] { Shared("Body", new string('x', 1000), "short") }, "s");

            var from = (string)preview.Details["fields"][0]["from"];
            Assert.True(from.Length < 300);
            Assert.EndsWith("…", from);
        }
    }
}
