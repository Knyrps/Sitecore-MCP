namespace SitecoreMcp.Server.Media
{
    /// <summary>What is known about a stored image before it is rendered: its MIME type and, when recorded, its size.</summary>
    public sealed class ImageSource
    {
        /// <summary>Describes a stored image; pass null dimensions when the size is not recorded.</summary>
        public ImageSource(string mimeType, int? width, int? height)
        {
            MimeType = mimeType;
            Width = width > 0 ? width : null;
            Height = height > 0 ? height : null;
        }

        /// <summary>The stored MIME type, e.g. "image/jpeg".</summary>
        public string MimeType { get; }

        /// <summary>The stored width in pixels, or null when unknown.</summary>
        public int? Width { get; }

        /// <summary>The stored height in pixels, or null when unknown.</summary>
        public int? Height { get; }

        /// <summary>Whether both dimensions are known.</summary>
        public bool HasSize => Width.HasValue && Height.HasValue;
    }
}
