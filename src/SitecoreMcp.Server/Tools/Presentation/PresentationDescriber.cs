using System.Collections;
using System.Web;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Layouts;

namespace SitecoreMcp.Server.Tools.Presentation
{
    /// <summary>
    /// Projects a parsed layout to JSON. A rendering records the item it places, a placeholder, a
    /// datasource, and a query-string parameter blob; this turns that into a readable shape and
    /// resolves each rendering item's name so the caller sees what a component actually is.
    /// </summary>
    public static class PresentationDescriber
    {
        /// <summary>Describes every rendering on a device: identity, placeholder, datasource, and parsed parameters.</summary>
        public static JArray Renderings(DeviceDefinition device, Database database)
        {
            var result = new JArray();
            if (device?.Renderings == null)
            {
                return result;
            }

            foreach (RenderingDefinition rendering in device.Renderings)
            {
                if (rendering == null)
                {
                    continue;
                }

                result.Add(Rendering(rendering, database));
            }

            return result;
        }

        /// <summary>Wraps a single changed rendering with the item and device it belongs to, for a write result.</summary>
        public static JObject Result(string itemPath, string deviceName, bool finalLayout, JObject rendering) => new JObject
        {
            ["item"] = itemPath,
            ["device"] = deviceName,
            ["finalLayout"] = finalLayout,
            ["rendering"] = rendering
        };

        /// <summary>Describes a single rendering instance.</summary>
        public static JObject Rendering(RenderingDefinition rendering, Database database)
        {
            var renderingItem = ResolveRendering(rendering.ItemID, database);

            return new JObject
            {
                ["uniqueId"] = rendering.UniqueId,
                ["renderingId"] = rendering.ItemID,
                ["renderingName"] = renderingItem?.Name,
                ["renderingPath"] = renderingItem?.Paths.FullPath,
                ["placeholder"] = rendering.Placeholder,
                ["datasource"] = string.IsNullOrEmpty(rendering.Datasource) ? null : rendering.Datasource,
                ["parameters"] = Parameters(rendering.Parameters)
            };
        }

        /// <summary>Parses a rendering's query-string parameter blob into a JSON object.</summary>
        public static JObject Parameters(string parameters)
        {
            var result = new JObject();
            if (string.IsNullOrEmpty(parameters))
            {
                return result;
            }

            var parsed = HttpUtility.ParseQueryString(parameters);
            foreach (string key in parsed.Keys)
            {
                if (key != null)
                {
                    result[key] = parsed[key];
                }
            }

            return result;
        }

        private static Item ResolveRendering(string itemId, Database database)
        {
            if (string.IsNullOrEmpty(itemId) || !ID.IsID(itemId))
            {
                return null;
            }

            return database?.GetItem(ID.Parse(itemId));
        }
    }
}
