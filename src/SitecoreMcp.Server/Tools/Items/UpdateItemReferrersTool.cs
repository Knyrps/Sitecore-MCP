using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Links;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="UpdateItemReferrersTool"/>.</summary>
    public sealed class UpdateItemReferrersArgs : ItemQueryArgs
    {
        /// <summary>The item incoming links should point at instead; omit to remove them.</summary>
        [McpParam(Description = "Path or ID of the item the links should point at instead. Omit to REMOVE the links rather than retarget them.")]
        public string NewTarget { get; set; }
    }

    /// <summary>
    /// Repoints or removes every link that targets an item, so it can be retired or replaced without
    /// leaving broken references behind. Edits are grouped per referring item and reported
    /// individually, since some may be locked or unwritable while others succeed.
    /// </summary>
    public sealed class UpdateItemReferrersTool : McpTool<UpdateItemReferrersArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_update_item_referrers";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Repoint every link that targets an item to a different item, or remove those links when " +
            "newTarget is omitted. Use it to retire or replace an item without leaving broken " +
            "references. This edits OTHER items — the ones referencing this one — so call " +
            "sitecore_get_item_referrers first to see the blast radius. Each referring item is " +
            "reported separately: one being locked or unwritable does not stop the rest.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UpdateItemReferrersArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            Item newTarget = null;
            if (!string.IsNullOrEmpty(args.NewTarget))
            {
                newTarget = ItemResolver.Resolve(context, args.NewTarget, args.Database, args.Language);
                if (newTarget.ID == item.ID)
                {
                    return McpToolResult.Failure("newTarget is the item itself; the links already point there.");
                }
            }

            var links = Sitecore.Globals.LinkDatabase.GetReferrers(item) ?? new ItemLink[0];
            if (links.Length == 0)
            {
                return McpToolResult.Structured(new JObject
                {
                    ["item"] = item.Paths.FullPath,
                    ["updated"] = 0,
                    ["hint"] = "Nothing references this item according to the Link Database, so there was nothing to change."
                });
            }

            // One edit per referring item rather than per link, so an item referencing this one from
            // several fields is opened, changed, and saved once.
            var updated = new JArray();
            var failed = new JArray();
            var skipped = 0;

            foreach (var group in links.GroupBy(link => link.SourceItemID))
            {
                var linksForSource = group.ToList();
                var source = LinkDescriber.Resolve(linksForSource[0].SourceDatabaseName, group.Key, context);
                if (source == null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var changedFields = Apply(source, linksForSource, newTarget);
                    if (changedFields.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    updated.Add(new JObject
                    {
                        ["sourceId"] = source.ID.ToString(),
                        ["sourcePath"] = source.Paths.FullPath,
                        ["fields"] = new JArray(changedFields.Cast<object>().ToArray())
                    });
                }
                catch (Exception ex)
                {
                    // A referrer that cannot be saved - locked by someone else, workflow, field
                    // security - must not abort the others, and must be named rather than swallowed.
                    failed.Add(new JObject
                    {
                        ["sourceId"] = source.ID.ToString(),
                        ["sourcePath"] = source.Paths.FullPath,
                        ["reason"] = ex.Message
                    });
                }
            }

            var result = new JObject
            {
                ["item"] = item.Paths.FullPath,
                ["action"] = newTarget == null ? "removed" : "retargeted",
                ["newTarget"] = newTarget?.Paths.FullPath,
                ["referrers"] = links.Length,
                ["updated"] = updated.Count,
                ["updatedItems"] = updated
            };

            if (failed.Count > 0)
            {
                result["failed"] = failed;
                result["warning"] = $"{failed.Count} referring item(s) could not be updated and still point at this item.";
            }

            if (skipped > 0)
            {
                result["skipped"] = skipped;
                result["hint"] = "Skipped referrers were unreadable by this client, or their field no " +
                                 "longer supports relinking, so they were left untouched.";
            }

            return McpToolResult.Structured(result);
        }

        /// <summary>
        /// Applies the relink or removal to every affected field on one referring item, inside a single
        /// edit. Returns the names of the fields actually changed.
        /// </summary>
        private static List<string> Apply(Item source, IReadOnlyList<ItemLink> links, Item newTarget)
        {
            // Work out what can be rewritten before opening an edit. Only field types that model links
            // know how to rewrite themselves; anything else is left alone rather than having its raw
            // value guessed at. Entering an edit that then changes nothing fails to save, which would
            // be reported as an error instead of the no-op it really is.
            var applicable = links
                .Where(link =>
                {
                    // The field can be gone if the template changed since the link was indexed.
                    var field = source.Fields[link.SourceFieldID];
                    return field != null && FieldTypeManager.GetField(field) != null;
                })
                .ToList();

            if (applicable.Count == 0)
            {
                return new List<string>();
            }

            var changed = new List<string>();
            ItemEditor.Edit(source, editable =>
            {
                foreach (var link in applicable)
                {
                    var custom = FieldTypeManager.GetField(editable.Fields[link.SourceFieldID]);
                    if (custom == null)
                    {
                        continue;
                    }

                    if (newTarget == null)
                    {
                        custom.RemoveLink(link);
                    }
                    else
                    {
                        custom.Relink(link, newTarget);
                    }

                    changed.Add(editable.Fields[link.SourceFieldID].Name);
                }
            });

            return changed;
        }
    }
}
