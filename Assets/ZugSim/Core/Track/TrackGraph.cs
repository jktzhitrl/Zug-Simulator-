using System;
using System.Collections.Generic;

namespace ZugSim.Core.Track
{
    public enum SignalAspect { Halt, Proceed, ProceedSlow }

    public sealed class TrackSignal
    {
        public int Id;
        public string Name;
        public int EdgeId;
        public double Offset;
        public SignalAspect Aspect = SignalAspect.Proceed;
        /// <summary>Geschwindigkeit bei "Fahrt mit Langsamfahrt" (z. B. Abzweig).</summary>
        public double SlowSpeedMs = Units.KmhToMs(60);
        /// <summary>Weichenknoten, den das Signal deckt (-1 = keiner). Zeigt Langsamfahrt bei Abzweig.</summary>
        public int GuardedSwitchNode = -1;
        /// <summary>Haltbegriff erzwungen (Szenario/Fahrdienstleiter).</summary>
        public bool ForcedHalt;
    }

    public struct GradeSegment { public double Start; public double Permille; }
    public struct CurveSegment { public double Start, End, SignedRadius; }
    public struct SpeedZone { public double Start, End, LimitMs; }

    public sealed class TrackNode
    {
        public int Id;
        public string Name;
        public readonly List<int> Incoming = new List<int>();
        /// <summary>Index 0 = Gerade, Index 1 = Abzweig.</summary>
        public readonly List<int> Outgoing = new List<int>();
        public bool Diverging;
        public bool IsSwitch { get { return Outgoing.Count > 1; } }
    }

    public sealed class TrackEdge
    {
        public int Id, FromNode, ToNode;
        public double Length;
        public double DefaultSpeedMs;
        public string Name;
        public readonly List<GradeSegment> Grades = new List<GradeSegment>();
        public readonly List<CurveSegment> Curves = new List<CurveSegment>();
        public readonly List<SpeedZone> Zones = new List<SpeedZone>();
        public readonly List<TrackSignal> Signals = new List<TrackSignal>();
        public EdgeGeometry Geometry;

        public TrackEdge Grade(double startOffset, double permille)
        {
            Grades.Add(new GradeSegment { Start = startOffset, Permille = permille });
            Grades.Sort((a, b) => a.Start.CompareTo(b.Start));
            return this;
        }

        /// <summary>signedRadius &gt; 0: Rechtskurve, &lt; 0: Linkskurve.</summary>
        public TrackEdge Curve(double start, double end, double signedRadius)
        {
            Curves.Add(new CurveSegment { Start = start, End = end, SignedRadius = signedRadius });
            return this;
        }

        public TrackEdge Limit(double start, double end, double kmh)
        {
            Zones.Add(new SpeedZone { Start = start, End = end, LimitMs = Units.KmhToMs(kmh) });
            return this;
        }

        public double GradeAt(double offset)
        {
            double g = 0;
            for (int i = 0; i < Grades.Count; i++)
            {
                if (Grades[i].Start <= offset) g = Grades[i].Permille; else break;
            }
            return g;
        }

        public double SignedRadiusAt(double offset)
        {
            for (int i = 0; i < Curves.Count; i++)
                if (offset >= Curves[i].Start && offset < Curves[i].End) return Curves[i].SignedRadius;
            return 0;
        }

        public double LimitAt(double offset)
        {
            double lim = DefaultSpeedMs;
            for (int i = 0; i < Zones.Count; i++)
                if (offset >= Zones[i].Start && offset < Zones[i].End && Zones[i].LimitMs < lim) lim = Zones[i].LimitMs;
            return lim;
        }
    }

    /// <summary>Gleisnetz als gerichteter Graph aus Knoten (Weichen, Enden) und Kanten (Streckenabschnitte).</summary>
    public sealed class TrackGraph
    {
        /// <summary>Weiche ist verriegelt, wenn ein Zug sie innerhalb dieser Strecke anfaehrt.</summary>
        public const double SwitchLockDistanceM = 120.0;

        public readonly List<TrackNode> Nodes = new List<TrackNode>();
        public readonly List<TrackEdge> Edges = new List<TrackEdge>();
        public readonly List<TrackSignal> Signals = new List<TrackSignal>();

        public TrackNode AddNode(string name)
        {
            var n = new TrackNode { Id = Nodes.Count, Name = name };
            Nodes.Add(n);
            return n;
        }

        public TrackEdge AddEdge(int from, int to, double length, double defaultSpeedKmh, string name = null)
        {
            var e = new TrackEdge
            {
                Id = Edges.Count, FromNode = from, ToNode = to, Length = length,
                DefaultSpeedMs = Units.KmhToMs(defaultSpeedKmh), Name = name ?? ("E" + Edges.Count),
            };
            Edges.Add(e);
            Nodes[from].Outgoing.Add(e.Id);
            Nodes[to].Incoming.Add(e.Id);
            return e;
        }

        public TrackSignal AddSignal(int edgeId, double offset, string name, int guardedSwitchNode = -1)
        {
            var s = new TrackSignal { Id = Signals.Count, Name = name, EdgeId = edgeId, Offset = offset, GuardedSwitchNode = guardedSwitchNode };
            Signals.Add(s);
            Edges[edgeId].Signals.Add(s);
            Edges[edgeId].Signals.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return s;
        }

        public int NextEdge(int edgeId)
        {
            var node = Nodes[Edges[edgeId].ToNode];
            if (node.Outgoing.Count == 0) return -1;
            if (node.Outgoing.Count == 1) return node.Outgoing[0];
            return node.Diverging ? node.Outgoing[1] : node.Outgoing[0];
        }

        /// <summary>
        /// Stellt eine Weiche. Verweigert, wenn der Zug sie gerade befaehrt oder bereits zu nah vor ihr ist (Verriegelung).
        /// </summary>
        public bool TrySetSwitch(int nodeId, bool diverging, TrackFollower train, double trainLengthM)
        {
            var node = Nodes[nodeId];
            if (!node.IsSwitch) return false;
            if (node.Diverging == diverging) return true;
            if (train != null)
            {
                if (node.Incoming.Contains(train.EdgeId) &&
                    Edges[train.EdgeId].Length - train.Offset < SwitchLockDistanceM) return false;
                if (node.Outgoing.Contains(train.EdgeId) && train.Offset < trainLengthM) return false;
            }
            node.Diverging = diverging;
            return true;
        }

        /// <summary>Aktualisiert Signalbegriffe (Halt erzwungen / Abzweig = Langsamfahrt / sonst Fahrt).</summary>
        public void UpdateSignals()
        {
            foreach (var s in Signals)
            {
                if (s.ForcedHalt) s.Aspect = SignalAspect.Halt;
                else if (s.GuardedSwitchNode >= 0 && Nodes[s.GuardedSwitchNode].Diverging) s.Aspect = SignalAspect.ProceedSlow;
                else s.Aspect = SignalAspect.Proceed;
            }
        }

        /// <summary>Ist Zielkante von der Startkante aus irgendwie erreichbar (Weichenstellung ignoriert)?</summary>
        public bool CanReach(int fromEdge, int toEdge)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(fromEdge);
            while (stack.Count > 0)
            {
                int e = stack.Pop();
                if (e == toEdge) return true;
                if (!seen.Add(e)) continue;
                foreach (int n in Nodes[Edges[e].ToNode].Outgoing) stack.Push(n);
            }
            return false;
        }

        /// <summary>Berechnet die 3D-Geometrie aller Kanten ausgehend von einem Startknoten.</summary>
        public void BuildGeometry(int rootNode, Pose rootPose)
        {
            var nodePose = new Pose?[Nodes.Count];
            nodePose[rootNode] = rootPose;
            var queue = new Queue<int>();
            queue.Enqueue(rootNode);
            var done = new HashSet<int>();
            while (queue.Count > 0)
            {
                int nid = queue.Dequeue();
                foreach (int eid in Nodes[nid].Outgoing)
                {
                    if (!done.Add(eid)) continue;
                    var edge = Edges[eid];
                    edge.Geometry = EdgeGeometry.Build(edge, nodePose[nid].Value);
                    if (!nodePose[edge.ToNode].HasValue)
                    {
                        nodePose[edge.ToNode] = edge.Geometry.End;
                        queue.Enqueue(edge.ToNode);
                    }
                }
            }
        }

        public Pose PoseAt(int edgeId, double offset) { return Edges[edgeId].Geometry.Sample(offset); }
    }
}
