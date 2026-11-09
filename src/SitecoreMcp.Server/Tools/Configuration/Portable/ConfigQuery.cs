using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace SitecoreMcp.Server.Tools.Configuration
{
    /// <summary>What to read from the merged configuration, and how much of it.</summary>
    public sealed class ConfigQueryOptions
    {
        /// <summary>The default number of descendant levels returned under a matched element.</summary>
        public const int DefaultDepth = 3;

        /// <summary>The deepest subtree that can be asked for.</summary>
        public const int MaxDepth = 20;

        /// <summary>The XPath selecting the nodes, e.g. <c>/sitecore/settings/setting[@name='Mcp.Enabled']</c>.</summary>
        public string XPath { get; set; }

        /// <summary>Return each matched element with its attributes only, no children.</summary>
        public bool NodeOnly { get; set; }

        /// <summary>How many levels of descendants to keep under a matched element (0 is the same as <see cref="NodeOnly"/>).</summary>
        public int Depth { get; set; } = DefaultDepth;

        /// <summary>Keep the <c>patch:source</c> attributes that say which file set a node.</summary>
        public bool IncludePatchSource { get; set; } = true;

        /// <summary>The most matched nodes returned.</summary>
        public int MaxNodes { get; set; } = 20;

        /// <summary>The most characters of XML returned across all nodes.</summary>
        public int MaxChars { get; set; } = 60000;
    }

    /// <summary>One matched node as returned to the caller.</summary>
    public sealed class ConfigNodeView
    {
        /// <summary>"element", "attribute" or "text".</summary>
        public string Kind { get; set; }

        /// <summary>A readable XPath locating the node, siblings told apart by name/id/type or position.</summary>
        public string Path { get; set; }

        /// <summary>The element (pruned and redacted) as XML; null for an attribute or text node.</summary>
        public string Xml { get; set; }

        /// <summary>The value of a matched attribute or text node (redacted where it looks secret).</summary>
        public string Value { get; set; }

        /// <summary>The element's own attributes (redacted), without namespace declarations or patch attributes.</summary>
        public IReadOnlyDictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

        /// <summary>The files that set this element (its patch:source), empty when stripped or unpatched.</summary>
        public IReadOnlyList<string> PatchSources { get; set; } = new string[0];

        /// <summary>How many child elements the element has in the merged configuration.</summary>
        public int ChildElements { get; set; }

        /// <summary>Whether some descendants were left out (node-only or the depth limit).</summary>
        public bool ChildrenOmitted { get; set; }
    }

    /// <summary>The outcome of a configuration query.</summary>
    public sealed class ConfigQueryResult
    {
        /// <summary>Whether the XPath was valid and matched something.</summary>
        public bool Succeeded { get; set; }

        /// <summary>Why the query failed, written for the model.</summary>
        public string Error { get; set; }

        /// <summary>How many nodes the XPath matched in total.</summary>
        public int Matched { get; set; }

        /// <summary>Whether matches or XML were cut by the node or character cap.</summary>
        public bool Truncated { get; set; }

        /// <summary>How many secret-looking values were replaced by <see cref="ConfigRedaction.Redacted"/>.</summary>
        public int RedactedValues { get; set; }

        /// <summary>The returned nodes, in document order.</summary>
        public IReadOnlyList<ConfigNodeView> Nodes { get; set; } = new ConfigNodeView[0];
    }

    /// <summary>
    /// Reads nodes out of a merged Sitecore configuration document (as <c>Factory.GetConfiguration()</c>
    /// or showconfig.aspx produce it) by XPath. Works on copies: the live document is never changed.
    /// Sitecore-free so the rules (paths, pruning, patch sources, redaction, caps) are unit-tested.
    /// </summary>
    public static class ConfigQuery
    {
        /// <summary>The namespace of patch:source and the other patch attributes.</summary>
        public const string PatchNamespace = "http://www.sitecore.net/xmlconfig/";

        private static readonly string[] IdentifyingAttributes = { "name", "id", "type", "path", "hint" };

        /// <summary>Runs the query against the merged configuration.</summary>
        public static ConfigQueryResult Run(XmlNode configuration, ConfigQueryOptions options)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            options = options ?? new ConfigQueryOptions();
            var xpath = string.IsNullOrWhiteSpace(options.XPath) ? "/sitecore" : options.XPath.Trim();

            var document = configuration as XmlDocument ?? configuration.OwnerDocument;
            var namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace("patch", PatchNamespace);
            namespaces.AddNamespace("set", PatchNamespace + "set/");
            namespaces.AddNamespace("role", PatchNamespace + "role/");

            XmlNodeList matches;
            try
            {
                matches = configuration.SelectNodes(xpath, namespaces);
            }
            catch (XPathException ex)
            {
                return Failed($"'{xpath}' is not a valid XPath: {ex.Message}");
            }

            if (matches == null || matches.Count == 0)
            {
                return Failed($"The XPath '{xpath}' matched nothing in the merged configuration. Check the element names " +
                              "(they are case-sensitive) and try a broader path such as //settings or //pipelines.");
            }

            var depth = options.NodeOnly ? 0 : Math.Max(0, Math.Min(options.Depth, ConfigQueryOptions.MaxDepth));
            var maxNodes = Math.Max(1, options.MaxNodes);
            var maxChars = Math.Max(1, options.MaxChars);

            var result = new ConfigQueryResult { Succeeded = true, Matched = matches.Count };
            var nodes = new List<ConfigNodeView>();
            var chars = 0;

            foreach (XmlNode match in matches)
            {
                if (nodes.Count >= maxNodes)
                {
                    result.Truncated = true;
                    break;
                }

                var view = Project(match, depth, options.IncludePatchSource, out var redacted);
                var size = (view.Xml ?? view.Value ?? string.Empty).Length;
                if (chars + size > maxChars)
                {
                    result.Truncated = true;
                    if (nodes.Count > 0) break;

                    // Even one node is too big: return it cut short rather than nothing.
                    if (view.Xml != null) view.Xml = view.Xml.Substring(0, maxChars) + "... [truncated]";
                    else if (view.Value != null) view.Value = view.Value.Substring(0, maxChars) + "... [truncated]";
                }

                chars += size;
                result.RedactedValues += redacted;
                nodes.Add(view);
            }

            result.Nodes = nodes;
            return result;
        }

        private static ConfigQueryResult Failed(string error) => new ConfigQueryResult { Succeeded = false, Error = error };

        private static ConfigNodeView Project(XmlNode node, int depth, bool includePatchSource, out int redacted)
        {
            redacted = 0;
            switch (node)
            {
                case XmlAttribute attribute:
                {
                    var value = ConfigRedaction.RedactedValue(attribute);
                    if (value != attribute.Value) redacted = 1;
                    return new ConfigNodeView
                    {
                        Kind = "attribute",
                        Path = PathOf(attribute.OwnerElement) + "/@" + attribute.Name,
                        Value = value
                    };
                }
                case XmlElement element:
                    return ProjectElement(element, depth, includePatchSource, out redacted);
                default:
                {
                    var value = ConfigRedaction.RedactedText(node);
                    if (value != node.Value) redacted = 1;
                    return new ConfigNodeView
                    {
                        Kind = "text",
                        Path = PathOf(node.ParentNode as XmlElement) + "/text()",
                        Value = value
                    };
                }
            }
        }

        private static ConfigNodeView ProjectElement(XmlElement element, int depth, bool includePatchSource, out int redacted)
        {
            // Work on a copy in a document of its own, so the live configuration is never touched.
            var scratch = new XmlDocument();
            var copy = (XmlElement)scratch.ImportNode(element, true);
            scratch.AppendChild(copy);

            var omitted = Prune(copy, depth);
            if (!includePatchSource) StripPatchAttributes(copy);
            redacted = ConfigRedaction.RedactInPlace(copy);

            var attributes = new Dictionary<string, string>();
            foreach (XmlAttribute attribute in copy.Attributes)
            {
                if (IsNamespaceDeclaration(attribute) || attribute.NamespaceURI == PatchNamespace) continue;
                attributes[attribute.Name] = attribute.Value;
            }

            var sources = includePatchSource
                ? SplitSources(element.GetAttribute("source", PatchNamespace))
                : new string[0];

            return new ConfigNodeView
            {
                Kind = "element",
                Path = PathOf(element),
                Xml = copy.OuterXml,
                Attributes = attributes,
                PatchSources = sources,
                ChildElements = element.ChildNodes.OfType<XmlElement>().Count(),
                ChildrenOmitted = omitted
            };
        }

        /// <summary>Removes descendants below the depth limit, leaving a comment saying how many were left out.</summary>
        private static bool Prune(XmlElement element, int depth)
        {
            var children = element.ChildNodes.Cast<XmlNode>().ToList();
            if (depth <= 0)
            {
                var elements = children.OfType<XmlElement>().Count();
                if (elements == 0) return false;

                foreach (var child in children) element.RemoveChild(child);
                element.AppendChild(element.OwnerDocument.CreateComment(
                    $" {elements} child element{(elements == 1 ? string.Empty : "s")} omitted (depth limit) "));
                return true;
            }

            var omitted = false;
            foreach (var child in children.OfType<XmlElement>())
            {
                omitted |= Prune(child, depth - 1);
            }

            return omitted;
        }

        private static void StripPatchAttributes(XmlElement element)
        {
            var remove = element.Attributes.Cast<XmlAttribute>()
                .Where(a => a.NamespaceURI == PatchNamespace || (IsNamespaceDeclaration(a) && a.Value == PatchNamespace))
                .ToList();
            foreach (var attribute in remove) element.Attributes.Remove(attribute);

            foreach (var child in element.ChildNodes.OfType<XmlElement>()) StripPatchAttributes(child);
        }

        private static bool IsNamespaceDeclaration(XmlAttribute attribute) =>
            attribute.Prefix == "xmlns" || attribute.Name == "xmlns";

        private static IReadOnlyList<string> SplitSources(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? new string[0]
                : value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        /// <summary>
        /// A readable path for an element: each step is the element name, told apart from same-named
        /// siblings by the first identifying attribute (name, id, type, path, hint) that is unique among
        /// them, else by its position.
        /// </summary>
        public static string PathOf(XmlElement element)
        {
            if (element == null) return string.Empty;

            var steps = new Stack<string>();
            for (var current = element; current != null; current = current.ParentNode as XmlElement)
            {
                steps.Push(Step(current));
            }

            var builder = new StringBuilder();
            foreach (var step in steps) builder.Append('/').Append(step);
            return builder.ToString();
        }

        private static string Step(XmlElement element)
        {
            var parent = element.ParentNode as XmlElement;
            if (parent == null) return element.Name;

            var siblings = parent.ChildNodes.OfType<XmlElement>().Where(e => e.Name == element.Name).ToList();
            if (siblings.Count == 1) return element.Name;

            foreach (var attribute in IdentifyingAttributes)
            {
                var value = element.GetAttribute(attribute);
                if (value.Length == 0 || value.IndexOf('\'') >= 0) continue;
                if (siblings.Count(s => s.GetAttribute(attribute) == value) == 1)
                {
                    return $"{element.Name}[@{attribute}='{value}']";
                }
            }

            return $"{element.Name}[{siblings.IndexOf(element) + 1}]";
        }
    }
}
