using System;
using System.Collections.Generic;
using Codely.Newtonsoft.Json;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityTcp.Editor.Helpers;

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// High-level Unity workflows (init_session, compile_and_validate, checkpoint).
    ///
    /// Important:
    /// - Workflows are advanced incrementally on each call to avoid blocking the editor thread.
    /// - Minimal state is persisted via SessionState so that the workflow can survive domain reloads.
    /// - Clients can poll by calling the same action again with op_id.
    /// </summary>
    public static class ManageWorkflow
    {
        private static readonly List<string> ValidActions = new List<string>
        {
            "init_session",
            "compile_and_validate",
            "checkpoint",
        };

        private const string KeyPrefix = "ManageWorkflow_";
        private static string CtxKey(string opId) => $"{KeyPrefix}Ctx_{opId}";

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required.");
            }

            if (!ValidActions.Contains(action))
            {
                string valid = string.Join(", ", ValidActions);
                return Response.Error($"Unknown action: '{action}'. Valid actions are: {valid}");
            }

            // Optional explicit op_id for polling
            string requestedOpId = @params["op_id"]?.ToString();
            string opId = null;
            JObject ctx = null;

            if (!string.IsNullOrEmpty(requestedOpId))
            {
                opId = requestedOpId;
                ctx = LoadContext(opId);
                if (ctx == null)
                {
                    return BuildErrorWithOpId(opId, "unknown_op_id", $"Unknown workflow op_id: {opId}");
                }

                // Guard: prevent action/op_id mismatches from accidentally advancing the wrong workflow.
                string ctxAction = ctx["action"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ctxAction) && !string.Equals(ctxAction, action, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildErrorWithOpId(
                        opId,
                        "action_mismatch",
                        $"Workflow op_id '{opId}' belongs to action '{ctxAction}', not '{action}'."
                    );
                }
            }

            if (ctx == null)
            {
                // Start a new workflow
                opId = Guid.NewGuid().ToString("N");
                ctx = new JObject
                {
                    ["op_id"] = opId,
                    ["action"] = action,
                    ["stage"] = "start",
                    ["createdAtUtcTicks"] = DateTime.UtcNow.Ticks,
                };

                // Per-action defaults (match TypeScript tool behavior)
                int timeoutSeconds =
                    @params["timeoutSeconds"]?.ToObject<int?>()
                    ?? (action == "init_session" ? 600 : 180);
                if (timeoutSeconds < 1) timeoutSeconds = 1;
                ctx["timeoutSeconds"] = timeoutSeconds;

                // Persist checkpoint parameters so polling doesn't require resending options
                if (action == "checkpoint")
                {
                    bool screenshot = @params["screenshot"]?.ToObject<bool?>() ?? true;
                    ctx["screenshot"] = screenshot;

                    string screenshotAction = @params["screenshotAction"]?.ToString();
                    if (string.IsNullOrEmpty(screenshotAction)) screenshotAction = "capture";
                    ctx["screenshotAction"] = screenshotAction;

                    ctx["screenshotPath"] = @params["screenshotPath"]?.ToString();
                    ctx["screenshotFilename"] = @params["screenshotFilename"]?.ToString();
                }

                SaveContext(opId, ctx);
            }

            // Overall workflow timeout guard
            if (IsTimedOut(ctx, out double elapsedSec))
            {
                DeleteContext(opId);
                return BuildErrorWithOpId(
                    opId,
                    "timeout",
                    $"unity_workflow '{action}' timed out after {Math.Round(elapsedSec)}s"
                );
            }

            try
            {
                switch (action)
                {
                    case "init_session":
                        return AdvanceInitSession(opId, ctx);
                    case "compile_and_validate":
                        return AdvanceCompileAndValidate(opId, ctx);
                    case "checkpoint":
                        return ExecuteCheckpoint(opId, ctx);
                    default:
                        return Response.Error($"Unknown action: '{action}'");
                }
            }
            catch (Exception e)
            {
                CodelyLogger.LogError($"[ManageWorkflow] Action '{action}' failed: {e}");
                DeleteContext(opId);
                return BuildErrorWithOpId(opId, "exception", e.Message);
            }
        }

        private static object AdvanceInitSession(string opId, JObject ctx)
        {
            string stage = ctx["stage"]?.ToString() ?? "start";
            var deltas = new List<object>();

            if (stage != "start" && stage != "waiting_idle")
            {
                DeleteContext(opId);
                return BuildErrorWithOpId(
                    opId,
                    "invalid_stage",
                    $"Invalid init_session stage: '{stage}'. Please retry init_session."
                );
            }

            if (stage == "start")
            {
                // 1) get_current_state
                var editorState = ManageEditor.HandleCommand(new JObject
                {
                    ["action"] = "get_current_state",
                });

                var editorJ = ToJObject(editorState);
                if (editorJ?["success"]?.ToObject<bool?>() == false)
                {
                    DeleteContext(opId);
                    return editorState;
                }

                // 2) clear console
                var clear = ReadConsole.HandleCommand(new JObject
                {
                    ["action"] = "clear",
                    ["scope"] = "all",
                });

                var clearJ = ToJObject(clear);
                if (clearJ?["success"]?.ToObject<bool?>() == false)
                {
                    DeleteContext(opId);
                    return clear;
                }

                string sinceToken =
                    clearJ?["data"]?["sinceToken"]?.ToString()
                    ?? StateComposer.GetCurrentConsoleToken();

                ctx["editor_state"] = editorJ;
                ctx["console_clear"] = clearJ;
                ctx["since_token"] = sinceToken;
                ctx["stage"] = "waiting_idle";
                SaveContext(opId, ctx);

                deltas.Add(ExtractStateDelta(editorJ));
                deltas.Add(ExtractStateDelta(clearJ));
            }

            // 3) wait_for_idle (poll)
            int timeoutSeconds = ctx["timeoutSeconds"]?.ToObject<int?>() ?? 600;
            var idle = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "wait_for_idle",
                ["timeoutSeconds"] = timeoutSeconds,
            });

            var idleJ = ToJObject(idle);
            if (idleJ?["success"]?.ToObject<bool?>() == false)
            {
                DeleteContext(opId);
                return idle;
            }

            ctx["idle"] = idleJ;
            SaveContext(opId, ctx);

            deltas.Add(ExtractStateDelta(idleJ));
            var mergedDelta = StateComposer.MergeStateDeltas(deltas.ToArray());

            var stateObj = ctx["editor_state"]?["state"];
            var data = new JObject
            {
                ["editor_state"] = ctx["editor_state"],
                ["console_clear"] = ctx["console_clear"],
                ["idle"] = idleJ,
            };

            string status = idleJ?["status"]?.ToString();
            if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                double poll = idleJ?["poll_interval"]?.ToObject<double?>() ?? 1.0;
                return BuildPending(
                    opId,
                    "Unity workflow init_session pending (waiting for idle)...",
                    poll,
                    data,
                    stateObj,
                    mergedDelta
                );
            }

            DeleteContext(opId);
            return BuildComplete(
                opId,
                "Unity workflow init_session completed",
                data,
                stateObj,
                mergedDelta
            );
        }

        private static object AdvanceCompileAndValidate(string opId, JObject ctx)
        {
            // Server-side guard: do not compile while playing/paused
            if (EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                DeleteContext(opId);
                return Response.Error(
                    "compile_blocked_in_play_mode",
                    new
                    {
                        code = "compile_blocked_in_play_mode",
                        message = "Compilation is not allowed while the editor is in Play/Paused mode. Stop Play mode (unity_editor.stop) before requesting compilation.",
                        playMode = EditorApplication.isPlaying ? "playing" : "paused",
                    }
                );
            }

            string stage = ctx["stage"]?.ToString() ?? "start";
            var deltas = new List<object>();

            // "waiting_retry" is the stage after an automatic cache-clear retry has been kicked off.
            if (stage != "start" && stage != "waiting_compile" && stage != "waiting_retry")
            {
                DeleteContext(opId);
                return BuildErrorWithOpId(
                    opId,
                    "invalid_stage",
                    $"Invalid compile_and_validate stage: '{stage}'. Please retry compile_and_validate."
                );
            }

            if (stage == "start")
            {
                // Start compilation pipeline: clear console → request compile → return op_id + since_token
                var start = ManageEditor.HandleCommand(new JObject
                {
                    ["action"] = "start_compilation_pipeline",
                });

                var startJ = ToJObject(start);
                if (startJ?["success"]?.ToObject<bool?>() == false)
                {
                    DeleteContext(opId);
                    return start;
                }

                string compileOpId =
                    startJ?["op_id"]?.ToString() ?? startJ?["opId"]?.ToString();
                string sinceToken = startJ?["since_token"]?.ToString();

                if (string.IsNullOrEmpty(compileOpId))
                {
                    DeleteContext(opId);
                    return BuildErrorWithOpId(opId, "missing_op_id", "Compilation pipeline did not return op_id");
                }

                ctx["start"] = startJ;
                ctx["compile_op_id"] = compileOpId;
                if (!string.IsNullOrEmpty(sinceToken)) ctx["since_token"] = sinceToken;
                ctx["stage"] = "waiting_compile";
                SaveContext(opId, ctx);

                deltas.Add(ExtractStateDelta(startJ));
            }

            // Select the active compile op_id / since_token depending on whether we are in a retry.
            bool isRetry = string.Equals(stage, "waiting_retry", StringComparison.OrdinalIgnoreCase);
            string compileOpId2 = isRetry
                ? ctx["retry_compile_op_id"]?.ToString()
                : ctx["compile_op_id"]?.ToString();
            string sinceToken2 = isRetry
                ? ctx["retry_since_token"]?.ToString()
                : ctx["since_token"]?.ToString();
            int timeoutSeconds = ctx["timeoutSeconds"]?.ToObject<int?>() ?? 180;

            if (string.IsNullOrEmpty(compileOpId2))
            {
                DeleteContext(opId);
                return BuildErrorWithOpId(
                    opId,
                    "missing_op_id",
                    "Workflow context missing compile_op_id. Please retry compile_and_validate."
                );
            }

            if (string.IsNullOrEmpty(sinceToken2))
            {
                sinceToken2 = StateComposer.GetCurrentConsoleToken();
                if (!string.IsNullOrEmpty(sinceToken2))
                {
                    if (isRetry) ctx["retry_since_token"] = sinceToken2;
                    else ctx["since_token"] = sinceToken2;
                    SaveContext(opId, ctx);
                }
            }

            // Wait for compile (poll)
            var wait = ManageEditor.HandleCommand(new JObject
            {
                ["action"] = "wait_for_compile",
                ["op_id"] = compileOpId2,
                ["timeoutSeconds"] = timeoutSeconds,
                ["since_token"] = sinceToken2,
            });

            var waitJ = ToJObject(wait);
            ctx["wait"] = waitJ;
            SaveContext(opId, ctx);

            deltas.Add(ExtractStateDelta(waitJ));
            var waitStatus = waitJ?["status"]?.ToString();
            bool waitFailed = waitJ?["success"]?.ToObject<bool?>() == false;

            if (string.Equals(waitStatus, "pending", StringComparison.OrdinalIgnoreCase) && !waitFailed)
            {
                double poll = waitJ?["poll_interval"]?.ToObject<double?>() ?? 1.0;
                var pendingData = new JObject
                {
                    ["stage"] = isRetry ? "waiting_retry" : "waiting_compile",
                    ["start"] = ctx["start"],
                    ["wait"] = waitJ,
                    ["op_id"] = compileOpId2,
                    ["since_token"] = sinceToken2,
                };

                var mergedPendingDelta = StateComposer.MergeStateDeltas(deltas.ToArray());
                return BuildPending(
                    opId,
                    "Unity workflow compile_and_validate pending (waiting for compile)...",
                    poll,
                    pendingData,
                    null,
                    mergedPendingDelta
                );
            }

            // Read console since token (even if wait failed)
            var consoleRead = ReadConsole.HandleCommand(new JObject
            {
                ["action"] = "get",
                ["since_token"] = sinceToken2,
            });

            var consoleJ = ToJObject(consoleRead);
            if (consoleJ?["success"]?.ToObject<bool?>() == false)
            {
                DeleteContext(opId);
                return consoleRead;
            }

            ctx["console"] = consoleJ;
            SaveContext(opId, ctx);

            // Count errors/warnings and collect structured compile_errors details.
            var entriesToken = consoleJ?["data"]?["entries"];
            var entriesArray = entriesToken as JArray;
            int errCount = 0;
            int warnCount = 0;
            var compileErrors = new JArray();

            if (entriesArray != null)
            {
                foreach (var entry in entriesArray)
                {
                    var t = entry?["type"]?.ToString();
                    if (string.IsNullOrEmpty(t)) continue;
                    var lower = t.ToLowerInvariant();
                    if (lower == "error" || lower == "exception")
                    {
                        errCount++;
                        compileErrors.Add(new JObject
                        {
                            ["message"] = entry["message"],
                            ["file"]    = entry["file"],
                            ["line"]    = entry["line"],
                        });
                    }
                    else if (lower == "warning")
                    {
                        warnCount++;
                    }
                }
            }

            bool hasErrors = errCount > 0;
            bool hasWarnings = warnCount > 0;

            // Auto-retry: when wait_for_compile itself failed but there are no console error
            // entries to explain why, the most likely cause is a stale Library/ScriptAssemblies
            // cache.  Kick off a clean recompile exactly once (do not retry the retry).
            if (waitFailed && !hasErrors && !isRetry)
            {
                CodelyLogger.Log("[ManageWorkflow] compile_and_validate: wait_for_compile failed with no console errors – clearing script assemblies cache and retrying.");

                var retryStart = ManageEditor.HandleCommand(new JObject
                {
                    ["action"]     = "start_compilation_pipeline",
                    ["clearCache"] = true,
                });
                var retryStartJ = ToJObject(retryStart);

                if (retryStartJ?["success"]?.ToObject<bool?>() != false)
                {
                    string retryOpId =
                        retryStartJ?["op_id"]?.ToString() ?? retryStartJ?["opId"]?.ToString();
                    string retrySinceToken = retryStartJ?["since_token"]?.ToString();

                    if (!string.IsNullOrEmpty(retryOpId))
                    {
                        bool cacheClearedFlag = retryStartJ?["cache_cleared"]?.ToObject<bool?>() ?? false;

                        ctx["retry_compile_op_id"] = retryOpId;
                        ctx["retry_since_token"]   = retrySinceToken;
                        ctx["cache_cleared"]        = cacheClearedFlag;
                        ctx["stage"]               = "waiting_retry";
                        SaveContext(opId, ctx);

                        var retryPendingData = new JObject
                        {
                            ["stage"]          = "waiting_retry",
                            ["cache_cleared"]  = cacheClearedFlag,
                            ["original_wait"]  = waitJ,
                        };
                        var retryDelta = StateComposer.MergeStateDeltas(deltas.ToArray());
                        return BuildPending(
                            opId,
                            "Unity workflow compile_and_validate: retrying after cache clear...",
                            2.0,
                            retryPendingData,
                            null,
                            retryDelta
                        );
                    }
                }

                // Retry setup failed – fall through to the normal error response below.
                CodelyLogger.LogWarning("[ManageWorkflow] compile_and_validate: retry compilation start failed; reporting original failure.");
            }

            bool success = !waitFailed && !hasErrors;

            var data = new JObject
            {
                ["start"]               = ctx["start"],
                ["wait"]                = waitJ,
                ["wait_failed"]         = waitFailed,
                ["console"]             = consoleJ,
                ["since_token"]         = sinceToken2,
                ["op_id"]               = compileOpId2,
                ["hasErrors"]           = hasErrors,
                ["hasWarnings"]         = hasWarnings,
                ["consoleErrorCount"]   = errCount,
                ["consoleWarningCount"] = warnCount,
                // Inline error details so callers do not need a separate console query.
                ["compile_errors"]      = compileErrors,
            };

            // Surface cache-clear metadata when this result came from a retry.
            if (isRetry)
            {
                data["was_retry"]      = true;
                data["cache_cleared"]  = ctx["cache_cleared"];
            }

            var mergedDelta = StateComposer.MergeStateDeltas(deltas.ToArray());
            DeleteContext(opId);

            if (success)
            {
                return BuildComplete(
                    opId,
                    "Unity workflow compile_and_validate completed",
                    data,
                    null,
                    mergedDelta
                );
            }

            // Build a descriptive error message that includes the first error's location so the
            // caller can act without needing to inspect data.compile_errors separately.
            string errorMessage;
            if (hasErrors && compileErrors.Count > 0)
            {
                var first = compileErrors[0] as JObject;
                string firstMsg  = first?["message"]?.ToString() ?? "";
                string firstFile = first?["file"]?.ToString() ?? "";
                int    firstLine = first?["line"]?.ToObject<int?>() ?? 0;
                string location  = !string.IsNullOrEmpty(firstFile)
                    ? $" ({firstFile}:{firstLine})"
                    : string.Empty;

                errorMessage = errCount == 1
                    ? $"Unity workflow compile_and_validate failed: 1 error{location}: {firstMsg}"
                    : $"Unity workflow compile_and_validate failed: {errCount} errors. First error{location}: {firstMsg}";
            }
            else if (waitFailed)
            {
                errorMessage = "Unity workflow compile_and_validate failed (wait_for_compile failed, no console errors found)";
            }
            else
            {
                errorMessage = "Unity workflow compile_and_validate failed (console has errors)";
            }

            return new Dictionary<string, object>
            {
                ["success"]      = false,
                ["status"]       = "complete",
                ["op_id"]        = opId,
                ["code"]         = waitFailed ? "compile_wait_failed" : "compilation_errors",
                ["error"]        = errorMessage,
                ["message"]      = errorMessage,
                ["data"]         = data,
                ["state_delta"]  = mergedDelta,
            };
        }

        private static object ExecuteCheckpoint(string opId, JObject ctx)
        {
            // checkpoint should finish in one call; no multi-step polling required.
            bool screenshot = ctx["screenshot"]?.ToObject<bool?>() ?? true;
            string screenshotAction = ctx["screenshotAction"]?.ToString() ?? "capture";
            string screenshotPath = ctx["screenshotPath"]?.ToString();
            string screenshotFilename = ctx["screenshotFilename"]?.ToString();

            var deltas = new List<object>();

            var save = ManageScene.HandleCommand(new JObject
            {
                ["action"] = "ensure_scene_saved",
            });

            var saveJ = ToJObject(save);
            if (saveJ?["success"]?.ToObject<bool?>() == false)
            {
                DeleteContext(opId);
                return save;
            }

            deltas.Add(ExtractStateDelta(saveJ));

            JObject screenshotJ = null;
            if (screenshot)
            {
                var shotParams = new JObject
                {
                    ["action"] = screenshotAction,
                };
                if (!string.IsNullOrEmpty(screenshotPath)) shotParams["path"] = screenshotPath;
                if (!string.IsNullOrEmpty(screenshotFilename)) shotParams["filename"] = screenshotFilename;

                var shot = ManageScreenshot.HandleCommand(shotParams);
                screenshotJ = ToJObject(shot);
                if (screenshotJ?["success"]?.ToObject<bool?>() == false)
                {
                    DeleteContext(opId);
                    return shot;
                }

                deltas.Add(ExtractStateDelta(screenshotJ));
            }

            var mergedDelta = StateComposer.MergeStateDeltas(deltas.ToArray());
            var data = new JObject
            {
                ["scene_saved"] = saveJ,
                ["screenshot"] = screenshot ? screenshotJ : null,
            };

            DeleteContext(opId);
            return BuildComplete(
                opId,
                "Unity workflow checkpoint completed",
                data,
                null,
                mergedDelta
            );
        }

        private static bool IsTimedOut(JObject ctx, out double elapsedSeconds)
        {
            elapsedSeconds = 0;
            try
            {
                long createdTicks = ctx["createdAtUtcTicks"]?.ToObject<long?>() ?? 0;
                if (createdTicks <= 0) return false;

                int timeoutSeconds = ctx["timeoutSeconds"]?.ToObject<int?>() ?? 0;
                if (timeoutSeconds <= 0) return false;

                elapsedSeconds =
                    (DateTime.UtcNow.Ticks - createdTicks) / (double)TimeSpan.TicksPerSecond;
                return elapsedSeconds > timeoutSeconds;
            }
            catch
            {
                return false;
            }
        }

        private static JObject LoadContext(string opId)
        {
            string json = GetSessionString(CtxKey(opId));
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveContext(string opId, JObject ctx)
        {
            if (string.IsNullOrEmpty(opId) || ctx == null) return;
            try
            {
                SessionState.SetString(CtxKey(opId), ctx.ToString(Formatting.None));
            }
            catch { }
        }

        private static void DeleteContext(string opId)
        {
            if (string.IsNullOrEmpty(opId)) return;
            try
            {
                // Use empty string instead of EraseString to maximize Unity version compatibility
                SessionState.SetString(CtxKey(opId), string.Empty);
            }
            catch { }
        }

        private static string GetSessionString(string key)
        {
            try
            {
                var v = SessionState.GetString(key, null);
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch
            {
                return null;
            }
        }

        private static JObject ToJObject(object obj)
        {
            if (obj == null) return null;
            try
            {
                if (obj is JObject j) return j;
                return JObject.FromObject(obj);
            }
            catch
            {
                return null;
            }
        }

        private static object ExtractStateDelta(JObject result)
        {
            if (result == null) return null;
            return result["state_delta"];
        }

        private static object BuildPending(
            string opId,
            string message,
            double pollInterval,
            JObject data,
            JToken state,
            object stateDelta
        )
        {
            var resp = new Dictionary<string, object>
            {
                ["success"] = true,
                ["status"] = "pending",
                ["op_id"] = opId,
                ["poll_interval"] = pollInterval,
                ["message"] = message,
                ["data"] = data,
            };

            if (state != null) resp["state"] = state;

            // Also surface operations delta for this workflow
            try
            {
                var opDelta = StateComposer.CreateOperationsDelta(
                    new object[]
                    {
                        new { id = opId, type = "workflow", progress = 0.0f, message = message },
                    }
                );
                resp["state_delta"] =
                    stateDelta != null
                        ? StateComposer.MergeStateDeltas(opDelta, stateDelta)
                        : opDelta;
            }
            catch
            {
                if (stateDelta != null) resp["state_delta"] = stateDelta;
            }

            return resp;
        }

        private static object BuildComplete(
            string opId,
            string message,
            JObject data,
            JToken state,
            object stateDelta
        )
        {
            // Workflows already return the final state/data needed by the agent.
            // Do not leave a later observation-invalidated reminder pending.
            StateComposer.PublishDirtyStateIfNeeded(false);

            var resp = new Dictionary<string, object>
            {
                ["success"] = true,
                ["status"] = "complete",
                ["op_id"] = opId,
                ["message"] = message,
                ["data"] = data,
            };

            if (state != null) resp["state"] = state;
            if (stateDelta != null) resp["state_delta"] = stateDelta;

            return resp;
        }

        private static object BuildErrorWithOpId(string opId, string code, string message)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["status"] = "error",
                ["op_id"] = opId,
                ["code"] = code,
                ["error"] = message,
                ["message"] = message,
            };
        }
    }
}

