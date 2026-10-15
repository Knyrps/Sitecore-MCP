using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="UpdateItemTool"/>.</summary>
    public sealed class UpdateItemArgs : ItemQueryArgs
    {
        /// <summary>The field-name to value map to write.</summary>
        [McpParam(Description = "Map of field name to new value.", Required = true)]
        public Dictionary<string, string> Fields { get; set; }

        /// <summary>The token from this tool's preview, needed only when a shared or unversioned field changes.</summary>
        [McpParam(Description =
            "Confirmation token, needed only when the update changes a shared or unversioned field. Omit it: such " +
            "an update then returns a preview and changes nothing (versioned-only updates are written directly). " +
            "The update runs when the same call, with identical arguments, carries the token the editor confirmed.")]
        public string ConfirmationToken { get; set; }
    }

    /// <summary>Writes field values to an existing item, checking field-write permission first.</summary>
    public sealed class UpdateItemTool : McpTool<UpdateItemArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_update_item";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Update field values on an existing Sitecore item. Only the fields you pass are changed; " +
            "an unknown field or one you cannot write is rejected before anything is saved. Changing a " +
            "shared or unversioned field is a direct change (it is live for every version at once): such " +
            "an update first returns a preview and changes nothing until the editor has confirmed.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UpdateItemArgs args, McpCallContext context)
        {
            if (args.Fields == null || args.Fields.Count == 0)
            {
                return McpToolResult.Failure("No fields to update were supplied.");
            }

            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);
            ItemEditor.ValidateFields(item, args.Fields.Keys, context);

            var planned = ItemEditor.PlanFieldChanges(item, args.Fields);
            if (SharedFieldWrites.NeedingConfirmation(planned).Count == 0)
            {
                return Write(item, args, context);
            }

            var preview = SharedFieldWrites.Preview(item.Paths.FullPath, item.Language.Name, planned, ItemFingerprint.Of(item));
            return context.ConfirmDirectChange(Name, args, args.ConfirmationToken, preview, () => Write(item, args, context));
        }

        private static McpToolResult Write(Sitecore.Data.Items.Item item, UpdateItemArgs args, McpCallContext context)
        {
            var write = ItemEditor.WriteFields(item, args.Fields, context);

            var result = new ItemProjector(context).ProjectSummary(item);
            result["updatedFields"] = new JArray(write.Written);
            if (write.NotPersisted.Count > 0)
            {
                result["notPersisted"] = new JArray(write.NotPersisted);
                result["warning"] =
                    "These fields reported saved but read back with their previous value, so the change " +
                    "did not persist (likely field security, a computed field, or a save handler): " +
                    string.Join(", ", write.NotPersisted) + ".";
            }
            return McpToolResult.Structured(result);
        }
    }
}
