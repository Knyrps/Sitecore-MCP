using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Security;
using SitecoreMcp.Server.Tests.Support;
using SitecoreMcp.Server.Transport;
using Xunit;

namespace SitecoreMcp.Server.Portable.Tests.Transport
{
    /// <summary>
    /// Which Sitecore identity a request runs as: a fixed-user client runs as its user, an
    /// editor-token client runs as the editor its token names and is refused without a valid one.
    /// </summary>
    public class EditorIdentityAuthenticationTests
    {
        private const string Secret = "0123456789abcdef0123456789abcdef-test-secret";
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        private static readonly McpClient Alice =
            McpClient.ForUser("alice", "alice-key", "sitecore\\svc-alice", allowWrites: true, databases: new[] { "master" });

        private static readonly McpClient CorePilot =
            McpClient.ForEditorToken("corepilot", "corepilot-key", "corepilot", allowWrites: true, databases: new[] { "master" });

        private static readonly McpClient OtherEditorClient =
            McpClient.ForEditorToken("other", "other-key", "other", allowWrites: false, databases: new[] { "master" });

        private static EditorTokenService Tokens(Func<DateTimeOffset> clock = null) =>
            new EditorTokenService(Secret, new EditorTokenOptions(), clock ?? (() => Now));

        private static McpAuthenticator Authenticator(EditorTokenService tokens) =>
            new McpAuthenticator(new List<McpClient> { Alice, CorePilot, OtherEditorClient }, tokens);

        private static FakeHttpRequest Call(string key, string editorToken = null)
        {
            var request = new FakeHttpRequest().WithHeader("Authorization", "Bearer " + key);
            return editorToken == null ? request : request.WithHeader(McpHeaders.EditorToken, editorToken);
        }

        [Fact]
        public void A_corepilot_call_with_a_valid_editor_token_runs_as_that_editor()
        {
            var token = Tokens().Issue("sitecore\\jane", isAdmin: false, audience: "corepilot").Token;

            var result = Authenticator(Tokens()).AuthenticateCall(Call("corepilot-key", token));

            Assert.True(result.Succeeded);
            Assert.Same(CorePilot, result.Client);
            Assert.Equal("sitecore\\jane", result.UserName);
            Assert.True(result.IsEditor);
        }

        [Fact]
        public void A_corepilot_call_without_an_editor_token_is_refused()
        {
            var result = Authenticator(Tokens()).AuthenticateCall(Call("corepilot-key"));

            Assert.False(result.Succeeded);
            Assert.Equal(EditorTokenFailure.Missing, result.EditorTokenFailure);
            Assert.Null(result.UserName);
        }

        [Fact]
        public void A_corepilot_call_with_an_expired_editor_token_is_refused()
        {
            var token = Tokens().Issue("sitecore\\jane", false, "corepilot").Token;

            var result = Authenticator(Tokens(() => Now.AddHours(1))).AuthenticateCall(Call("corepilot-key", token));

            Assert.False(result.Succeeded);
            Assert.Equal(EditorTokenFailure.Expired, result.EditorTokenFailure);
        }

        [Fact]
        public void A_corepilot_call_with_a_tampered_editor_token_is_refused()
        {
            var token = Tokens().Issue("sitecore\\jane", false, "corepilot").Token;
            var tampered = token.Substring(0, token.Length - 2) + (token.EndsWith("AA") ? "BB" : "AA");

            var result = Authenticator(Tokens()).AuthenticateCall(Call("corepilot-key", tampered));

            Assert.False(result.Succeeded);
            Assert.Equal(EditorTokenFailure.InvalidSignature, result.EditorTokenFailure);
        }

        [Fact]
        public void A_corepilot_call_with_a_token_issued_for_another_client_is_refused()
        {
            var token = Tokens().Issue("sitecore\\jane", false, "other").Token;

            var result = Authenticator(Tokens()).AuthenticateCall(Call("corepilot-key", token));

            Assert.False(result.Succeeded);
            Assert.Equal(EditorTokenFailure.WrongAudience, result.EditorTokenFailure);
        }

        [Fact]
        public void Editor_token_clients_are_refused_when_no_signing_secret_is_configured()
        {
            var token = Tokens().Issue("sitecore\\jane", false, "corepilot").Token;

            var result = Authenticator(tokens: null).AuthenticateCall(Call("corepilot-key", token));

            Assert.False(result.Succeeded);
            Assert.Equal(EditorTokenFailure.NotConfigured, result.EditorTokenFailure);
        }

        [Fact]
        public void A_fixed_user_client_runs_as_its_configured_user()
        {
            var result = Authenticator(Tokens()).AuthenticateCall(Call("alice-key"));

            Assert.True(result.Succeeded);
            Assert.Same(Alice, result.Client);
            Assert.Equal("sitecore\\svc-alice", result.UserName);
            Assert.False(result.IsEditor);
        }

        [Fact]
        public void A_fixed_user_client_cannot_switch_identity_with_an_editor_token()
        {
            var token = Tokens().Issue("sitecore\\admin", true, "alice").Token;

            var result = Authenticator(Tokens()).AuthenticateCall(Call("alice-key", token));

            Assert.True(result.Succeeded);
            Assert.Equal("sitecore\\svc-alice", result.UserName);
        }

        [Fact]
        public void Fixed_user_clients_keep_working_without_any_editor_token_configuration()
        {
            var result = new McpAuthenticator(new List<McpClient> { Alice }).AuthenticateCall(Call("alice-key"));

            Assert.True(result.Succeeded);
            Assert.Equal("sitecore\\svc-alice", result.UserName);
        }

        [Fact]
        public void A_wrong_key_is_refused_before_any_editor_token_is_considered()
        {
            var token = Tokens().Issue("sitecore\\jane", false, "corepilot").Token;

            var result = Authenticator(Tokens()).AuthenticateCall(Call("nope", token));

            Assert.False(result.Succeeded);
            Assert.Null(result.Client);
            Assert.Equal(EditorTokenFailure.None, result.EditorTokenFailure);
        }
    }
}
