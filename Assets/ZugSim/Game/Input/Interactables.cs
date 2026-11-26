using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZugSim.Game
{
    /// <summary>Basisklasse fuer per Touch bedienbare 3D-Objekte im Cockpit.</summary>
    public abstract class Interactable3D : MonoBehaviour
    {
        public virtual void OnPointerDown(PointerInfo p) { }
        public virtual void OnPointerDrag(PointerInfo p) { }
        public virtual void OnPointerUp(PointerInfo p) { }
    }

    /// <summary>
    /// Hebel (Fahrschalter / Bremse): Ziehen nach oben schiebt den Hebel nach vorn. Optional mit Rasten.
    /// </summary>
    public sealed class Lever3D : Interactable3D
    {
        public Transform Pivot;
        public float MinAngle = -32f, MaxAngle = 32f;
        /// <summary>0 = stufenlos, sonst Anzahl der Stufen (Rasten bei 0..1 in gleichen Abstaenden).</summary>
        public int Notches;
        /// <summary>Bildschirmanteil (Hoehe), der fuer den vollen Hebelweg gezogen werden muss.</summary>
        public float DragScreenFraction = 0.28f;
        public float Value { get; private set; }
        public bool Dragging { get; private set; }
        public event Action<float> Changed;

        float raw;
        float shown;

        public void SetValue(float v, bool silent = true)
        {
            raw = Mathf.Clamp01(v);
            Value = Snap(raw);
            if (!silent && Changed != null) Changed(Value);
        }

        float Snap(float v)
        {
            return Notches > 0 ? Mathf.Round(v * Notches) / Notches : v;
        }

        public override void OnPointerDown(PointerInfo p) { Dragging = true; }

        public override void OnPointerDrag(PointerInfo p)
        {
            float dv = p.Delta.y / (Screen.height * DragScreenFraction);
            raw = Mathf.Clamp01(raw + dv);
            float snapped = Snap(raw);
            if (Mathf.Abs(snapped - Value) > 1e-4f)
            {
                Value = snapped;
                if (Changed != null) Changed(Value);
            }
        }

        public override void OnPointerUp(PointerInfo p) { Dragging = false; raw = Value; }

        void LateUpdate()
        {
            shown = Mathf.Lerp(shown, Value, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            if (Pivot != null) Pivot.localRotation = Quaternion.Euler(Mathf.Lerp(MinAngle, MaxAngle, shown), 0, 0);
        }
    }

    /// <summary>Taster (gedrueckt halten oder antippen).</summary>
    public sealed class PushButton3D : Interactable3D
    {
        public Transform Cap;
        public Vector3 PressOffset = new Vector3(0, -0.012f, 0);
        public bool Held { get; private set; }
        public event Action Pressed;
        public event Action Released;
        Vector3 rest;
        bool init;

        void Awake() { if (Cap != null) { rest = Cap.localPosition; init = true; } }

        public override void OnPointerDown(PointerInfo p) { Held = true; if (Pressed != null) Pressed(); }
        public override void OnPointerUp(PointerInfo p) { Held = false; if (Released != null) Released(); }

        void LateUpdate()
        {
            if (Cap == null) return;
            if (!init) { rest = Cap.localPosition; init = true; }
            Cap.localPosition = Vector3.Lerp(Cap.localPosition, rest + (Held ? PressOffset : Vector3.zero), 0.5f);
        }
    }

    /// <summary>Rundinstrument: Zeiger dreht sich linear zwischen Min- und Maximalwinkel.</summary>
    public sealed class Gauge3D : MonoBehaviour
    {
        public Transform Needle;
        public float MinValue, MaxValue = 1f;
        public float MinAngle = -135f, MaxAngle = 135f;
        public float Damping = 12f;
        float shown;

        public void Set(float value)
        {
            float t = Mathf.InverseLerp(MinValue, MaxValue, value);
            shown = Mathf.Lerp(shown, t, 1f - Mathf.Exp(-Damping * Time.unscaledDeltaTime));
            if (Needle != null) Needle.localRotation = Quaternion.Euler(0, 0, -Mathf.Lerp(MinAngle, MaxAngle, shown));
        }
    }

    /// <summary>Leuchtmelder im Cockpit.</summary>
    public sealed class Lamp3D : MonoBehaviour
    {
        public Renderer Target;
        public Material OnMat, OffMat;
        public void Set(bool on) { if (Target != null) Target.sharedMaterial = on ? OnMat : OffMat; }
    }

    /// <summary>
    /// Verteilt Zeiger auf HUD-Elemente, 3D-Bedienelemente oder die Kamera.
    /// </summary>
    public sealed class InteractionRouter
    {
        public Camera Cam;
        public Func<PointerInfo, bool> HudCapture;           // true = HUD verarbeitet den Zeiger
        public Action<PointerInfo> HudEvent;
        public Action<Vector2> OnLook;                         // ein Finger auf freier Flaeche
        public Action<float> OnPinch;                          // Zwei-Finger-Zoom (+ = naeher)

        readonly PointerSource source = new PointerSource();
        readonly List<PointerInfo> pointers = new List<PointerInfo>();
        readonly Dictionary<int, Interactable3D> captured = new Dictionary<int, Interactable3D>();
        readonly HashSet<int> hudOwned = new HashSet<int>();
        readonly Dictionary<int, Vector2> lookPositions = new Dictionary<int, Vector2>();

        public void Update()
        {
            source.Poll(pointers);
            float scroll = PointerSource.ScrollDelta();
            if (Mathf.Abs(scroll) > 0.001f && OnPinch != null) OnPinch(scroll * 0.5f);

            foreach (var p in pointers)
            {
                if (p.Phase == PointerPhase.Began)
                {
                    if (HudCapture != null && HudCapture(p)) { hudOwned.Add(p.Id); continue; }
                    Interactable3D hit = Pick(p.Position);
                    if (hit != null) { captured[p.Id] = hit; hit.OnPointerDown(p); }
                    else lookPositions[p.Id] = p.Position;
                    continue;
                }

                if (hudOwned.Contains(p.Id))
                {
                    if (HudEvent != null) HudEvent(p);
                    if (p.Phase == PointerPhase.Ended) hudOwned.Remove(p.Id);
                    continue;
                }

                Interactable3D it;
                if (captured.TryGetValue(p.Id, out it))
                {
                    if (p.Phase == PointerPhase.Ended) { it.OnPointerUp(p); captured.Remove(p.Id); }
                    else it.OnPointerDrag(p);
                    continue;
                }

                if (lookPositions.ContainsKey(p.Id))
                {
                    if (p.Phase == PointerPhase.Ended) { lookPositions.Remove(p.Id); continue; }
                    lookPositions[p.Id] = p.Position;
                    if (lookPositions.Count >= 2) continue;       // Pinch wird unten behandelt
                    if (OnLook != null) OnLook(p.Delta);
                }
            }

            // Pinch: zwei freie Zeiger
            if (lookPositions.Count == 2 && OnPinch != null)
            {
                Vector2 a = Vector2.zero, b = Vector2.zero; int n = 0; Vector2 da = Vector2.zero, db = Vector2.zero;
                foreach (var p in pointers)
                {
                    if (!lookPositions.ContainsKey(p.Id)) continue;
                    if (n == 0) { a = p.Position; da = p.Delta; } else { b = p.Position; db = p.Delta; }
                    n++;
                }
                if (n == 2)
                {
                    float before = ((a - da) - (b - db)).magnitude;
                    float now = (a - b).magnitude;
                    OnPinch((now - before) / Mathf.Max(Screen.height * 0.25f, 1f));
                }
            }
        }

        Interactable3D Pick(Vector2 screen)
        {
            if (Cam == null) return null;
            Ray ray = Cam.ScreenPointToRay(screen);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 6f, ~0, QueryTriggerInteraction.Collide))
            {
                var proxy = hit.collider.GetComponentInParent<InteractableProxy>();
                if (proxy != null) return proxy.Target;
                return hit.collider.GetComponentInParent<Interactable3D>();
            }
            return null;
        }

        public void ReleaseAll()
        {
            foreach (var kv in captured) kv.Value.OnPointerUp(new PointerInfo { Id = kv.Key, Phase = PointerPhase.Ended });
            captured.Clear(); hudOwned.Clear(); lookPositions.Clear();
        }
    }
}
