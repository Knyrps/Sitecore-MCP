using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Sitecore.Resources.Media;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>Arguments for <see cref="UploadMediaTool"/>.</summary>
    public sealed class UploadMediaArgs
    {
        /// <summary>The media library folder to upload into, by path or ID.</summary>
        [McpParam(Description = "Media library folder to upload into (path or ID). Defaults to /sitecore/media library.")]
        public string Parent { get; set; }

        /// <summary>The media item name.</summary>
        [McpParam(Description = "Name for the media item.", Required = true)]
        public string Name { get; set; }

        /// <summary>The original file name with extension, which determines the media type.</summary>
        [McpParam(Description = "File name WITH extension (e.g. logo.png) - the extension decides the media type (Image, File, Pdf, ...).", Required = true)]
        public string FileName { get; set; }

        /// <summary>The file content, base64-encoded.</summary>
        [McpParam(Description = "The file content, base64-encoded. Subject to the endpoint's request-size limit (~1 MB body by default, so roughly 700 KB of file).", Required = true)]
        public string DataBase64 { get; set; }

        /// <summary>Alternate text for image media.</summary>
        [McpParam(Description = "Alternate text (images). Optional.")]
        public string Alt { get; set; }

        /// <summary>Whether an existing media item of the same name is overwritten.</summary>
        [McpParam(Description = "Overwrite an existing media item with the same name. Default false (fails instead).")]
        public bool? Overwrite { get; set; }

        /// <summary>The database to upload into; defaults to master.</summary>
        [McpParam(Description = "Database name. Defaults to 'master'.")]
        public string Database { get; set; }
    }

    /// <summary>
    /// Uploads a file into the media library from a base64 payload, creating a proper media item with
    /// an attached blob - the missing half of referencing media from content.
    /// </summary>
    public sealed class UploadMediaTool : McpTool<UploadMediaArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_upload_media";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Upload a file into the Sitecore media library from base64 content, creating a media item " +
            "with its blob attached (the extension in fileName decides the media type). Fails on an " +
            "existing sibling unless overwrite=true. The endpoint's request-size limit applies, so " +
            "this suits icons and documents up to a few hundred KB, not large videos.";

        /// <inheritdoc />
        protected override McpToolResult Execute(UploadMediaArgs args, McpCallContext context)
        {
            var db = context.ResolveDatabase(args.Database);
            ItemHelper.ValidateName(args.Name, "Invalid media item name: ");

            if (string.IsNullOrWhiteSpace(Path.GetExtension(args.FileName)))
            {
                throw new McpToolException("fileName must include an extension (e.g. logo.png) - it decides the media type.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(args.DataBase64);
            }
            catch (FormatException)
            {
                throw new McpToolException("dataBase64 is not valid base64.");
            }

            if (bytes.Length == 0)
            {
                throw new McpToolException("The decoded file content is empty.");
            }

            var parentPath = string.IsNullOrWhiteSpace(args.Parent) ? "/sitecore/media library" : args.Parent.Trim();
            var parent = db.GetItem(parentPath);
            if (parent == null)
            {
                throw new McpToolException($"Media folder '{parentPath}' was not found in '{db.Name}'.");
            }

            var destination = $"{parent.Paths.FullPath}/{args.Name}";
            var overwrite = args.Overwrite.GetValueOrDefault(false);
            if (!overwrite && db.GetItem(destination) != null)
            {
                return McpToolResult.Failure($"'{destination}' already exists. Pass overwrite=true to replace it.");
            }

            var options = new MediaCreatorOptions
            {
                Database = db,
                Destination = destination,
                FileBased = false,
                IncludeExtensionInItemName = false,
                Versioned = false,
                OverwriteExisting = overwrite,
                AlternateText = args.Alt ?? string.Empty,
                Language = context.ResolveLanguage(null)
            };

            Sitecore.Data.Items.Item created;
            using (var stream = new MemoryStream(bytes))
            {
                created = MediaManager.Creator.CreateFromStream(stream, args.FileName, options);
            }

            if (created == null)
            {
                throw new McpToolException("Sitecore did not create the media item.");
            }

            // MediaCreatorOptions.AlternateText does not reliably reach the Alt field on an
            // unversioned item, so write it explicitly when the template has one.
            if (!string.IsNullOrEmpty(args.Alt) && created.Fields["Alt"] != null &&
                string.IsNullOrEmpty(created["Alt"]))
            {
                ItemEditor.Edit(created, editable => editable["Alt"] = args.Alt);
            }

            return McpToolResult.Structured(new JObject
            {
                ["id"] = created.ID.ToString(),
                ["path"] = created.Paths.FullPath,
                ["template"] = created.TemplateName,
                ["sizeBytes"] = bytes.Length,
                ["extension"] = created["Extension"],
                ["mediaUrl"] = MediaManager.GetMediaUrl(created)
            });
        }
    }
}
