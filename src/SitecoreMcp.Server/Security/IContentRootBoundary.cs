namespace SitecoreMcp.Server.Security
{
    /// <summary>
    /// The content roots an installation lets CorePilot work in. This is the seam the content-root
    /// configuration plugs into; until roots are configured, <see cref="UnrestrictedContentRoots"/>
    /// treats the whole tree as one root.
    /// </summary>
    public interface IContentRootBoundary
    {
        /// <summary>
        /// Returns the path of the content root that contains <paramref name="path"/> (the root itself
        /// counts as inside), or null when the path lies outside every content root.
        /// </summary>
        string RootOf(string database, string path);
    }

    /// <summary>No content roots configured: everything is inside one root spanning the tree.</summary>
    public sealed class UnrestrictedContentRoots : IContentRootBoundary
    {
        /// <summary>The shared instance.</summary>
        public static readonly UnrestrictedContentRoots Instance = new UnrestrictedContentRoots();

        private UnrestrictedContentRoots()
        {
        }

        /// <inheritdoc />
        public string RootOf(string database, string path) => "/";
    }
}
