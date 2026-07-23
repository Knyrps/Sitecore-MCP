using Newtonsoft.Json.Linq;

namespace SitecoreMcp.Server.Tools
{
    /// <summary>A resolved, clamped paging window: where a page starts and how many it may hold.</summary>
    public readonly struct PageRange
    {
        /// <summary>Creates a window at <paramref name="offset"/> holding up to <paramref name="limit"/> items.</summary>
        public PageRange(int offset, int limit)
        {
            Offset = offset;
            Limit = limit;
        }

        /// <summary>The number of items to skip before the page.</summary>
        public int Offset { get; }

        /// <summary>The maximum number of items on the page.</summary>
        public int Limit { get; }
    }

    /// <summary>
    /// Shared paging for list-style tools: clamps a requested offset/limit to sane bounds and builds
    /// the standard result envelope, so every list tool pages and reports the same way. Each tool
    /// keeps its own default and maximum limit, so the caps stay tunable per tool.
    /// </summary>
    public static class Paging
    {
        /// <summary>
        /// Clamps a requested offset and limit: offset to at least zero, limit to
        /// [1, <paramref name="maxLimit"/>], applying <paramref name="defaultLimit"/> when either is
        /// unset or non-positive.
        /// </summary>
        public static PageRange Resolve(int? offset, int? limit, int defaultLimit, int maxLimit)
        {
            var resolvedOffset = offset.GetValueOrDefault(0);
            if (resolvedOffset < 0) resolvedOffset = 0;

            var resolvedLimit = limit.GetValueOrDefault(defaultLimit);
            if (resolvedLimit < 1) resolvedLimit = defaultLimit;
            if (resolvedLimit > maxLimit) resolvedLimit = maxLimit;

            return new PageRange(resolvedOffset, resolvedLimit);
        }

        /// <summary>Clamps a value into the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);

        /// <summary>
        /// Builds the standard list envelope: the full <paramref name="total"/>, the window's offset,
        /// this page's count, whether more remain, and the items under <paramref name="itemsKey"/>.
        /// </summary>
        public static JObject Envelope(string itemsKey, JArray items, int total, PageRange range) => new JObject
        {
            ["total"] = total,
            ["offset"] = range.Offset,
            ["count"] = items.Count,
            ["hasMore"] = range.Offset + items.Count < total,
            [itemsKey] = items
        };
    }
}
