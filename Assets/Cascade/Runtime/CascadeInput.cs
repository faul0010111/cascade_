using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cascade.Runtime
{
    public enum InputKey { W, A, S, D, E, Q, F, G, Tab, Space, Period, R, LeftShift }

    /// <summary>Thin wrapper so the project works with the Input System package or the legacy Input Manager.</summary>
    public static class CascadeInput
    {
#if ENABLE_INPUT_SYSTEM
        private static Key Map(InputKey k)
        {
            switch (k)
            {
                case InputKey.W: return Key.W;
                case InputKey.A: return Key.A;
                case InputKey.S: return Key.S;
                case InputKey.D: return Key.D;
                case InputKey.E: return Key.E;
                case InputKey.Q: return Key.Q;
                case InputKey.F: return Key.F;
                case InputKey.G: return Key.G;
                case InputKey.Tab: return Key.Tab;
                case InputKey.Space: return Key.Space;
                case InputKey.Period: return Key.Period;
                case InputKey.R: return Key.R;
                default: return Key.LeftShift;
            }
        }

        public static bool Held(InputKey k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;
        public static bool Pressed(InputKey k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool LeftClick => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
        public static bool MiddleHeld => Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
        private static KeyCode Map(InputKey k)
        {
            switch (k)
            {
                case InputKey.W: return KeyCode.W;
                case InputKey.A: return KeyCode.A;
                case InputKey.S: return KeyCode.S;
                case InputKey.D: return KeyCode.D;
                case InputKey.E: return KeyCode.E;
                case InputKey.Q: return KeyCode.Q;
                case InputKey.F: return KeyCode.F;
                case InputKey.G: return KeyCode.G;
                case InputKey.Tab: return KeyCode.Tab;
                case InputKey.Space: return KeyCode.Space;
                case InputKey.Period: return KeyCode.Period;
                case InputKey.R: return KeyCode.R;
                default: return KeyCode.LeftShift;
            }
        }

        public static bool Held(InputKey k) => Input.GetKey(Map(k));
        public static bool Pressed(InputKey k) => Input.GetKeyDown(Map(k));
        public static Vector2 MousePosition => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool LeftClick => Input.GetMouseButtonDown(0);
        public static bool RightHeld => Input.GetMouseButton(1);
        public static bool MiddleHeld => Input.GetMouseButton(2);
#endif

        /// <summary>Mouse position in IMGUI coordinates (origin top-left).</summary>
        public static Vector2 GuiMousePosition => new Vector2(MousePosition.x, Screen.height - MousePosition.y);
    }
}
