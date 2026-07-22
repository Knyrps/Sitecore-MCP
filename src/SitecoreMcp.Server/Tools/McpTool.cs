using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Base class for tools with a typed argument POCO. Handles binding and required-field
    /// validation so implementations only deal with strongly-typed, already-validated arguments.
    /// </summary>
    /// <typeparam name="TArgs">The argument POCO, annotated with [McpParam].</typeparam>
    public abstract class McpTool<TArgs> : IMcpTool where TArgs : new()
    {
        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <inheritdoc />
        public virtual bool RequiresWrite => false;

        /// <inheritdoc />
        public Type ArgumentType => typeof(TArgs);

        /// <inheritdoc />
        public McpToolResult Invoke(JObject arguments, McpCallContext context)
        {
            TArgs typed;
            try
            {
                typed = (arguments ?? new JObject()).ToObject<TArgs>() ?? new TArgs();
            }
            catch (JsonException ex)
            {
                return McpToolResult.Failure("Invalid arguments: " + ex.Message);
            }

            var supplied = arguments ?? new JObject();
            var missing = FirstMissingRequired(supplied);
            if (missing != null)
            {
                return McpToolResult.Failure($"Missing required argument '{missing}'.");
            }

            var result = Execute(typed, context);
            NoteIgnoredArguments(result, supplied);
            return result;
        }

        /// <summary>
        /// Adds a note when the caller supplied arguments this tool does not define. Schemas are
        /// lenient (unknown args are dropped), so without this a misplaced argument fails silently.
        /// </summary>
        private void NoteIgnoredArguments(McpToolResult result, JObject supplied)
        {
            if (result == null || result.IsError)
            {
                return;
            }

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in typeof(TArgs).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                known.Add(JsonNaming.ToJsonName(property));
            }

            var ignored = supplied.Properties()
                .Select(p => p.Name)
                .Where(name => !known.Contains(name))
                .ToList();

            if (ignored.Count > 0)
            {
                result.Content.Add(McpContent.FromText(
                    $"Note: these arguments are not defined by {Name} and were ignored: " +
                    $"{string.Join(", ", ignored)}. Check the tool's inputSchema."));
            }
        }

        /// <summary>Runs the tool with validated, strongly-typed arguments.</summary>
        protected abstract McpToolResult Execute(TArgs args, McpCallContext context);

        private static string FirstMissingRequired(JObject arguments)
        {
            foreach (var property in typeof(TArgs).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var meta = property.GetCustomAttribute<McpParamAttribute>();
                if (meta?.Required != true)
                {
                    continue;
                }

                var name = JsonNaming.ToJsonName(property);
                var token = arguments[name];
                if (token == null || token.Type == JTokenType.Null ||
                    (token.Type == JTokenType.String && string.IsNullOrEmpty(token.Value<string>())))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
