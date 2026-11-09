using System;
using System.Collections.Generic;
using System.Linq;

namespace SitecoreMcp.Server.Tools.Configuration
{
    /// <summary>Which configuration to read: the live one, or one rebuilt for other rules and layers.</summary>
    public sealed class ConfigView
    {
        /// <summary>Whether the request was valid.</summary>
        public bool Succeeded { get; set; }

        /// <summary>Why it was not, written for the model.</summary>
        public string Error { get; set; }

        /// <summary>No rules and no layers were asked for: read the live merged configuration.</summary>
        public bool IsLiveConfiguration { get; set; }

        /// <summary>The rule definitions for a RuleBasedConfigReader, keyed "name:define" (e.g. "role:define").</summary>
        public IDictionary<string, string> RuleDefinitions { get; set; } = new Dictionary<string, string>();

        /// <summary>The configuration layers to include; empty means every layer.</summary>
        public IReadOnlyList<string> Layers { get; set; } = new string[0];
    }

    /// <summary>
    /// Turns a role/layer view request into what showconfig.aspx hands Sitecore's
    /// <c>RuleBasedConfigReader</c> (decompiled Sitecore.Client 10.3.0 and 10.4.0, identical): each
    /// requested rule becomes a "name:define" entry; a known rule that was not requested keeps the
    /// instance's first non-empty current value; layers are a pipe-separated list.
    /// </summary>
    public static class ConfigViewRules
    {
        /// <summary>The suffix Sitecore's rule-based reader expects on a rule definition key.</summary>
        public const string DefineSuffix = ":define";

        /// <summary>Builds the view.</summary>
        /// <param name="requested">Rule name to value (pipe-separated for several), e.g. role = ContentManagement.</param>
        /// <param name="layers">Pipe-separated layer names (Sitecore, Modules, Custom, Environment), or null for all.</param>
        /// <param name="knownRuleNames">The rule names this instance defines (IConfigurationRulesContext).</param>
        /// <param name="currentValue">The instance's current values of a rule.</param>
        public static ConfigView Build(
            IDictionary<string, string> requested,
            string layers,
            IEnumerable<string> knownRuleNames,
            Func<string, string[]> currentValue)
        {
            var known = (knownRuleNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var layerList = (layers ?? string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            var asked = (requested ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                .ToList();

            if (asked.Count == 0 && layerList.Length == 0)
            {
                return new ConfigView { Succeeded = true, IsLiveConfiguration = true };
            }

            var definitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in asked)
            {
                var name = known.FirstOrDefault(k => string.Equals(k, pair.Key.Trim(), StringComparison.OrdinalIgnoreCase));
                if (name == null)
                {
                    return new ConfigView
                    {
                        Succeeded = false,
                        Error = $"'{pair.Key}' is not a configuration rule on this instance. Known rules: {string.Join(", ", known)}."
                    };
                }

                definitions[name.ToLowerInvariant() + DefineSuffix] = pair.Value.Trim();
            }

            foreach (var name in known)
            {
                var key = name.ToLowerInvariant() + DefineSuffix;
                if (definitions.ContainsKey(key)) continue;

                var value = currentValue?.Invoke(name)?.FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (value != null) definitions[key] = value;
            }

            return new ConfigView { Succeeded = true, RuleDefinitions = definitions, Layers = layerList };
        }
    }
}
