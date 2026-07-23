using Newtonsoft.Json.Linq;
using Sitecore.Jobs;
using SitecoreMcp.Server.Protocol;
using SitecoreMcp.Server.Schema;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>Arguments for <see cref="StopJobTool"/>.</summary>
    public sealed class StopJobArgs
    {
        /// <summary>The handle of the job to abort.</summary>
        [McpParam(Description = "Handle of the job to abort, as returned by sitecore_get_jobs or by the tool that started it.", Required = true)]
        public string Handle { get; set; }
    }

    /// <summary>
    /// Requests that a running job abort. Sitecore's abort is cooperative: setting the state asks
    /// the job to stop, and an abortable job unwinds at its next safe point. There is no forced
    /// kill, so this reports what it requested rather than claiming the job has stopped.
    /// </summary>
    public sealed class StopJobTool : McpTool<StopJobArgs>
    {
        /// <inheritdoc />
        public override string Name => "sitecore_stop_job";

        /// <inheritdoc />
        public override bool RequiresWrite => true;

        /// <inheritdoc />
        public override string Description =>
            "Request that a running Sitecore job abort. The abort is cooperative and only works for " +
            "jobs that declare themselves abortable: the job stops at its next safe point, so this " +
            "returns 'requested', not 'stopped'. Poll sitecore_get_jobs to see whether the job " +
            "actually reached the Aborted state. A job that is already finished, or that does not " +
            "support aborting, is reported as such and is left running.";

        /// <inheritdoc />
        protected override McpToolResult Execute(StopJobArgs args, McpCallContext context)
        {
            var job = GetJobsTool.FindJob(args.Handle);
            if (job == null)
            {
                throw new McpToolException(
                    $"No job with handle '{args.Handle}'. It may have finished and been released, or the handle may be from a previous application lifetime.");
            }

            var result = JobDescriber.Describe(job);
            result["abortRequested"] = false;

            if (job.IsDone)
            {
                result["reason"] = "The job has already finished; there is nothing to abort.";
                return McpToolResult.Structured(result);
            }

            // Sitecore exposes no forced stop: no BaseJob.Abort(), no JobManager.Stop(). The only
            // way to kill a non-abortable job would be aborting its thread, which can tear down a
            // partial write and leak locks, so a job that does not opt in is left alone.
            if (job.Options?.Abortable != true)
            {
                result["reason"] = "This job does not support aborting. Sitecore offers no safe way to " +
                                   "force it to stop, so it has been left running.";
                return McpToolResult.Structured(result);
            }

            job.Status.State = JobState.AbortRequested;

            result["abortRequested"] = true;
            result["state"] = job.Status.State.ToString();
            result["note"] = "Abort requested. The job stops at its next safe point - poll " +
                             "sitecore_get_jobs with this handle to confirm it reached Aborted.";
            return McpToolResult.Structured(result);
        }
    }
}
