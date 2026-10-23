using System;
using System.Collections.Generic;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>
    /// The words of a <c>nameContains</c> search. Each word must be in the item name: the search adds one
    /// <c>Name.Contains(word)</c> per word. One <c>Contains</c> with the whole text does not work for several words:
    /// the Solr provider tokenizes the value and matches items that have any one of the words, so "Bulk A" listed every
    /// item with an "a" in its name before "Bulk A" itself.
    /// </summary>
    public static class NameTerms
    {
        private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

        /// <summary>The distinct words of <paramref name="value"/> (case-insensitive), in order; empty for blank input.</summary>
        public static IReadOnlyList<string> Split(string value)
        {
            var terms = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return terms;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in value.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(word))
                {
                    terms.Add(word);
                }
            }

            return terms;
        }
    }
}
