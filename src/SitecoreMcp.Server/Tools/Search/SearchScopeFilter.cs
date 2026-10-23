using System;
using System.Linq;
using System.Linq.Expressions;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Data;

namespace SitecoreMcp.Server.Tools.Search
{
    /// <summary>
    /// Applies the call's search roots to an index query, combined with the root path the caller asked
    /// for. Every index-backed tool (search, grep, facet) goes through here so search only ever looks
    /// under the search roots.
    /// </summary>
    internal static class SearchScopeFilter
    {
        /// <summary>
        /// Restricts <paramref name="query"/> to the requested root (path or ID; null for none) within the
        /// search roots. Returns null when nothing can match - the requested root lies outside every
        /// search root, or no search root exists or is readable - so the caller returns an empty result.
        /// Throws when the requested root does not exist, as before.
        /// </summary>
        public static IQueryable<SearchResultItem> Restrict(
            IQueryable<SearchResultItem> query, Database db, string requestedRoot, McpCallContext context)
        {
            string requestedPath = null;
            if (!string.IsNullOrEmpty(requestedRoot))
            {
                var root = db.GetItem(requestedRoot);
                if (root == null)
                {
                    throw new McpToolException($"Root '{requestedRoot}' was not found.");
                }

                requestedPath = root.Paths.FullPath;
            }

            var starts = context.Scope.SearchStartPaths(requestedPath);
            if (starts.Count == 1 && starts[0] == null)
            {
                // No search roots and no requested root: the whole index, as before.
                return query;
            }

            var parameter = Expression.Parameter(typeof(SearchResultItem), "i");
            Expression body = null;
            foreach (var path in starts)
            {
                var start = db.GetItem(path);
                if (start == null) continue;

                var id = start.ID;
                Expression<Func<SearchResultItem, bool>> underStart = i => i.Paths.Contains(id);
                var clause = new ParameterSwap(underStart.Parameters[0], parameter).Visit(underStart.Body);
                body = body == null ? clause : Expression.OrElse(body, clause);
            }

            return body == null
                ? null
                : query.Where(Expression.Lambda<Func<SearchResultItem, bool>>(body, parameter));
        }

        private sealed class ParameterSwap : ExpressionVisitor
        {
            private readonly ParameterExpression _from;
            private readonly ParameterExpression _to;

            public ParameterSwap(ParameterExpression from, ParameterExpression to)
            {
                _from = from;
                _to = to;
            }

            protected override Expression VisitParameter(ParameterExpression node) =>
                node == _from ? _to : base.VisitParameter(node);
        }
    }
}
