using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Templates
{
    /// <summary>
    /// One field within a template section: its name, Sitecore field type, and the flags and source
    /// that shape how it stores and edits values. Shared by every tool that builds template fields.
    /// </summary>
    public sealed class TemplateFieldDefinition
    {
        /// <summary>The name of the field. Must be unique across the whole template.</summary>
        [McpParam(Description = "The name of the field. Must be unique across the whole template.", Required = true)]
        public string Name { get; set; }

        /// <summary>The Sitecore field type, e.g. "Single-Line Text", "Rich Text", "Droplink", "Image".</summary>
        [McpParam(Description = "The Sitecore field type, e.g. Single-Line Text, Rich Text, Droplink, Image. Must match an existing field type exactly.", Required = true)]
        public string Type { get; set; }

        /// <summary>Whether the field shares one value across all languages.</summary>
        [McpParam(Description = "Whether the field shares one value across all languages.")]
        public bool IsShared { get; set; }

        /// <summary>Whether the field holds one value across all versions of a language.</summary>
        [McpParam(Description = "Whether the field holds one value across all versions of a language.")]
        public bool IsUnversioned { get; set; }

        /// <summary>The field's source, e.g. a datasource path for a link or list field. Optional.</summary>
        [McpParam(Description = "The field's source, e.g. a datasource path for a link or list field. Optional.")]
        public string Source { get; set; }
    }

    /// <summary>A named section grouping a set of fields within a template.</summary>
    public sealed class TemplateSectionDefinition
    {
        /// <summary>The name of the section. Must be unique within the template.</summary>
        [McpParam(Description = "The name of the section. Must be unique within the template.", Required = true)]
        public string Name { get; set; }

        /// <summary>The fields defined in this section.</summary>
        [McpParam(Description = "The fields defined in this section.")]
        public TemplateFieldDefinition[] Fields { get; set; }
    }
}
