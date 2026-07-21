using System;
using System.Collections;
using System.Collections.Generic;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine.Video;
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Tracks active effect video generation tasks.
    /// </summary>
    public static class EffectVideoTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, EffectVideoTaskInfo> _activeTasks = new Dictionary<string, EffectVideoTaskInfo>();
        private static int _taskIdCounter = 0;

        private const string SessionKeyIds = "TJGen_EffectVideo_Ids";
        private const string SessionKeyFmt = "TJGen_EffectVideo_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string prompt;
            public string status;
            public int    progress;
            public string videoPath;
            public string materialPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string placeholderPath;
            public string backendTaskId;
        }

        public class EffectVideoTaskInfo
        {
            public string TaskId { get; set; }
            public string Prompt { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string VideoPath { get; set; }
            public string MaterialPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string BackendTaskId { get; set; }
        }

        internal static void SaveToSession(EffectVideoTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId          = info.TaskId,
                prompt          = info.Prompt ?? "",
                status          = info.Status,
                progress        = info.Progress,
                videoPath       = info.VideoPath ?? "",
                materialPath    = info.MaterialPath ?? "",
                errorMessage    = info.ErrorMessage ?? "",
                startTimeTicks  = info.StartTime.Ticks,
                endTimeTicks    = info.EndTime?.Ticks ?? 0,
                previewUrl      = info.PreviewUrl ?? "",
                placeholderPath = info.PlaceholderPath ?? "",
                backendTaskId   = info.BackendTaskId ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static EffectVideoTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json)) return null;
            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new EffectVideoTaskInfo
            {
                TaskId          = p.taskId,
                Prompt          = p.prompt,
                Status          = p.status,
                Progress        = p.progress,
                VideoPath       = p.videoPath,
                MaterialPath    = p.materialPath,
                ErrorMessage    = p.errorMessage,
                PreviewUrl      = p.previewUrl,
                StartTime       = new DateTime(p.startTimeTicks),
                EndTime         = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
                PlaceholderPath = p.placeholderPath,
                BackendTaskId   = p.backendTaskId
            };

            if (info.Status == "generating" || info.Status == "initializing")
            {
                info.Status       = "interrupted";
                info.ErrorMessage = "Generation was interrupted (domain reload). Please re-generate.";
                info.EndTime      = DateTime.Now;
                SaveToSession(info);
            }

            _activeTasks[taskId] = info;
            return info;
        }

        public static string CreateTask(string prompt, string placeholderPath, string backendTaskId = null)
        {
            string taskId = $"effectvideo_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var task = new EffectVideoTaskInfo
            {
                TaskId          = taskId,
                Prompt          = prompt ?? "",
                Status          = "generating",
                StartTime       = DateTime.Now,
                PlaceholderPath = placeholderPath,
                BackendTaskId   = backendTaskId
            };
            _activeTasks[taskId] = task;
            SaveToSession(task);

            return taskId;
        }

        public static void MarkTaskCompleted(string taskId, string videoPath, string materialPath, string previewUrl)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status       = "completed";
                task.Progress     = 100;
                task.VideoPath    = videoPath;
                task.MaterialPath = materialPath;
                task.PreviewUrl   = previewUrl;
                task.EndTime      = DateTime.Now;
                SaveToSession(task);
            }
        }

        public static void MarkTaskFailed(string taskId, string errorMessage)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status       = "failed";
                task.ErrorMessage = errorMessage;
                task.EndTime      = DateTime.Now;
                SaveToSession(task);
            }
        }

        public static EffectVideoTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task)) return task;
            return TryRestoreFromSession(taskId);
        }

        public static List<EffectVideoTaskInfo> GetAllTasks()
        {
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!string.IsNullOrEmpty(ids))
            {
                foreach (var id in ids.Split('|'))
                {
                    if (!string.IsNullOrEmpty(id) && !_activeTasks.ContainsKey(id))
                        TryRestoreFromSession(id);
                }
            }
            return new List<EffectVideoTaskInfo>(_activeTasks.Values);
        }

        public static void RemoveTask(string taskId)
        {
            _activeTasks.Remove(taskId);
            SessionState.EraseString(string.Format(SessionKeyFmt, taskId));
            string ids = SessionState.GetString(SessionKeyIds, "");
            var list = new List<string>(ids.Split('|'));
            list.Remove(taskId);
            SessionState.SetString(SessionKeyIds, string.Join("|", list));
        }

        public static void CleanupCompletedTasks()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _activeTasks)
            {
                if ((kvp.Value.Status == "completed" || kvp.Value.Status == "failed") &&
                    kvp.Value.EndTime.HasValue &&
                    (DateTime.Now - kvp.Value.EndTime.Value).TotalMinutes > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                _activeTasks.Remove(id);
        }
#endif
    }

    /// <summary>
    /// CustomTool for generating effect videos with automatic green-screen keying.
    /// Backend: gpt-image-2 (auto green-screen prompt) → seedance2 (green-screen video).
    /// Unity: creates ChromaKey material → VideoPlayer + RenderTexture plays transparent video at runtime.
    /// No frame extraction — the shader keys in real-time during playback.
    /// </summary>
    public static class GenerateEffectVideoTool
    {
        [ExecuteCustomTool.CustomTool("generate_effect_video",
            "Generate an effect video with automatic green-screen keying. " +
            "Backend: 1) generates art with auto green-screen background (prompt doesn't need 'green screen'), " +
            "2) generates green-screen effect video. " +
            "Unity: 3) creates ChromaKey material — use with VideoPlayer + RenderTexture for real-time transparent playback. " +
            "Key parameters: prompt (VFX description, e.g. 'fire explosion', 'magic glow'), " +
            "videoDuration (optional: 4-15 seconds, default 5), " +
            "videoRatio (optional: '16:9', '9:16', '1:1', '4:3', default '16:9'), " +
            "videoResolution (optional: '720p' or '480p', default '720p'), " +
            "output_path (optional save path). " +
            "IMPORTANT: Full pipeline takes 90-180 seconds. " +
            "Wait at least 10 seconds before the first query_effect_video_status call. " +
            "A placeholder_path is returned immediately — you can reference it right away.")]
        public static object GenerateEffectVideo(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateEffectVideoTool] Generating effect video with parameters: {parameters}");

                string generatorId = "effect_video_wf";
                string prompt      = parameters["prompt"]?.ToString();
                string outputPath  = parameters["output_path"]?.ToString();
                string sessionId   = parameters["session_id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(prompt))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'prompt' parameter is required" }
                    };
                }

                var config = ConfigManager.GetGeneratorConfig(ConfigType.Video, generatorId);
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Cannot find video generator config for '{generatorId}'." }
                    };
                }

                var generator = new DynamicGenerator(config);
                generator.SetTextPrompt(prompt);
                GenerateVideoTool.ApplyVideoParametersInternal(generator, generatorId, parameters);

                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator, sessionId);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateEffectVideoTool] Submit failed [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateEffectVideoTool] Submit success, backend_task_id={submitResult.BackendTaskId}");

                string placeholderPath = GenerateVideoTool.CreatePlaceholderVideo(outputPath, "EffectVideo");
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = EffectVideoTaskTracker.CreateTask(prompt, placeholderPath, capturedBackendTaskId);

                var host = new EffectVideoPipelineHost(
                    placeholderPath,
                    sessionId,
                    (savedPath, previewUrl) =>
                    {
                        // Video downloaded — create ChromaKey material for real-time keying
                        TJLog.Log($"[GenerateEffectVideoTool] Video downloaded, creating ChromaKey material: {savedPath}");

                        string outputFolder = Path.GetDirectoryName(savedPath)?.Replace('\\', '/');
                        PathUtils.EnsureAssetFolder(outputFolder);

                        var postResult = GreenScreenVideoPostProcess.ProcessVideo(savedPath, outputFolder);

                        string materialPath = "";
                        if (postResult.Success)
                        {
                            materialPath = postResult.MaterialPath;
                            // 自动在场景中创建特效播放 GameObject
                            SetupEffectVideoInScene(savedPath, materialPath);
                        }
                        else
                        {
                            TJLog.LogError($"[GenerateEffectVideoTool] ChromaKey material creation failed: {postResult.Error}");
                        }

                        EffectVideoTaskTracker.MarkTaskCompleted(taskId, savedPath, materialPath, previewUrl);

                        var t = EffectVideoTaskTracker.GetTask(taskId);
                        var notifyPayload = new JObject
                        {
                            ["session_id"]    = sessionId,
                            ["generator_id"]   = generatorId,
                            ["prompt"]         = prompt ?? "",
                            ["video_path"]     = savedPath ?? "",
                            ["material_path"]  = materialPath ?? "",
                            ["preview_url"]    = previewUrl ?? "",
                            ["progress"]       = 100,
                            ["start_time"]     = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            ["end_time"]       = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                        };
                        if (!postResult.Success)
                            notifyPayload["warning"] = $"ChromaKey material creation failed: {postResult.Error}";

                        GenerationNotifier.NotifyCompleted("generate_effect_video", taskId, capturedBackendTaskId, notifyPayload);
                    },
                    errorMsg =>
                    {
                        EffectVideoTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_effect_video", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["generator_id"] = generatorId, ["prompt"] = prompt ?? "" });
                    }
                );

                var pipeline = new GenerationPipeline(host, ConfigType.Video, GenerationRequestOrigin.Agent, sessionId);
                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateEffectVideoTool] Pipeline started, task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder: {placeholderPath}");

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Effect video generation started (image → video → ChromaKey material). " +
                        "END THIS RESPONSE TURN immediately. A <bg_task_done> notification will arrive (~2-3 min) " +
                        "containing video_path and material_path. " +
                        "POLLING IS STRICTLY FORBIDDEN — only call query_effect_video_status ONCE after 300s if no notification arrives." },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       generatorId },
                    { "prompt",             prompt ?? "" },
                    { "placeholder_path",   placeholderPath },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) },
                    { "estimated_wait_seconds", 180 },
                    { "notification_mode",  "bg_task_done" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateEffectVideoTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating effect video: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("query_effect_video_status",
            "Query the status of an effect video generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'video_path' (green-screen MP4) and 'material_path' (ChromaKey material). " +
            "Status values: 'generating', 'completed', 'failed', 'interrupted'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryEffectVideoStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' parameter is required" }
                    };
                }

                var task = EffectVideoTaskTracker.GetTask(taskId);
                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Task '{taskId}' not found." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success",       true },
                    { "task_id",       task.TaskId },
                    { "generator_id",  "effect_video_wf" },
                    { "status",        task.Status },
                    { "progress",      task.Progress },
                    { "prompt",        task.Prompt },
                    { "start_time",    task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.VideoPath))     result["video_path"]     = task.VideoPath;
                if (!string.IsNullOrEmpty(task.MaterialPath))  result["material_path"]  = task.MaterialPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage))  result["error"]          = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"]  = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating" && !string.IsNullOrEmpty(task.PlaceholderPath))
                    result["placeholder_path"] = task.PlaceholderPath;

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[QueryEffectVideoStatus] Query error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying task status: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("list_effect_video_tasks", "List all active and recent effect video generation tasks")]
        public static object ListEffectVideoTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = EffectVideoTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id",      task.TaskId },
                        { "generator_id", "effect_video_wf" },
                        { "status",       task.Status },
                        { "progress",     task.Progress },
                        { "prompt",       task.Prompt },
                        { "start_time",   task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.VideoPath))     taskData["video_path"]    = task.VideoPath;
                    if (!string.IsNullOrEmpty(task.MaterialPath))  taskData["material_path"] = task.MaterialPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage))  taskData["error"]         = task.ErrorMessage;
                    if (task.EndTime.HasValue) taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count",   taskList.Count },
                    { "tasks",   taskList }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[ListEffectVideoTasks] List error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error listing tasks: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

#if UNITY_EDITOR
        private static void SetupEffectVideoInScene(string videoPath, string materialPath)
        {
            try
            {
                var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(videoPath);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (clip == null || mat == null)
                    return;

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "EffectVideo_" + Path.GetFileNameWithoutExtension(videoPath);
                go.transform.position = new Vector3(0, 1, 0);

                int w = (int)clip.width;
                int h = (int)clip.height;
                if (w > 0 && h > 0)
                {
                    float aspect = (float)w / h;
                    go.transform.localScale = new Vector3(aspect, 1, 1);
                }

                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = mat;

                var player = go.AddComponent<VideoPlayer>();
                player.clip = clip;
                player.renderMode = VideoRenderMode.RenderTexture;
                player.isLooping = true;
                player.playOnAwake = true;
                player.audioOutputMode = VideoAudioOutputMode.None;

                // EffectVideoController manages RT lifecycle across domain reloads
                var controller = go.AddComponent<EffectVideoController>();
                controller.Initialize(mat);

                var cam = Camera.main;
                if (cam == null) cam = UnityObjectCompat.FindObjectOfType<Camera>();
                if (cam != null)
                {
                    cam.transform.position = new Vector3(0, 1, -3);
                    cam.transform.LookAt(go.transform);
                    cam.fieldOfView = 60f;
                }

                EditorUtility.SetDirty(go);
                Undo.RegisterCreatedObjectUndo(go, "Create Effect Video Player");
                Selection.activeGameObject = go;

                TJLog.Log($"[GenerateEffectVideoTool] Scene GameObject created: {go.name}");
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[GenerateEffectVideoTool] SetupEffectVideoInScene error: {e.Message}");
            }
        }

#endif

    }

#if UNITY_EDITOR
    /// <summary>
    /// Pipeline host for effect video generation.
    /// After video download, creates a ChromaKey material for real-time green-screen keying.
    /// </summary>
    internal class EffectVideoPipelineHost : IGenerationPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _sessionId;
        private readonly Action<string, string> _onVideoDownloaded;
        private readonly Action<string> _onFailed;

        public EffectVideoPipelineHost(
            string placeholderPath,
            string sessionId,
            Action<string, string> onVideoDownloaded,
            Action<string> onFailed)
        {
            _placeholderPath   = placeholderPath;
            _placeholderRef    = TJGeneratorsAssetReference.FromPath(placeholderPath);
            _sessionId         = sessionId ?? "";
            _onVideoDownloaded = onVideoDownloaded;
            _onFailed          = onFailed;
        }

        public TJGeneratorsAssetReference GetTargetAsset() => _placeholderRef;

        public void StartEditorCoroutine(IEnumerator coroutine)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(coroutine);
        }

        public void RefreshHistory() { }
        public void ShowPreviewModel(string assetPath) { }
        public void RefreshUserInfo() { }
        public void Repaint() { }
        public void StartGeneration(ModelGeneratorBase generator) { }

        public void ShowDialog(string title, string message)
        {
            ErrorDialogUtils.ShowErrorDialog(title, message, (errorMessage) => _onFailed?.Invoke(errorMessage), "GenerateEffectVideoTool");
        }

        public string GetTextureSavePath(ModelGeneratorBase generator) => null;
        public void OnTextureSaved(string savePath, ModelGeneratorBase generator) { }

        public string GetAudioSavePath(ModelGeneratorBase generator) => null;
        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }

        public string GetVideoSavePath(ModelGeneratorBase generator) => _placeholderPath;

        public void OnVideoSaved(string savePath, ModelGeneratorBase generator)
        {
            TJLog.Log($"[GenerateEffectVideoTool] Video saved: {savePath}");

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
            TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(savePath), _sessionId);

            string previewUrl = generator.CurrentPreviewUrl;
            _onVideoDownloaded?.Invoke(savePath, previewUrl);
        }
    }
#endif
}
