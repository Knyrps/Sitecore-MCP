using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Security.AccessControl;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>Arguments for <see cref="GrepTool"/>.</summary>
    public sealed class GrepArgs
    {
        /// <summary>The literal substring or regular expression to match against field values.</summary>
        [McpParam(Description = "Text to find in field values. A literal substring unless regex=true.", Required = true)]
        public string Pattern { get; set; }

        /// <summary>The subtree to scan, by path or ID. Required, to bound the scan.</summary>
        [McpParam(Description = "Path or ID of the subtree to scan. Required.", Required = true)]
        public string RootPath { get; set; }

        /// <summary>Whether to treat the pattern as a .NET regular expression.</summary>
        [McpParam(Description = "Treat the pattern as a regular expression. Default false.")]
        public bool? Regex { get; set; }

        /// <summary>Whether matching is case-sensitive.</summary>
        [McpParam(Description = "Case-sensitive matching. Default false.")]
        public bool? CaseSensitive { get; set; }

        /// <summary>Specific fields to scan; when omitted, all readable fields with a value are scanned.</summary>
        [McpParam(Description = "Specific field names to scan. Omit to scan all readable fields with a value.")]
        public string[] Fields { get; set; }

        /// <summary>Which database to scan; defaults to master.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }

        /// <summary>The maximum number of items to load and scan.</summary>
        [McpParam(Description = "Maximum items to scan (default 1000, max 5000).")]
        public int? MaxScan { get; set; }

        /// <summary>The maximum number of matches to return in one call.</summary>
        [McpParam(Description = "Maximum matches to return (default 50, max 200).")]
        public int? Limit { get; set; }

        /// <summary>The number of matches to skip, for paging.</summary>
        [McpParam(Description = "Number of matches to skip before returning results.")]
        public int? Offset { get; set; }
    }

    /// <summary>
    /// Scans raw field values under a subtree for a literal substring or regex, returning each match
    /// with its field and a snippet. This is the exact/regex search the index cannot do (e.g. finding
    /// every item whose fields reference a specific GUID), bounded by a required root and a scan cap.
    /// </summary>
    public sealed class GrepTool : McpTool<GrepArgs>
    {
        private const int DefaultMaxScan = 1000;
        private const int MaxMaxScan = 5000;
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;
        private const int MaxCollected = 1000;
        private const int SnippetPad = 40;

        /// <inheritdoc />
        public override string Name => "sitecore_grep";

        /// <inheritdoc />
        public override string Description =>
            "Find a literal string or regex in the raw value of ANY field of items under a path — " +
            "including standard and security fields the search index does not cover. Use this for " +
            "'items where a field contains X', exact/substring/regex matches, or locating every item " +
            "that references a specific ID, URL, or string. Requires a root path and is scan-capped.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GrepArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);

            var root = db.GetItem(args.RootPath);
            if (root == null)
            {
                throw new McpToolException($"Root '{args.RootPath}' was not found.");
            }

            var index = ContentSearchManager.GetIndex($"sitecore_{db.Name}_index");
            if (index == null)
            {
                throw new McpToolException($"Search index for database '{db.Name}' does not exist.");
            }

            var range = Paging.Resolve(args.Offset, args.Limit, DefaultLimit, MaxLimit);
            var maxScan = Paging.Clamp(args.MaxScan.GetValueOrDefault(DefaultMaxScan), 1, MaxMaxScan);
            var matcher = BuildMatcher(args);
            var wanted = args.Fields != null && args.Fields.Length > 0
                ? new HashSet<string>(args.Fields, StringComparer.OrdinalIgnoreCase)
                : null;

            var candidateIds = ScopedItemIds(index, root.ID, maxScan, out var totalUnderRoot);
            // totalUnderRoot counts documents (item x language x version); we truncated only if that
            // exceeded the scan cap, not merely because languages inflate the doc count.
            var scanTruncated = totalUnderRoot > maxScan;

            var matches = new List<JObject>();
            var scanned = 0;
            var collectionTruncated = false;

            foreach (var id in candidateIds)
            {
                var item = db.GetItem(id);
                if (item == null)
                {
                    continue;
                }

                scanned++;
                item.Fields.ReadAll();

                foreach (Field field in item.Fields)
                {
                    if (wanted != null && !wanted.Contains(field.Name) && !wanted.Contains(field.Key))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(field.Value) ||
                        !AuthorizationManager.IsAllowed(field, AccessRight.FieldRead, context.User))
                    {
                        continue;
                    }

                    var hit = matcher(field.Value);
                    if (!hit.Success)
                    {
                        continue;
                    }

                    matches.Add(new JObject
                    {
                        ["id"] = item.ID.ToString(),
                        ["path"] = item.Paths.FullPath,
                        ["field"] = field.Name,
                        ["snippet"] = Snippet(field.Value, hit.Index, hit.Length)
                    });

                    if (matches.Count >= MaxCollected)
                    {
                        collectionTruncated = true;
                        break;
                    }
                }

                if (collectionTruncated)
                {
                    break;
                }
            }

            var page = matches.Skip(range.Offset).Take(range.Limit).ToList();

            // Bespoke envelope: grep reports scan bookkeeping (scanned/scanTruncated) and its total
            // under 'matches', so it does not use the shared Paging.Envelope shape.
            return McpToolResult.Structured(new JObject
            {
                ["root"] = root.Paths.FullPath,
                ["scanned"] = scanned,
                ["scanTruncated"] = scanTruncated,
                ["matches"] = matches.Count,
                ["matchesTruncated"] = collectionTruncated,
                ["offset"] = range.Offset,
                ["count"] = page.Count,
                ["hasMore"] = range.Offset + page.Count < matches.Count,
                ["hits"] = new JArray(page.Cast<object>().ToArray())
            });
        }

        private struct GrepHit
        {
            public bool Success;
            public int Index;
            public int Length;
        }

        private static Func<string, GrepHit> BuildMatcher(GrepArgs args)
        {
            var caseSensitive = args.CaseSensitive.GetValueOrDefault(false);

            if (args.Regex.GetValueOrDefault(false))
            {
                var options = RegexOptions.CultureInvariant |
                              (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                Regex regex;
                try
                {
                    regex = new Regex(args.Pattern, options, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException ex)
                {
                    throw new McpToolException($"Invalid regular expression: {ex.Message}");
                }

                return value =>
                {
                    try
                    {
                        var m = regex.Match(value);
                        return new GrepHit { Success = m.Success, Index = m.Index, Length = m.Length };
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return default;
                    }
                };
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var pattern = args.Pattern;
            return value =>
            {
                var i = value.IndexOf(pattern, comparison);
                return i >= 0
                    ? new GrepHit { Success = true, Index = i, Length = pattern.Length }
                    : default;
            };
        }

        private static List<ID> ScopedItemIds(ISearchIndex index, ID rootId, int maxScan, out long totalUnderRoot)
        {
            using (var searchContext = index.CreateSearchContext())
            {
                var results = searchContext.GetQueryable<SearchResultItem>()
                    .Where(i => i.Paths.Contains(rootId))
                    .Take(maxScan)
                    .GetResults();

                totalUnderRoot = results.TotalSearchResults;
                return results.Hits
                    .Select(h => h.Document.ItemId)
                    .Where(id => !ID.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
            }
        }

        private static string Snippet(string value, int index, int length)
        {
            var start = Math.Max(0, index - SnippetPad);
            var end = Math.Min(value.Length, index + length + SnippetPad);
            var slice = value.Substring(start, end - start);

            if (start > 0) slice = "..." + slice;
            if (end < value.Length) slice = slice + "...";
            return slice;
        }
    }
}
