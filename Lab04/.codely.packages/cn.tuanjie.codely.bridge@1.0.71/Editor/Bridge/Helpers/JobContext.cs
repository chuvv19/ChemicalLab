using System;
using System.Collections;
using Codely.Newtonsoft.Json;

namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// A tracked async job: the native <see cref="RequestId"/> to reply on, plus an optional
    /// <see cref="Response"/> outcome that gets serialized when the job finishes.
    ///
    /// Created via a runner's CreateJob(requestId, name). A coroutine reports its outcome with
    /// <see cref="SetResult"/> / <see cref="SetError"/> (it cannot return a value). A Task may
    /// return a Response from its result; otherwise the AsyncTaskRunner fills the context.
    ///
    /// Result and Error are Response objects and may be null. When null, <see cref="ToResponseJson"/>
    /// synthesizes a Response from the job name/id.
    /// </summary>
    public sealed class JobContext
    {
        /// <summary>Native request id — passed to NativeUnityTcpBridgeHost.EnqueueResponse.</summary>
        public ulong RequestId { get; }

        /// <summary>Human-readable job name (e.g. the command type), echoed in the response.</summary>
        public string Name { get; }

        /// <summary>Stable id for correlating a detached job across the client's poll requests.</summary>
        public string JobId { get; }

        /// <summary>
        /// When true, the finished job is memorized (see <see cref="DetachedJobs"/>) instead of
        /// being sent on <see cref="RequestId"/>: the client early-returns with <see cref="JobId"/>
        /// and polls for the result later.
        /// </summary>
        public bool Detached { get; }

        internal JobContext(ulong requestId, string name, bool detached)
        {
            RequestId = requestId;
            Name = name;
            Detached = detached;
            JobId = Guid.NewGuid().ToString("N");
        }

        /// <summary>Success Response to send, or null to synthesize from the job name.</summary>
        internal object Result { get; private set; }

        /// <summary>Error Response to send, or null to synthesize from the job name.</summary>
        internal object Error { get; private set; }

        internal bool Failed { get; private set; }

        /// <summary>True once <see cref="SetResult"/> or <see cref="SetError"/> has been called.</summary>
        internal bool HasOutcome { get; private set; }

        /// <summary>
        /// Records the success Response. Pass null to let <see cref="ToResponseJson"/> build a
        /// default success from the job name.
        /// </summary>
        public void SetResult(object response = null)
        {
            Failed = false;
            Error = null;
            Result = response;
            HasOutcome = true;
        }

        /// <summary>
        /// Records the error Response. Pass null to let <see cref="ToResponseJson"/> build a
        /// default error from the job name.
        /// </summary>
        public void SetError(object response = null)
        {
            Failed = true;
            Result = null;
            Error = response;
            HasOutcome = true;
        }

        /// <summary>Builds the response JSON the runner enqueues to the client when finished.</summary>
        internal string ToResponseJson()
        {
            var jobData = new { job = Name, id = JobId };
            object response = Failed
                ? (Error ?? Response.Error(Name ?? "Operation failed.", jobData))
                : (Result ?? Response.Success(Name ?? "Operation completed.", jobData));
            return JsonConvert.SerializeObject(response);
        }

        /// <summary>
        /// Early-return response for a detached job: tells the client the job started and to poll
        /// for the result later using <see cref="JobId"/>.
        /// </summary>
        public object ToPendingResponse()
            => Response.Success("Job started.", new { job = Name, id = JobId, status = "pending" });

        /// <summary>True when <paramref name="value"/> looks like a <see cref="Response"/> payload.</summary>
        internal static bool IsResponse(object value)
            => value is IDictionary d && d.Contains("success");
    }
}
