using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Codely.Newtonsoft.Json.Linq;

namespace UnityTcp.Editor.Helpers
{
    public static class StateDirtyPolicy
    {
        private static readonly Dictionary<string, HashSet<string>> ReadonlyActions =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["manage_editor"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "get_current_state",
                    "get_state",
                    "get_project_root",
                    "get_windows",
                    "get_active_tool",
                    "get_selection",
                    "get_tags",
                    "get_layers",
                    "get_compilation_summary",
                    "wait_for_compile",
                    "wait_for_idle",
                    "wait_for_stop",
                    "publish_dirty_state_if_needed",
                    // Play mode transitions only change editor runtime state, not
                    // project content. The new playMode is already conveyed in the
                    // tool response data — marking state dirty would trigger a
                    // spurious unity_observation_invalidated notification that
                    // surfaces as noise in the chat during streaming play/stop.
                    "play",
                    "stop",
                    "pause",
                    "step"
                },
                ["manage_gameobject"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "find",
                    "list_children",
                    "get_components",
                    "select"
                },
                ["manage_asset"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "search",
                    "get_info",
                    "get_components",
                    "ensure_meta_integrity"
                },
                ["manage_scene"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "get_hierarchy",
                    "get_active",
                    "get_build_settings"
                },
                ["manage_script"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "read",
                    "validate",
                    "get_sha"
                },
                ["manage_shader"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "detect_render_pipeline",
                    "read"
                },
                ["manage_package"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "list_packages",
                    "wait_for_upm"
                },
                ["manage_bake"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "wait_for_bake"
                },
                ["manage_workflow"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "init_session",
                    "compile_and_validate",
                    "checkpoint"
                },
                ["manage_window_bridge"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "list_windows",
                    "focus_window",
                    "resolve_native_window",
                    "input",
                    "start_stream_server",
                    "stop_stream_server",
                    "get_stream_server_status",
                    "start_offscreen_stream",
                    "stop_offscreen_stream"
                },
                ["manage_webrtc_stream"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "start_session",
                    "stop_session",
                    "get_status",
                    "list_sources",
                    "inject_input"
                },
                ["manage_gameview"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Game view resolution is editor UI state, not project content.
                    // Marking state dirty would trigger a spurious
                    // unity_observation_invalidated notification.
                    "get_resolution",
                    "set_resolution",
                    "list_resolutions"
                },
                ["read_console"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "get",
                    "clear"
                }
            };

        public static object Apply(string commandName, string action, object response)
        {
            if (response == null || !IsSuccessful(response))
            {
                return response;
            }

            if (IsReadonly(commandName, action))
            {
                return response;
            }

            var stateDelta = GetMemberValue(response, "state_delta");
            if (HasExplicitNoOpDirtyFalse(stateDelta))
            {
                return response;
            }

            if (HasDirtySignal(stateDelta))
            {
                StateComposer.MarkDirty();
                return response;
            }

            StateComposer.MarkDirty();
            return WithStateDelta(response, CreateDefaultStateDelta(commandName, action));
        }

        public static bool IsReadonly(string commandName, string action)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return true;
            }

            if (!ReadonlyActions.TryGetValue(commandName, out var actions))
            {
                return false;
            }

            return string.IsNullOrEmpty(action) || actions.Contains(action);
        }

        private static bool IsSuccessful(object response)
        {
            if (!TryGetMemberValue(response, "success", out var value))
            {
                return false;
            }

            if (value is bool b)
            {
                return b;
            }

            if (value is JValue jValue && jValue.Type == JTokenType.Boolean)
            {
                return jValue.Value<bool>();
            }

            return bool.TryParse(value?.ToString(), out var parsed) && parsed;
        }

        private static object WithStateDelta(object response, object stateDelta)
        {
            var existingStateDelta = GetMemberValue(response, "state_delta");
            var mergedStateDelta = existingStateDelta != null
                ? StateComposer.MergeStateDeltas(existingStateDelta, stateDelta)
                : stateDelta;

            if (response is JObject jObject)
            {
                jObject["state_delta"] = JObject.FromObject(mergedStateDelta);
                return jObject;
            }

            if (response is IDictionary<string, object> genericDictionary)
            {
                genericDictionary["state_delta"] = mergedStateDelta;
                return response;
            }

            if (response is IDictionary dictionary)
            {
                dictionary["state_delta"] = mergedStateDelta;
                return response;
            }

            var clone = JObject.FromObject(response);
            clone["state_delta"] = JObject.FromObject(mergedStateDelta);
            return clone;
        }

        private static object CreateDefaultStateDelta(string commandName, string action)
        {
            if (string.Equals(commandName, "manage_scene", StringComparison.OrdinalIgnoreCase)
                && string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
            {
                return StateComposer.CreateSceneDelta(dirty: false);
            }

            if (IsAssetOrProjectCommand(commandName))
            {
                return StateComposer.CreateAssetDelta(new object[0]);
            }

            if (string.Equals(commandName, "manage_editor", StringComparison.OrdinalIgnoreCase))
            {
                return CreateEditorDefaultStateDelta(action);
            }

            return StateComposer.CreateSceneDelta(dirty: true);
        }

        private static object CreateEditorDefaultStateDelta(string action)
        {
            if (string.Equals(action, "play", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object>
                {
                    ["editor"] = new Dictionary<string, object>
                    {
                        ["playMode"] = "playing"
                    }
                };
            }

            if (string.Equals(action, "pause", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object>
                {
                    ["editor"] = new Dictionary<string, object>
                    {
                        ["playMode"] = "paused"
                    }
                };
            }

            if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, object>
                {
                    ["editor"] = new Dictionary<string, object>
                    {
                        ["playMode"] = "stopped"
                    }
                };
            }

            return new Dictionary<string, object>
            {
                ["project"] = new Dictionary<string, object>
                {
                    ["dirty"] = true
                }
            };
        }

        private static bool IsAssetOrProjectCommand(string commandName)
        {
            return string.Equals(commandName, "manage_asset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "manage_script", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "manage_shader", StringComparison.OrdinalIgnoreCase)
                || string.Equals(commandName, "manage_package", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasExplicitNoOpDirtyFalse(object value)
        {
            if (value == null)
            {
                return false;
            }

            var token = ToJToken(value);
            if (token == null || token.Type != JTokenType.Object)
            {
                return false;
            }

            bool sawDirtyFalse = false;
            bool sawDirtyTrue = false;
            foreach (var property in ((JObject)token).DescendantsAndSelf())
            {
                if (property is JProperty jProperty && string.Equals(jProperty.Name, "dirty", StringComparison.OrdinalIgnoreCase))
                {
                    if (jProperty.Value.Type == JTokenType.Boolean && jProperty.Value.Value<bool>())
                    {
                        sawDirtyTrue = true;
                    }
                    else if (jProperty.Value.Type == JTokenType.Boolean && !jProperty.Value.Value<bool>())
                    {
                        sawDirtyFalse = true;
                    }
                }
            }

            return sawDirtyFalse && !sawDirtyTrue && !HasAssetProjectOrEditorDelta(token);
        }

        private static bool HasDirtySignal(object value)
        {
            if (value == null)
            {
                return false;
            }

            var token = ToJToken(value);
            if (token == null)
            {
                return false;
            }

            return HasDirtySignal(token);
        }

        private static bool HasDirtySignal(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                foreach (var property in obj.Properties())
                {
                    if (string.Equals(property.Name, "dirty", StringComparison.OrdinalIgnoreCase)
                        && property.Value.Type == JTokenType.Boolean
                        && property.Value.Value<bool>())
                    {
                        return true;
                    }

                    if (string.Equals(property.Name, "assets", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.Name, "project", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.Name, "editor", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (HasDirtySignal(property.Value))
                    {
                        return true;
                    }
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    if (HasDirtySignal(child))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAssetProjectOrEditorDelta(JToken token)
        {
            if (token == null || token.Type != JTokenType.Object)
            {
                return false;
            }

            var obj = (JObject)token;
            return obj["assets"] != null || obj["project"] != null || obj["editor"] != null;
        }

        private static object GetMemberValue(object source, string key)
        {
            TryGetMemberValue(source, key, out var value);
            return value;
        }

        private static bool TryGetMemberValue(object source, string key, out object value)
        {
            value = null;
            if (source == null)
            {
                return false;
            }

            if (source is JObject jObject && jObject.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var token))
            {
                value = token;
                return true;
            }

            if (source is IDictionary<string, object> genericDictionary)
            {
                foreach (var pair in genericDictionary)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
            }

            if (source is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }

            var property = source.GetType().GetProperty(
                key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            );
            if (property == null || property.GetIndexParameters().Length > 0)
            {
                return false;
            }

            value = property.GetValue(source);
            return true;
        }

        private static JToken ToJToken(object value)
        {
            if (value is JToken token)
            {
                return token;
            }

            try
            {
                return JToken.FromObject(value);
            }
            catch
            {
                return null;
            }
        }
    }
}
