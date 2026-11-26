using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Safety;
using ZugSim.Core.Service;

namespace ZugSim.Game
{
    /// <summary>
    /// Prozedurales Fuehrerstand-Cockpit mit Touch-Hebeln, Tastern und funktionierenden Instrumenten.
    /// Wird am Zugkopf befestigt; Koordinaten lokal: +Z = Fahrtrichtung, +X = rechts, +Y = oben, Ursprung = Schienenoberkante an der Stirnseite.
    /// </summary>
    public sealed class CabView : MonoBehaviour
    {
        public static readonly Vector3 EyeLocal = new Vector3(-0.35f, 2.62f, -2.3f);

        public Lever3D ThrottleLever, BrakeLever;
        public PushButton3D HornButton, DoorOpenButton, DoorCloseButton, SifaButton, ReleaseButton;
        public Gauge3D Speedometer, MainPipeGauge, CylinderGauge, ForceGauge;
        public Lamp3D LampSifa, LampWarning, LampBrake, LampDoors, LampDeparture;

        const float BrakeEmergencyThreshold = 0.97f;
        const float BrakeServiceRange = 0.85f;

        public static CabView Build(Transform parent, TrainSpec spec)
        {
            var root = new GameObject("Cab");
            root.transform.SetParent(parent, false);
            var cab = root.AddComponent<CabView>();

            var dark = Procedural.Lit(new Color(0.10f, 0.11f, 0.12f), 0.25f);
            var panel = Procedural.Lit(new Color(0.20f, 0.22f, 0.25f), 0.3f);
            var trim = Procedural.Lit(new Color(0.55f, 0.57f, 0.60f), 0.5f, 0.4f);
            var floor = Procedural.Lit(new Color(0.18f, 0.18f, 0.2f), 0.1f);
            var red = Procedural.Lit(new Color(0.75f, 0.1f, 0.08f), 0.4f);
            var black = Procedural.Lit(new Color(0.03f, 0.03f, 0.03f), 0.4f);
            var green = Procedural.Lit(new Color(0.1f, 0.5f, 0.15f), 0.4f);

            // Kabinenhuelle (Rahmen: Seitenwaende, Dach, Fensterpfosten) - Frontscheibe bleibt offen
            Procedural.Box("Floor", root.transform, new Vector3(0, 1.0f, -1.6f), new Vector3(2.9f, 0.1f, 3.2f), floor);
            Procedural.Box("WallL", root.transform, new Vector3(-1.45f, 2.4f, -1.9f), new Vector3(0.1f, 2.8f, 2.6f), dark);
            Procedural.Box("WallR", root.transform, new Vector3(1.45f, 2.4f, -1.9f), new Vector3(0.1f, 2.8f, 2.6f), dark);
            Procedural.Box("Back", root.transform, new Vector3(0, 2.4f, -3.2f), new Vector3(2.9f, 2.8f, 0.1f), dark);
            Procedural.Box("Roof", root.transform, new Vector3(0, 3.85f, -1.7f), new Vector3(2.9f, 0.1f, 3.0f), dark);
            Procedural.Box("PillarL", root.transform, new Vector3(-1.1f, 2.85f, -0.35f), new Vector3(0.12f, 2.0f, 0.12f), dark);
            Procedural.Box("PillarR", root.transform, new Vector3(1.1f, 2.85f, -0.35f), new Vector3(0.12f, 2.0f, 0.12f), dark);
            Procedural.Box("PillarM", root.transform, new Vector3(0.0f, 2.85f, -0.35f), new Vector3(0.06f, 2.0f, 0.06f), black);
            Procedural.Box("Header", root.transform, new Vector3(0, 3.82f, -0.35f), new Vector3(2.9f, 0.2f, 0.4f), dark);

            // Pult
            var desk = new GameObject("Desk");
            desk.transform.SetParent(root.transform, false);
            desk.transform.localPosition = new Vector3(0, 1.75f, -1.25f);
            Procedural.Box("DeskBase", desk.transform, new Vector3(0, -0.4f, 0.05f), new Vector3(2.9f, 0.8f, 1.2f), panel);
            var slope = new GameObject("Slope");
            slope.transform.SetParent(desk.transform, false);
            slope.transform.localPosition = new Vector3(0, 0.05f, 0.1f);
            slope.transform.localRotation = Quaternion.Euler(-25f, 0, 0);
            Procedural.Box("DeskTop", slope.transform, Vector3.zero, new Vector3(2.9f, 0.06f, 0.9f), panel);

            // Instrumente (auf der Pultschraege, Ziffernblatt zeigt Richtung Fahrer = -Z)
            cab.Speedometer = MakeGauge(slope.transform, "Tacho", new Vector3(0.22f, 0.06f, 0.18f), 0.30f,
                0f, Units.MsToKmh(spec.MaxSpeedMs) < 200 ? 200f : 300f, -135f, 135f,
                Units.MsToKmh(spec.MaxSpeedMs) < 200 ? 10 : 15, 4, Color.white, true, "km/h");
            cab.MainPipeGauge = MakeGauge(slope.transform, "HL/BZ", new Vector3(0.75f, 0.06f, 0.20f), 0.20f, 0f, 6f, -120f, 120f, 6, 2, new Color(0.9f, 0.9f, 0.3f), false, "bar");
            var cylNeedle = MakeNeedle(cab.MainPipeGauge.transform, 0.085f, new Color(0.3f, 0.8f, 1f));
            cab.CylinderGauge = cab.MainPipeGauge.gameObject.AddComponent<Gauge3D>();
            cab.CylinderGauge.Needle = cylNeedle;
            cab.CylinderGauge.MinValue = 0; cab.CylinderGauge.MaxValue = 6; cab.CylinderGauge.MinAngle = -120; cab.CylinderGauge.MaxAngle = 120;
            cab.ForceGauge = MakeGauge(slope.transform, "Zugkraft", new Vector3(1.15f, 0.06f, 0.2f), 0.16f, -1f, 1f, -120f, 120f, 4, 2, new Color(0.4f, 0.9f, 0.5f), false, "Zugkr.");

            // Meldeleuchten
            cab.LampSifa = MakeLamp(slope.transform, "Sifa", new Vector3(-0.15f, 0.05f, 0.32f), new Color(1f, 0.85f, 0.1f), "SIFA");
            cab.LampWarning = MakeLamp(slope.transform, "Ueberw", new Vector3(0.1f, 0.05f, 0.32f), new Color(1f, 0.3f, 0.1f), "ÜBERW.");
            cab.LampBrake = MakeLamp(slope.transform, "Zwang", new Vector3(0.35f, 0.05f, 0.32f), new Color(1f, 0.1f, 0.1f), "ZWANG");
            cab.LampDoors = MakeLamp(slope.transform, "Tueren", new Vector3(0.6f, 0.05f, 0.32f), new Color(0.2f, 1f, 0.3f), "TÜREN");
            cab.LampDeparture = MakeLamp(slope.transform, "Abfahrt", new Vector3(0.85f, 0.05f, 0.32f), new Color(0.3f, 0.8f, 1f), "ABF.");

            // Hebel (grosse Touch-Ziele)
            cab.ThrottleLever = MakeLever(slope.transform, "Fahrschalter", new Vector3(-0.95f, 0.04f, 0.10f), green, spec.ThrottleNotches, -32f, 32f, "FAHREN");
            cab.BrakeLever = MakeLever(slope.transform, "Bremshebel", new Vector3(-0.50f, 0.04f, 0.10f), red, 0, -32f, 32f, "BREMSE");

            // Taster
            cab.HornButton = MakeButton(slope.transform, "Horn", new Vector3(1.20f, 0.04f, -0.25f), new Color(0.9f, 0.8f, 0.1f), "HORN");
            cab.DoorOpenButton = MakeButton(slope.transform, "TuerAuf", new Vector3(0.78f, 0.04f, -0.28f), new Color(0.2f, 0.7f, 0.3f), "TÜR AUF");
            cab.DoorCloseButton = MakeButton(slope.transform, "TuerZu", new Vector3(0.48f, 0.04f, -0.28f), new Color(0.8f, 0.2f, 0.2f), "TÜR ZU");
            cab.SifaButton = MakeButton(slope.transform, "SifaTaste", new Vector3(-0.15f, 0.04f, -0.28f), new Color(0.9f, 0.9f, 0.9f), "SIFA");
            cab.ReleaseButton = MakeButton(slope.transform, "Quittieren", new Vector3(0.15f, 0.04f, -0.28f), new Color(0.2f, 0.4f, 0.9f), "FREI");

            return cab;
        }

        static Lamp3D MakeLamp(Transform parent, string name, Vector3 pos, Color color, string label)
        {
            var go = Procedural.Sphere(name, parent, pos, 0.022f, Procedural.Lit(color * 0.25f, 0.2f));
            var lamp = go.AddComponent<Lamp3D>();
            lamp.Target = go.GetComponent<Renderer>();
            lamp.OffMat = Procedural.Lit(color * 0.22f, 0.2f);
            lamp.OnMat = Procedural.Emissive(color, 3f);
            Procedural.Text(name + "Label", parent, pos + new Vector3(0, 0.012f, -0.035f), label, 0.006f, new Color(0.85f, 0.85f, 0.85f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            lamp.Set(false);
            return lamp;
        }

        static Transform MakeNeedle(Transform gaugeRoot, float length, Color color)
        {
            var needle = new GameObject("Needle").transform;
            needle.SetParent(gaugeRoot, false);
            needle.localPosition = new Vector3(0, 0, 0.004f);
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(needle, false);
            bar.transform.localScale = new Vector3(0.005f, length, 0.002f);
            bar.transform.localPosition = new Vector3(0, length * 0.5f, 0);
            bar.GetComponent<Renderer>().sharedMaterial = Procedural.Unlit(color);
            return needle;
        }

        static Gauge3D MakeGauge(Transform parent, string name, Vector3 pos, float diameter, float min, float max, float a0, float a1,
            int majors, int minors, Color arc, bool numbers, string unit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90f, 0, 0);   // Ziffernblatt flach auf dem Pult, schaut nach oben/zum Fahrer
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(diameter, diameter, 1);
            var tex = Procedural.GaugeFace(a0 < 0 ? a0 + 360f : a0, a1 < 0 ? a1 + 360f : a1, majors, minors, arc);
            var mat = Procedural.Lit(Color.white, 0.1f, 0f, tex);
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            var bezel = Procedural.Cylinder(name + "Bezel", go.transform, new Vector3(0, 0, 0.003f), diameter * 0.52f, 0.004f, Procedural.Lit(new Color(0.05f, 0.05f, 0.06f), 0.3f));
            bezel.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            var needle = MakeNeedle(go.transform, diameter * 0.42f, new Color(1f, 0.35f, 0.1f));
            var g = go.AddComponent<Gauge3D>();
            g.Needle = needle; g.MinValue = min; g.MaxValue = max; g.MinAngle = a0; g.MaxAngle = a1;

            if (numbers)
            {
                int count = majors;
                for (int i = 0; i <= count; i++)
                {
                    float t = i / (float)count;
                    float ang = Mathf.Lerp(a0, a1, t) * Mathf.Deg2Rad;
                    float val = Mathf.Lerp(min, max, t);
                    if (i % 2 == 1 && count > 12) continue;
                    var tm = Procedural.Text("n" + i, go.transform, new Vector3(Mathf.Sin(ang), Mathf.Cos(ang), 0.004f) * diameter * 0.27f,
                        Mathf.RoundToInt(val).ToString(), 0.0065f * diameter / 0.3f, Color.white);
                }
            }
            var label = Procedural.Text("unit", go.transform, new Vector3(0, -diameter * 0.18f, 0.004f), unit, 0.006f * diameter / 0.3f, new Color(0.8f, 0.8f, 0.8f));
            return g;
        }

        static Lever3D MakeLever(Transform parent, string name, Vector3 pos, Material knobMat, int notches, float minA, float maxA, string label)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            Procedural.Box("Slot", root.transform, new Vector3(0, 0.005f, 0), new Vector3(0.07f, 0.01f, 0.38f), Procedural.Lit(Color.black, 0.2f));
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, 0.01f, 0);
            Procedural.Cylinder("Shaft", pivot, new Vector3(0, 0.13f, 0), 0.012f, 0.26f, Procedural.Lit(new Color(0.7f, 0.7f, 0.72f), 0.6f, 0.8f));
            var knob = Procedural.Sphere("Knob", pivot, new Vector3(0, 0.29f, 0), 0.04f, knobMat);
            // Grosse, unsichtbare Touchflaeche
            var hit = new GameObject("Hit");
            hit.transform.SetParent(pivot, false);
            hit.transform.localPosition = new Vector3(0, 0.2f, 0);
            var cap = hit.AddComponent<CapsuleCollider>();
            cap.radius = 0.11f; cap.height = 0.5f;
            var lever = root.AddComponent<Lever3D>();
            lever.Pivot = pivot; lever.Notches = notches; lever.MinAngle = minA; lever.MaxAngle = maxA;
            hit.AddComponent<InteractableProxy>().Target = lever;
            var t = Procedural.Text(name + "Label", root.transform, new Vector3(0, 0.006f, -0.27f), label, 0.008f, new Color(0.9f, 0.9f, 0.9f));
            t.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            return lever;
        }

        static PushButton3D MakeButton(Transform parent, string name, Vector3 pos, Color color, string label)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            Procedural.Cylinder("Base", root.transform, new Vector3(0, 0.008f, 0), 0.065f, 0.016f, Procedural.Lit(new Color(0.08f, 0.08f, 0.09f), 0.3f));
            var cap = Procedural.Cylinder("Cap", root.transform, new Vector3(0, 0.03f, 0), 0.052f, 0.03f, Procedural.Lit(color, 0.5f));
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.1f; col.height = 0.12f; col.center = new Vector3(0, 0.04f, 0);
            var b = root.AddComponent<PushButton3D>();
            b.Cap = cap.transform;
            var t = Procedural.Text(name + "Label", root.transform, new Vector3(0, 0.006f, -0.1f), label, 0.0065f, new Color(0.9f, 0.9f, 0.9f));
            t.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            return b;
        }

        // ---------- Betrieb ----------

        /// <summary>Hebel -> Fahrerbefehl.</summary>
        public void ReadControls(ref DriverInput input)
        {
            input.Throttle = ThrottleLever.Value;
            float b = BrakeLever.Value;
            input.EmergencyBrake = b >= BrakeEmergencyThreshold;
            input.Brake = Mathf.Clamp01(b / BrakeServiceRange);
        }

        public void SetLevers(float throttle, float brakeLeverValue)
        {
            if (!ThrottleLever.Dragging) ThrottleLever.SetValue(throttle);
            if (!BrakeLever.Dragging) BrakeLever.SetValue(brakeLeverValue);
        }

        public void UpdateInstruments(SimulationWorld w, float blink)
        {
            var t = w.Train;
            Speedometer.Set((float)t.SpeedKmh);
            MainPipeGauge.Set((float)t.Brakes.MainPipeBar);
            CylinderGauge.Set((float)t.Brakes.CylinderBar);
            ForceGauge.Set((float)((t.TractiveForceN - t.BrakeForceN) / t.Spec.MaxTractiveForceN));
            LampSifa.Set(w.Safety.SifaWarning && blink > 0.5f);
            LampWarning.Set(w.Safety.State == SafetyState.Warning && blink > 0.5f);
            LampBrake.Set(w.Safety.EmergencyBrakeActive);
            LampDoors.Set(t.Doors.State != DoorState.Closed);
            LampDeparture.Set(w.Service != null && w.Service.DepartureReady);
        }
    }

    /// <summary>Leitet Treffer eines Kindkolliders an einen Interactable3D weiter.</summary>
    public sealed class InteractableProxy : MonoBehaviour
    {
        public Interactable3D Target;
    }
}
