using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GoingBatty
{
    /// <summary>
    /// Thin input layer that works whether the project uses the new Input System,
    /// the legacy Input Manager, or both.
    /// </summary>
    public static class InputWrap
    {
        public static Vector2 Move()
        {
            float x = 0, y = 0;
            if (Key(KeyName.D)) x += 1;
            if (Key(KeyName.A)) x -= 1;
            if (Key(KeyName.W)) y += 1;
            if (Key(KeyName.S)) y -= 1;
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1 ? v.normalized : v;
        }

        /// <summary>Mouse movement this frame, roughly in "legacy axis" units.</summary>
        public static Vector2 Look()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.delta.ReadValue() * 0.05f;
            return Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        }

        public enum KeyName { W, A, S, D, Space, Escape, Tab, Q, Enter }

        public static bool Key(KeyName k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return Ctl(kb, k).isPressed;
#else
            return Input.GetKey(Legacy(k));
#endif
        }

        public static bool KeyDown(KeyName k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return Ctl(kb, k).wasPressedThisFrame;
#else
            return Input.GetKeyDown(Legacy(k));
#endif
        }

        public static bool FireDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        public static bool FireHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        public static bool AltFireDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl Ctl(Keyboard kb, KeyName k)
        {
            switch (k)
            {
                case KeyName.W: return kb.wKey;
                case KeyName.A: return kb.aKey;
                case KeyName.S: return kb.sKey;
                case KeyName.D: return kb.dKey;
                case KeyName.Space: return kb.spaceKey;
                case KeyName.Escape: return kb.escapeKey;
                case KeyName.Tab: return kb.tabKey;
                case KeyName.Q: return kb.qKey;
                default: return kb.enterKey;
            }
        }
#else
        static KeyCode Legacy(KeyName k)
        {
            switch (k)
            {
                case KeyName.W: return KeyCode.W;
                case KeyName.A: return KeyCode.A;
                case KeyName.S: return KeyCode.S;
                case KeyName.D: return KeyCode.D;
                case KeyName.Space: return KeyCode.Space;
                case KeyName.Escape: return KeyCode.Escape;
                case KeyName.Tab: return KeyCode.Tab;
                case KeyName.Q: return KeyCode.Q;
                default: return KeyCode.Return;
            }
        }
#endif
    }
}
