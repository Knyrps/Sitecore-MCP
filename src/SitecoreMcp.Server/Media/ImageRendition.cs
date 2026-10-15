namespace SitecoreMcp.Server.Media
{
    /// <summary>An image ready to return to a client: bytes within the limits, plus what was done to them.</summary>
    public sealed class ImageRendition
    {
        /// <summary>The encoded image bytes, never more than the configured byte limit.</summary>
        public byte[] Data { get; set; }

        /// <summary>The normalised MIME type of <see cref="Data"/>.</summary>
        public string MimeType { get; set; }

        /// <summary>The returned width in pixels, or null when the stored size is unknown.</summary>
        public int? Width { get; set; }

        /// <summary>The returned height in pixels, or null when the stored size is unknown.</summary>
        public int? Height { get; set; }

        /// <summary>The stored width in pixels, or null when unknown.</summary>
        public int? OriginalWidth { get; set; }

        /// <summary>The stored height in pixels, or null when unknown.</summary>
        public int? OriginalHeight { get; set; }

        /// <summary>Whether the image was scaled down from its stored size; null when the stored size is unknown.</summary>
        public bool? Resized { get; set; }
    }
}
