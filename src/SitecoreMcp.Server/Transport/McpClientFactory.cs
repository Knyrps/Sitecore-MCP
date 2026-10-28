using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Transport
{
    /// <summary>The raw attributes of one &lt;client&gt; config node, before any rule is applied.</summary>
    public sealed class McpClientDefinition
    {
        /// <summary>The unique config id; also the default editor-token audience.</summary>
        public string Id { get; set; }

        /// <summary>The environment variable holding the client's key.</summary>
        public string KeyEnvVar { get; set; }

        /// <summary>The fixed Sitecore user, for a client that does not require an editor token.</summary>
        public string User { get; set; }

        /// <summary>Whether calls must carry an editor token and run as the editor it names.</summary>
        public bool RequiresEditorToken { get; set; }

        /// <summary>The editor-token audience; defaults to <see cref="Id"/>.</summary>
        public string Audience { get; set; }

        /// <summary>Whether the client may invoke write tools.</summary>
        public bool AllowWrites { get; set; }

        /// <summary>The database allow-list, comma- or pipe-separated; defaults to master.</summary>
        public string Databases { get; set; }

        /// <summary>
        /// The client's content roots, pipe-separated; replaces the global list when set. Unset means
        /// the global content roots apply.
        /// </summary>
        public string ContentRoots { get; set; }

        /// <summary>The client's search roots, pipe-separated; replaces the global list when set.</summary>
        public string SearchRoots { get; set; }

        /// <summary>The client's hidden fields (names or IDs), pipe-separated; added to the global list.</summary>
        public string HiddenFields { get; set; }

        /// <summary>
        /// The confirmDirectChanges attribute; null when absent. An editor-token client always confirms
        /// (false is refused); a fixed-user client falls back to the factory's default.
        /// </summary>
        public bool? ConfirmDirectChanges { get; set; }
    }

    /// <summary>The client built from a definition, or why the definition was refused.</summary>
    public sealed class McpClientCreation
    {
        private McpClientCreation(McpClient client, string error)
        {
            Client = client;
            Error = error;
        }

        /// <summary>Whether a client was built.</summary>
        public bool Succeeded => Client != null;

        /// <summary>The built client, or null.</summary>
        public McpClient Client { get; }

        /// <summary>Why the definition was refused, for the log; null on success.</summary>
        public string Error { get; }

        internal static McpClientCreation Success(McpClient client) => new McpClientCreation(client, null);

        internal static McpClientCreation Fail(string error) => new McpClientCreation(null, error);
    }

    /// <summary>
    /// Applies the client rules to a config definition. Kept free of Sitecore so the rules are
    /// unit-testable: a keyless client is never registered, every client has exactly one identity
    /// source (a fixed user or an editor token, never both), an editor-token client always confirms
    /// direct changes, and the database list defaults to master.
    /// </summary>
    public sealed class McpClientFactory
    {
        private readonly Func<string, string> _readEnvironment;
        private readonly bool _confirmDirectChangesByDefault;

        /// <summary>Creates the factory over a reader for environment variables (key lookup).</summary>
        /// <param name="readEnvironment">Reads an environment variable by name.</param>
        /// <param name="confirmDirectChangesByDefault">
        /// Whether a fixed-user client without a confirmDirectChanges attribute confirms direct changes
        /// (the Mcp.ConfirmDirectChanges setting). Editor-token clients always confirm.
        /// </param>
        public McpClientFactory(Func<string, string> readEnvironment, bool confirmDirectChangesByDefault = false)
        {
            _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
            _confirmDirectChangesByDefault = confirmDirectChangesByDefault;
        }

        /// <summary>Builds the client a definition describes, or reports why it must be skipped.</summary>
        public McpClientCreation Create(McpClientDefinition definition)
        {
            var name = string.IsNullOrEmpty(definition.Id) ? definition.User ?? "(unnamed)" : definition.Id;

            if (string.IsNullOrEmpty(definition.KeyEnvVar))
            {
                return McpClientCreation.Fail($"Skipping client '{name}': keyEnvVar is missing.");
            }

            if (definition.RequiresEditorToken)
            {
                // The point of an editor-token client is that it acts as the logged-in editor. A
                // fixed user would be a silent fallback identity, so the combination is refused.
                if (!string.IsNullOrEmpty(definition.User))
                {
                    return McpClientCreation.Fail(
                        $"Skipping client '{name}': a client with requiresEditorToken=\"true\" cannot have a fixed user (user=\"{definition.User}\"). Remove the user attribute; calls run as the editor named by the token.");
                }

                // CorePilot shows the editor every direct change before it runs; that guarantee rests on
                // the server, so an editor-token client that tries to switch it off is refused outright.
                if (definition.ConfirmDirectChanges == false)
                {
                    return McpClientCreation.Fail(
                        $"Skipping client '{name}': a client with requiresEditorToken=\"true\" cannot set confirmDirectChanges=\"false\"; the editor must confirm its direct changes. Remove the attribute.");
                }

                if (string.IsNullOrEmpty(definition.Audience) && string.IsNullOrEmpty(definition.Id))
                {
                    return McpClientCreation.Fail(
                        "Skipping an editor-token client without an id or audience: its tokens need an audience to be bound to.");
                }
            }
            else if (string.IsNullOrEmpty(definition.User))
            {
                return McpClientCreation.Fail(
                    $"Skipping client '{name}': it needs either a user or requiresEditorToken=\"true\".");
            }

            var key = _readEnvironment(definition.KeyEnvVar);
            if (string.IsNullOrEmpty(key))
            {
                // Never register a keyless client: a null key would risk matching an empty presented key.
                return McpClientCreation.Fail(
                    $"Environment variable '{definition.KeyEnvVar}' is not set; client '{name}' disabled.");
            }

            var databases = SplitList(definition.Databases);
            if (databases.Count == 0)
            {
                databases.Add("master");
            }

            var scope = new ContentScope(
                SplitPipeList(definition.ContentRoots),
                SplitPipeList(definition.SearchRoots),
                SplitPipeList(definition.HiddenFields));

            return McpClientCreation.Success(definition.RequiresEditorToken
                ? McpClient.ForEditorToken(definition.Id, key,
                    string.IsNullOrEmpty(definition.Audience) ? definition.Id : definition.Audience,
                    definition.AllowWrites, databases, scope)
                : McpClient.ForUser(definition.Id, key, definition.User, definition.AllowWrites, databases, scope,
                    definition.ConfirmDirectChanges ?? _confirmDirectChangesByDefault));
        }

        /// <summary>
        /// Splits a pipe-separated scope list (paths and field names may contain commas). Returns null
        /// for an unset or blank value, meaning "not configured here".
        /// </summary>
        internal static List<string> SplitPipeList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var result = new List<string>();
            foreach (var part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }

            return result;
        }

        private static List<string> SplitList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(value)) return result;

            foreach (var part in value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }

            return result;
        }
    }
}
