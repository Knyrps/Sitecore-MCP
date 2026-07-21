using System.Reflection;
using Newtonsoft.Json;

namespace SitecoreMcp.Server.Schema
{
    /// <summary>
    /// The single source of truth for a property's JSON name. Both the schema generator and the
    /// argument binder use it, so the name the model is shown always matches the name we bind.
    /// </summary>
    public static class JsonNaming
    {
        /// <summary>Returns the [JsonProperty] name if set, otherwise the camelCased property name.</summary>
        public static string ToJsonName(PropertyInfo property)
        {
            var jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
            if (!string.IsNullOrEmpty(jsonProperty?.PropertyName))
            {
                return jsonProperty.PropertyName;
            }

            return char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
        }
    }
}
