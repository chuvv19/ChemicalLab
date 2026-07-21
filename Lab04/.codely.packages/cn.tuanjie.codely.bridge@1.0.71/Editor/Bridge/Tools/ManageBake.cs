using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityTcp.Editor.Helpers;

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// [EXPERIMENTAL] Handles baking operations (NavMesh, Lighting, etc.).
    /// Compatible with Unity 2022.3 LTS.
    /// </summary>
    public static class ManageBake
    {
        // Store callbacks for proper unsubscription
        private static readonly Dictionary<string, EditorApplication.CallbackFunction> _updateCallbacks = new Dictionary<string, EditorApplication.CallbackFunction>();
        private static readonly object _callbackLock = new object();
        // Store async operations for NavMesh baking
        private static readonly Dictionary<string, List<AsyncOperation>> _navMeshBakeOperations = new Dictionary<string, List<AsyncOperation>>();

        // Runtime check for AI Navigation package availability
        private static bool? _hasAINavigation = null;
        private static Type _navMeshSurfaceType = null;
        private static MethodInfo _buildNavMeshMethod = null;
        private static MethodInfo _updateNavMeshMethod = null;
        private static PropertyInfo _activeSurfacesProperty = null;
        private static Type _navMeshType = null;
        private static MethodInfo _calculateTriangulationMethod = null;
        private static MethodInfo _removeAllNavMeshDataMethod = null;

        /// <summary>
        /// Reset the AI Navigation package cache. Call this after installing the package
        /// to force re-checking for available types.
        /// </summary>
        private static void ResetAINavigationCache()
        {
            _hasAINavigation = null;
            _navMeshSurfaceType = null;
            _buildNavMeshMethod = null;
            _updateNavMeshMethod = null;
            _activeSurfacesProperty = null;
            _navMeshType = null;
            _calculateTriangulationMethod = null;
            _removeAllNavMeshDataMethod = null;
        }

        private static bool HasAINavigation()
        {
            if (_hasAINavigation.HasValue)
                return _hasAINavigation.Value;

            try
            {
                // First, check if the package is installed via PackageManager
                bool packageInstalled = false;
                try
                {
#if UNITY_2021_2_OR_NEWER
                    // Use GetAllRegisteredPackages for Unity 2021.2+
                    var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                    packageInstalled = packages.Any(p => p.name == "com.unity.ai.navigation");
#else
                    // Fallback for older Unity versions
                    var listRequest = Client.List(true, false);
                    while (!listRequest.IsCompleted)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                    if (listRequest.Status == StatusCode.Success)
                    {
                        packageInstalled = listRequest.Result.Any(p => p.name == "com.unity.ai.navigation");
                    }
#endif
                }
                catch (Exception ex)
                {
                    CodelyLogger.LogWarning($"[ManageBake] Error checking package installation: {ex.Message}");
                    // Continue with type checking as fallback
                }

                // Try to find NavMeshSurface type (Unity.AI.Navigation namespace from com.unity.ai.navigation package)
                // Try multiple methods to find the type
                _navMeshSurfaceType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
                
                if (_navMeshSurfaceType == null)
                {
                    // Try with full assembly qualified name variations
                    _navMeshSurfaceType = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
                }
                
                if (_navMeshSurfaceType == null)
                {
                    // Fallback: search in loaded assemblies by name first
                    System.Reflection.Assembly targetAssembly = null;
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var assemblyName = assembly.GetName().Name;
                        if (assemblyName == "Unity.AI.Navigation" || assemblyName.Contains("Unity.AI.Navigation"))
                        {
                            targetAssembly = assembly;
                            break;
                        }
                    }
                    
                    if (targetAssembly != null)
                    {
                        _navMeshSurfaceType = targetAssembly.GetType("Unity.AI.Navigation.NavMeshSurface");
                    }
                }
                
                if (_navMeshSurfaceType == null)
                {
                    // Last resort: search all assemblies
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _navMeshSurfaceType = assembly.GetType("Unity.AI.Navigation.NavMeshSurface");
                        if (_navMeshSurfaceType != null) break;
                    }
                }

                if (_navMeshSurfaceType != null)
                {
                    _buildNavMeshMethod = _navMeshSurfaceType.GetMethod("BuildNavMesh", BindingFlags.Public | BindingFlags.Instance);
                    _updateNavMeshMethod = _navMeshSurfaceType.GetMethod("UpdateNavMesh", BindingFlags.Public | BindingFlags.Instance);
                    _activeSurfacesProperty = _navMeshSurfaceType.GetProperty("activeSurfaces", BindingFlags.Public | BindingFlags.Static);
                }

                // Try to find NavMesh type (UnityEngine.AI namespace - still used by the package)
                _navMeshType = Type.GetType("UnityEngine.AI.NavMesh, UnityEngine.AIModule");
                if (_navMeshType == null)
                {
                    foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _navMeshType = assembly.GetType("UnityEngine.AI.NavMesh");
                        if (_navMeshType != null) break;
                    }
                }

                if (_navMeshType != null)
                {
                    _calculateTriangulationMethod = _navMeshType.GetMethod("CalculateTriangulation", BindingFlags.Public | BindingFlags.Static);
                    _removeAllNavMeshDataMethod = _navMeshType.GetMethod("RemoveAllNavMeshData", BindingFlags.Public | BindingFlags.Static);
                }

                // Check both package installation and required types/methods
                bool hasRequiredTypes = _navMeshSurfaceType != null && _buildNavMeshMethod != null && _navMeshType != null;

                // If package is installed but types are missing, check compilation status
                if (packageInstalled && !hasRequiredTypes)
                {
                    bool isCompiling = EditorApplication.isCompiling;
                    string compilationStatus = isCompiling ? "compiling" : "idle";
                    
                    // Collect diagnostic information
                    var loadedAssemblies = System.AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => a.GetName().Name.Contains("AI") || a.GetName().Name.Contains("Navigation"))
                        .Select(a => a.GetName().Name)
                        .ToList();
                    
                    string diagnosticInfo = "";
                    if (loadedAssemblies.Count > 0)
                    {
                        diagnosticInfo = $" Found related assemblies: {string.Join(", ", loadedAssemblies)}.";
                    }
                    else
                    {
                        diagnosticInfo = " No AI/Navigation assemblies found in loaded assemblies.";
                    }
                    
                    string typeStatus = "";
                    if (_navMeshSurfaceType == null)
                    {
                        typeStatus += " NavMeshSurface type not found.";
                    }
                    else
                    {
                        typeStatus += $" NavMeshSurface found, but methods missing: BuildNavMesh={_buildNavMeshMethod != null}, UpdateNavMesh={_updateNavMeshMethod != null}, activeSurfaces={_activeSurfacesProperty != null}.";
                    }
                    
                    if (_navMeshType == null)
                    {
                        typeStatus += " NavMesh type not found.";
                    }
                    
                    CodelyLogger.LogWarning(
                        $"[ManageBake] com.unity.ai.navigation package is installed but required types/methods are not available. " +
                        $"Editor is currently {compilationStatus}.{diagnosticInfo}{typeStatus} " +
                        (isCompiling 
                            ? "Please wait for compilation to complete, then call 'unity_editor { \"action\": \"wait_for_idle\" }' before retrying."
                            : "The package may need to be reloaded. Try restarting Unity or wait a moment and retry.")
                    );
                }

                // Package installation check is primary, but we also need the types to be available
                // If package is installed but types are missing and we're not compiling, return false
                // If we're compiling, also return false (types won't be available until compilation completes)
                _hasAINavigation = packageInstalled && hasRequiredTypes && !EditorApplication.isCompiling;
            }
            catch (Exception ex)
            {
                CodelyLogger.LogWarning($"[ManageBake] Error checking for AI Navigation package: {ex.Message}");
                _hasAINavigation = false;
            }

            return _hasAINavigation.Value;
        }

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
                    case "bake_navmesh":
                        return BakeNavMesh(@params);
                    case "bake_lighting":
                        return BakeLighting(@params);
                    case "wait_for_bake":
                        return WaitForBake(@params);
                    case "clear_navmesh":
                        return ClearNavMesh();
                    case "clear_baked_data":
                        return ClearBakedData();
                    default:
                        return Response.Error(
                            $"Unknown action: '{action}'. Valid actions: bake_navmesh, bake_lighting, wait_for_bake, clear_navmesh, clear_baked_data."
                        );
                }
            }
            catch (Exception e)
            {
                CodelyLogger.LogError($"[ManageBake] Action '{action}' failed: {e}");
                return Response.Error($"[EXPERIMENTAL] Bake operation failed: {e.Message}");
            }
        }

        private static object BakeNavMesh(JObject @params)
        {
            try
            {
                // Reset cache and re-check if first check fails (in case package was just installed)
                if (!HasAINavigation())
                {
                    ResetAINavigationCache();
                    if (!HasAINavigation())
                    {
                        // Check if package is installed but types are not available
                        bool packageInstalled = false;
                        try
                        {
#if UNITY_2021_2_OR_NEWER
                            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                            packageInstalled = packages.Any(p => p.name == "com.unity.ai.navigation");
#else
                            var listRequest = Client.List(true, false);
                            while (!listRequest.IsCompleted)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                            if (listRequest.Status == StatusCode.Success)
                            {
                                packageInstalled = listRequest.Result.Any(p => p.name == "com.unity.ai.navigation");
                            }
#endif
                        }
                        catch { }
                        
                        bool isCompiling = EditorApplication.isCompiling;
                        
                        string errorMessage;
                        if (packageInstalled && isCompiling)
                        {
                            errorMessage = 
                                "[EXPERIMENTAL] NavMesh baking requires AI Navigation package types to be loaded. " +
                                "The package is installed but Unity is currently compiling. " +
                                "Please wait for compilation to complete by calling 'unity_editor { \"action\": \"wait_for_idle\" }', then retry.";
                        }
                        else if (packageInstalled)
                        {
                            errorMessage = 
                                "[EXPERIMENTAL] NavMesh baking requires AI Navigation package types to be loaded. " +
                                "The package 'com.unity.ai.navigation' is installed but required types are not available. " +
                                "This may happen if: (1) compilation is in progress, (2) the package needs to be reloaded, or (3) Unity needs to be restarted. " +
                                "Try: (1) Call 'unity_editor { \"action\": \"wait_for_idle\" }' to ensure compilation is complete, " +
                                "(2) Wait a few seconds and retry, or (3) Restart Unity.";
                        }
                        else
                        {
                            errorMessage = 
                                "[EXPERIMENTAL] NavMesh baking requires AI Navigation package. " +
                                "Install 'com.unity.ai.navigation' via Package Manager using: " +
                                "'unity_package { \"action\": \"install_package\", \"id_or_url\": \"com.unity.ai.navigation\" }', " +
                                "then wait for installation and compilation to complete using 'unity_editor { \"action\": \"wait_for_idle\" }'.";
                        }
                        
                        return Response.Error(errorMessage);
                    }
                }

                var writeCheck = WriteGuard.CheckWriteAllowed("bake_navmesh");
                if (writeCheck != null) return writeCheck;

                var job = AsyncOperationTracker.CreateJob(
                    AsyncOperationTracker.JobType.NavMeshBake,
                    "Baking NavMesh..."
                );

                // Get all active NavMeshSurface components in the scene
                List<object> surfaces = new List<object>();
                if (_activeSurfacesProperty != null)
                {
                    var activeSurfaces = _activeSurfacesProperty.GetValue(null);
                    if (activeSurfaces is System.Collections.IList surfaceList)
                    {
                        foreach (var surface in surfaceList)
                        {
                            surfaces.Add(surface);
                        }
                    }
                }

                if (surfaces.Count == 0)
                {
                    // Fallback: find all NavMeshSurface components using Resources.FindObjectsOfTypeAll
                    if (_navMeshSurfaceType != null)
                    {
                        var allObjects = Resources.FindObjectsOfTypeAll(_navMeshSurfaceType);
                        foreach (var obj in allObjects)
                        {
                            if (obj != null)
                            {
                                surfaces.Add(obj);
                            }
                        }
                    }
                }

                if (surfaces.Count == 0)
                {
                    return Response.Error("[EXPERIMENTAL] No NavMeshSurface components found in the scene. Add a NavMeshSurface component to a GameObject to bake NavMesh.");
                }

                // Check if we should use async baking (UpdateNavMesh) or sync baking (BuildNavMesh)
                bool useAsync = @params["async"]?.ToObject<bool?>() ?? false;
                List<AsyncOperation> asyncOps = new List<AsyncOperation>();

                if (useAsync && _updateNavMeshMethod != null)
                {
                    // Use async UpdateNavMesh for each surface that has existing data
                    foreach (var surface in surfaces)
                    {
                        try
                        {
                            var navMeshDataProperty = _navMeshSurfaceType.GetProperty("navMeshData");
                            if (navMeshDataProperty != null)
                            {
                                var navMeshData = navMeshDataProperty.GetValue(surface);
                                if (navMeshData != null)
                                {
                                    var asyncOp = _updateNavMeshMethod.Invoke(surface, new object[] { navMeshData }) as AsyncOperation;
                                    if (asyncOp != null)
                                    {
                                        asyncOps.Add(asyncOp);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // If UpdateNavMesh fails, fall back to BuildNavMesh
                        }
                    }
                }

                // If no async operations were started, use synchronous BuildNavMesh
                if (asyncOps.Count == 0)
                {
                    foreach (var surface in surfaces)
                    {
                        _buildNavMeshMethod?.Invoke(surface, null);
                    }
                    
                    // For synchronous baking, complete immediately
                    bool hasNavMeshData = false;
                    try
                    {
                        // First check NavMeshSurface components for navMeshData
                        if (_navMeshSurfaceType != null)
                        {
                            var navMeshDataProperty = _navMeshSurfaceType.GetProperty("navMeshData");
                            if (navMeshDataProperty != null)
                            {
                                foreach (var surface in surfaces)
                                {
                                    if (surface != null)
                                    {
                                        var navMeshData = navMeshDataProperty.GetValue(surface);
                                        if (navMeshData != null)
                                        {
                                            hasNavMeshData = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Fallback: check global NavMesh if NavMeshSurface check didn't find anything
                        if (!hasNavMeshData && _calculateTriangulationMethod != null)
                        {
                            var triangulation = _calculateTriangulationMethod.Invoke(null, null);
                            if (triangulation != null)
                            {
                                var verticesProperty = triangulation.GetType().GetProperty("vertices");
                                if (verticesProperty != null)
                                {
                                    var vertices = verticesProperty.GetValue(triangulation) as Array;
                                    hasNavMeshData = vertices != null && vertices.Length > 0;
                                }
                            }
                        }
                    }
                    catch { }

                    AsyncOperationTracker.CompleteJob(job.OpId, "NavMesh baking completed", new
                    {
                        hasNavMeshData = hasNavMeshData,
                        surfacesBaked = surfaces.Count
                    });
                    StateComposer.MarkDirty();

                    return AsyncOperationTracker.CreateCompleteResponse(job);
                }
                else
                {
                    // Store async operations for tracking
                    lock (_callbackLock)
                    {
                        _navMeshBakeOperations[job.OpId] = asyncOps;
                    }

                    // Create and store callback delegate for proper unsubscription
                    EditorApplication.CallbackFunction callback = () => CheckNavMeshBake(job.OpId);
                    lock (_callbackLock)
                    {
                        _updateCallbacks[job.OpId] = callback;
                    }
                    EditorApplication.update += callback;

                    // Return standardized pending response
                    var response = AsyncOperationTracker.CreatePendingResponse(job) as Dictionary<string, object>;
                    response["poll_interval"] = 2.0;
                    response["message"] = "[EXPERIMENTAL] NavMesh baking started (async)";
                    response["data"] = new { type = "navmesh", surfacesCount = surfaces.Count };
                    return response;
                }
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to start NavMesh baking: {e.Message}");
            }
        }

        private static void CheckNavMeshBake(string opId)
        {
            if (!HasAINavigation())
                return;

            try
            {
                // Check if async operations are still running
                List<AsyncOperation> asyncOps = null;
                lock (_callbackLock)
                {
                    if (_navMeshBakeOperations.TryGetValue(opId, out asyncOps))
                    {
                        // Check if all operations are done
                        bool allDone = asyncOps.All(op => op != null && op.isDone);
                        
                        if (allDone)
                        {
                            // Remove from tracking
                            _navMeshBakeOperations.Remove(opId);
                            
                            // Properly unsubscribe using stored delegate
                            if (_updateCallbacks.TryGetValue(opId, out var callback))
                            {
                                EditorApplication.update -= callback;
                                _updateCallbacks.Remove(opId);
                            }
                            
                            // Check if NavMesh data exists using reflection
                            bool hasNavMeshData = false;
                            try
                            {
                                // First check NavMeshSurface components for navMeshData
                                if (_navMeshSurfaceType != null)
                                {
                                    var navMeshDataProperty = _navMeshSurfaceType.GetProperty("navMeshData");
                                    if (navMeshDataProperty != null && _activeSurfacesProperty != null)
                                    {
                                        var activeSurfaces = _activeSurfacesProperty.GetValue(null);
                                        if (activeSurfaces is System.Collections.IList surfaceList)
                                        {
                                            foreach (var surface in surfaceList)
                                            {
                                                if (surface != null)
                                                {
                                                    var navMeshData = navMeshDataProperty.GetValue(surface);
                                                    if (navMeshData != null)
                                                    {
                                                        hasNavMeshData = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // Fallback: check global NavMesh if NavMeshSurface check didn't find anything
                                if (!hasNavMeshData && _calculateTriangulationMethod != null)
                                {
                                    var triangulation = _calculateTriangulationMethod.Invoke(null, null);
                                    if (triangulation != null)
                                    {
                                        var verticesProperty = triangulation.GetType().GetProperty("vertices");
                                        if (verticesProperty != null)
                                        {
                                            var vertices = verticesProperty.GetValue(triangulation) as Array;
                                            hasNavMeshData = vertices != null && vertices.Length > 0;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // If we can't check, assume no data
                                hasNavMeshData = false;
                            }
                            
                            AsyncOperationTracker.CompleteJob(opId, "NavMesh baking completed", new
                            {
                                hasNavMeshData = hasNavMeshData,
                                surfacesBaked = asyncOps.Count
                            });

                            StateComposer.MarkDirty();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CodelyLogger.LogError($"[ManageBake] Error in CheckNavMeshBake: {ex.Message}");
            }
        }

        private static object BakeLighting(JObject @params)
        {
            try
            {
                var writeCheck = WriteGuard.CheckWriteAllowed("bake_lighting");
                if (writeCheck != null) return writeCheck;

                var job = AsyncOperationTracker.CreateJob(
                    AsyncOperationTracker.JobType.LightingBake,
                    "Baking lighting..."
                );

                // Start async bake
                Lightmapping.BakeAsync();

                // Create and store callback delegate for proper unsubscription
                EditorApplication.CallbackFunction callback = () => CheckLightingBake(job.OpId);
                lock (_callbackLock)
                {
                    _updateCallbacks[job.OpId] = callback;
                }
                EditorApplication.update += callback;

                // Return standardized pending response
                var response = AsyncOperationTracker.CreatePendingResponse(job) as Dictionary<string, object>;
                response["poll_interval"] = 2.0;
                response["message"] = "[EXPERIMENTAL] Lighting baking started";
                response["data"] = new { type = "lighting" };
                return response;
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to start lighting baking: {e.Message}");
            }
        }

        private static void CheckLightingBake(string opId)
        {
            if (!Lightmapping.isRunning)
            {
                // Properly unsubscribe using stored delegate
                EditorApplication.CallbackFunction callback;
                lock (_callbackLock)
                {
                    if (_updateCallbacks.TryGetValue(opId, out callback))
                    {
                        EditorApplication.update -= callback;
                        _updateCallbacks.Remove(opId);
                    }
                }
                
                AsyncOperationTracker.CompleteJob(opId, "Lighting baking completed", new
                {
                    hasLightingData = Lightmapping.lightingDataAsset != null
                });

                StateComposer.MarkDirty();
            }
        }

        private static object WaitForBake(JObject @params)
        {
            try
            {
                string opId = @params["op_id"]?.ToString();
                int timeoutSeconds = @params["timeoutSeconds"]?.ToObject<int?>() ?? 600;

                if (string.IsNullOrEmpty(opId))
                    return Response.Error("'op_id' parameter required for wait_for_bake.");

                var job = AsyncOperationTracker.GetJob(opId);
                if (job == null)
                    return Response.Error($"Operation {opId} not found.");

                if (job.Type != AsyncOperationTracker.JobType.NavMeshBake && 
                    job.Type != AsyncOperationTracker.JobType.LightingBake)
                    return Response.Error($"Operation {opId} is not a bake operation.");

                if (AsyncOperationTracker.IsJobTimedOut(opId, timeoutSeconds))
                {
                    AsyncOperationTracker.FailJob(opId, $"Bake operation timed out after {timeoutSeconds} seconds");
                    return AsyncOperationTracker.CreateErrorResponse(job);
                }

                switch (job.Status)
                {
                    case AsyncOperationTracker.JobStatus.Complete:
                        var response = AsyncOperationTracker.CreateCompleteResponse(job);
                        return response;
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
                return Response.Error($"[EXPERIMENTAL] Failed to wait for bake: {e.Message}");
            }
        }

        private static object ClearNavMesh()
        {
            try
            {
                if (!HasAINavigation())
                {
                    return Response.Error("[EXPERIMENTAL] NavMesh operations require AI Navigation package.");
                }

                var writeCheck = WriteGuard.CheckWriteAllowed("clear_navmesh");
                if (writeCheck != null) return writeCheck;

                // Clear NavMesh using reflection - try RemoveAllNavMeshData first
                int clearedCount = 0;
                if (_removeAllNavMeshDataMethod != null)
                {
                    _removeAllNavMeshDataMethod.Invoke(null, null);
                    clearedCount++;
                }
                
                // Also clear all NavMeshSurface components
                if (_activeSurfacesProperty != null)
                {
                    var activeSurfaces = _activeSurfacesProperty.GetValue(null);
                    if (activeSurfaces is System.Collections.IList surfaceList)
                    {
                        var removeDataMethod = _navMeshSurfaceType.GetMethod("RemoveData", BindingFlags.Public | BindingFlags.Instance);
                        foreach (var surface in surfaceList)
                        {
                            try
                            {
                                removeDataMethod?.Invoke(surface, null);
                                clearedCount++;
                            }
                            catch { }
                        }
                    }
                }
                
                StateComposer.MarkDirty();

                return Response.Success($"[EXPERIMENTAL] NavMesh data cleared ({clearedCount} surfaces).");
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to clear NavMesh: {e.Message}");
            }
        }

        private static object ClearBakedData()
        {
            try
            {
                var writeCheck = WriteGuard.CheckWriteAllowed("clear_baked_data");
                if (writeCheck != null) return writeCheck;

                Lightmapping.Clear();
                StateComposer.MarkDirty();

                return Response.Success("[EXPERIMENTAL] Baked lighting data cleared.");
            }
            catch (Exception e)
            {
                return Response.Error($"[EXPERIMENTAL] Failed to clear baked data: {e.Message}");
            }
        }
    }
}

