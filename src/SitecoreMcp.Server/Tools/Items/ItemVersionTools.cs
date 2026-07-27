using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Fields;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="AddItemVersionTool"/>.</summary>
    public sealed class AddItemVersionArgs : ItemQueryArgs
    {
        /// <summary>A language whose latest version's field values seed the new version.</summary>
        [McpParam(Description = "Copy field values from this language's latest version into the new version (for translation workflows). Omit to add an empty version based on the target language's latest.")]
        public string SourceLanguage { get; set; }
    }

    /// <summary>
    /// Adds a version to an item in a language, optionally seeding it with another language's field
    /// values - the common start of a translation.
    /// </summary>
    public sealed class AddItemVersionTool : McpTool<AddItemVersionArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_add_item_version";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Add a new version of a Sitecore item in a language ('language' selects which). Pass " +
            "sourceLanguage to seed the new version with that language's latest field values (a " +
            "translation starting point); otherwise the new version starts from the target language's " +
            "latest. Reports the created version number and any copied fields.";

        /// <inheritdoc />
        protected override McpToolResult Execute(AddItemVersionArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            var created = item.Versions.AddVersion();
            if (created == null)
            {
                throw new McpToolException("Sitecore did not create a new version (the user may lack version-write access).");
            }

            var copied = new List<string>();
            if (!string.IsNullOrWhiteSpace(args.SourceLanguage))
            {
                var source = ItemResolver.Resolve(context, args.Path, args.Database, args.SourceLanguage);
                if (source.Versions.Count == 0)
                {
                    throw new McpToolException($"The item has no versions in language '{source.Language.Name}' to copy from.");
                }

                source.Fields.ReadAll();
                var values = new Dictionary<string, string>();
                foreach (Field field in source.Fields)
                {
                    // Shared fields hold one value across languages, and versioned __ fields
                    // (workflow, locks, statistics) must not be cloned into a new version.
                    if (!field.Name.StartsWith("__") && !field.Shared && !string.IsNullOrEmpty(field.Value))
                    {
                        values[field.Name] = field.Value;
                    }
                }

                if (values.Count > 0)
                {
                    var write = ItemEditor.WriteFields(created, values, context);
                    copied.AddRange(write.Written);
                }
            }

            var result = new JObject
            {
                ["item"] = created.Paths.FullPath,
                ["language"] = created.Language.Name,
                ["version"] = created.Version.Number,
                ["versionsInLanguage"] = created.Versions.GetVersionNumbers().Length
            };
            if (!string.IsNullOrWhiteSpace(args.SourceLanguage))
            {
                result["copiedFrom"] = args.SourceLanguage;
                result["copiedFields"] = new JArray(copied.Cast<object>().ToArray());
            }

            return McpToolResult.Structured(result);
        }
    }

    /// <summary>Arguments for <see cref="RemoveItemVersionTool"/>.</summary>
    public sealed class RemoveItemVersionArgs : ItemQueryArgs
    {
        /// <summary>The version number to remove; the latest when omitted.</summary>
        [McpParam(Description = "Version number to remove. Omit to remove the latest version in the language.")]
        public int? Version { get; set; }
    }

    /// <summary>Removes one version of an item in a language.</summary>
    public sealed class RemoveItemVersionTool : McpTool<RemoveItemVersionArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_remove_item_version";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Remove one version of a Sitecore item in a language ('language' selects which; 'version' " +
            "picks the number, defaulting to the latest). Removing the last version leaves the item " +
            "with no versions in that language - it stops existing there - and the result says so.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RemoveItemVersionArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            var numbers = item.Versions.GetVersionNumbers().Select(v => v.Number).ToList();
            if (numbers.Count == 0)
            {
                return McpToolResult.Failure($"The item has no versions in language '{item.Language.Name}'.");
            }

            var target = args.Version ?? numbers.Max();
            if (!numbers.Contains(target))
            {
                return McpToolResult.Failure(
                    $"Version {target} does not exist in language '{item.Language.Name}'. Versions: {string.Join(", ", numbers)}.");
            }

            var versionItem = item.Database.GetItem(item.ID, item.Language, Sitecore.Data.Version.Parse(target));
            if (versionItem == null)
            {
                return McpToolResult.Failure($"Version {target} could not be loaded.");
            }

            versionItem.Versions.RemoveVersion();

            // Report what is actually left, from a fresh read.
            var fresh = item.Database.GetItem(item.ID, item.Language);
            var remaining = fresh?.Versions.GetVersionNumbers().Select(v => v.Number).ToArray() ?? new int[0];

            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["language"] = item.Language.Name,
                ["removedVersion"] = target,
                ["remainingVersions"] = new JArray(remaining.Cast<object>().ToArray())
            };
            if (remaining.Length == 0)
            {
                result["note"] = "That was the last version: the item now has no versions in this language.";
            }

            return McpToolResult.Structured(result);
        }
    }
}
