using System.Collections.Generic;
using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Track;

namespace ZugSim.Game
{
    /// <summary>Darstellung des Triebzugs: Wagenkasten folgen dem Gleis (Drehgestelle), Kopfwagen mit Cockpit und Frontlicht.</summary>
    public sealed class TrainView : MonoBehaviour
    {
        public Transform HeadAnchor;        // Position/Ausrichtung der Stirnseite des Zuges
        public CabView Cab;
        public Light HeadLight;
        public Transform CenterAnchor;

        TrainSpec spec;
        TrackGraph graph;
        readonly List<Transform> cars = new List<Transform>();
        GameObject headCarShell;
        Renderer[] frontLamps;
        float carLength;
        const float BogieOffset = 9.0f;

        public static TrainView Build(Transform parent, TrainSpec spec, TrackGraph graph)
        {
            var go = new GameObject("Train");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<TrainView>();
            v.spec = spec; v.graph = graph;
            v.carLength = (float)(spec.LengthM / spec.CarCount);

            v.HeadAnchor = new GameObject("HeadAnchor").transform;
            v.HeadAnchor.SetParent(go.transform, false);
            v.CenterAnchor = new GameObject("CenterAnchor").transform;
            v.CenterAnchor.SetParent(go.transform, false);

            for (int i = 0; i < spec.CarCount; i++) v.cars.Add(v.BuildCar(i).transform);

            v.Cab = CabView.Build(v.HeadAnchor, spec);
            var lampGo = new GameObject("HeadLight");
            lampGo.transform.SetParent(v.HeadAnchor, false);
            lampGo.transform.localPosition = new Vector3(0, 2.2f, 1.0f);
            v.HeadLight = lampGo.AddComponent<Light>();
            v.HeadLight.type = LightType.Spot;
            v.HeadLight.range = 450f; v.HeadLight.spotAngle = 38f; v.HeadLight.intensity = 0f;
            v.HeadLight.color = new Color(1f, 0.96f, 0.85f);
            v.HeadLight.shadows = LightShadows.None;
            return v;
        }

        GameObject BuildCar(int index)
        {
            var car = new GameObject("Car" + index);
            car.transform.SetParent(transform, false);
            float L = carLength - 0.4f;
            var white = Procedural.Lit(new Color(0.92f, 0.93f, 0.95f), 0.55f, 0.1f);
            var red = Procedural.Lit(new Color(0.75f, 0.08f, 0.08f), 0.6f, 0.1f);
            var glass = Procedural.Lit(new Color(0.05f, 0.08f, 0.12f), 0.9f, 0.2f);
            var dark = Procedural.Lit(new Color(0.08f, 0.08f, 0.09f), 0.2f);
            var shell = new GameObject("Shell");
            shell.transform.SetParent(car.transform, false);
            // Optionaler Kunst-Austausch: Resources/ZugSim/HeadCar bzw. TrainCar (Pivot Wagenmitte, +Z = Fahrtrichtung)
            var custom = Resources.Load<GameObject>(index == 0 ? "ZugSim/HeadCar" : "ZugSim/TrainCar");
            if (custom != null)
            {
                var inst = Instantiate(custom, shell.transform);
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                if (index == 0) headCarShell = shell;
                return car;
            }
            Procedural.Box("Lower", shell.transform, new Vector3(0, 1.35f, 0), new Vector3(2.9f, 1.5f, L), red);
            Procedural.Box("Upper", shell.transform, new Vector3(0, 2.85f, 0), new Vector3(2.9f, 1.5f, L), white);
            Procedural.Box("Roof", shell.transform, new Vector3(0, 3.7f, 0), new Vector3(2.4f, 0.25f, L - 0.6f), dark);
            Procedural.Box("WindowsL", shell.transform, new Vector3(-1.46f, 2.8f, 0), new Vector3(0.06f, 0.9f, L - 3f), glass);
            Procedural.Box("WindowsR", shell.transform, new Vector3(1.46f, 2.8f, 0), new Vector3(0.06f, 0.9f, L - 3f), glass);
            Procedural.Box("Skirt", shell.transform, new Vector3(0, 0.75f, 0), new Vector3(2.7f, 0.5f, L - 0.2f), dark);
            foreach (float z in new[] { -BogieOffset + 0.0f, BogieOffset })
            {
                float zz = Mathf.Clamp(z, -L * 0.5f + 2f, L * 0.5f - 2f);
                Procedural.Box("Bogie", car.transform, new Vector3(0, 0.55f, zz), new Vector3(2.5f, 0.9f, 2.8f), dark);
            }
            if (index == 1 || spec.CarCount < 3)
            {
                Procedural.Box("Panto", shell.transform, new Vector3(0, 4.1f, 0), new Vector3(0.1f, 0.8f, 0.1f), dark);
                Procedural.Box("PantoHead", shell.transform, new Vector3(0, 4.55f, 0), new Vector3(1.8f, 0.05f, 0.1f), dark);
            }
            if (index == 0)
            {
                headCarShell = shell;
                // Stirnseite: Frontscheibe und Lampen (Aussenansicht)
                var lamp = Procedural.Emissive(new Color(1f, 0.97f, 0.85f), 2.5f);
                Procedural.Box("FrontGlass", shell.transform, new Vector3(0, 2.75f, L * 0.5f + 0.02f), new Vector3(2.3f, 1.0f, 0.05f), glass);
                var l1 = Procedural.Box("LampL", shell.transform, new Vector3(-1.0f, 1.25f, L * 0.5f + 0.04f), new Vector3(0.3f, 0.2f, 0.05f), lamp);
                var l2 = Procedural.Box("LampR", shell.transform, new Vector3(1.0f, 1.25f, L * 0.5f + 0.04f), new Vector3(0.3f, 0.2f, 0.05f), lamp);
                var l3 = Procedural.Box("LampTop", shell.transform, new Vector3(0, 1.9f, L * 0.5f + 0.04f), new Vector3(0.3f, 0.2f, 0.05f), lamp);
                frontLamps = new[] { l1.GetComponent<Renderer>(), l2.GetComponent<Renderer>(), l3.GetComponent<Renderer>() };
            }
            return car;
        }

        /// <summary>Wagen-Stirnseite des Kopfwagens: lokale Z-Koordinate (vorderes Ende) im Wagenkasten.</summary>
        public void UpdateFromWorld(SimulationWorld w, bool cabView, float dt)
        {
            var f = w.Follower;
            // Wagen von vorn nach hinten
            for (int i = 0; i < cars.Count; i++)
            {
                float centerBehind = carLength * (i + 0.5f);
                TrackPose front = PoseBehind(f, centerBehind - BogieOffset);
                TrackPose rear = PoseBehind(f, centerBehind + BogieOffset);
                Vector3 pf = TrackView.ToVector(front), pr = TrackView.ToVector(rear);
                Vector3 dir = pf - pr;
                cars[i].position = (pf + pr) * 0.5f;
                if (dir.sqrMagnitude > 1e-6f) cars[i].rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            // Stirnseite = vorderes Ende des Kopfwagens
            TrackPose head = PoseBehind(f, 0);
            HeadAnchor.position = TrackView.ToVector(head);
            HeadAnchor.rotation = TrackView.ToRotation(head);
            CenterAnchor.position = cars[Mathf.Min(1, cars.Count - 1)].position + Vector3.up * 2f;
            CenterAnchor.rotation = cars[Mathf.Min(1, cars.Count - 1)].rotation;
            if (headCarShell != null) headCarShell.SetActive(!cabView);
            Cab.gameObject.SetActive(cabView);
        }

        TrackPose PoseBehind(TrackFollower f, float behind)
        {
            int e; double o;
            if (behind <= 0) { e = f.EdgeId; o = f.Offset - behind; if (o > graph.Edges[e].Length) o = graph.Edges[e].Length; }
            else f.LocateBehind(behind, out e, out o);
            return graph.PoseAt(e, o);
        }

        public void SetLights(bool on, float lightScale)
        {
            HeadLight.intensity = on ? 6f * lightScale : 0f;
            if (frontLamps != null) { /* Lampen sind immer emissive; Nachtbetrieb wirkt ueber Scheinwerfer */ }
        }
    }
}
