using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
#endif

namespace LiquidVolumeFX {

    static class InputProxy {

        public static void SetupEventSystem() {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;
            var standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null) {
                Object.Destroy(standaloneModule);
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null) {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }
#endif
        }

        public static bool GetKey(KeyCode keyCode) {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(keyCode);
#elif ENABLE_INPUT_SYSTEM
            return GetKeyInternal(keyCode, false);
#else
            return false;
#endif
        }

        public static bool GetKeyDown(KeyCode keyCode) {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#elif ENABLE_INPUT_SYSTEM
            return GetKeyInternal(keyCode, true);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static bool GetKeyInternal(KeyCode keyCode, bool down) {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            var control = GetKeyControl(keyboard, keyCode);
            if (control == null) return false;
            return down ? control.wasPressedThisFrame : control.isPressed;
        }

        static KeyControl GetKeyControl(Keyboard keyboard, KeyCode keyCode) {
            switch (keyCode) {
                case KeyCode.LeftArrow: return keyboard.leftArrowKey;
                case KeyCode.RightArrow: return keyboard.rightArrowKey;
                case KeyCode.UpArrow: return keyboard.upArrowKey;
                case KeyCode.DownArrow: return keyboard.downArrowKey;
                case KeyCode.A: return keyboard.aKey;
                case KeyCode.F: return keyboard.fKey;
                case KeyCode.Q: return keyboard.qKey;
                case KeyCode.R: return keyboard.rKey;
                case KeyCode.S: return keyboard.sKey;
                case KeyCode.W: return keyboard.wKey;
                default:
                    return null;
            }
        }
#endif
    }
}



