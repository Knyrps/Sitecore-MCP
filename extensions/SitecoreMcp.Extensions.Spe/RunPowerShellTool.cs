using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Spe.Core.Host;
using Spe.Core.Settings;
using SitecoreMcp.Server.Diagnostics;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;
using SitecoreMcp.Server.Tools;
using SitecoreMcp.Server.Tools.Confirmation;

namespace SitecoreMcp.Extensions.Spe
{
    /// <summary>Arguments for <see cref="RunPowerShellTool"/>.</summary>
    public sealed class RunPowerShellArgs
    {
        /// <summary>The PowerShell script to run.</summary>
        [McpParam(Description = "PowerShell script to run in a fresh SPE session (the SPE provider is loaded: master:/..., Get-Item, New-Item, Copy-Item, ...).", Required = true)]
        public string Script { get; set; }

        /// <summary>The token from this tool's preview, for a client that confirms direct changes.</summary>
        [McpParam(Description =
            "Confirmation token from this tool's preview. Omit on the first call; repeat the call with the " +
            "same script and the token the editor confirmed.")]
        public string ConfirmationToken { get; set; }
    }

    /// <summary>
    /// Runs a PowerShell script through Sitecore PowerShell Extensions as the calling user, in a
    /// throwaway session, and returns its output. One call can do bulk work (copy a subtree, add
    /// language versions) that would otherwise take hundreds of item calls.
    /// </summary>
    public sealed class RunPowerShellTool : McpTool<RunPowerShellArgs>, IDirectChangeDescription
    {
        private const int MaxOutputChars = 50000;

        /// <inheritdoc />
        public override string Name => "sitecore_run_powershell";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override bool RequiresAdmin => true;

        /// <inheritdoc />
        public override string Description =>
            "Run a Sitecore PowerShell Extensions script as the calling user and return its output and " +
            "errors. Use it for bulk work the item tools would need many calls for. It can change anything, " +
            "so it is a direct change: called without a confirmationToken it only returns a preview of the " +
            "script; it runs once the editor has confirmed. Administrator only.";

        /// <inheritdoc />
        public string DescriptionWithoutConfirmation =>
            "Run a Sitecore PowerShell Extensions script as the calling user and return its output and " +
            "errors. Use it for bulk work the item tools would need many calls for. It runs on the first " +
            "call and can change anything; prefer the item tools for single changes. Administrator only.";

        /// <inheritdoc />
        protected override McpToolResult Execute(RunPowerShellArgs args, McpCallContext context)
        {
            var script = args.Script ?? string.Empty;
            var hash = Hash(script);
            var preview = new DirectChangePreview(
                $"Run a PowerShell script ({script.Length} characters) as '{context.User?.Name}'.",
                new JObject { ["script"] = script, ["sha256"] = hash },
                hash);

            return context.ConfirmDirectChange(Name, args, args.ConfirmationToken, preview, () => Run(script, hash, context));
        }

        private static McpToolResult Run(string script, string hash, McpCallContext context)
        {
            McpLog.Info($"sitecore_run_powershell as '{context.User?.Name}' sha256={hash}:{Environment.NewLine}{script}");
            var started = DateTime.UtcNow;

            using (var session = ScriptSessionManager.NewSession(ApplicationNames.RemoteAutomation, false))
            {
                string output;
                string failure = null;
                try
                {
                    var results = session.ExecuteScriptPart(script, true) ?? new System.Collections.Generic.List<object>();
                    output = string.Join(Environment.NewLine, results.Select(r => r?.ToString()).Where(s => !string.IsNullOrEmpty(s)));
                }
                catch (Exception ex)
                {
                    output = string.Empty;
                    failure = ex.InnerException?.Message ?? ex.Message;
                }

                var host = session.Output?.ToString()?.Trim() ?? string.Empty;
                var errors = new JArray(ReadErrors(session));
                if (failure != null) errors.Add(failure);

                var combined = string.Join(Environment.NewLine, new[] { host, output.Trim() }.Where(s => s.Length > 0));
                var truncated = combined.Length > MaxOutputChars;

                var payload = new JObject
                {
                    ["succeeded"] = errors.Count == 0,
                    ["output"] = truncated ? combined.Substring(0, MaxOutputChars) : combined,
                    ["outputTruncated"] = truncated,
                    ["errors"] = errors,
                    ["durationMs"] = (long)(DateTime.UtcNow - started).TotalMilliseconds
                };

                var result = McpToolResult.Structured(payload);
                result.IsError = failure != null;
                return result;
            }
        }

        /// <summary>
        /// Non-terminating errors (a failed Get-Item) are only rendered into the text output; the
        /// session's $Error lists every error the script raised, so the result can report them.
        /// </summary>
        private static string[] ReadErrors(ScriptSession session)
        {
            try
            {
                return (session.ExecuteScriptPart("$Error | ForEach-Object { $_.ToString() }", false) ?? new System.Collections.Generic.List<object>())
                    .Select(e => e?.ToString())
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Reverse()
                    .ToArray();
            }
            catch (Exception ex)
            {
                return new[] { "Could not read $Error: " + ex.Message };
            }
        }

        private static string Hash(string script)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(script)).Select(b => b.ToString("x2")));
            }
        }
    }
}
