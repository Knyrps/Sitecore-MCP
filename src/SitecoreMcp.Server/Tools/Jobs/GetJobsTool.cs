using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sitecore.Abstractions;
using Sitecore.Jobs;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>Arguments for <see cref="GetJobsTool"/>.</summary>
    public sealed class GetJobsArgs
    {
        /// <summary>A specific job handle to describe, as returned by a tool that starts a job.</summary>
        [McpParam(Description = "Job handle to look up, as returned by a tool that starts a job (e.g. sitecore_publish_item). Omit to list jobs.")]
        public string Handle { get; set; }

        /// <summary>Case-insensitive substring filter on the job category.</summary>
        [McpParam(Description = "Only jobs whose category contains this text (case-insensitive).")]
        public string Category { get; set; }

        /// <summary>Whether to list only jobs that have not finished; defaults to true.</summary>
        [McpParam(Description = "Only jobs that are still running. Defaults to true; pass false to include finished jobs still held in memory.")]
        public bool? ActiveOnly { get; set; }
    }

    /// <summary>
    /// Lists Sitecore jobs, or describes one by handle. This is how a caller follows long-running
    /// work (a publish, an index rebuild) to completion, since those tools return as soon as the job
    /// has started rather than waiting for it.
    /// </summary>
    public sealed class GetJobsTool : McpTool<GetJobsArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_get_jobs";

        /// <inheritdoc />
        public override string Description =>
            "List running Sitecore jobs, or describe one by handle. Tools that start long-running " +
            "work (sitecore_publish_item) return a handle immediately — poll it here to see state " +
            "and progress. A finished job is only listed while Sitecore still holds it in memory.";

        /// <inheritdoc />
        protected override McpToolResult Execute(GetJobsArgs args, McpCallContext context)
        {
            if (!string.IsNullOrEmpty(args.Handle))
            {
                return Describe(args.Handle);
            }

            var activeOnly = args.ActiveOnly.GetValueOrDefault(true);
            var jobs = JobManager.GetJobs() ?? new BaseJob[0];

            var matches = jobs
                .Where(job => job != null)
                .Where(job => !activeOnly || !job.IsDone)
                .Where(job => string.IsNullOrEmpty(args.Category) ||
                              (job.Category ?? string.Empty).IndexOf(args.Category, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(JobDescriber.Describe)
                .ToArray();

            return McpToolResult.Structured(new JObject
            {
                ["activeOnly"] = activeOnly,
                ["count"] = matches.Length,
                ["jobs"] = new JArray(matches.Cast<object>().ToArray())
            });
        }

        private static McpToolResult Describe(string handle)
        {
            var job = FindJob(handle);
            if (job == null)
            {
                // A finished job is reaped once its AfterLife elapses, so an unknown handle is a
                // normal outcome for a job that already completed - not an error.
                return McpToolResult.Structured(new JObject
                {
                    ["handle"] = handle,
                    ["found"] = false,
                    ["hint"] = "No job with this handle. It may have finished and been released, " +
                               "or the handle may be from a previous application lifetime."
                });
            }

            var result = JobDescriber.Describe(job);
            result["found"] = true;
            return McpToolResult.Structured(result);
        }

        /// <summary>
        /// Resolves a job from a handle string. Shared with the stop tool so both accept exactly the
        /// same handle format that job-starting tools hand out.
        /// </summary>
        internal static BaseJob FindJob(string handle)
        {
            var parsed = Sitecore.Handle.Parse(handle);
            return parsed == null ? null : JobManager.GetJob(parsed);
        }
    }
}
