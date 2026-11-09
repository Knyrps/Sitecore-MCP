using Newtonsoft.Json.Linq;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Security;

namespace SitecoreMcp.Server.Tools.Confirmation
{
    /// <summary>
    /// Implemented by a direct-change tool to describe itself to a client that does not confirm direct
    /// changes, where its usual description (a preview first, then confirmation) would be wrong.
    /// </summary>
    public interface IDirectChangeDescription
    {
        /// <summary>The tool's description for a client whose direct changes run on the first call.</summary>
        string DescriptionWithoutConfirmation { get; }
    }

    /// <summary>
    /// Implemented by a direct-change tool that always needs confirmation, whatever the calling client's
    /// <c>confirmDirectChanges</c> setting (package install: it can deploy code and restart the site).
    /// Its description and token argument are listed unchanged to every client, and it runs through
    /// <see cref="DirectChangeGate"/> with confirmation required.
    /// </summary>
    public interface IAlwaysConfirmedDirectChange
    {
    }

    /// <summary>
    /// Fits a listed tool's wording to the calling client. Only descriptions change: the input schema
    /// keeps its confirmationToken argument for every client, because CorePilot pins the upstream tool
    /// list and tells direct changes apart by that argument.
    /// </summary>
    public static class DirectChangeDescriptions
    {
        /// <summary>The confirmationToken argument's description for a client that does not confirm direct changes.</summary>
        public const string TokenDescriptionWithoutConfirmation =
            "Not needed for this client: its direct changes run on the first call, and a token passed here is ignored.";

        /// <summary>
        /// The descriptor as the client should see it. Unchanged for a confirming client; for one that
        /// does not confirm, a copy with the tool's <see cref="IDirectChangeDescription"/> wording and the
        /// token argument described as unneeded. The original descriptor is never modified.
        /// </summary>
        public static ToolDescriptor ForClient(ToolDescriptor descriptor, object tool, bool confirmDirectChanges)
        {
            if (confirmDirectChanges || descriptor == null || tool is IAlwaysConfirmedDirectChange) return descriptor;

            var schema = descriptor.InputSchema;
            var token = schema?["properties"]?[ConfirmationCanonicalizer.TokenArgument] as JObject;
            var described = tool as IDirectChangeDescription;
            if (token == null && described == null) return descriptor;

            if (token != null)
            {
                schema = (JObject)schema.DeepClone();
                schema["properties"][ConfirmationCanonicalizer.TokenArgument]["description"] = TokenDescriptionWithoutConfirmation;
            }

            return new ToolDescriptor
            {
                Name = descriptor.Name,
                Title = descriptor.Title,
                Description = described?.DescriptionWithoutConfirmation ?? descriptor.Description,
                InputSchema = schema,
                OutputSchema = descriptor.OutputSchema
            };
        }
    }
}
