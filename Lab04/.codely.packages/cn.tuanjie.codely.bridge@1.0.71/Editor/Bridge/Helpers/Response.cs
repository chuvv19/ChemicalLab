using System;
using System.Collections.Generic;

namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// Provides static methods for creating standardized success and error response objects.
    /// Ensures consistent JSON structure for communication back to the Codely client.
    /// 
    /// Response format aligns with the OpenAPI spec:
    /// - ImmediateResponse: { success, message, data?, state?, state_delta? }
    /// - PendingResponse: { _mcp_status, op_id, poll_interval, message, state?, state_delta? }
    /// </summary>
    public static class Response
    {
        /// <summary>
        /// Creates a standardized success response object with optional state.
        /// </summary>
        /// <param name="message">A message describing the successful operation.</param>
        /// <param name="data">Optional additional data to include in the response.</param>
        /// <param name="includeState">Whether to include full state snapshot (default: false).</param>
        /// <param name="stateDelta">Optional state delta for incremental updates.</param>
        /// <returns>An object representing the success response.</returns>
        public static object Success(string message, object data = null, bool includeState = false, object stateDelta = null)
        {
            var response = new Dictionary<string, object>
            {
                { "success", true },
                { "message", message }
            };

            if (data != null)
            {
                response["data"] = data;
            }

            // Include state if explicitly requested
            if (includeState)
            {
                response["state"] = StateComposer.BuildFullState();
            }

            // Include state_delta if provided
            if (stateDelta != null)
            {
                response["state_delta"] = stateDelta;
            }

            return response;
        }

        /// <summary>
        /// Creates a standardized success response with automatic state delta.
        /// Use this for write operations that modify Unity state.
        /// </summary>
        public static object SuccessWithDelta(string message, object data = null, object stateDelta = null)
        {
            // Writes only mark state dirty. A later state read or explicit publish
            // advances the internal published-state marker.
            StateComposer.MarkDirty();
            return Success(message, data, includeState: false, stateDelta: stateDelta);
        }

        /// <summary>
        /// Creates a success response with a top-level state delta payload.
        /// </summary>
        public static object DeltaState(string message, object state)
        {
            return new
            {
                success = true,
                message,
                state
            };
        }

        /// <summary>
        /// Creates a standardized success response with full state snapshot.
        /// Use this for operations that require the client to have the latest state.
        /// </summary>
        public static object SuccessWithState(string message, object data = null)
        {
            StateComposer.PublishDirtyStateIfNeeded();
            return Success(message, data, includeState: true);
        }

        /// <summary>
        /// Creates a standardized error response object.
        /// </summary>
        /// <param name="errorCodeOrMessage">A message describing the error.</param>
        /// <param name="data">Optional additional data (e.g., error details) to include.</param>
        /// <param name="includeState">Whether to include full state snapshot for recovery (default: false).</param>
        /// <returns>An object representing the error response.</returns>
        public static object Error(string errorCodeOrMessage, object data = null, bool includeState = false)
        {
            var response = new Dictionary<string, object>
            {
                { "success", false },
                { "message", errorCodeOrMessage },
                { "code", errorCodeOrMessage },
                { "error", errorCodeOrMessage }
            };

            if (data != null)
            {
                response["data"] = data;
            }

            // Include state on error for recovery scenarios
            if (includeState)
            {
                response["state"] = StateComposer.BuildFullState();
            }

            return response;
        }

        /// <summary>
        /// Legacy revision conflict factory retained for binary compatibility.
        /// Writes no longer use this path.
        /// </summary>
        /// <param name="clientRev">Ignored legacy client revision.</param>
        /// <param name="serverRev">Ignored legacy server revision.</param>
        /// <returns>A generic success response.</returns>
        public static object Conflict(int clientRev, int serverRev)
        {
            return Success("Legacy revision marker ignored.");
        }

        /// <summary>
        /// Legacy overload for backward compatibility.
        /// </summary>
        [Obsolete("Use Success(message, data, includeState, stateDelta) instead.")]
        public static object SuccessLegacy(string message, object data = null)
        {
            if (data != null)
            {
                return new
                {
                    success = true,
                    message = message,
                    data = data,
                };
            }
            else
            {
                return new { success = true, message = message };
            }
        }
    }
}

