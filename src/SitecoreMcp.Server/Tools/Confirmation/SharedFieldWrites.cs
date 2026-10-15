using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Tools.Confirmation
{
    /// <summary>One field write as planned, before it is applied.</summary>
    public sealed class PlannedFieldChange
    {
        /// <summary>Creates the planned change.</summary>
        public PlannedFieldChange(string name, bool shared, bool unversioned, string oldValue, string newValue)
        {
            Name = name;
            Shared = shared;
            Unversioned = unversioned;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        /// <summary>The field name.</summary>
        public string Name { get; }

        /// <summary>Whether the field holds one value across every version and language.</summary>
        public bool Shared { get; }

        /// <summary>Whether the field holds one value across every version of a language.</summary>
        public bool Unversioned { get; }

        /// <summary>The value before the write.</summary>
        public string OldValue { get; }

        /// <summary>The value the write sets.</summary>
        public string NewValue { get; }

        /// <summary>Whether the write actually changes the value.</summary>
        public bool Changes => !string.Equals(OldValue, NewValue, StringComparison.Ordinal);

        /// <summary>Whether the field's value reaches beyond the version being edited.</summary>
        public bool ReachesBeyondVersion => Shared || Unversioned;
    }

    /// <summary>
    /// Decides when a field write is a direct change and describes it. A shared or unversioned field
    /// cannot be held in a pending version - writing it is live for every version at once - so a write
    /// that changes one needs the editor's confirmation (ADR 0001).
    /// </summary>
    public static class SharedFieldWrites
    {
        private const int MaxPreviewValueLength = 200;

        /// <summary>The changes that need confirmation: shared or unversioned fields whose value actually changes.</summary>
        public static IReadOnlyList<PlannedFieldChange> NeedingConfirmation(IEnumerable<PlannedFieldChange> changes) =>
            (changes ?? Enumerable.Empty<PlannedFieldChange>())
                .Where(change => change.ReachesBeyondVersion && change.Changes)
                .ToList();

        /// <summary>Builds the preview of a field write that touches shared or unversioned fields.</summary>
        public static DirectChangePreview Preview(string itemPath, string language, IReadOnlyList<PlannedFieldChange> changes, string state)
        {
            var reaching = NeedingConfirmation(changes);
            var parts = new List<string>();

            var shared = reaching.Where(c => c.Shared).Select(c => "'" + c.Name + "'").ToList();
            if (shared.Count > 0)
            {
                parts.Add($"shared {Plural(shared.Count, "field")} {string.Join(", ", shared)} (one value for every version and language)");
            }

            var unversioned = reaching.Where(c => !c.Shared && c.Unversioned).Select(c => "'" + c.Name + "'").ToList();
            if (unversioned.Count > 0)
            {
                parts.Add($"unversioned {Plural(unversioned.Count, "field")} {string.Join(", ", unversioned)} (one value for every version in '{language}')");
            }

            var summary = $"Write {string.Join(" and ", parts)} on '{itemPath}'. The new value is live immediately and cannot be discarded with a pending version.";

            var fields = new JArray();
            foreach (var change in changes.Where(c => c.Changes))
            {
                fields.Add(new JObject
                {
                    ["name"] = change.Name,
                    ["scope"] = change.Shared ? "shared" : change.Unversioned ? "unversioned" : "versioned",
                    ["from"] = Shorten(change.OldValue),
                    ["to"] = Shorten(change.NewValue)
                });
            }

            return new DirectChangePreview(summary, new JObject
            {
                ["item"] = itemPath,
                ["language"] = language,
                ["fields"] = fields
            }, state);
        }

        private static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

        private static string Shorten(string value) =>
            value.Length <= MaxPreviewValueLength ? value : value.Substring(0, MaxPreviewValueLength) + "…";
    }
}
