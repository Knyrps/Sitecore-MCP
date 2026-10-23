using System;
using System.Collections.Generic;
using SitecoreMcp.Server.Tools;

namespace SitecoreMcp.Server.Media
{
    /// <summary>
    /// Decides how a stored image is rendered for a client so it stays within <see cref="ImageLimits"/>:
    /// a longest side no larger than the configured (or a smaller requested) dimension, and encoded
    /// bytes no larger than the byte limit, shrinking further when the first rendering is too heavy.
    /// The resizing itself is delegated, so this holds no Sitecore or imaging dependency.
    /// </summary>
    public sealed class MediaImageEncoder
    {
        private const int MaxAttempts = 6;

        /// <summary>The image types MCP clients pass to models, keyed by every alias we accept.</summary>
        private static readonly Dictionary<string, string> SupportedTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = "image/png",
                ["image/jpeg"] = "image/jpeg",
                ["image/jpg"] = "image/jpeg",
                ["image/pjpeg"] = "image/jpeg",
                ["image/gif"] = "image/gif",
                ["image/webp"] = "image/webp"
            };

        private readonly ImageLimits _limits;

        /// <summary>Creates an encoder enforcing <paramref name="limits"/>.</summary>
        public MediaImageEncoder(ImageLimits limits)
        {
            _limits = limits ?? new ImageLimits();
        }

        /// <summary>The MIME types an image can be returned as, for messages.</summary>
        public static string SupportedTypeList => "image/png, image/jpeg, image/gif, image/webp";

        /// <summary>The normalised MIME type for <paramref name="mimeType"/>, or null when it cannot be returned as an image.</summary>
        public static string NormaliseMimeType(string mimeType) =>
            !string.IsNullOrWhiteSpace(mimeType) && SupportedTypes.TryGetValue(mimeType.Trim(), out var normalised)
                ? normalised
                : null;

        /// <summary>
        /// Renders <paramref name="source"/> within the limits. <paramref name="render"/> must return the
        /// image scaled to fit a square box of the given side, never upscaled, in its own format.
        /// Throws <see cref="McpToolException"/> for an unsupported type or an image that cannot be made
        /// small enough.
        /// </summary>
        public ImageRendition Encode(ImageSource source, int? requestedMaxDimension, Func<int, byte[]> render)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (render == null) throw new ArgumentNullException(nameof(render));

            var mimeType = NormaliseMimeType(source.MimeType);
            if (mimeType == null)
            {
                var stored = string.IsNullOrWhiteSpace(source.MimeType) ? "an unknown type" : $"'{source.MimeType}'";
                throw new McpToolException(
                    $"This media is {stored}, which cannot be returned as an image. Supported: {SupportedTypeList}.");
            }

            var bound = Math.Min(_limits.MaxDimension, Math.Max(ImageLimits.MinDimension, requestedMaxDimension ?? _limits.MaxDimension));
            if (source.HasSize)
            {
                bound = Math.Min(bound, Math.Max(source.Width.Value, source.Height.Value));
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var data = render(bound) ?? throw new McpToolException("The image could not be read.");
                if (data.Length <= _limits.MaxBytes)
                {
                    return Rendition(source, mimeType, data, bound);
                }

                // Bytes follow pixel count, so scale the side by the square root of the overshoot, with
                // a margin so the next attempt usually fits, and always make progress.
                var scale = Math.Sqrt((double)_limits.MaxBytes / data.Length) * 0.9;
                var next = Math.Min((int)Math.Floor(bound * scale), bound - 1);
                if (next < ImageLimits.MinDimension)
                {
                    break;
                }

                bound = next;
            }

            throw new McpToolException(
                $"The image cannot be reduced to within the {_limits.MaxBytes} byte limit at a usable size " +
                $"(at least {ImageLimits.MinDimension}px).");
        }

        private static ImageRendition Rendition(ImageSource source, string mimeType, byte[] data, int bound)
        {
            var rendition = new ImageRendition
            {
                Data = data,
                MimeType = mimeType,
                OriginalWidth = source.Width,
                OriginalHeight = source.Height
            };

            if (source.HasSize)
            {
                var width = source.Width.Value;
                var height = source.Height.Value;
                var scale = Math.Min(1.0, (double)bound / Math.Max(width, height));
                rendition.Width = Math.Max(1, (int)Math.Round(width * scale));
                rendition.Height = Math.Max(1, (int)Math.Round(height * scale));
                rendition.Resized = scale < 1.0;
            }

            return rendition;
        }
    }
}
