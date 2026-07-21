using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// Memo store for detached jobs. A detached job does not respond on its original request —
    /// the client early-returns with the job id and polls later. While running, the job sits in
    /// <see cref="_pending"/>; when it finishes, the runner moves it to <see cref="_completed"/>
    /// (holding its outcome) until the client picks it up via <see cref="Check"/>.
    ///
    /// Runtime-only and main-thread-only: entries do NOT survive a domain reload (neither do the
    /// jobs themselves). Completed entries that are never collected expire after
    /// <see cref="CompletedTtlSeconds"/> so the store cannot grow unbounded.
    /// </summary>
    public static class DetachedJobs
    {
        public enum Status
        {
            Unknown,   // no such job id (never existed, already collected, or expired)
            Pending,   // still running
            Complete,  // finished — result returned and removed
        }

        private sealed class Completed
        {
            public JobContext Ctx;
            public double At;   // EditorApplication.timeSinceStartup when it finished
        }

        private static readonly Dictionary<string, JobContext> _pending   = new Dictionary<string, JobContext>();
        private static readonly Dictionary<string, Completed>   _completed = new Dictionary<string, Completed>();

        private const int CompletedTtlSeconds = 300;

        /// <summary>Records a detached job as running. Called when the runner enrolls it.</summary>
        internal static void MarkPending(JobContext ctx) => _pending[ctx.JobId] = ctx;

        /// <summary>Memorizes a finished detached job's outcome until the client collects it.</summary>
        internal static void Store(JobContext ctx)
        {
            _pending.Remove(ctx.JobId);
            _completed[ctx.JobId] = new Completed { Ctx = ctx, At = EditorApplication.timeSinceStartup };
            Prune();
        }

        /// <summary>
        /// Client poll: looks up a detached job by id. On <see cref="Status.Complete"/> the
        /// response JSON is returned and the entry removed (single collection).
        /// </summary>
        public static Status Check(string jobId, out string responseJson)
        {
            Prune();
            responseJson = null;
            if (string.IsNullOrEmpty(jobId)) return Status.Unknown;

            if (_completed.TryGetValue(jobId, out var completed))
            {
                responseJson = completed.Ctx.ToResponseJson();
                _completed.Remove(jobId);
                return Status.Complete;
            }
            return _pending.ContainsKey(jobId) ? Status.Pending : Status.Unknown;
        }

        private static void Prune()
        {
            if (_completed.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            var expired = _completed
                .Where(kv => now - kv.Value.At > CompletedTtlSeconds)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in expired)
                _completed.Remove(key);
        }
    }
}
