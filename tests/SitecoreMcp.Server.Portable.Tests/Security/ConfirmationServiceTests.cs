using System;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Security;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class ConfirmationServiceTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        private static ConfirmationBinding Delete(string path = "/sitecore/content/Home/About", string editor = "sitecore\\jane", string state = "rev-1") =>
            new ConfirmationBinding("sitecore_delete_item", editor, JObject.Parse($"{{ \"path\": \"{path}\" }}"), state);

        [Fact]
        public void A_confirmation_redeems_for_the_change_it_was_issued_for()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete());

            var redemption = service.Redeem(issued.Token, Delete());

            Assert.True(redemption.Succeeded);
            Assert.Equal(ConfirmationFailure.None, redemption.Failure);
        }

        [Fact]
        public void A_confirmation_is_single_use()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete());

            Assert.True(service.Redeem(issued.Token, Delete()).Succeeded);
            var reuse = service.Redeem(issued.Token, Delete());

            Assert.False(reuse.Succeeded);
            Assert.Equal(ConfirmationFailure.AlreadyUsed, reuse.Failure);
        }

        [Fact]
        public void A_confirmation_expires_after_its_lifetime()
        {
            var now = Now;
            var service = new ConfirmationService(options: new ConfirmationOptions { Lifetime = TimeSpan.FromMinutes(5) }, clock: () => now);
            var issued = service.Issue(Delete());
            Assert.Equal(Now.AddMinutes(5), issued.ExpiresAt);

            now = Now.AddMinutes(5);
            var expired = service.Redeem(issued.Token, Delete());

            Assert.Equal(ConfirmationFailure.Expired, expired.Failure);
        }

        [Fact]
        public void A_confirmation_still_redeems_just_before_it_expires()
        {
            var now = Now;
            var service = new ConfirmationService(options: new ConfirmationOptions { Lifetime = TimeSpan.FromMinutes(5) }, clock: () => now);
            var issued = service.Issue(Delete());

            now = Now.AddMinutes(5).AddSeconds(-1);

            Assert.True(service.Redeem(issued.Token, Delete()).Succeeded);
        }

        [Fact]
        public void A_confirmation_is_rejected_when_the_arguments_differ_from_the_preview()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete("/sitecore/content/Home/About"));

            var redemption = service.Redeem(issued.Token, Delete("/sitecore/content/Home"));

            Assert.Equal(ConfirmationFailure.Mismatch, redemption.Failure);
        }

        [Fact]
        public void A_confirmation_is_rejected_for_another_tool()
        {
            var service = new ConfirmationService(clock: () => Now);
            var args = JObject.Parse("{ \"path\": \"/sitecore/content/Home/About\" }");
            var issued = service.Issue(new ConfirmationBinding("sitecore_delete_item", "sitecore\\jane", args, "rev-1"));

            var redemption = service.Redeem(issued.Token, new ConfirmationBinding("sitecore_publish_item", "sitecore\\jane", args, "rev-1"));

            Assert.Equal(ConfirmationFailure.Mismatch, redemption.Failure);
        }

        [Fact]
        public void A_confirmation_is_rejected_for_another_editor()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete(editor: "sitecore\\jane"));

            Assert.Equal(ConfirmationFailure.Mismatch, service.Redeem(issued.Token, Delete(editor: "sitecore\\sam")).Failure);
        }

        [Fact]
        public void Editor_names_compare_case_insensitively_like_Sitecore_accounts()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete(editor: "sitecore\\Jane"));

            Assert.True(service.Redeem(issued.Token, Delete(editor: "SITECORE\\jane")).Succeeded);
        }

        [Fact]
        public void A_confirmation_is_rejected_when_the_target_changed_since_the_preview()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete(state: "rev-1"));

            Assert.Equal(ConfirmationFailure.Mismatch, service.Redeem(issued.Token, Delete(state: "rev-2")).Failure);
        }

        [Fact]
        public void A_rejected_attempt_does_not_spend_the_confirmation()
        {
            var service = new ConfirmationService(clock: () => Now);
            var issued = service.Issue(Delete("/sitecore/content/Home/About"));

            service.Redeem(issued.Token, Delete("/sitecore/content/Home"));

            Assert.True(service.Redeem(issued.Token, Delete("/sitecore/content/Home/About")).Succeeded);
        }

        [Fact]
        public void Argument_layout_does_not_matter_only_values_do()
        {
            var service = new ConfirmationService(clock: () => Now);
            var previewed = JObject.Parse("{ \"path\": \"/a\", \"permanent\": true, \"database\": null, \"fields\": { \"b\": \"2\", \"a\": \"1\" } }");
            var confirmed = JObject.Parse("{ \"fields\": { \"a\": \"1\", \"b\": \"2\" }, \"permanent\": true, \"path\": \"/a\", \"confirmationToken\": \"whatever\" }");
            var issued = service.Issue(new ConfirmationBinding("sitecore_update_item", "sitecore\\jane", previewed, "s"));

            Assert.True(service.Redeem(issued.Token, new ConfirmationBinding("sitecore_update_item", "sitecore\\jane", confirmed, "s")).Succeeded);
        }

        [Fact]
        public void A_changed_nested_value_is_a_different_change()
        {
            var service = new ConfirmationService(clock: () => Now);
            var previewed = JObject.Parse("{ \"path\": \"/a\", \"fields\": { \"Title\": \"Old\" } }");
            var confirmed = JObject.Parse("{ \"path\": \"/a\", \"fields\": { \"Title\": \"New\" } }");
            var issued = service.Issue(new ConfirmationBinding("sitecore_update_item", "sitecore\\jane", previewed, "s"));

            Assert.Equal(ConfirmationFailure.Mismatch,
                service.Redeem(issued.Token, new ConfirmationBinding("sitecore_update_item", "sitecore\\jane", confirmed, "s")).Failure);
        }

        [Fact]
        public void A_token_issued_by_another_server_is_rejected()
        {
            var ours = new ConfirmationService(clock: () => Now);
            var theirs = new ConfirmationService(clock: () => Now);
            var issued = theirs.Issue(Delete());

            Assert.Equal(ConfirmationFailure.Mismatch, ours.Redeem(issued.Token, Delete()).Failure);
        }

        [Theory]
        [InlineData(null, ConfirmationFailure.Missing)]
        [InlineData("  ", ConfirmationFailure.Missing)]
        [InlineData("not-a-token", ConfirmationFailure.Malformed)]
        [InlineData("v1.abc.notanumber.sig", ConfirmationFailure.Malformed)]
        [InlineData("v9.abc.1790000000.c2ln", ConfirmationFailure.Malformed)]
        [InlineData("v1.abc.1790000000.c2ln", ConfirmationFailure.Mismatch)]
        public void Missing_or_malformed_tokens_are_rejected(string token, ConfirmationFailure expected)
        {
            var service = new ConfirmationService(clock: () => Now);

            var redemption = service.Redeem(token, Delete());

            Assert.Equal(expected, redemption.Failure);
            Assert.False(string.IsNullOrEmpty(redemption.Message));
        }

        [Fact]
        public void The_oldest_outstanding_confirmations_are_dropped_beyond_the_limit()
        {
            var now = Now;
            var service = new ConfirmationService(options: new ConfirmationOptions { MaxOutstanding = 2 }, clock: () => now);
            var first = service.Issue(Delete("/a"));
            now = now.AddSeconds(1);
            var second = service.Issue(Delete("/b"));
            now = now.AddSeconds(1);
            var third = service.Issue(Delete("/c"));

            Assert.Equal(ConfirmationFailure.AlreadyUsed, service.Redeem(first.Token, Delete("/a")).Failure);
            Assert.True(service.Redeem(second.Token, Delete("/b")).Succeeded);
            Assert.True(service.Redeem(third.Token, Delete("/c")).Succeeded);
        }

        [Fact]
        public void A_short_signing_key_is_refused()
        {
            Assert.Throws<ArgumentException>(() => new ConfirmationService(key: new byte[16]));
        }
    }
}
