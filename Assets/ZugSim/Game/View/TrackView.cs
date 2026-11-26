using System.Collections.Generic;
using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Routes;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Game
{
    /// <summary>
    /// Erzeugt die Strecke prozedural aus dem Gleisgraphen: Schotterbett mit Schwellen-Textur, Schienen, Gelaende,
    /// Oberleitungsmasten, Baeume, Bahnsteige und Signale. Alles ist zu Chunks zusammengefasst (wenige Draw Calls).
    /// </summary>
    public sealed class TrackView : MonoBehaviour
    {
        const float ChunkLength = 240f;
        const float SectionStep = 2f;

        readonly Dictionary<int, SignalView> signals = new Dictionary<int, SignalView>();
        TrackGraph graph;
        Material railMat;

        public static Vector3 ToVector(TrackPose p) { return new Vector3((float)p.X, (float)p.Y, (float)p.Z); }

        public static Quaternion ToRotation(TrackPose p)
        {
            Vector3 fwd = new Vector3(
                (float)(System.Math.Cos(p.Pitch) * System.Math.Sin(p.Heading)),
                (float)System.Math.Sin(p.Pitch),
                (float)(System.Math.Cos(p.Pitch) * System.Math.Cos(p.Heading)));
            return Quaternion.LookRotation(fwd, Vector3.up);
        }

        public static TrackView Build(Transform parent, RouteData route, Timetable timetable)
        {
            var go = new GameObject("Track");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<TrackView>();
            view.graph = route.Graph;
            view.BuildAll(route, timetable);
            return view;
        }

        void BuildAll(RouteData route, Timetable tt)
        {
            var ballastMat = Procedural.Lit(Color.white, 0.05f, 0f, Procedural.BallastTexture());
            railMat = Procedural.Lit(new Color(0.45f, 0.45f, 0.48f), 0.75f, 0.8f);
            var grassMat = Procedural.Lit(Color.white, 0.02f, 0f, Procedural.GrassTexture());
            var mastMat = Procedural.Lit(new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.6f);
            var wireMat = Procedural.Lit(new Color(0.1f, 0.1f, 0.1f), 0.3f);
            var trunkMat = Procedural.Lit(new Color(0.3f, 0.2f, 0.12f), 0.1f);
            var leafMat = Procedural.Lit(new Color(0.12f, 0.35f, 0.14f), 0.1f);
            var rng = new System.Random(7);

            foreach (var edge in graph.Edges)
            {
                var geo = edge.Geometry;
                float len = (float)edge.Length;
                for (float c0 = 0; c0 < len; c0 += ChunkLength)
                {
                    float c1 = Mathf.Min(c0 + ChunkLength, len);
                    var ballast = new MeshBuilder(); var rails = new MeshBuilder(); var ground = new MeshBuilder();
                    var masts = new MeshBuilder(); var wires = new MeshBuilder(); var trunks = new MeshBuilder(); var leaves = new MeshBuilder();

                    Vector3 prevPos = Vector3.zero, prevRight = Vector3.zero;
                    bool havePrev = false;
                    float u = c0;
                    while (true)
                    {
                        float o = Mathf.Min(u, c1);
                        TrackPose p = geo.Sample(o);
                        Vector3 pos = ToVector(p);
                        Quaternion rot = ToRotation(p);
                        Vector3 right = rot * Vector3.right;
                        if (havePrev)
                        {
                            float v0 = (u - SectionStep) / 2.4f, v1 = u / 2.4f;
                            Strip(ballast, prevPos, prevRight, pos, right, -2.7f, -0.55f, -1.85f, -0.05f, v0, v1, Vector3.left + Vector3.up);
                            Strip(ballast, prevPos, prevRight, pos, right, -1.85f, -0.05f, 1.85f, -0.05f, v0, v1, Vector3.up);
                            Strip(ballast, prevPos, prevRight, pos, right, 1.85f, -0.05f, 2.7f, -0.55f, v0, v1, Vector3.right + Vector3.up);
                            foreach (float sx in new[] { -0.7175f, 0.7175f })
                            {
                                Strip(rails, prevPos, prevRight, pos, right, sx - 0.035f, -0.05f, sx - 0.035f, 0.115f, v0, v1, Vector3.left);
                                Strip(rails, prevPos, prevRight, pos, right, sx - 0.035f, 0.115f, sx + 0.035f, 0.115f, v0, v1, Vector3.up);
                                Strip(rails, prevPos, prevRight, pos, right, sx + 0.035f, 0.115f, sx + 0.035f, -0.05f, v0, v1, Vector3.right);
                            }
                            // Gelaende (breiter Streifen, Welt-UVs)
                            Strip(ground, prevPos, prevRight, pos, right, -140f, -0.65f, -2.7f, -0.58f, prevPos.z / 12f, pos.z / 12f, Vector3.up, 11.5f);
                            Strip(ground, prevPos, prevRight, pos, right, 2.7f, -0.58f, 140f, -0.65f, prevPos.z / 12f, pos.z / 12f, Vector3.up, 11.5f);
                        }
                        prevPos = pos; prevRight = right; havePrev = true;
                        if (u >= c1) break;
                        u = Mathf.Min(u + SectionStep, c1);
                    }

                    // Oberleitungsmasten alle 60 m
                    float side = edge.Id == route.EdgeLoop ? 1f : -1f;
                    for (float m = Mathf.Ceil(c0 / 60f) * 60f; m < c1; m += 60f)
                    {
                        TrackPose pm = geo.Sample(m);
                        Quaternion rm = ToRotation(pm);
                        Vector3 pos = ToVector(pm) + rm * new Vector3(side * 3.3f, 0, 0);
                        masts.AddBox(pos + Vector3.up * 3.2f, rm, new Vector3(0.28f, 6.4f, 0.28f));
                        masts.AddBox(ToVector(pm) + rm * new Vector3(side * 1.6f, 6.1f, 0), rm, new Vector3(3.4f, 0.12f, 0.15f));
                        TrackPose pn = geo.Sample(Mathf.Min(m + 60f, len));
                        Vector3 a = ToVector(pm) + Vector3.up * 5.5f, b = ToVector(pn) + Vector3.up * 5.5f;
                        AddWire(wires, a, b, 0.04f);
                        AddWire(wires, a + Vector3.up * 0.6f, b + Vector3.up * 0.6f, 0.025f);
                    }

                    // Baeume (deterministisch zufaellig)
                    for (float t = c0; t < c1; t += 11f)
                    {
                        if (rng.NextDouble() < 0.35) continue;
                        float lateral = (float)(18 + rng.NextDouble() * 90) * (rng.NextDouble() < 0.5 ? -1 : 1);
                        TrackPose pt = geo.Sample(t + (float)rng.NextDouble() * 8f);
                        Quaternion rt = ToRotation(pt);
                        Vector3 pos = ToVector(pt) + rt * new Vector3(lateral, -0.6f, 0);
                        float h = 6f + (float)rng.NextDouble() * 7f;
                        trunks.AddBox(pos + Vector3.up * (h * 0.2f), Quaternion.identity, new Vector3(0.5f, h * 0.4f, 0.5f));
                        leaves.AddPyramid(pos + Vector3.up * (h * 0.25f), h * 0.32f, h * 0.8f);
                    }

                    Emit(ballast, "Ballast", ballastMat); Emit(rails, "Rails", railMat); Emit(ground, "Ground", grassMat);
                    Emit(masts, "Masts", mastMat); Emit(wires, "Wires", wireMat); Emit(trunks, "Trunks", trunkMat); Emit(leaves, "Leaves", leafMat);
                }
            }

            BuildStations(route, tt);
            BuildSignals();
        }

        // Streifen zwischen zwei Querschnitten. Querschnitt = Linie von (xa,ya) nach (xb,yb) in Gleis-Koordinaten.
        static void Strip(MeshBuilder mb, Vector3 p0, Vector3 r0, Vector3 p1, Vector3 r1,
            float xa, float ya, float xb, float yb, float v0, float v1, Vector3 outward, float uMax = 1f)
        {
            Vector3 a0 = p0 + r0 * xa + Vector3.up * ya, b0 = p0 + r0 * xb + Vector3.up * yb;
            Vector3 a1 = p1 + r1 * xa + Vector3.up * ya, b1 = p1 + r1 * xb + Vector3.up * yb;
            // Reihenfolge: a0 -> a1 -> b1 -> b0 (um das Rechteck herum)
            mb.AddQuad(a0, a1, b1, b0, outward, new Vector2(0, v0), new Vector2(0, v1), new Vector2(uMax, v1), new Vector2(uMax, v0));
        }

        static void AddWire(MeshBuilder mb, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 0.01f) return;
            mb.AddBox((a + b) * 0.5f, Quaternion.LookRotation(d.normalized), new Vector3(thickness, thickness, d.magnitude));
        }

        void Emit(MeshBuilder mb, string name, Material mat)
        {
            if (mb.VertexCount == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = (name == "Ground" || name == "Ballast" || name == "Wires" || name == "Rails")
                ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            go.isStatic = true;
        }

        void BuildStations(RouteData route, Timetable tt)
        {
            if (tt == null) return;
            var platMat = Procedural.Lit(new Color(0.62f, 0.62f, 0.64f), 0.1f);
            var edgeMat = Procedural.Lit(new Color(0.95f, 0.85f, 0.1f), 0.2f);
            var roofMat = Procedural.Lit(new Color(0.25f, 0.3f, 0.35f), 0.3f);
            var signMat = Procedural.Lit(new Color(0.05f, 0.2f, 0.55f), 0.3f);
            foreach (var stop in tt.Stops)
            {
                var edge = graph.Edges[stop.EdgeId];
                var geo = edge.Geometry;
                float center = (float)stop.TargetOffset - 45f;
                TrackPose pc = geo.Sample(center);
                Quaternion rc = ToRotation(pc);
                var go = new GameObject("Station_" + stop.Station);
                go.transform.SetParent(transform, false);
                go.transform.position = ToVector(pc);
                go.transform.rotation = Quaternion.Euler(0, rc.eulerAngles.y, 0);
                float pLen = 160f;
                Procedural.Box("Platform", go.transform, new Vector3(3.9f, 0.38f - 0.05f, 0), new Vector3(4.0f, 0.76f, pLen), platMat);
                Procedural.Box("Edge", go.transform, new Vector3(2.3f, 0.77f - 0.05f, 0), new Vector3(0.5f, 0.02f, pLen), edgeMat);
                for (int i = -2; i <= 2; i++)
                {
                    Procedural.Box("Post", go.transform, new Vector3(5.2f, 2.0f, i * 30f), new Vector3(0.2f, 4.0f, 0.2f), roofMat);
                }
                Procedural.Box("Roof", go.transform, new Vector3(4.6f, 4.05f, 0), new Vector3(3.6f, 0.15f, 70f), roofMat);
                var name = Procedural.Text("Name", go.transform, new Vector3(2.8f, 2.6f, 0), stop.Station, 0.05f, Color.white);
                name.transform.localRotation = Quaternion.Euler(0, -90f, 0);
                Procedural.Box("NameBoard", go.transform, new Vector3(2.75f, 2.6f, 0), new Vector3(0.05f, 0.9f, 9f), signMat);
                // Halttafel an der Soll-Halteposition
                TrackPose ph = geo.Sample((float)stop.TargetOffset);
                var marker = new GameObject("StopMarker");
                marker.transform.SetParent(transform, false);
                marker.transform.position = ToVector(ph);
                marker.transform.rotation = Quaternion.Euler(0, ToRotation(ph).eulerAngles.y, 0);
                Procedural.Box("Pole", marker.transform, new Vector3(2.6f, 0.8f, 0), new Vector3(0.1f, 1.6f, 0.1f), roofMat);
                var board = Procedural.Box("Board", marker.transform, new Vector3(2.6f, 1.7f, 0), new Vector3(0.05f, 0.6f, 0.6f), Procedural.Emissive(new Color(1f, 0.9f, 0.1f), 0.6f));
            }
        }

        void BuildSignals()
        {
            foreach (var s in graph.Signals)
            {
                var edge = graph.Edges[s.EdgeId];
                TrackPose p = edge.Geometry.Sample((float)s.Offset);
                var go = new GameObject("Signal_" + s.Name);
                go.transform.SetParent(transform, false);
                go.transform.position = ToVector(p) + ToRotation(p) * new Vector3(3.0f, 0, 0);
                go.transform.rotation = Quaternion.Euler(0, ToRotation(p).eulerAngles.y, 0);
                var v = SignalView.Build(go.transform, s);
                signals[s.Id] = v;
            }
        }

        public void UpdateSignals()
        {
            foreach (var kv in signals) kv.Value.Apply(graph.Signals[kv.Key]);
        }
    }

    /// <summary>Lichthauptsignal mit rot / gruen / gelb.</summary>
    public sealed class SignalView : MonoBehaviour
    {
        Renderer red, green, yellow;
        Material redOn, redOff, greenOn, greenOff, yellowOn, yellowOff;
        SignalAspect last = (SignalAspect)(-1);

        public static SignalView Build(Transform parent, TrackSignal s)
        {
            var v = parent.gameObject.AddComponent<SignalView>();
            var pole = Procedural.Lit(new Color(0.25f, 0.25f, 0.27f), 0.3f);
            Procedural.Box("Pole", parent, new Vector3(0, 2.5f, 0), new Vector3(0.18f, 5f, 0.18f), pole);
            Procedural.Box("Head", parent, new Vector3(0, 5.2f, -0.1f), new Vector3(0.7f, 1.8f, 0.3f), Procedural.Lit(new Color(0.04f, 0.04f, 0.04f), 0.3f));
            v.redOn = Procedural.Emissive(new Color(1f, 0.1f, 0.05f), 4f); v.redOff = Procedural.Lit(new Color(0.2f, 0.02f, 0.02f), 0.2f);
            v.greenOn = Procedural.Emissive(new Color(0.1f, 1f, 0.2f), 4f); v.greenOff = Procedural.Lit(new Color(0.02f, 0.15f, 0.04f), 0.2f);
            v.yellowOn = Procedural.Emissive(new Color(1f, 0.8f, 0.1f), 4f); v.yellowOff = Procedural.Lit(new Color(0.18f, 0.14f, 0.02f), 0.2f);
            v.red = Procedural.Sphere("Red", parent, new Vector3(0, 5.75f, -0.28f), 0.2f, v.redOff).GetComponent<Renderer>();
            v.yellow = Procedural.Sphere("Yellow", parent, new Vector3(0, 5.2f, -0.28f), 0.2f, v.yellowOff).GetComponent<Renderer>();
            v.green = Procedural.Sphere("Green", parent, new Vector3(0, 4.65f, -0.28f), 0.2f, v.greenOff).GetComponent<Renderer>();
            // Signal zeigt entgegen der Fahrtrichtung (Lampen zum Zug)
            parent.rotation = parent.rotation * Quaternion.Euler(0, 180f, 0);
            Procedural.Text("Name", parent, new Vector3(0, 6.6f, 0), s.Name, 0.04f, Color.white);
            return v;
        }

        public void Apply(TrackSignal s)
        {
            if (s.Aspect == last) return;
            last = s.Aspect;
            red.sharedMaterial = s.Aspect == SignalAspect.Halt ? redOn : redOff;
            green.sharedMaterial = s.Aspect != SignalAspect.Halt ? greenOn : greenOff;
            yellow.sharedMaterial = s.Aspect == SignalAspect.ProceedSlow ? yellowOn : yellowOff;
        }
    }
}
