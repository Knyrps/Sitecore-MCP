using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>
    /// Builds template structure: validates a section/field definition, applies base templates,
    /// creates sections and their fields, and wires standard values. Kept separate from the tools
    /// so future template tools (e.g. an update tool) reuse the same rules and creation logic.
    /// </summary>
    public static class TemplateBuilder
    {
        /// <summary>
        /// Validates the whole section/field definition before anything is created: section and
        /// field names, that every field carries a known field type, and that section and field
        /// names do not collide. Throws <see cref="McpToolException"/> on the first problem so a bad
        /// definition leaves the tree untouched.
        /// </summary>
        public static void Validate(IReadOnlyList<TemplateSectionDefinition> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                return;
            }

            var knownTypes = KnownFieldTypeNames();
            var sectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                ItemHelper.ValidateName(section.Name, "Invalid section name: ");
                if (!sectionNames.Add(section.Name))
                {
                    throw new McpToolException($"Duplicate section name '{section.Name}'.");
                }

                if (section.Fields == null)
                {
                    continue;
                }

                foreach (var field in section.Fields)
                {
                    ItemHelper.ValidateName(field.Name, "Invalid field name: ");

                    if (string.IsNullOrWhiteSpace(field.Type))
                    {
                        throw new McpToolException($"Field '{field.Name}' has no type.");
                    }

                    if (knownTypes.Count > 0 && !knownTypes.Contains(field.Type))
                    {
                        throw new McpToolException(
                            $"Field type '{field.Type}' (field '{field.Name}') is not a known Sitecore field type. " +
                            UnknownTypeHint(field.Type, knownTypes));
                    }

                    // A field name resolves against the whole template, not just its section, so a
                    // duplicate anywhere makes item.Fields[name] ambiguous. Reject rather than hint.
                    if (!fieldNames.Add(field.Name))
                    {
                        throw new McpToolException(
                            $"Duplicate field name '{field.Name}'. Field names must be unique across the entire template so each can be addressed by name.");
                    }
                }
            }
        }

        /// <summary>
        /// Points the template's base-template field at the given base templates, so the new template
        /// inherits their fields. Pass at least the Standard Template to get the standard fields.
        /// </summary>
        public static void SetBaseTemplates(Item template, IReadOnlyList<Item> baseTemplates)
        {
            var ids = string.Join("|", baseTemplates.Select(b => b.ID.ToString()));
            ItemEditor.Edit(template, editable => editable[FieldIDs.BaseTemplate] = ids);
        }

        /// <summary>
        /// Creates each section and its fields under the template, writing each field's type, flags,
        /// and source. Assumes <see cref="Validate"/> has already passed.
        /// </summary>
        public static void AddSections(TemplateItem template, IReadOnlyList<TemplateSectionDefinition> sections)
        {
            if (sections == null)
            {
                return;
            }

            foreach (var section in sections)
            {
                var createdSection = template.AddSection(section.Name);
                if (section.Fields == null)
                {
                    continue;
                }

                foreach (var field in section.Fields)
                {
                    var createdField = createdSection.AddField(field.Name);
                    ItemEditor.Edit(createdField.InnerItem, editable =>
                    {
                        editable["Type"] = field.Type;
                        if (field.IsShared) editable["Shared"] = "1";
                        if (field.IsUnversioned) editable["Unversioned"] = "1";
                        if (!string.IsNullOrEmpty(field.Source)) editable["Source"] = field.Source;
                    });
                }
            }
        }

        /// <summary>
        /// Creates the template's __Standard Values item (based on the template itself) and wires the
        /// template's standard-values field to it. Returns the existing one if already present.
        /// </summary>
        public static Item CreateStandardValues(TemplateItem template)
        {
            var inner = template.InnerItem;

            var existing = inner.Children[StandardValuesName];
            if (existing != null)
            {
                return existing;
            }

            var standardValues = inner.Add(StandardValuesName, new TemplateID(template.ID));
            ItemEditor.Edit(inner, editable => editable[FieldIDs.StandardValues] = standardValues.ID.ToString());
            return standardValues;
        }

        private const string StandardValuesName = "__Standard Values";

        /// <summary>
        /// Builds the "here is what you can use instead" tail for an unknown-field-type error: the
        /// closest real types on this instance (matching ignoring case, spaces, and punctuation, so a
        /// typo like "Single Line Text" points at "Single-Line Text"), or the full valid set when
        /// nothing is close. Always instance-specific, so custom types are offered too.
        /// </summary>
        private static string UnknownTypeHint(string input, ISet<string> knownTypes)
        {
            var suggestions = SuggestFieldTypes(input, knownTypes);
            if (suggestions.Count > 0)
            {
                return $"Closest matches: {string.Join(", ", suggestions)}.";
            }

            var all = knownTypes.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            return $"Valid field types: {string.Join(", ", all)}.";
        }

        private static IReadOnlyList<string> SuggestFieldTypes(string input, IEnumerable<string> knownTypes)
        {
            var target = Normalize(input);
            var exact = new List<string>();
            var partial = new List<string>();

            foreach (var name in knownTypes)
            {
                var normalized = Normalize(name);
                if (normalized == target)
                {
                    exact.Add(name);
                }
                else if (target.Length >= 3 && (normalized.Contains(target) || target.Contains(normalized)))
                {
                    partial.Add(name);
                }
            }

            // An exact match (ignoring case/spacing/punctuation) is the intended type, so a
            // punctuation typo resolves cleanly without the noisier substring matches alongside it.
            var best = exact.Count > 0
                ? (IEnumerable<string>)exact
                : partial.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

            return best
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private static string Normalize(string value) =>
            new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        /// <summary>
        /// Collects the set of valid field type names from the core database's /sitecore/system/Field
        /// types (the field-type registry lives in core, not the content databases). Falls back to
        /// every descendant name when the type items cannot be told apart by template, and returns an
        /// empty set (validation skipped) when core or the root is unavailable, so this never blocks
        /// on a quirk or on a user without core read access.
        /// </summary>
        private static ISet<string> KnownFieldTypeNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var core = Factory.GetDatabase("core", false);
            var root = core?.GetItem("/sitecore/system/Field types");
            if (root == null)
            {
                return names;
            }

            var descendants = root.Axes.GetDescendants();
            foreach (var descendant in descendants)
            {
                if (string.Equals(descendant.TemplateName, "Template field type", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(descendant.Name);
                }
            }

            if (names.Count == 0)
            {
                foreach (var descendant in descendants)
                {
                    names.Add(descendant.Name);
                }
            }

            return names;
        }
    }
}
