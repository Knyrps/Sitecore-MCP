using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Tests.Support;
using SitecoreMcp.Server.Transport;
using SitecoreMcp.Server.Transport.Gates;
using Xunit;

namespace SitecoreMcp.Server.Tests.Transport
{
    public class GateTests
    {
        private static ISet<string> Set(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void Https_gate_rejects_plaintext_when_required()
        {
            var result = new HttpsGate(true).Evaluate(new FakeHttpRequest { IsSecureConnection = false });
            Assert.False(result.Passed);
            Assert.Equal(403, result.StatusCode);
        }

        [Fact]
        public void Https_gate_allows_plaintext_when_not_required()
        {
            Assert.True(new HttpsGate(false).Evaluate(new FakeHttpRequest { IsSecureConnection = false }).Passed);
        }

        [Fact]
        public void Origin_gate_allows_a_missing_origin()
        {
            Assert.True(new OriginGate(Set("https://allowed")).Evaluate(new FakeHttpRequest()).Passed);
        }

        [Fact]
        public void Origin_gate_rejects_a_disallowed_origin()
        {
            var request = new FakeHttpRequest().WithHeader("Origin", "https://evil");
            Assert.False(new OriginGate(Set("https://allowed")).Evaluate(request).Passed);
        }

        [Fact]
        public void Origin_gate_allows_an_allowed_origin()
        {
            var request = new FakeHttpRequest().WithHeader("Origin", "https://allowed");
            Assert.True(new OriginGate(Set("https://allowed")).Evaluate(request).Passed);
        }

        [Theory]
        [InlineData(null, 415)]
        [InlineData("text/plain", 415)]
        [InlineData("application/json", 0)]
        [InlineData("application/json; charset=utf-8", 0)]
        public void MediaType_gate_requires_json_content_type(string contentType, int expectedReject)
        {
            var request = new FakeHttpRequest().WithHeader("Content-Type", contentType).WithHeader("Accept", "application/json");
            var result = new MediaTypeGate().Evaluate(request);

            if (expectedReject == 0) Assert.True(result.Passed);
            else Assert.Equal(expectedReject, result.StatusCode);
        }

        [Fact]
        public void MediaType_gate_rejects_an_accept_that_excludes_json()
        {
            var request = new FakeHttpRequest()
                .WithHeader("Content-Type", "application/json")
                .WithHeader("Accept", "text/html");

            Assert.Equal(406, new MediaTypeGate().Evaluate(request).StatusCode);
        }

        [Fact]
        public void BodySize_gate_rejects_an_oversized_declared_length()
        {
            var result = new BodySizeGate(1000).Evaluate(new FakeHttpRequest { ContentLength = 1001 });
            Assert.Equal(413, result.StatusCode);
        }

        [Fact]
        public void ClientAddress_gate_ignores_forwarded_header_from_an_untrusted_peer()
        {
            var request = new FakeHttpRequest { RemoteAddress = "10.0.0.9" }
                .WithHeader("X-Forwarded-For", "127.0.0.1");

            // Peer 10.0.0.9 is not a trusted proxy, so the spoofed XFF must be ignored and the peer rejected.
            var result = new ClientAddressGate(Set("127.0.0.1"), Set()).Evaluate(request);
            Assert.False(result.Passed);
        }

        [Fact]
        public void ClientAddress_gate_honours_forwarded_header_from_a_trusted_proxy()
        {
            var request = new FakeHttpRequest { RemoteAddress = "10.0.0.9" }
                .WithHeader("X-Forwarded-For", "127.0.0.1");

            var result = new ClientAddressGate(Set("127.0.0.1"), Set("10.0.0.9")).Evaluate(request);
            Assert.True(result.Passed);
        }

        [Fact]
        public void ClientAddress_gate_with_no_allow_list_permits_everyone()
        {
            var result = new ClientAddressGate(Set(), Set()).Evaluate(new FakeHttpRequest { RemoteAddress = "8.8.8.8" });
            Assert.True(result.Passed);
        }

        [Fact]
        public void Chain_returns_the_first_failure_in_order()
        {
            var settings = new McpSettings { RequireHttps = true };
            settings.AllowedOrigins.Add("https://allowed");
            var chain = GateChain.FromSettings(settings);

            // Fails HTTPS (first gate) even though the Origin would also fail; order is what we assert.
            var request = new FakeHttpRequest { IsSecureConnection = false }.WithHeader("Origin", "https://evil");
            var result = chain.Evaluate(request);

            Assert.Equal(403, result.StatusCode);
            Assert.Contains("HTTPS", result.ToOutcome().Body);
        }
    }
}
