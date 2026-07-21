using System;

namespace SitecoreMcp.Server.Schema
{
    /// <summary>
    /// Supplies the JSON Schema text for one argument property. The same POCO drives both binding
    /// and schema generation, so the two cannot drift.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class McpParamAttribute : Attribute
    {
        /// <summary>The description emitted for this property in the JSON Schema, written for the model.</summary>
        public string Description { get; set; }

        /// <summary>Whether the property is listed in the schema's "required" array.</summary>
        public bool Required { get; set; }

        /// <summary>Closed set of allowed string values, emitted as JSON Schema "enum".</summary>
        public string[] Enum { get; set; }
    }
}
