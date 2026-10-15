namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// Adapts a call's <see cref="ContentScope"/> content roots to <see cref="IContentRootBoundary"/>,
    /// so move, copy, rename and delete refuse to cross the same roots every other write respects.
    /// Content roots apply to every database alike, so the database is not consulted.
    /// </summary>
    public sealed class ContentScopeBoundary : IContentRootBoundary
    {
        private readonly ContentScope _scope;

        /// <summary>Wraps the scope; null means unrestricted.</summary>
        public ContentScopeBoundary(ContentScope scope)
        {
            _scope = scope ?? ContentScope.Unrestricted;
        }

        /// <inheritdoc />
        public string RootOf(string database, string path) => _scope.ContentRootOf(path);
    }
}
