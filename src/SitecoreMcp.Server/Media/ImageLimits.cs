namespace SitecoreMcp.Server.Media
{
    /// <summary>
    /// The ceiling on an image returned to a client: its longest side in pixels and its encoded size in
    /// bytes. Images are resized to fit, never sent larger, so a model is never handed a huge payload.
    /// </summary>
    public sealed class ImageLimits
    {
        /// <summary>The longest side used when none (or an invalid one) is configured.</summary>
        public const int DefaultMaxDimension = 1024;

        /// <summary>The byte ceiling used when none (or an invalid one) is configured: 1 MB before base64.</summary>
        public const long DefaultMaxBytes = 1024 * 1024;

        /// <summary>The smallest longest side an image is ever rendered at; below it an image is useless to a model.</summary>
        public const int MinDimension = 32;

        /// <summary>Creates limits, replacing a non-positive value with its default.</summary>
        public ImageLimits(int maxDimension = DefaultMaxDimension, long maxBytes = DefaultMaxBytes)
        {
            MaxDimension = maxDimension > 0 ? System.Math.Max(maxDimension, MinDimension) : DefaultMaxDimension;
            MaxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
        }

        /// <summary>The longest side, in pixels, of a returned image.</summary>
        public int MaxDimension { get; }

        /// <summary>The largest encoded size, in bytes (before base64), of a returned image.</summary>
        public long MaxBytes { get; }
    }
}
