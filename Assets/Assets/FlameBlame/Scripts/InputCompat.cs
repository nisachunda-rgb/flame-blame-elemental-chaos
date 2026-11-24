using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FlameBlame
{
    /// <summary>Works with both the new Input System (Unity 6 default) and the old Input Manager.</summary>
    public static class InputCompat
    {
        public static Vector2 Move()
        {
            float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) y -= 1f;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) y += 1f;
#else
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
#endif
            return new Vector2(x, y);
        }

        public static Vector2 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        public static bool SprayHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return (Mouse.current != null && Mouse.current.leftButton.isPressed)
                || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);
#else
            return Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);
#endif
        }

        public static bool InteractPressed() => KeyDown("e");
        public static bool RestartPressed() => KeyDown("r");
        public static bool ViewPressed() => KeyDown("v");

        public static bool StartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                || (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame));
#else
            return Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
#endif
        }

        static bool KeyDown(string key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return false;
            switch (key)
            {
                case "e": return k.eKey.wasPressedThisFrame;
                case "r": return k.rKey.wasPressedThisFrame;
                case "v": return k.vKey.wasPressedThisFrame;
            }
            return false;
#else
            switch (key)
            {
                case "e": return Input.GetKeyDown(KeyCode.E);
                case "r": return Input.GetKeyDown(KeyCode.R);
                case "v": return Input.GetKeyDown(KeyCode.V);
            }
            return false;
#endif
        }
    }
}
