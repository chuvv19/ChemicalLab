using System;
using System.Collections.Generic;
using System.Linq;
using Codely.Newtonsoft.Json;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// Manages long-running asynchronous operation states (compilation, UPM, baking, etc.)
    /// Provides operation ID generation, status tracking, and timeout management.
    /// </summary>
    public static class AsyncOperationTracker
    {
        /// <summary>
        /// Job status enum matching MCP protocol.
        /// </summary>
        public enum JobStatus
        {
            Pending,
            Complete,
            Error
        }

        /// <summary>
        /// Job type enum for categorizing operations.
        /// </summary>
        public enum JobType
        {
            Compilation,
            UpmPackage,
            NavMeshBake,
            LightingBake,
            PlayMode,
            Custom
        }

        /// <summary>
        /// Represents a tracked job/operation.
        /// </summary>
        public class Job
        {
            public string OpId { get; set; }
            public JobType Type { get; set; }
            public JobStatus Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public string Message { get; set; }
            public object Data { get; set; }
            public string ErrorMessage { get; set; }
            public float Progress { get; set; } // 0.0 to 1.0
        }

        // Job storage — backed by SessionState so jobs survive domain reloads
        private const string SessionKeyJobs = "AsyncOperationTracker_Jobs";
        private static readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>();
        private static readonly object _jobsLock = new object();

        // Default timeout in seconds
        private const int DefaultTimeoutSeconds = 300;

        [Serializable]
        private class JobRecord
        {
            public string OpId;
            public int Type;
            public int Status;
            public long CreatedAtTicks;
            public long? CompletedAtTicks;
            public string Message;
            public string DataJson;
            public string ErrorMessage;
            public float Progress;
        }

        static AsyncOperationTracker()
        {
            LoadFromSessionState();
        }

        /// <summary>
        /// Creates a new job with a unique op_id and registers it.
        /// </summary>
        public static Job CreateJob(JobType type, string message = null)
        {
            var job = new Job
            {
                OpId = GenerateOpId(),
                Type = type,
                Status = JobStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                Message = message ?? $"{type} operation started",
                Progress = 0.0f
            };

            lock (_jobsLock)
            {
                _jobs[job.OpId] = job;
                PersistJobs();
            }

            return job;
        }

        /// <summary>
        /// Gets a job by op_id.
        /// </summary>
        public static Job GetJob(string opId)
        {
            lock (_jobsLock)
            {
                return _jobs.TryGetValue(opId, out var job) ? job : null;
            }
        }

        /// <summary>
        /// Updates job status to Complete.
        /// </summary>
        public static void CompleteJob(string opId, string message = null, object data = null)
        {
            lock (_jobsLock)
            {
                if (_jobs.TryGetValue(opId, out var job))
                {
                    job.Status = JobStatus.Complete;
                    job.CompletedAt = DateTime.UtcNow;
                    job.Progress = 1.0f;
                    if (message != null) job.Message = message;
                    if (data != null) job.Data = data;
                    PersistJobs();
                }
            }
        }

        /// <summary>
        /// Updates job status to Error.
        /// </summary>
        public static void FailJob(string opId, string errorMessage)
        {
            lock (_jobsLock)
            {
                if (_jobs.TryGetValue(opId, out var job))
                {
                    job.Status = JobStatus.Error;
                    job.CompletedAt = DateTime.UtcNow;
                    job.ErrorMessage = errorMessage;
                    job.Message = $"Operation failed: {errorMessage}";
                    PersistJobs();
                }
            }
        }

        /// <summary>
        /// Updates job progress (0.0 to 1.0).
        /// </summary>
        public static void UpdateProgress(string opId, float progress, string message = null)
        {
            lock (_jobsLock)
            {
                if (_jobs.TryGetValue(opId, out var job))
                {
                    job.Progress = Mathf.Clamp01(progress);
                    if (message != null) job.Message = message;
                    PersistJobs();
                }
            }
        }

        /// <summary>
        /// Removes a job from tracking.
        /// </summary>
        public static void RemoveJob(string opId)
        {
            lock (_jobsLock)
            {
                if (_jobs.Remove(opId))
                    PersistJobs();
            }
        }

        /// <summary>
        /// Gets all pending jobs of a specific type.
        /// </summary>
        public static List<Job> GetPendingJobs(JobType? type = null)
        {
            lock (_jobsLock)
            {
                return _jobs.Values
                    .Where(j => j.Status == JobStatus.Pending && (!type.HasValue || j.Type == type.Value))
                    .ToList();
            }
        }

        /// <summary>
        /// Cleans up old jobs that have been completed or timed out.
        /// Should be called periodically.
        /// </summary>
        public static void CleanupOldJobs(int maxAgeSeconds = 3600)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-maxAgeSeconds);
            
            lock (_jobsLock)
            {
                var toRemove = _jobs
                    .Where(kv => kv.Value.CompletedAt.HasValue && kv.Value.CompletedAt.Value < cutoff)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var opId in toRemove)
                {
                    _jobs.Remove(opId);
                }

                if (toRemove.Count > 0)
                {
                    PersistJobs();
                    CodelyLogger.Log($"[AsyncOperationTracker] Cleaned up {toRemove.Count} old jobs");
                }
            }
        }

        /// <summary>
        /// Checks if a job has timed out.
        /// </summary>
        public static bool IsJobTimedOut(string opId, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            lock (_jobsLock)
            {
                if (_jobs.TryGetValue(opId, out var job))
                {
                    if (job.Status == JobStatus.Pending)
                    {
                        var elapsed = (DateTime.UtcNow - job.CreatedAt).TotalSeconds;
                        return elapsed > timeoutSeconds;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Creates a Pending async operation response for a job.
        /// Protocol: status/poll_interval/op_id
        /// </summary>
        public static object CreatePendingResponse(Job job, object stateDelta = null)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "pending",
                ["poll_interval"] = 1.0, // Poll every 1 second
                ["op_id"] = job.OpId,
                ["success"] = true,
                ["message"] = job.Message,
                ["data"] = new
                {
                    type = job.Type.ToString(),
                    progress = job.Progress,
                    createdAt = job.CreatedAt.ToString("o")
                }
            };
            
            // Add operations state delta showing the new pending operation
            var opDelta = StateComposer.CreateOperationsDelta(new[] { 
                new { id = job.OpId, type = job.Type.ToString(), progress = job.Progress, message = job.Message }
            });
            response["state_delta"] = stateDelta != null 
                ? StateComposer.MergeStateDeltas(opDelta, stateDelta) 
                : opDelta;
            
            return response;
        }

        /// <summary>
        /// Creates a Complete async operation response for a job.
        /// Protocol: status/op_id
        /// </summary>
        public static object CreateCompleteResponse(Job job, object stateDelta = null)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "complete",
                ["op_id"] = job.OpId,
                ["success"] = true,
                ["message"] = job.Message,
                ["data"] = job.Data
            };
            
            // Include state_delta if provided
            if (stateDelta != null)
            {
                response["state_delta"] = stateDelta;
            }
            
            return response;
        }

        /// <summary>
        /// Creates an Error async operation response for a job.
        /// Protocol: status/op_id
        /// </summary>
        public static object CreateErrorResponse(Job job, object stateDelta = null)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "error",
                ["op_id"] = job.OpId,
                ["success"] = false,
                ["message"] = job.Message,
                ["error"] = job.ErrorMessage
            };
            
            // Include state_delta if provided
            if (stateDelta != null)
            {
                response["state_delta"] = stateDelta;
            }
            
            return response;
        }

        /// <summary>
        /// Generates a unique operation ID.
        /// </summary>
        private static string GenerateOpId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static void LoadFromSessionState()
        {
            try
            {
                var json = SessionState.GetString(SessionKeyJobs, null);
                if (string.IsNullOrEmpty(json))
                    return;

                var records = JsonConvert.DeserializeObject<Dictionary<string, JobRecord>>(json);
                if (records == null)
                    return;

                lock (_jobsLock)
                {
                    _jobs.Clear();
                    foreach (var record in records.Values)
                    {
                        var job = FromRecord(record);
                        if (job != null)
                            _jobs[job.OpId] = job;
                    }
                }
            }
            catch (Exception ex)
            {
                CodelyLogger.LogWarning($"[AsyncOperationTracker] Failed to load jobs from SessionState: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the in-memory job dictionary to SessionState.
        /// Must be called while holding <see cref="_jobsLock"/>.
        /// </summary>
        private static void PersistJobs()
        {
            try
            {
                var records = _jobs.ToDictionary(kv => kv.Key, kv => ToRecord(kv.Value));
                SessionState.SetString(SessionKeyJobs, JsonConvert.SerializeObject(records));
            }
            catch (Exception ex)
            {
                CodelyLogger.LogWarning($"[AsyncOperationTracker] Failed to persist jobs to SessionState: {ex.Message}");
            }
        }

        private static JobRecord ToRecord(Job job)
        {
            return new JobRecord
            {
                OpId = job.OpId,
                Type = (int)job.Type,
                Status = (int)job.Status,
                CreatedAtTicks = job.CreatedAt.Ticks,
                CompletedAtTicks = job.CompletedAt?.Ticks,
                Message = job.Message,
                DataJson = job.Data != null ? JsonConvert.SerializeObject(job.Data) : null,
                ErrorMessage = job.ErrorMessage,
                Progress = job.Progress
            };
        }

        private static Job FromRecord(JobRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.OpId))
                return null;

            object data = null;
            if (!string.IsNullOrEmpty(record.DataJson))
            {
                try
                {
                    data = JToken.Parse(record.DataJson);
                }
                catch
                {
                    data = record.DataJson;
                }
            }

            return new Job
            {
                OpId = record.OpId,
                Type = (JobType)record.Type,
                Status = (JobStatus)record.Status,
                CreatedAt = new DateTime(record.CreatedAtTicks, DateTimeKind.Utc),
                CompletedAt = record.CompletedAtTicks.HasValue
                    ? new DateTime(record.CompletedAtTicks.Value, DateTimeKind.Utc)
                    : (DateTime?)null,
                Message = record.Message,
                Data = data,
                ErrorMessage = record.ErrorMessage,
                Progress = record.Progress
            };
        }

        /// <summary>
        /// Gets count of all jobs by status.
        /// </summary>
        public static Dictionary<JobStatus, int> GetJobCounts()
        {
            lock (_jobsLock)
            {
                return _jobs.Values
                    .GroupBy(j => j.Status)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }
    }
}

