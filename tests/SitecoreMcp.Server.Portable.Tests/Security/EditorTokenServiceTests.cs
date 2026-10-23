using System;
using System.Text;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Security;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Security
{
    public class EditorTokenServiceTests
    {
        private const string Secret = "0123456789abcdef0123456789abcdef-test-secret";
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        private static EditorTokenService Service(string secret = Secret, Func<DateTimeOffset> clock = null) =>
            new EditorTokenService(secret, new EditorTokenOptions
            {
                Issuer = "SitecoreMcp",
                Lifetime = TimeSpan.FromMinutes(15),
                ClockSkew = TimeSpan.FromSeconds(60)
            }, clock ?? (() => Now));

        [Fact]
        public void An_issued_token_validates_to_the_editor_it_was_issued_for()
        {
            var service = Service();
            var issued = service.Issue("sitecore\\jane", isAdmin: false, audience: "corepilot");

            var result = service.Validate(issued.Token, "corepilot");

            Assert.True(result.Succeeded);
            Assert.Equal("sitecore\\jane", result.UserName);
            Assert.False(result.IsAdmin);
        }
        [Fact]
        public void The_admin_claim_is_carried_as_issued()
        {
            var service = Service();
            var issued = service.Issue("sitecore\\admin", isAdmin: true, audience: "corepilot");

            Assert.True(service.Validate(issued.Token, "corepilot").IsAdmin);
        }

        [Fact]
        public void A_token_expires_after_its_lifetime()
        {
            var issued = Service().Issue("sitecore\\jane", false, "corepilot");
            Assert.Equal(Now.AddMinutes(15), issued.ExpiresAt);

            var withinSkew = Service(clock: () => Now.AddMinutes(15).AddSeconds(59)).Validate(issued.Token, "corepilot");
            var afterSkew = Service(clock: () => Now.AddMinutes(16)).Validate(issued.Token, "corepilot");

            Assert.True(withinSkew.Succeeded);
            Assert.Equal(EditorTokenFailure.Expired, afterSkew.Failure);
            Assert.Null(afterSkew.UserName);
        }

        [Fact]
        public void A_token_from_the_future_is_not_yet_valid()
        {
            var issued = Service(clock: () => Now.AddMinutes(5)).Issue("sitecore\\jane", false, "corepilot");

            Assert.Equal(EditorTokenFailure.NotYetValid, Service().Validate(issued.Token, "corepilot").Failure);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_missing_token_is_refused(string token)
        {
            Assert.Equal(EditorTokenFailure.Missing, Service().Validate(token, "corepilot").Failure);
        }

        [Theory]
        [InlineData("not-a-token")]
        [InlineData("a.b")]
        [InlineData("a.b.c.d")]
        [InlineData("!!!.???.***")]
        public void A_malformed_token_is_refused(string token)
        {
            Assert.Equal(EditorTokenFailure.Malformed, Service().Validate(token, "corepilot").Failure);
        }

        [Fact]
        public void A_token_for_another_client_is_refused()
        {
            var service = Service();
            var issued = service.Issue("sitecore\\jane", false, "some-other-client");

            Assert.Equal(EditorTokenFailure.WrongAudience, service.Validate(issued.Token, "corepilot").Failure);
        }

        [Fact]
        public void A_token_from_another_issuer_is_refused()
        {
            var foreign = new EditorTokenService(Secret, new EditorTokenOptions { Issuer = "Someone.Else" }, () => Now);
            var issued = foreign.Issue("sitecore\\jane", false, "corepilot");

            Assert.Equal(EditorTokenFailure.WrongIssuer, Service().Validate(issued.Token, "corepilot").Failure);
        }

        [Fact]
        public void A_token_signed_with_another_secret_is_refused()
        {
            var issued = Service(secret: "another-secret-that-is-long-enough-to-use").Issue("sitecore\\jane", false, "corepilot");

            Assert.Equal(EditorTokenFailure.InvalidSignature, Service().Validate(issued.Token, "corepilot").Failure);
        }

        [Fact]
        public void A_token_whose_payload_was_changed_is_refused()
        {
            var service = Service();
            var parts = service.Issue("sitecore\\jane", false, "corepilot").Token.Split('.');
            var payload = JObject.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[1])));
            payload["sub"] = "sitecore\\admin";
            payload["admin"] = true;
            var tampered = parts[0] + "." + ToBase64Url(payload.ToString()) + "." + parts[2];

            var result = service.Validate(tampered, "corepilot");

            Assert.Equal(EditorTokenFailure.InvalidSignature, result.Failure);
            Assert.Null(result.UserName);
        }

        [Fact]
        public void An_unsigned_alg_none_token_is_refused()
        {
            var header = ToBase64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
            var payload = ToBase64Url("{\"iss\":\"SitecoreMcp\",\"aud\":\"corepilot\",\"sub\":\"sitecore\\\\admin\",\"exp\":"
                + Now.AddHours(1).ToUnixTimeSeconds() + "}");

            Assert.False(Service().Validate(header + "." + payload + ".", "corepilot").Succeeded);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("too-short")]
        public void A_missing_or_short_secret_is_rejected_up_front(string secret)
        {
            Assert.Throws<ArgumentException>(() => new EditorTokenService(secret));
        }

        private static string ToBase64Url(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string segment)
        {
            var base64 = segment.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
        }
    }
}
