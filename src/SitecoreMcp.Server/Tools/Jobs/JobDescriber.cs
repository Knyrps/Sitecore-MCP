using System;
using System.Collections;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using Sitecore.Abstractions;
using Sitecore.Publishing;

namespace SitecoreMcp.Server.Tools.Jobs
{
    /// <summary>
    /// Projects a Sitecore job to JSON for tool results. Long-running work (publish, index rebuild)
    /// is reported through this shape so a caller can poll one handle and read progress the same way
    /// regardless of what kind of job it is.
    /// </summary>
    public static class JobDescriber
    {
        // A long-running job can accumulate thousands of log lines; return only the tail so a poll
        // cannot flood the caller's context.
        private const int MaxMessages = 20;

        /// <summary>
        /// Describes a job: identity, state, progress, whether it can be aborted, and the tail of its
        /// messages and exceptions.
        /// </summary>
        public static JObject Describe(BaseJob job)
        {
            if (job == null)
            {
                return null;
            }

            var status = job.Status;

            var result = new JObject
            {
                ["handle"] = job.Handle?.ToString(),
                ["name"] = job.Name,
                ["displayName"] = job.DisplayName,
                ["category"] = job.Category,
                ["isDone"] = job.IsDone,
                ["queuedAt"] = job.QueueTime.ToString("o"),
                ["abortable"] = job.Options?.Abortable
            };

            if (status != null)
            {
                result["state"] = status.State.ToString();
                result["failed"] = status.Failed;
                result["processed"] = status.Processed;
                result["total"] = status.Total;

                var messages = status.GetMessages() ?? new string[0];
                result["messageCount"] = messages.Length;
                result["messages"] = Tail(messages);

                var exceptions = Exceptions(status);
                if (exceptions.Count > 0)
                {
                    result["exceptions"] = exceptions;
                }
            }

            return result;
        }

        /// <summary>
        /// Describes a publish by its handle. A publish handle is not a job handle - PublishManager
        /// tracks it separately - so this projects the same shape (state, progress, messages) to keep
        /// polling uniform no matter which kind of handle the caller holds.
        /// </summary>
        public static JObject DescribePublish(string handle, PublishStatus status)
        {
            if (status == null)
            {
                return null;
            }

            var messages = Messages(status.Messages);

            return new JObject
            {
                ["handle"] = handle,
                ["kind"] = "publish",
                ["state"] = status.State.ToString(),
                ["isDone"] = status.IsDone,
                ["failed"] = status.Failed,
                ["expired"] = status.Expired,
                ["processed"] = status.Processed,
                ["currentTarget"] = status.CurrentTarget?.Name,
                ["currentLanguage"] = status.CurrentLanguage?.Name,
                ["messageCount"] = messages.Length,
                ["messages"] = Tail(messages)
            };
        }

        private static string[] Messages(StringCollection messages)
        {
            if (messages == null)
            {
                return new string[0];
            }

            var result = new string[messages.Count];
            messages.CopyTo(result, 0);
            return result;
        }

        private static JArray Tail(string[] messages)
        {
            var start = Math.Max(0, messages.Length - MaxMessages);
            var tail = new JArray();
            for (var i = start; i < messages.Length; i++)
            {
                tail.Add(messages[i]);
            }

            return tail;
        }

        private static JArray Exceptions(BaseJobStatus status)
        {
            var result = new JArray();
            if (!(status.Exceptions is IEnumerable list))
            {
                return result;
            }

            foreach (var entry in list)
            {
                if (entry is Exception exception)
                {
                    result.Add(exception.Message);
                }

                if (result.Count >= MaxMessages)
                {
                    break;
                }
            }

            return result;
        }
    }
}
