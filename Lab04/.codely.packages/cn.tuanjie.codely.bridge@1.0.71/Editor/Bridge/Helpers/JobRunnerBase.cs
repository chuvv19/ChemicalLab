using System;
using System.Collections.Generic;
using UnityEditor;
using UnityTcp.Editor.Native;

namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// Shared lifecycle for the async job runners: an in-flight job registry, the per-frame tick
    /// loop, timeout, cancellation, and the requestId → NativeUnityTcpBridgeHost.EnqueueResponse
    /// delivery. Subclasses implement only the one differing step — <see cref="Advance"/> — which
    /// polls a Task or MoveNexts a coroutine.
    ///
    /// Everything runs on the editor main thread (Tick is called once per editor frame from the
    /// top-level command loop). Jobs are runtime-only and are NOT persisted across domain reloads.
    /// </summary>
    public abstract class JobRunnerBase
    {
        public const int DefaultTimeoutSeconds = 300;

        /// <summary>Result of advancing a job one step.</summary>
        protected enum Step
        {
            Running,    // still in flight — keep ticking
            Finished,   // done — respond to the client and drop it
            HandedOff,  // another runner took ownership — drop it, it will respond
        }

        protected sealed class Job
        {
            public JobContext Ctx;
            public object     Work;      // Task or IEnumerator
            public double     Deadline;  // EditorApplication.timeSinceStartup; 0 = no timeout

            public bool TimedOut => Deadline > 0 && EditorApplication.timeSinceStartup > Deadline;
        }

        private readonly List<Job> _jobs = new List<Job>();

        /// <summary>
        /// Creates a job/context bound to a native request id, with a name for the response.
        /// When <paramref name="detached"/> is true the finished job is memorized for later
        /// polling instead of being sent on the request (see <see cref="DetachedJobs"/>).
        /// </summary>
        public JobContext CreateJob(ulong requestId, string name, bool detached = false)
            => new JobContext(requestId, name, detached);

        protected void Enroll(JobContext ctx, object work, int timeoutSeconds)
        {
            if (ctx == null)  throw new ArgumentNullException(nameof(ctx));
            if (work == null) throw new ArgumentNullException(nameof(work));

            _jobs.Add(new Job
            {
                Ctx = ctx,
                Work = work,
                Deadline = timeoutSeconds > 0
                    ? EditorApplication.timeSinceStartup + timeoutSeconds
                    : 0,
            });

            if (ctx.Detached) DetachedJobs.MarkPending(ctx);
        }

        /// <summary>Advance one job by one step. Implemented per runner (Task vs coroutine).</summary>
        protected abstract Step Advance(Job job);

        /// <summary>
        /// Releases a job's work when it leaves the registry (finished, faulted, timed out, or
        /// cancelled). Coroutine jobs override this to Dispose their enumerators — a C# iterator's
        /// finally blocks run ONLY on Dispose, never on a caught throw, so without this the
        /// enumerator's cleanup (e.g. dropping a log-capture subscription) would never execute.
        /// Base is a no-op; Task jobs need no disposal (disposing an in-flight Task throws).
        /// </summary>
        protected virtual void Cleanup(Job job) { }

        /// <summary>
        /// Called once per editor frame from the top-level loop: advances every job by one step
        /// and enqueues the response for any that finished (or timed out / threw).
        /// </summary>
        public void Tick()
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                var job = _jobs[i];

                Step step;
                if (job.TimedOut)
                {
                    job.Ctx.SetError(Response.Error("Operation timed out."));
                    step = Step.Finished;
                }
                else
                {
                    try { step = Advance(job); }
                    catch (Exception ex) { job.Ctx.SetError(Response.Error(SafeError(ex))); step = Step.Finished; }
                }

                if (step == Step.Running) continue;

                _jobs.RemoveAt(i);
                // Dispose the work (runs the coroutine's finally blocks) before finalizing so any
                // cleanup it performs — e.g. releasing a log-capture subscription — happens now.
                // HandedOff means another runner took ownership, so leave its work alone.
                if (step != Step.HandedOff) SafeCleanup(job);
                if (step == Step.Finished) Finalize(job.Ctx);
            }
        }

        /// <summary>
        /// Fails and responds to every in-flight job — e.g. on play-mode exit, server stop, or
        /// domain reload — so no client request is left hanging.
        /// </summary>
        public void CancelAll(string reason = "Operation canceled.")
        {
            var snapshot = _jobs.ToArray();
            _jobs.Clear();
            foreach (var job in snapshot)
            {
                job.Ctx.SetError(Response.Error(reason));
                SafeCleanup(job);
                Finalize(job.Ctx);
            }
        }

        // Cleanup must never throw into the tick loop or the cancel sweep — a user coroutine's
        // finally block could raise. Swallow and log so one bad job can't strand the others.
        private void SafeCleanup(Job job)
        {
            try { Cleanup(job); }
            catch (Exception ex) { CodelyLogger.LogWarning($"[JobRunner] Cleanup failed: {ex.Message}"); }
        }

        // Deliver a finished job: memorize detached jobs for later polling; otherwise respond now.
        private static void Finalize(JobContext ctx)
        {
            if (ctx.Detached)
                DetachedJobs.Store(ctx);
            else
                NativeUnityTcpBridgeHost.EnqueueResponse(ctx.RequestId, ctx.ToResponseJson());
        }

        // Reading Exception.StackTrace can itself throw on Mono when the stack passes through
        // async state machines — read it defensively.
        protected static string SafeError(Exception e)
        {
            if (e == null) return "Unknown error.";
            string trace;
            try { trace = e.StackTrace; } catch { trace = "(stack trace unavailable)"; }
            return $"{e.Message}\n{trace}";
        }
    }
}
