using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using Sitecore.Data.Items;
using Sitecore.Resources.Media;
using SitecoreMcp.Server.Media;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="GetMediaImageTool"/>.</summary>
    public sealed class GetMediaImageArgs : ItemQueryArgs
    {
        /// <summary>A smaller longest side than the configured limit, e.g. for a thumbnail.</summary>
        [McpParam(Description = "Longest side in pixels to resize to. Optional; capped at the server's configured limit (the default).")]
        public int? MaxDimension { get; set; }
    }

    /// <summary>
    /// Returns a media-library image as MCP image content, resized to fit the configured limits, so a
    /// client can show it to a model. Reads as the calling user: an image they cannot read is not found.
    /// </summary>
    public sealed class GetMediaImageTool : McpTool<GetMediaImageArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_media_image";

        /// <inheritdoc />
        public override string Description =>
            "Look at a media library image: returns it as image content (PNG, JPEG, GIF or WebP) the " +
            "model can see, scaled down to fit the server's size limits, plus its path, MIME type, " +
            "dimensions and alt text. Use it to describe an image, write alt text, or check that an " +
            "image suits the content referencing it. Pass maxDimension for a smaller preview. SVG and " +
            "non-image media (PDF, documents) cannot be returned as images.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetMediaImageArgs args, McpCallContext context)
        {
            var item = ItemResolver.Resolve(context, args.Path, args.Database, args.Language);

            // The blob is the image: if it is hidden, or the item has none, there is nothing to return.
            var blob = item.Fields["Blob"];
            if (context.IsHidden(blob) || !MediaManager.HasMediaContent(item))
            {
                throw new McpToolException($"'{item.Paths.FullPath}' is not a media item with an attached file.");
            }

            MediaItem mediaItem = item;
            var source = new ImageSource(
                mediaItem.MimeType,
                ReadDimension(context, item, "Width"),
                ReadDimension(context, item, "Height"));

            var limits = new ImageLimits(context.Settings.MaxImageDimension, context.Settings.MaxImageBytes);
            var media = MediaManager.GetMedia(mediaItem);
            var image = new MediaImageEncoder(limits).Encode(source, args.MaxDimension, bound => Render(media, bound));

            var metadata = new JObject
            {
                ["id"] = item.ID.ToString(),
                ["path"] = item.Paths.FullPath,
                ["mimeType"] = image.MimeType,
                ["width"] = image.Width,
                ["height"] = image.Height,
                ["originalWidth"] = image.OriginalWidth,
                ["originalHeight"] = image.OriginalHeight,
                ["resized"] = image.Resized,
                ["sizeBytes"] = image.Data.Length,
                ["limits"] = new JObject
                {
                    ["maxDimension"] = limits.MaxDimension,
                    ["maxBytes"] = limits.MaxBytes
                }
            };

            var alt = context.FindVisibleField(item, "Alt");
            if (alt != null)
            {
                metadata["alt"] = alt.Value;
            }

            return McpToolResult.Image(image.Data, image.MimeType, metadata);
        }

        /// <summary>Renders the image within a square box of side <paramref name="bound"/> through Sitecore's media pipeline, which never upscales.</summary>
        private static byte[] Render(Sitecore.Resources.Media.Media media, int bound)
        {
            var options = new MediaOptions { MaxWidth = bound, MaxHeight = bound };
            using (var stream = media.GetStream(options))
            {
                if (stream?.Stream == null)
                {
                    throw new McpToolException("The media item has no readable file.");
                }

                using (var buffer = new MemoryStream())
                {
                    stream.Stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static int? ReadDimension(McpCallContext context, Item item, string fieldName)
        {
            var field = context.FindVisibleField(item, fieldName);
            return field != null && int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : (int?)null;
        }
    }
}
