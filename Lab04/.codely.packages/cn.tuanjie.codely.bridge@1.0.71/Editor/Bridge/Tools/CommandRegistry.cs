using System;
using System.Collections.Generic;
using Codely.Newtonsoft.Json.Linq;

using UnityTcp.Editor.Helpers;
namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Registry for all Unity Tool command handlers (Upgraded Version)
    /// </summary>
    public static class CommandRegistry
    {
        // Maps command names to the corresponding static HandleCommand method in tool classes
        private static readonly Dictionary<string, Func<JObject, object>> _handlers = new Dictionary<string, Func<JObject, object>>
        {
            // Core tools
            { "HandleManageScript", ManageScript.HandleCommand },
            { "HandleManageScene", ManageScene.HandleCommand },
            { "HandleManageEditor", ManageEditor.HandleCommand },
            { "HandleManageGameObject", ManageGameObject.HandleCommand },
            { "HandleManageAsset", ManageAsset.HandleCommand },
            { "HandleReadConsole", ReadConsole.HandleCommand },
            { "HandleExecuteMenuItem", ExecuteMenuItem.HandleCommand },
            { "HandleManageShader", ManageShader.HandleCommand},
            { "HandleManageScreenshot", ManageScreenshot.HandleCommand },
            // [EXPERIMENTAL] Phase 3 tools
            { "HandleManagePackage", ManagePackage.HandleCommand },
            { "HandleManageBake", ManageBake.HandleCommand },
            { "HandleManageUIToolkit", ManageUIToolkit.HandleCommand },
            { "HandleManageGameView", ManageGameView.HandleCommand },
            // Custom tool execution (API Spec aligned)
            { "HandleExecuteCustomTool", ExecuteCustomTool.HandleCommand },
            { "HandleExecuteCSharpScript", ExecuteCSharpScript.HandleCommand },
            // [INTERNAL] Not exposed to LLM - for agent execution layer only
            { "Handle_InternalStateDirty", _InternalStateDirtyNotifier.HandleCommand },
        };

        /// <summary>
        /// Gets a command handler by name.
        /// </summary>
        /// <param name="commandName">Name of the command handler (e.g., "HandleManageAsset").</param>
        /// <returns>The command handler function if found, null otherwise.</returns>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            // Use case-insensitive comparison for flexibility, although Python side should be consistent
            return _handlers.TryGetValue(commandName, out var handler) ? handler : null;
            // Consider adding logging here if a handler is not found
            /*
            if (_handlers.TryGetValue(commandName, out var handler)) {
                return handler;
            } else {
                CodelyLogger.LogError($\"[CommandRegistry] No handler found for command: {commandName}\");
                return null;
            }
            */
        }
    }
}

