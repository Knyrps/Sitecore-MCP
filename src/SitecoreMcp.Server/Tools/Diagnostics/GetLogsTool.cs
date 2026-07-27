using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Sitecore.Configuration;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Diagnostics
{
    /// <summary>Arguments for <see cref="GetLogsTool"/>.</summary>
    public sealed class GetLogsArgs
    {
        /// <summary>The log file to read, by name or prefix; omit to list the available files.</summary>
        [McpParam(Description = "Log file name (exact) or prefix (e.g. 'log', 'mcp.log') - a prefix picks the most recent match. Omit to LIST the available log files instead.")]
        public string File { get; set; }

        /// <summary>Only lines containing this level token.</summary>
        [McpParam(Description = "Only lines of this level.", Enum = new[] { "ERROR", "WARN", "INFO", "DEBUG", "FATAL" })]
        public string Level { get; set; }

        /// <summary>Only lines containing this text.</summary>
        [McpParam(Description = "Only lines containing this text (case-insensitive).")]
        public string Search { get; set; }

        /// <summary>The maximum number of matching lines returned, from the end of the file.</summary>
        [McpParam(Description = "Maximum matching lines to return, from the file's end (default 100, max 500).")]
        public int? Lines { get; set; }
    }

    /// <summary>
    /// Reads Sitecore's log files: lists what exists, or tails one with level and text filters. Reads
    /// the file share-tolerantly (logs are open for writing) and only the tail, so a large log cannot
    /// flood the response.
    /// </summary>
    public sealed class GetLogsTool : McpTool<GetLogsArgs>
    {
        private const int DefaultLines = 100;
        private const int MaxLines = 500;
        private const long TailBytes = 1024 * 1024;

        /// <inheritdoc />
        public override string Name => "sitecore_get_logs";

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Read Sitecore's logs: omit 'file' to list the log files (name, size, last write), or " +
            "name one (a prefix like 'log' or 'mcp.log' picks the most recent match) to tail it, " +
            "filtered by level and/or a search string. Returns the LAST matching lines. Only the " +
            "final 1 MB of a file is scanned, so very old lines need the file read another way. " +
            "Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetLogsArgs args, McpCallContext context)
        {
            // DataFolder is usually the virtual "/App_Data", so it must be mapped to a physical path.
            var logsFolder = Path.Combine(Sitecore.IO.FileUtil.MapPath(Settings.DataFolder), "logs");
            if (!Directory.Exists(logsFolder))
            {
                throw new McpToolException($"The logs folder was not found at '{logsFolder}'.");
            }

            var files = new DirectoryInfo(logsFolder).GetFiles("*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (string.IsNullOrWhiteSpace(args.File))
            {
                return McpToolResult.Structured(new JObject
                {
                    ["folder"] = logsFolder,
                    ["count"] = files.Count,
                    ["files"] = new JArray(files.Select(f => (object)new JObject
                    {
                        ["name"] = f.Name,
                        ["sizeKb"] = f.Length / 1024,
                        ["lastWrite"] = f.LastWriteTimeUtc.ToString("o")
                    }).ToArray())
                });
            }

            var wanted = args.File.Trim();
            var file = files.FirstOrDefault(f => f.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                       ?? files.FirstOrDefault(f => f.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            if (file == null)
            {
                var names = string.Join(", ", files.Take(15).Select(f => f.Name));
                throw new McpToolException($"No log file matches '{wanted}'. Recent files: {names}.");
            }

            var maxLines = Paging.Clamp(args.Lines.GetValueOrDefault(DefaultLines), 1, MaxLines);
            var lines = Tail(file.FullName, out var scannedWholeFile);

            var filtered = lines.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(args.Level))
            {
                var level = args.Level.Trim().ToUpperInvariant();
                filtered = filtered.Where(line => line.IndexOf(level, StringComparison.Ordinal) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(args.Search))
            {
                filtered = filtered.Where(line => line.IndexOf(args.Search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var matches = filtered.ToList();
            var page = matches.Skip(Math.Max(0, matches.Count - maxLines)).ToList();

            return McpToolResult.Structured(new JObject
            {
                ["file"] = file.Name,
                ["sizeKb"] = file.Length / 1024,
                ["lastWrite"] = file.LastWriteTimeUtc.ToString("o"),
                ["matched"] = matches.Count,
                ["returned"] = page.Count,
                ["tailCoversWholeFile"] = scannedWholeFile,
                ["lines"] = new JArray(page.Cast<object>().ToArray())
            });
        }

        /// <summary>
        /// Reads the last chunk of the file, tolerating the active log writer, and returns its
        /// complete lines. <paramref name="wholeFile"/> reports whether the chunk covered the file.
        /// </summary>
        private static string[] Tail(string path, out bool wholeFile)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                wholeFile = stream.Length <= TailBytes;
                if (!wholeFile)
                {
                    stream.Seek(-TailBytes, SeekOrigin.End);
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var text = reader.ReadToEnd();
                    var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    // The first line of a mid-file chunk is almost always cut in half; drop it.
                    return wholeFile || lines.Length == 0 ? lines : lines.Skip(1).ToArray();
                }
            }
        }
    }
}
