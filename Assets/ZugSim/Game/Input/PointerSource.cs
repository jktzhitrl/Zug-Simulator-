using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ZugSim.Game
{
    public enum PointerPhase { Began, Moved, Ended }

    public struct PointerInfo
    {
        public int Id;
        public Vector2 Position;     // Bildschirmpixel, Ursprung unten links
        public Vector2 Delta;
        public PointerPhase Phase;
    }

    /// <summary>
    /// Einheitliche Zeiger-Eingabe (Multi-Touch auf Tablets, Maus im Editor), unabhaengig davon ob das alte oder
    /// das neue Input System aktiv ist.
    /// </summary>
    public sealed class PointerSource
    {
        readonly Dictionary<int, Vector2> last = new Dictionary<int, Vector2>();
        readonly List<int> seen = new List<int>();

        public void Poll(List<PointerInfo> result)
        {
            result.Clear();
            seen.Clear();
#if ENABLE_INPUT_SYSTEM
            var ts = Touchscreen.current;
            if (ts != null)
            {
                for (int i = 0; i < ts.touches.Count; i++)
                {
                    var t = ts.touches[i];
                    if (!t.press.isPressed && !t.press.wasReleasedThisFrame) continue;
                    Emit(result, t.touchId.ReadValue(), t.position.ReadValue(), t.press.wasPressedThisFrame, t.press.wasReleasedThisFrame);
                }
            }
            var mouse = Mouse.current;
            if (mouse != null && (ts == null || ts.touches.Count == 0))
                Emit(result, -1, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame, mouse.leftButton.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                bool began = t.phase == TouchPhase.Began;
                bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                Emit(result, t.fingerId, t.position, began, ended);
            }
            if (Input.touchCount == 0)
                Emit(result, -1, Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0), Input.GetMouseButton(0));
#endif
            // nicht mehr gesehene Zeiger aufraeumen
            var stale = new List<int>();
            foreach (var kv in last) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (int id in stale) last.Remove(id);
        }

        void Emit(List<PointerInfo> result, int id, Vector2 pos, bool began, bool ended, bool held = true)
        {
            if (!began && !ended && !held) return;
            seen.Add(id);
            Vector2 prev;
            bool had = last.TryGetValue(id, out prev);
            Vector2 delta = (had && !began) ? pos - prev : Vector2.zero;
            result.Add(new PointerInfo
            {
                Id = id, Position = pos, Delta = delta,
                Phase = began ? PointerPhase.Began : (ended ? PointerPhase.Ended : PointerPhase.Moved),
            });
            if (ended) last.Remove(id); else last[id] = pos;
        }

        /// <summary>Scrollrad / Mausrad (Editor) fuer Zoom.</summary>
        public static float ScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null ? m.scroll.ReadValue().y * 0.01f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }
    }

    /// <summary>Tastatur-Steuerung zum Testen im Editor / mit Bluetooth-Tastatur.</summary>
    public static class KeyboardControls
    {
        public struct Keys
        {
            public bool ThrottleUp, ThrottleDown, BrakeUp, BrakeDown, Horn, Emergency, Sifa, Doors;
        }

        public static Keys Poll()
        {
            var k = new Keys();
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return k;
            k.ThrottleUp = kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            k.ThrottleDown = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame;
            k.BrakeUp = kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
            k.BrakeDown = kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame;
            k.Horn = kb.hKey.isPressed;
            k.Emergency = kb.spaceKey.wasPressedThisFrame;
            k.Sifa = kb.leftCtrlKey.wasPressedThisFrame;
            k.Doors = kb.tKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            k.ThrottleUp = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            k.ThrottleDown = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
            k.BrakeUp = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
            k.BrakeDown = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
            k.Horn = Input.GetKey(KeyCode.H);
            k.Emergency = Input.GetKeyDown(KeyCode.Space);
            k.Sifa = Input.GetKeyDown(KeyCode.LeftControl);
            k.Doors = Input.GetKeyDown(KeyCode.T);
#endif
            return k;
        }
    }
}
