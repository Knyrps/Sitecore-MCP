using System;
using System.Linq;
using System.Web;
using Sitecore;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Layouts;
using SitecoreMcp.Server.Tools.Items;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>
    /// Reads and writes an item's presentation. Sitecore keeps two layout fields — a shared base
    /// (__Renderings) and a per-version final layout (__Final Renderings) stored as a delta over the
    /// base — and only LayoutField.GetFieldValue resolves the effective layout. This centralises that
    /// so the tools never touch the raw XML or the delta mechanics directly.
    /// </summary>
    public static class LayoutEditor
    {
        private const string DevicesRoot = "/sitecore/layout/Devices";

        /// <summary>Reads the item's effective layout for the chosen field (final by default).</summary>
        public static LayoutDefinition Read(Item item, bool finalLayout)
        {
            var field = FieldFor(item, finalLayout);
            var xml = LayoutField.GetFieldValue(field);
            return string.IsNullOrEmpty(xml) ? new LayoutDefinition() : LayoutDefinition.Parse(xml);
        }

        /// <summary>
        /// Mutates the item's layout and writes it back inside an edit. For the final field the new
        /// layout is stored as a delta against the shared base, matching how the Experience Editor
        /// saves a page-level change; the shared field stores the full layout.
        /// </summary>
        public static void Edit(Item item, bool finalLayout, Action<LayoutDefinition> mutate)
        {
            var field = FieldFor(item, finalLayout);
            var xml = LayoutField.GetFieldValue(field);
            var layout = string.IsNullOrEmpty(xml) ? new LayoutDefinition() : LayoutDefinition.Parse(xml);

            mutate(layout);
            var newXml = layout.ToXml();

            ItemEditor.Edit(item, editable =>
            {
                var editField = FieldFor(editable, finalLayout);
                if (finalLayout)
                {
                    // Store a delta against the shared base so the item still inherits later base
                    // changes for anything it did not override.
                    LayoutField.SetFieldValue(editField, newXml, LayoutField.GetBaseLayoutValue(editField));
                }
                else
                {
                    LayoutField.SetFieldValue(editField, newXml);
                }
            });
        }

        /// <summary>
        /// Resets the item's layout field to standard-values inheritance. Returns false when the field
        /// already holds no local value, so the caller can report a benign no-op rather than an error.
        /// </summary>
        public static bool Reset(Item item, bool finalLayout)
        {
            var field = FieldFor(item, finalLayout);
            if (!field.ContainsStandardValue && string.IsNullOrEmpty(field.GetValue(false, false)))
            {
                return false;
            }

            ItemEditor.Edit(item, editable => FieldFor(editable, finalLayout).Reset());
            return true;
        }

        /// <summary>Resolves a device item by name (e.g. "Default") to drive which device's renderings are read.</summary>
        public static Item ResolveDevice(Item context, string name)
        {
            var deviceName = string.IsNullOrEmpty(name) ? "Default" : name;
            var root = context.Database.GetItem(DevicesRoot);
            var device = root?.Children[deviceName];
            if (device == null)
            {
                throw new McpToolException($"Device '{deviceName}' was not found under {DevicesRoot}.");
            }

            return device;
        }

        /// <summary>Gets the device's definition within a layout, or null when the layout has no entry for it.</summary>
        public static DeviceDefinition Device(LayoutDefinition layout, Item deviceItem) =>
            layout.GetDevice(deviceItem.ID.ToString());

        /// <summary>
        /// Gets the device's definition, adding an empty one to the layout when it has no entry yet, so
        /// a rendering can be placed on a device the item does not presently override.
        /// </summary>
        public static DeviceDefinition GetOrCreateDevice(LayoutDefinition layout, Item deviceItem)
        {
            var id = deviceItem.ID.ToString();
            var device = layout.GetDevice(id);
            if (device != null)
            {
                return device;
            }

            device = new DeviceDefinition { ID = id };
            layout.Devices.Add(device);
            return device;
        }

        /// <summary>
        /// Finds a rendering on a device by its unique ID, throwing a clear error that lists the unique
        /// IDs actually present when it is not found, so a stale or mistyped ID is easy to correct.
        /// </summary>
        public static RenderingDefinition RequireRendering(DeviceDefinition device, string uniqueId)
        {
            var rendering = device?.GetRenderingByUniqueId(uniqueId);
            if (rendering != null)
            {
                return rendering;
            }

            var present = device?.Renderings == null
                ? string.Empty
                : string.Join(", ", device.Renderings
                    .Cast<RenderingDefinition>()
                    .Where(r => r != null)
                    .Select(r => r.UniqueId)
                    .Take(20));

            throw new McpToolException(
                $"No rendering with uniqueId '{uniqueId}' on this device." +
                (string.IsNullOrEmpty(present) ? " The device has no renderings." : $" Present unique IDs: {present}.") +
                " Call sitecore_get_renderings to list them.");
        }

        /// <summary>Encodes a name/value parameter map back into the query-string blob a rendering stores.</summary>
        public static string EncodeParameters(System.Collections.Generic.IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return string.Empty;
            }

            var query = HttpUtility.ParseQueryString(string.Empty);
            foreach (var pair in parameters)
            {
                query[pair.Key] = pair.Value ?? string.Empty;
            }

            return query.ToString();
        }

        private static Field FieldFor(Item item, bool finalLayout) =>
            item.Fields[finalLayout ? FieldIDs.FinalLayoutField : FieldIDs.LayoutField];
    }
}
