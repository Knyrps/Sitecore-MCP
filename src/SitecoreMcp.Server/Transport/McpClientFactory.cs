using System;
using System.Collections.Generic;

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
    /// source (a fixed user or an editor token, never both), and the database list defaults to master.
    /// </summary>
    public sealed class McpClientFactory
    {
        private readonly Func<string, string> _readEnvironment;

        /// <summary>Creates the factory over a reader for environment variables (key lookup).</summary>
        public McpClientFactory(Func<string, string> readEnvironment)
        {
            _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
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

            return McpClientCreation.Success(definition.RequiresEditorToken
                ? McpClient.ForEditorToken(definition.Id, key,
                    string.IsNullOrEmpty(definition.Audience) ? definition.Id : definition.Audience,
                    definition.AllowWrites, databases)
                : McpClient.ForUser(definition.Id, key, definition.User, definition.AllowWrites, databases));
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
