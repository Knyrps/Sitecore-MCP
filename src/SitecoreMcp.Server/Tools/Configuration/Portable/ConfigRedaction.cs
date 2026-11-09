using System;
using System.Linq;
using System.Xml;

namespace SitecoreMcp.Server.Tools.Configuration
{
    /// <summary>
    /// Hides configuration values that look like secrets before they leave the server. There is
    /// deliberately no switch to turn this off: the merged configuration has <c>$(env:...)</c>
    /// variables already resolved, so a secret injected through the environment appears in clear, and
    /// everything a tool returns ends up in a model provider's context and the client's transcripts.
    /// An administrator who needs a raw value reads the file on the server.
    /// </summary>
    public static class ConfigRedaction
    {
        /// <summary>The marker that replaces a redacted value.</summary>
        public const string Redacted = "[redacted]";

        private static readonly string[] SensitiveWords = { "password", "secret", "key", "token", "connectionstring" };

        // A name ending like this names (or points at) a secret rather than holding it:
        // connectionStringName="master", keyEnvVar="SITECORE_MCP_KEY".
        private static readonly string[] NamingSuffixes = { "name", "envvar" };

        /// <summary>
        /// Whether an attribute, element or setting name looks like it holds a secret: it contains
        /// password, secret, key, token or connectionString (case-insensitive), and does not merely name one.
        /// </summary>
        public static bool IsSensitiveName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            if (NamingSuffixes.Any(suffix => lower.EndsWith(suffix, StringComparison.Ordinal))) return false;
            return SensitiveWords.Any(word => lower.IndexOf(word, StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// Redacts secret-looking values on an element and its descendants, in place (call it on a copy):
        /// any attribute whose own name is sensitive; the value attribute and text of an element whose
        /// name attribute is sensitive (<c>&lt;setting name="X.ApiKey" value="..."/&gt;</c>); and the
        /// text of an element whose own name is sensitive. Returns how many values were redacted.
        /// </summary>
        public static int RedactInPlace(XmlElement element)
        {
            if (element == null) return 0;

            var count = 0;
            var namedSecret = IsSensitiveName(element.GetAttribute("name"));

            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (IsStructural(attribute) || attribute.LocalName == "name") continue;
                if (IsSensitiveName(attribute.LocalName) || (namedSecret && attribute.LocalName == "value"))
                {
                    if (attribute.Value != Redacted && attribute.Value.Length > 0)
                    {
                        attribute.Value = Redacted;
                        count++;
                    }
                }
            }

            if (namedSecret || IsSensitiveName(element.LocalName))
            {
                foreach (var text in element.ChildNodes.OfType<XmlCharacterData>().Where(IsValueText).ToList())
                {
                    if (text.Value.Trim().Length == 0) continue;
                    text.Value = Redacted;
                    count++;
                }
            }

            foreach (var child in element.ChildNodes.OfType<XmlElement>().ToList())
            {
                count += RedactInPlace(child);
            }

            return count;
        }

        /// <summary>The value an attribute would show after redaction.</summary>
        public static string RedactedValue(XmlAttribute attribute)
        {
            if (attribute == null) return null;
            if (IsStructural(attribute) || attribute.LocalName == "name") return attribute.Value;

            var owner = attribute.OwnerElement;
            var namedSecret = owner != null && IsSensitiveName(owner.GetAttribute("name")) && attribute.LocalName == "value";
            return (IsSensitiveName(attribute.LocalName) || namedSecret) && attribute.Value.Length > 0 ? Redacted : attribute.Value;
        }

        /// <summary>The value a text node would show after redaction.</summary>
        public static string RedactedText(XmlNode text)
        {
            var owner = text?.ParentNode as XmlElement;
            if (owner == null) return text?.Value;
            var sensitive = IsSensitiveName(owner.GetAttribute("name")) || IsSensitiveName(owner.LocalName);
            return sensitive && text.Value.Trim().Length > 0 ? Redacted : text.Value;
        }

        private static bool IsValueText(XmlCharacterData node) =>
            node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA;

        private static bool IsStructural(XmlAttribute attribute) =>
            attribute.Prefix == "xmlns" || attribute.LocalName == "xmlns" || attribute.NamespaceURI == ConfigQuery.PatchNamespace;
    }
}
