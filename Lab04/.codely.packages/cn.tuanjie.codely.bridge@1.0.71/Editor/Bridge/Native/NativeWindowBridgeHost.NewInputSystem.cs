using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityTcp.Editor.Native
{
    internal static partial class NativeWindowBridgeHost
    {
#if UNITY_EDITOR_WIN
        // ── New Input System (com.unity.inputsystem) keyboard injection ──
        //
        // The new Input System (activeInputHandler=2) processes keyboard through
        // WM_INPUT (Raw Input), NOT WM_KEYDOWN. The WM_INPUT path is guarded by
        // `isFocused = GetApplication().IsFocused()` which returns false when the
        // editor is offscreen. So neither WM_KEYDOWN nor WM_INPUT reaches the new
        // Input System in streaming/offscreen mode.
        //
        // Solution: use InputSystem.QueueStateEvent to directly inject keyboard
        // state into the new Input System's managed event queue, bypassing the
        // native Win32 message pipeline and the isFocused check entirely.
        //
        // QueueStateEvent replaces the ENTIRE keyboard state, so we must track
        // all held keys and send the complete state on each keydown/keyup.
        // s_WinHeldKeys already tracks held keys as browser key strings.
        private static Type s_InputSystemType;
        private static Type s_KeyboardType;
        private static Type s_KeyboardStateType;
        private static Type s_KeyType;
        private static System.Reflection.MethodInfo s_QueueStateEventMethod;
        private static System.Reflection.MethodInfo s_GetDeviceMethod;
        private static System.Reflection.MethodInfo s_KeySetMethod;
        private static bool s_InputSystemRefResolved;
        // Negative cache: once we've determined InputSystem is not installed
        // (or type resolution fails), skip the expensive assembly scan on
        // every subsequent keydown/keyup.
        private static bool s_InputSystemNotFound;
        private static System.Reflection.FieldInfo s_InputManagerInstanceField;
        private static System.Reflection.FieldInfo s_InputManagerHasFocusField;
        private static int s_InputSystemDiagCount = 0;

        // Consume one diagnostic log budget slot for noisy reflection/input probes.
        private static bool TryConsumeInputSystemDiagBudget(int limit = 3)
        {
            if (s_InputSystemDiagCount >= limit) return false;
            s_InputSystemDiagCount++;
            return true;
        }

        // Emit InputSystem diagnostics with a shared rate limit.
        private static void LogInputSystemDiagLimited(string message, int limit = 3)
        {
            if (TryConsumeInputSystemDiagBudget(limit))
                LogVerbose(message);
        }

        /// <summary>
        /// Lazily resolve Input System types via reflection. The package may not
        /// be installed in all projects, so all access is reflection-based.
        /// Uses a negative cache to avoid repeated assembly scans on the input
        /// hot path when InputSystem is absent.
        /// </summary>
        private static bool TryResolveInputSystemRefs()
        {
            if (s_InputSystemRefResolved) return true;
            if (s_InputSystemNotFound) return false;

            try
            {
                // Find the InputSystem assembly by name first (more reliable than GetType)
                const string asmName = "Unity.InputSystem";
                System.Reflection.Assembly inputAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == asmName)
                    {
                        inputAsm = asm;
                        break;
                    }
                }

                // Fallback: search by type name
                if (inputAsm == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var type = asm.GetType("UnityEngine.InputSystem.InputSystem");
                        if (type != null)
                        {
                            inputAsm = asm;
                            break;
                        }
                    }
                }

                if (inputAsm == null)
                {
                    if (TryConsumeInputSystemDiagBudget())
                    {
                        var asmNames = new List<string>();
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            string n = a.GetName().Name;
                            if (n.Contains("Input") || n.Contains("input"))
                                asmNames.Add(n);
                        }
                        LogVerbose($"[NWB-NewInputSystem] Could not find InputSystem assembly. Input-related assemblies: [{string.Join(", ", asmNames)}]");
                    }
                    s_InputSystemNotFound = true;
                    return false;
                }

                s_InputSystemType = inputAsm.GetType("UnityEngine.InputSystem.InputSystem");
                if (s_InputSystemType == null)
                {
                    LogInputSystemDiagLimited("[NWB-NewInputSystem] Found assembly but InputSystem type is null");
                    s_InputSystemNotFound = true;
                    return false;
                }

                s_KeyboardType = s_InputSystemType.Assembly.GetType("UnityEngine.InputSystem.Keyboard");
                s_KeyboardStateType = s_InputSystemType.Assembly.GetType("UnityEngine.InputSystem.LowLevel.KeyboardState");
                s_KeyType = s_InputSystemType.Assembly.GetType("UnityEngine.InputSystem.Key");

                if (s_KeyboardType == null || s_KeyboardStateType == null || s_KeyType == null)
                {
                    LogInputSystemDiagLimited($"[NWB-NewInputSystem] Type resolution failed: Keyboard={s_KeyboardType != null} KeyboardState={s_KeyboardStateType != null} Key={s_KeyType != null}");
                    s_InputSystemNotFound = true;
                    return false;
                }

                // InputSystem.GetDevice<Keyboard>() -> Keyboard.current
                // Use GetMethods + filter to avoid AmbiguousMatchException from multiple GetDevice overloads.
                foreach (var m in s_InputSystemType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (m.Name == "GetDevice" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    {
                        s_GetDeviceMethod = m.MakeGenericMethod(s_KeyboardType);
                        break;
                    }
                }

                if (s_GetDeviceMethod == null)
                {
                    LogInputSystemDiagLimited("[NWB-NewInputSystem] GetDevice method not found");
                    s_InputSystemNotFound = true;
                    return false;
                }

                // InputSystem.QueueStateEvent<TState>(InputDevice device, TState state, double time = -1)
                foreach (var m in s_InputSystemType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (m.Name == "QueueStateEvent" && m.IsGenericMethod)
                    {
                        var parms = m.GetParameters();
                        if (parms.Length == 3 && parms[2].ParameterType == typeof(double))
                        {
                            s_QueueStateEventMethod = m.MakeGenericMethod(s_KeyboardStateType);
                            break;
                        }
                    }
                }

                if (s_QueueStateEventMethod == null)
                {
                    LogInputSystemDiagLimited("[NWB-NewInputSystem] QueueStateEvent method not found");
                    s_InputSystemNotFound = true;
                    return false;
                }

                // KeyboardState.Set(Key key, bool state) - used to set individual key bits.
                foreach (var m in s_KeyboardStateType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (m.Name == "Set")
                    {
                        var parms = m.GetParameters();
                        if (parms.Length == 2 && parms[0].ParameterType == s_KeyType && parms[1].ParameterType == typeof(bool))
                        {
                            s_KeySetMethod = m;
                            break;
                        }
                    }
                }

                // InputManager.s_Manager (singleton) and m_HasFocus field.
                var inputManagerType = s_InputSystemType.Assembly.GetType("UnityEngine.InputSystem.InputManager");
                if (inputManagerType != null)
                {
                    s_InputManagerInstanceField = s_InputSystemType.GetField("s_Manager",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (s_InputManagerInstanceField != null)
                    {
                        s_InputManagerHasFocusField = inputManagerType.GetField("m_HasFocus",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    }
                }

                // Mark as resolved only after everything above succeeds.
                s_InputSystemRefResolved = true;
                return true;
            }
            catch (Exception ex)
            {
                LogInputSystemDiagLimited($"[NWB-NewInputSystem] TryResolveInputSystemRefs exception: type={ex.GetType().Name} msg={ex.Message}");
                s_InputSystemNotFound = true;
                return false;
            }
        }

        /// <summary>
        /// Parse an InputSystem.Key enum name using Enum.IsDefined/Enum.Parse,
        /// returning null on failure instead of throwing ArgumentException.
        /// </summary>
        private static object TryParseKey(string keyName)
        {
            if (Enum.IsDefined(s_KeyType, keyName))
                return Enum.Parse(s_KeyType, keyName);
            return null;
        }

        /// <summary>
        /// Map a browser key string to an InputSystem.Key enum value.
        /// Uses TryParse to avoid throwing on cross-version enum name changes.
        /// </summary>
        private static object BrowserKeyToInputSystemKey(string key)
        {
            if (string.IsNullOrEmpty(key) || s_KeyType == null) return null;

            // Single-character keys: a-z, 0-9, symbols
            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'a' && c <= 'z') return TryParseKey(c.ToString().ToUpperInvariant());
                if (c >= 'A' && c <= 'Z') return TryParseKey(c.ToString());
                if (c >= '0' && c <= '9') return TryParseKey("Digit" + c);
                switch (c)
                {
                    case ' ': return TryParseKey("Space");
                    case '-': return TryParseKey("Minus");
                    case '=': return TryParseKey("Equals");
                    case '[': return TryParseKey("LeftBracket");
                    case ']': return TryParseKey("RightBracket");
                    case ';': return TryParseKey("Semicolon");
                    case ',': return TryParseKey("Comma");
                    case '.': return TryParseKey("Period");
                    case '/': return TryParseKey("Slash");
                    case '`': return TryParseKey("Backquote");
                    case '\\': return TryParseKey("Backslash");
                    case '\'': return TryParseKey("Quote");
                }
                return null;
            }

            switch (key)
            {
                case "Enter":
                case "Return": return TryParseKey("Enter");
                case "Tab": return TryParseKey("Tab");
                case "Escape": return TryParseKey("Escape");
                case "Backspace": return TryParseKey("Backspace");
                case "Delete": return TryParseKey("Delete");
                case "Insert": return TryParseKey("Insert");
                case "Home": return TryParseKey("Home");
                case "End": return TryParseKey("End");
                case "PageUp": return TryParseKey("PageUp");
                case "PageDown": return TryParseKey("PageDown");
                case "ArrowUp": return TryParseKey("UpArrow");
                case "ArrowDown": return TryParseKey("DownArrow");
                case "ArrowLeft": return TryParseKey("LeftArrow");
                case "ArrowRight": return TryParseKey("RightArrow");
                case "Shift": return TryParseKey("LeftShift");
                case "Control": return TryParseKey("LeftCtrl");
                case "Alt": return TryParseKey("LeftAlt");
                case "Meta": return TryParseKey("LeftMeta");
                case "CapsLock": return TryParseKey("CapsLock");
                case "F1": return TryParseKey("F1");
                case "F2": return TryParseKey("F2");
                case "F3": return TryParseKey("F3");
                case "F4": return TryParseKey("F4");
                case "F5": return TryParseKey("F5");
                case "F6": return TryParseKey("F6");
                case "F7": return TryParseKey("F7");
                case "F8": return TryParseKey("F8");
                case "F9": return TryParseKey("F9");
                case "F10": return TryParseKey("F10");
                case "F11": return TryParseKey("F11");
                case "F12": return TryParseKey("F12");
                // Numpad keys
                case "Numpad0": return TryParseKey("Numpad0");
                case "Numpad1": return TryParseKey("Numpad1");
                case "Numpad2": return TryParseKey("Numpad2");
                case "Numpad3": return TryParseKey("Numpad3");
                case "Numpad4": return TryParseKey("Numpad4");
                case "Numpad5": return TryParseKey("Numpad5");
                case "Numpad6": return TryParseKey("Numpad6");
                case "Numpad7": return TryParseKey("Numpad7");
                case "Numpad8": return TryParseKey("Numpad8");
                case "Numpad9": return TryParseKey("Numpad9");
                case "NumpadEnter": return TryParseKey("NumpadEnter");
                case "NumpadMultiply": return TryParseKey("NumpadMultiply");
                case "NumpadAdd": return TryParseKey("NumpadAdd");
                case "NumpadSubtract": return TryParseKey("NumpadSubtract");
                case "NumpadDecimal": return TryParseKey("NumpadDecimal");
                case "NumpadDivide": return TryParseKey("NumpadDivide");
                // Extended keys
                case "ContextMenu": return TryParseKey("ContextMenu");
                case "PrintScreen": return TryParseKey("PrintScreen");
                case "ScrollLock": return TryParseKey("ScrollLock");
                case "Pause": return TryParseKey("Pause");
                case "NumLock": return TryParseKey("NumLock");
                default: return null;
            }
        }

        /// <summary>
        /// Inject the current set of held keys into the new Input System via QueueStateEvent.
        /// This bypasses the native Win32 message pipeline and the isFocused check that blocks
        /// WM_INPUT in offscreen mode. Call after updating s_WinHeldKeys.
        /// </summary>
        private static void QueueInputSystemKeyboardState()
        {
            if (!TryResolveInputSystemRefs())
            {
                LogInputSystemDiagLimited("[NWB-NewInputSystem] TryResolveInputSystemRefs returned false - InputSystem package not found");
                return;
            }

            try
            {
                // Get Keyboard.current
                object keyboard = s_GetDeviceMethod.Invoke(null, null);
                if (keyboard == null)
                {
                    LogInputSystemDiagLimited("[NWB-NewInputSystem] Keyboard.current is null - keyboard device not created");
                    return;
                }

                // Temporarily force m_HasFocus=true on the InputManager so that
                // OnUpdate does not early-out and discard our queued StateEvent.
                // Restore the original value afterwards to avoid corrupting
                // InputManager's focus state for other consumers.
                object manager = null;
                bool originalFocus = false;
                bool focusWasForced = false;

                if (s_InputManagerInstanceField != null && s_InputManagerHasFocusField != null)
                {
                    manager = s_InputManagerInstanceField.GetValue(null);
                    if (manager != null)
                    {
                        originalFocus = (bool)s_InputManagerHasFocusField.GetValue(manager);
                        if (!originalFocus)
                        {
                            s_InputManagerHasFocusField.SetValue(manager, true);
                            focusWasForced = true;
                            LogInputSystemDiagLimited("[NWB-NewInputSystem] Temporarily forced m_HasFocus=true (was false - offscreen mode causes Application.isFocused=false, which makes InputManager.OnUpdate discard all queued events)", 5);
                        }
                    }
                }

                try
                {
                    // Create a default KeyboardState (all keys released), then set held keys.
                    object state = Activator.CreateInstance(s_KeyboardStateType);
                    int keysSet = 0;
                    foreach (string heldKey in s_WinHeldKeys)
                    {
                        object inputKey = BrowserKeyToInputSystemKey(heldKey);
                        if (inputKey != null && s_KeySetMethod != null)
                        {
                            s_KeySetMethod.Invoke(state, new object[] { inputKey, true });
                            keysSet++;
                        }
                    }

                    // Passing time=-1 can crash in offscreen mode when InputRuntime.s_Instance is null.
                    double eventTime = EditorApplication.timeSinceStartup;
                    LogInputSystemDiagLimited($"[NWB-NewInputSystem] QueueStateEvent: held={s_WinHeldKeys.Count} keysSet={keysSet} time={eventTime:F3}", 5);

                    s_QueueStateEventMethod.Invoke(null, new object[] { keyboard, state, eventTime });
                }
                finally
                {
                    // Restore the original m_HasFocus value so the InputManager's
                    // focus state is not permanently altered.
                    if (focusWasForced && manager != null)
                    {
                        s_InputManagerHasFocusField.SetValue(manager, originalFocus);
                    }
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"[NWB-NewInputSystem] QueueInputSystemKeyboardState error: type={ex.GetType().Name} msg={ex.Message}");
                if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null)
                {
                    LogVerbose($"[NWB-NewInputSystem] Inner exception: type={tie.InnerException.GetType().Name} msg={tie.InnerException.Message}");
                }
            }
        }
#endif
    }
}
