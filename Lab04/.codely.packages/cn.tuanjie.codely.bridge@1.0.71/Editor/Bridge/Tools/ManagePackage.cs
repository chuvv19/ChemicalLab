using System;
using System.Collections.Generic;
using System.Linq;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityTcp.Editor.Helpers;

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// [EXPERIMENTAL] Handles Unity Package Manager (UPM) operations.
    /// Supports installing, removing, and querying packages.
    /// Compatible with Unity 2022.3 LTS.
    /// </summary>
    public static class ManagePackage
    {
        private static readonly Dictionary<string, Request> _activeRequests = new Dictionary<string, Request>();
        private static readonly Dictionary<string, EditorApplication.CallbackFunction> _updateCallbacks = new Dictionary<string, EditorApplication.CallbackFunction>();
        private static readonly object _requestLock = new object();

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString().ToLower();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required.");
            }

            try
            {
                switch (action)
                {
                    case "install_package":
                        return InstallPackage(@params);
                    case "remove_package":
                        return RemovePackage(@params);
                    case "wait_for_upm":
                        return WaitForUpm(@params);
                    case "list_packages":
                        return ListPackages();
                    default:
                        return Response.Error(
                            $"Unknown action: '{action}'. Valid actions: install_package, remove_package, wait_for_upm, list_packages."
                        );
                }
            }
            catch (Exception e)
            {
                CodelyLogger.LogError($"[ManagePackage] Action '{action}' failed: {e}");
                return Response.Error($"[EXPERIMENTAL] Package operation failed: {e.Message}");
            }
        }

        private static object InstallPackage(JObject @params)
        {
            try
            {
                string idOrUrl = @params["id_or_url"]?.ToString();
                if (string.IsNullOrEmpty(idOrUrl))
                    return Response.Error("'id_or_url' parameter required for install_package.");

                // Handle optional version parameter
                string version = @params["version"]?.ToString();
                
                // Append version to package identifier if provided and not already in format
                // e.g., "com.unity.package" + "1.2.3" -> "com.unity.package@1.2.3"
                if (!string.IsNullOrEmpty(version) && 
                    !idOrUrl.Contains("@") && 
                    !idOrUrl.StartsWith("http"))
                {
                    idOrUrl = $"{idOrUrl}@{version}";
                }

                // If this tool call is installing TextMesh Pro, mark its essential
                // resources for silent import BEFORE the request starts. The flag
                // lives in SessionState, so it survives the domain reload UPM
                // triggers and is picked up by TmpEssentialsAutoImporter while that
                // reload loads the TMP assembly -- ahead of any validate step. This
                // prevents the interactive "TMP Importer" window and the missing
                // default-font NullReferenceException.
                // Use Contains, not StartsWith: id_or_url may be a git URL, a
                // file:/local tarball, or a folder path that embeds the package
                // name rather than leading with it.
                if (idOrUrl.Contains("com.unity.textmeshpro"))
                {
                    TmpEssentialsAutoImporter.ScheduleImport();
                }

                // Create job
                var job = AsyncOperationTracker.CreateJob(
                    AsyncOperationTracker.JobType.UpmPackage,
                    $"Installing package: {idOrUrl}"
                );

                // Start UPM request
                AddRequest addRequest = Client.Add(idOrUrl);
                lock (_requestLock)
                {
                    _activeRequests[job.OpId] = addRequest;
                }

                // Create and store callback delegate for proper unsubscription
                EditorApplication.CallbackFunction callback = () => CheckUpmRequest(job.OpId);
                lock (_requestLock)
                {
                    _updateCallbacks[job.OpId] = callback;
                }
                EditorApplication.update += callback;

                // Return standardized pending response
                var response = AsyncOperationTracker.CreatePendingResponse(job) as Dictionary<string, object>;
                response["poll_interval"] = 2.0; // UPM needs longer poll interval
                response["message"] = $"[EXPERIMENTAL] Package installation started: {idOrUrl}";
                response["data"] = new { package = idOrUrl, type = "install" };
                return response;
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to start package installation: {e.Message}");
            }
        }

        private static object RemovePackage(JObject @params)
        {
            try
            {
                string packageName = @params["package_name"]?.ToString();
                if (string.IsNullOrEmpty(packageName))
                    return Response.Error("'package_name' parameter required for remove_package.");

                var job = AsyncOperationTracker.CreateJob(
                    AsyncOperationTracker.JobType.UpmPackage,
                    $"Removing package: {packageName}"
                );

                RemoveRequest removeRequest = Client.Remove(packageName);
                lock (_requestLock)
                {
                    _activeRequests[job.OpId] = removeRequest;
                }

                // Create and store callback delegate for proper unsubscription
                EditorApplication.CallbackFunction callback = () => CheckUpmRequest(job.OpId);
                lock (_requestLock)
                {
                    _updateCallbacks[job.OpId] = callback;
                }
                EditorApplication.update += callback;

                // Return standardized pending response
                var response = AsyncOperationTracker.CreatePendingResponse(job) as Dictionary<string, object>;
                response["poll_interval"] = 2.0; // UPM needs longer poll interval
                response["message"] = $"[EXPERIMENTAL] Package removal started: {packageName}";
                response["data"] = new { package = packageName, type = "remove" };
                return response;
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to start package removal: {e.Message}");
            }
        }

        private static void CheckUpmRequest(string opId)
        {
            Request request;
            EditorApplication.CallbackFunction callback;
            lock (_requestLock)
            {
                if (!_activeRequests.TryGetValue(opId, out request))
                {
                    return;
                }
                _updateCallbacks.TryGetValue(opId, out callback);
            }

            if (request.IsCompleted)
            {
                // Properly unsubscribe using stored delegate
                if (callback != null)
                {
                    EditorApplication.update -= callback;
                }
                
                lock (_requestLock)
                {
                    _activeRequests.Remove(opId);
                    _updateCallbacks.Remove(opId);
                }

                if (request.Status == StatusCode.Success)
                {
                    AsyncOperationTracker.CompleteJob(opId, "Package operation completed successfully");

                    // Backstop: if the install did NOT trigger a domain reload (e.g.
                    // TMP was already compiled), the pre-install flag is still pending
                    // here -- re-attempt the silent import now. No-op once essentials
                    // exist. (The pre-Client.Add hook in InstallPackage handles the
                    // normal reload case before validate runs.)
                    if (request is AddRequest addResult &&
                        addResult.Result != null &&
                        addResult.Result.name == "com.unity.textmeshpro")
                    {
                        TmpEssentialsAutoImporter.ScheduleImport();
                    }

                    StateComposer.MarkDirty();
                }
                else
                {
                    AsyncOperationTracker.FailJob(opId, $"Package operation failed: {request.Error?.message}");
                }
            }
        }

        private static object WaitForUpm(JObject @params)
        {
            try
            {
                string opId = @params["op_id"]?.ToString();
                int timeoutSeconds = @params["timeoutSeconds"]?.ToObject<int?>() ?? 300;

                if (string.IsNullOrEmpty(opId))
                    return Response.Error("'op_id' parameter required for wait_for_upm.");

                var job = AsyncOperationTracker.GetJob(opId);
                if (job == null)
                    return Response.Error($"Operation {opId} not found.");

                if (job.Type != AsyncOperationTracker.JobType.UpmPackage)
                    return Response.Error($"Operation {opId} is not a UPM operation.");

                if (AsyncOperationTracker.IsJobTimedOut(opId, timeoutSeconds))
                {
                    AsyncOperationTracker.FailJob(opId, $"UPM operation timed out after {timeoutSeconds} seconds");
                    return AsyncOperationTracker.CreateErrorResponse(job);
                }

                switch (job.Status)
                {
                    case AsyncOperationTracker.JobStatus.Complete:
                        return AsyncOperationTracker.CreateCompleteResponse(job);
                    case AsyncOperationTracker.JobStatus.Error:
                        return AsyncOperationTracker.CreateErrorResponse(job);
                    case AsyncOperationTracker.JobStatus.Pending:
                        return AsyncOperationTracker.CreatePendingResponse(job);
                    default:
                        return Response.Error($"Unknown job status: {job.Status}");
                }
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to wait for UPM: {e.Message}");
            }
        }

        private static object ListPackages()
        {
            try
            {
                ListRequest listRequest = Client.List(true, false);
                
                // Wait for request to complete (synchronous for simplicity)
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(100);
                }

                if (listRequest.Status == StatusCode.Success)
                {
                    var packages = listRequest.Result.Select(p => new
                    {
                        name = p.name,
                        version = p.version,
                        displayName = p.displayName,
                        description = p.description,
                        source = p.source.ToString()
                    }).ToList();

                    return Response.Success(
                        $"[EXPERIMENTAL] Retrieved {packages.Count} packages.",
                        packages
                    );
                }
                else
                {
                    return Response.Error($"[EXPERIMENTAL] Failed to list packages: {listRequest.Error?.message}");
                }
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to list packages: {e.Message}");
            }
        }
    }
}

