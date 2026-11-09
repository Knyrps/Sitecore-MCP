using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SitecoreMcp.Server.Tools.Packages
{
    /// <summary>
    /// Package file names: a single file in the packages folder (<c>Settings.PackagePath</c>), never a
    /// path, so no argument can reach another folder.
    /// </summary>
    public static class PackageFileName
    {
        /// <summary>The package extension.</summary>
        public const string Extension = ".zip";

        /// <summary>The saved package project extension (the Package Designer's format).</summary>
        public const string ProjectExtension = ".xml";

        private const int MaxLength = 150;

        /// <summary>
        /// Turns a name into a package file name: trimmed, ".zip" added when missing, and refused when it
        /// is empty, has a path separator, a drive, "..", a leading dot, a character outside letters,
        /// digits, space, dot, dash, underscore and parentheses, or another extension.
        /// </summary>
        public static bool TryNormalize(string name, out string fileName, out string error)
        {
            fileName = null;
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return Fail("A package name is required.", out error);
            if (trimmed.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || trimmed.Contains(".."))
            {
                return Fail($"'{trimmed}' is not a plain file name: packages are addressed by name inside the packages folder.", out error);
            }

            if (trimmed.StartsWith(".", StringComparison.Ordinal)) return Fail($"'{trimmed}' starts with a dot.", out error);
            if (trimmed.Any(c => !(char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '-' || c == '_' || c == '(' || c == ')')))
            {
                return Fail($"'{trimmed}' contains characters other than letters, digits, space, '.', '-', '_' and parentheses.", out error);
            }

            var extension = Path.GetExtension(trimmed);
            if (!extension.Equals(Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (extension.Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail($"'{trimmed}' is a package project, not a package; name the .zip.", out error);
                }

                trimmed += Extension;
            }

            if (trimmed.Length > MaxLength) return Fail($"The package name is longer than {MaxLength} characters.", out error);

            fileName = trimmed;
            error = null;
            return true;
        }

        /// <summary>The project file saved next to a package: the same base name with ".xml".</summary>
        public static string ProjectFor(string packageFileName) =>
            Path.GetFileNameWithoutExtension(packageFileName) + ProjectExtension;

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    /// <summary>
    /// Web-root-relative paths as packages use them ("/bin/My.dll"): forward slashes, one leading
    /// slash, no "..", no drive or share, and never the web root itself.
    /// </summary>
    public static class WebRootPath
    {
        /// <summary>Normalizes a path the caller gave, refusing anything that could leave the web root.</summary>
        public static bool TryNormalize(string input, out string path, out string error)
        {
            path = null;
            var value = (input ?? string.Empty).Trim();
            if (value.StartsWith("\\\\", StringComparison.Ordinal) || value.IndexOf(':') >= 0)
            {
                error = $"'{input}' is not web-root relative; give a path such as /bin/My.Module.dll.";
                return false;
            }

            if (value.StartsWith("~", StringComparison.Ordinal)) value = value.Substring(1);
            var segments = value.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == ".." || s == "."))
            {
                error = $"'{input}' contains '.' or '..' segments; paths must stay inside the web root.";
                return false;
            }

            if (segments.Length == 0)
            {
                error = "The web root itself cannot be a file source; name the folders or files to include.";
                return false;
            }

            path = "/" + string.Join("/", segments);
            error = null;
            return true;
        }

        /// <summary>The physical path of a normalized web-root path.</summary>
        public static string ToPhysical(string webRoot, string virtualPath) =>
            Path.Combine(webRoot, virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>How a file in a package is treated.</summary>
    public enum PackageFileClass
    {
        /// <summary>Static content (assets, media files).</summary>
        Content,

        /// <summary>Runs code or changes configuration: installing it restarts the site or executes code.</summary>
        Code,

        /// <summary>Never packaged and never installed (secrets, license, data, Sitecore-MCP's own files).</summary>
        Forbidden
    }

    /// <summary>The classification of one file path, with the reason.</summary>
    public sealed class PackageFileVerdict
    {
        /// <summary>Creates the verdict.</summary>
        public PackageFileVerdict(PackageFileClass @class, string reason)
        {
            Class = @class;
            Reason = reason;
        }

        /// <summary>The class.</summary>
        public PackageFileClass Class { get; }

        /// <summary>Why, for Code and Forbidden; null for Content.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Which files a package may carry. The same rules apply when building a package (a forbidden file
    /// is never read into one) and when installing one (a forbidden file is never written, and code
    /// needs <c>Mcp.Packages.AllowCode</c>).
    /// </summary>
    public static class PackageFilePolicy
    {
        private static readonly string[] SecretExtensions = { ".pfx", ".p12", ".pem", ".key", ".snk", ".env", ".user", ".publishsettings" };

        private static readonly string[] CodeExtensions =
        {
            ".dll", ".exe", ".config", ".aspx", ".ascx", ".asax", ".ashx", ".asmx", ".master", ".cshtml", ".vbhtml", ".svc", ".ps1", ".bat", ".cmd"
        };

        /// <summary>Classifies a normalized web-root path ("/bin/My.dll").</summary>
        public static PackageFileVerdict Classify(string virtualPath)
        {
            var path = (virtualPath ?? string.Empty).Replace('\\', '/');
            var lower = path.ToLowerInvariant();
            var name = lower.Substring(lower.LastIndexOf('/') + 1);
            var extension = Path.GetExtension(name);

            if (name == "web.config") return Forbidden("web.config holds machine keys, connection settings and authentication; it is never packaged or replaced");
            if (name.StartsWith("connectionstrings", StringComparison.Ordinal)) return Forbidden("connection strings hold credentials");
            if (name == "license.xml") return Forbidden("the Sitecore license is never packaged or replaced");
            if (lower.StartsWith("/app_data/", StringComparison.Ordinal) || lower == "/app_data")
                return Forbidden("App_Data holds the license, logs, packages and other instance data");
            if (SecretExtensions.Contains(extension) || name.StartsWith(".env", StringComparison.Ordinal))
                return Forbidden("certificates, keys and environment files are secrets");
            if (lower.StartsWith("/app_config/include/sitecoremcp/", StringComparison.Ordinal) ||
                (lower.StartsWith("/app_config/", StringComparison.Ordinal) && name.StartsWith("sitecoremcp.", StringComparison.Ordinal)) ||
                (lower.StartsWith("/bin/", StringComparison.Ordinal) && name.StartsWith("sitecoremcp.", StringComparison.Ordinal)))
                return Forbidden("Sitecore-MCP's own files and configuration cannot be packaged or replaced through Sitecore-MCP");

            if (lower.StartsWith("/bin/", StringComparison.Ordinal)) return Code("assemblies in /bin run in the site and restart it");
            if (lower.StartsWith("/app_config/", StringComparison.Ordinal)) return Code("files under App_Config change configuration and restart the site");
            if (CodeExtensions.Contains(extension)) return Code($"'{extension}' files are compiled or executed by the site");

            return new PackageFileVerdict(PackageFileClass.Content, null);
        }

        private static PackageFileVerdict Forbidden(string reason) => new PackageFileVerdict(PackageFileClass.Forbidden, reason);

        private static PackageFileVerdict Code(string reason) => new PackageFileVerdict(PackageFileClass.Code, reason);
    }

    /// <summary>A file left out of a folder source, with the reason.</summary>
    public sealed class SkippedFile
    {
        /// <summary>Creates the entry.</summary>
        public SkippedFile(string path, string reason)
        {
            Path = path;
            Reason = reason;
        }

        /// <summary>The web-root path.</summary>
        public string Path { get; }

        /// <summary>Why it was left out.</summary>
        public string Reason { get; }
    }

    /// <summary>The files a package will contain.</summary>
    public sealed class PackageFileSelectionResult
    {
        /// <summary>Whether every source was valid.</summary>
        public bool Succeeded { get; set; }

        /// <summary>Why not.</summary>
        public string Error { get; set; }

        /// <summary>The web-root paths of the files to package, sorted and distinct.</summary>
        public IReadOnlyList<string> Files { get; set; } = new string[0];

        /// <summary>Files inside a folder source that were left out (forbidden).</summary>
        public IReadOnlyList<SkippedFile> Skipped { get; set; } = new SkippedFile[0];
    }

    /// <summary>
    /// Resolves the file sources of a new package to an explicit list of files. Folders are expanded
    /// here rather than left as dynamic sources, so a forbidden file inside a folder can be left out and
    /// the package holds exactly what the result reports.
    /// </summary>
    public static class PackageFileSelection
    {
        /// <summary>Expands the sources against the physical web root.</summary>
        public static PackageFileSelectionResult Expand(string webRoot, IEnumerable<string> sources, int maxFiles)
        {
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<SkippedFile>();

            foreach (var source in sources ?? Enumerable.Empty<string>())
            {
                if (!WebRootPath.TryNormalize(source, out var path, out var error)) return Failed(error);

                var physical = WebRootPath.ToPhysical(webRoot, path);
                if (File.Exists(physical))
                {
                    var verdict = PackageFilePolicy.Classify(path);
                    if (verdict.Class == PackageFileClass.Forbidden) return Failed($"'{path}' cannot be packaged: {verdict.Reason}.");
                    files.Add(path);
                }
                else if (Directory.Exists(physical))
                {
                    var folderVerdict = PackageFilePolicy.Classify(path + "/");
                    if (folderVerdict.Class == PackageFileClass.Forbidden) return Failed($"'{path}' cannot be packaged: {folderVerdict.Reason}.");

                    foreach (var file in Directory.EnumerateFiles(physical, "*", SearchOption.AllDirectories))
                    {
                        var relative = "/" + file.Substring(webRoot.TrimEnd(Path.DirectorySeparatorChar, '/').Length)
                            .Replace(Path.DirectorySeparatorChar, '/').TrimStart('/');
                        var verdict = PackageFilePolicy.Classify(relative);
                        if (verdict.Class == PackageFileClass.Forbidden)
                        {
                            skipped.Add(new SkippedFile(relative, verdict.Reason));
                            continue;
                        }

                        files.Add(relative);
                        if (files.Count > maxFiles) return TooMany(maxFiles);
                    }
                }
                else
                {
                    return Failed($"'{path}' does not exist in the web root.");
                }

                if (files.Count > maxFiles) return TooMany(maxFiles);
            }

            return new PackageFileSelectionResult { Succeeded = true, Files = files.ToList(), Skipped = skipped };
        }

        private static PackageFileSelectionResult TooMany(int maxFiles) =>
            Failed($"The file sources select more than {maxFiles} files; name narrower folders.");

        private static PackageFileSelectionResult Failed(string error) =>
            new PackageFileSelectionResult { Succeeded = false, Error = error };
    }
}
