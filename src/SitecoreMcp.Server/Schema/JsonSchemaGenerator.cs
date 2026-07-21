using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Schema
{
    /// <summary>
    /// Builds a JSON Schema object from an argument POCO by reflection. Deliberately small: it
    /// covers the shapes our tool arguments actually use (primitives, enums, arrays, nested
    /// objects) and no more, so it needs no schema library.
    /// </summary>
    public static class JsonSchemaGenerator
    {
        public static JObject Generate(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return BuildObject(type, new HashSet<Type>());
        }

        private static JObject BuildObject(Type type, HashSet<Type> seen)
        {
            // Guard against a type that references itself; such a property is described as a bare object.
            if (!seen.Add(type))
            {
                return new JObject { ["type"] = "object" };
            }

            var properties = new JObject();
            var required = new JArray();

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                var meta = property.GetCustomAttribute<McpParamAttribute>();
                var name = JsonNaming.ToJsonName(property);

                var schema = BuildProperty(property.PropertyType, meta, seen);
                if (!string.IsNullOrEmpty(meta?.Description))
                {
                    schema["description"] = meta.Description;
                }

                properties[name] = schema;
                if (meta?.Required == true)
                {
                    required.Add(name);
                }
            }

            var result = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };
            if (required.Count > 0)
            {
                result["required"] = required;
            }

            seen.Remove(type);
            return result;
        }

        private static JObject BuildProperty(Type type, McpParamAttribute meta, HashSet<Type> seen)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (meta?.Enum != null && meta.Enum.Length > 0)
            {
                return new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(meta.Enum.Cast<object>())
                };
            }

            var primitive = PrimitiveType(type);
            if (primitive != null)
            {
                return new JObject { ["type"] = primitive };
            }

            if (TryGetElementType(type, out var elementType))
            {
                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = BuildProperty(elementType, null, seen)
                };
            }

            return BuildObject(type, seen);
        }

        private static string PrimitiveType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            return null;
        }

        private static bool TryGetElementType(Type type, out Type elementType)
        {
            elementType = null;

            if (type == typeof(string)) return false;

            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return true;
            }

            var enumerable = new[] { type }
                .Concat(type.GetInterfaces())
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable != null)
            {
                elementType = enumerable.GetGenericArguments()[0];
                return true;
            }

            // Non-generic IEnumerable has no element type to describe; treat it as a nested object.
            return false;
        }
    }
}
