using System;

namespace SitecoreMcp.Server.Security
{
    /// <summary>The kind of structural change being pre-checked.</summary>
    public enum RelocationKind
    {
        /// <summary>The item leaves its parent for a new one.</summary>
        Move,

        /// <summary>A copy of the item is created under a new parent; the source stays.</summary>
        Copy,

        /// <summary>The item keeps its parent but changes name (and so path).</summary>
        Rename
    }

    /// <summary>The facts a relocation pre-check needs, gathered from Sitecore by the calling tool.</summary>
    public sealed class RelocationRequest
    {
        /// <summary>Move, copy or rename.</summary>
        public RelocationKind Kind { get; set; }

        /// <summary>The database the items live in.</summary>
        public string Database { get; set; }

        /// <summary>The full path of the item being moved, copied or renamed.</summary>
        public string SourcePath { get; set; }

        /// <summary>The full path of the destination parent (move and copy only).</summary>
        public string DestinationPath { get; set; }

        /// <summary>Whether the editor may delete the source (required to move it away).</summary>
        public bool CanDeleteSource { get; set; }

        /// <summary>Whether the editor may rename the source.</summary>
        public bool CanRenameSource { get; set; }

        /// <summary>Whether the editor may create items under the destination.</summary>
        public bool CanCreateInDestination { get; set; }
    }

    /// <summary>The outcome of a relocation pre-check.</summary>
    public sealed class RelocationCheck
    {
        private RelocationCheck(string reason)
        {
            Reason = reason;
        }

        /// <summary>Whether the change may go ahead.</summary>
        public bool Allowed => Reason == null;

        /// <summary>Why the change is refused, written for the model; null when allowed.</summary>
        public string Reason { get; }

        internal static readonly RelocationCheck Pass = new RelocationCheck(null);

        internal static RelocationCheck Refuse(string reason) => new RelocationCheck(reason);
    }

    /// <summary>
    /// Pre-checks a move, copy or rename before anything changes: the item and its destination must
    /// sit inside the same content root (a content root itself is never moved or renamed, since that
    /// would shift the boundary; copying one is fine), and the editor must hold the rights the change needs - delete on the
    /// source and create on the destination for a move, create on the destination for a copy, rename
    /// on the item for a rename. Sitecore-free: the tool supplies the paths and access answers.
    /// </summary>
    public static class RelocationPolicy
    {
        /// <summary>Checks the request against the content roots (null means none configured).</summary>
        public static RelocationCheck Check(RelocationRequest request, IContentRootBoundary roots)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            roots = roots ?? UnrestrictedContentRoots.Instance;

            var sourceRoot = roots.RootOf(request.Database, request.SourcePath);
            if (sourceRoot == null)
            {
                return RelocationCheck.Refuse($"'{request.SourcePath}' is outside the content roots CorePilot may change.");
            }

            if (request.Kind != RelocationKind.Copy &&
                string.Equals(sourceRoot, request.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return RelocationCheck.Refuse($"'{request.SourcePath}' is a content root; content roots cannot be moved or renamed.");
            }

            if (request.Kind == RelocationKind.Rename)
            {
                return request.CanRenameSource
                    ? RelocationCheck.Pass
                    : RelocationCheck.Refuse($"Not permitted to rename '{request.SourcePath}'.");
            }

            var destinationRoot = roots.RootOf(request.Database, request.DestinationPath);
            if (destinationRoot == null)
            {
                return RelocationCheck.Refuse($"'{request.DestinationPath}' is outside the content roots CorePilot may change.");
            }

            if (!string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                return RelocationCheck.Refuse(
                    $"'{request.SourcePath}' and '{request.DestinationPath}' are in different content roots " +
                    $"('{sourceRoot}' and '{destinationRoot}'); items cannot be moved or copied across content roots.");
            }

            if (request.Kind == RelocationKind.Move && !request.CanDeleteSource)
            {
                return RelocationCheck.Refuse($"Not permitted to move '{request.SourcePath}': moving needs the right to delete it from its current parent.");
            }

            if (!request.CanCreateInDestination)
            {
                return RelocationCheck.Refuse($"Not permitted to create items under '{request.DestinationPath}'.");
            }

            return RelocationCheck.Pass;
        }
    }
}
