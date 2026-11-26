using System;
using System.Collections.Generic;

namespace ZugSim.Core.Track
{
    public enum FeatureKind { Signal, SpeedLimit, Switch, EndOfTrack }

    public struct TrackFeature
    {
        public FeatureKind Kind;
        public double Distance;
        /// <summary>SpeedLimit: m/s. Switch: 1 = Abzweig, 0 = gerade.</summary>
        public double Value;
        public int Id;
        public string Name;
        public SignalAspect Aspect;
        public double SlowSpeedMs;
    }

    /// <summary>Position des Zugkopfes im Gleisgraphen (Kante + Offset) inklusive Historie fuer Zugende/Wagen.</summary>
    public sealed class TrackFollower
    {
        struct Entry { public int EdgeId; public double TravelledAtStart; }

        public readonly TrackGraph Graph;
        public int EdgeId { get; private set; }
        public double Offset { get; private set; }
        public double Travelled { get; private set; }
        public bool AtEndOfTrack { get; private set; }

        readonly List<Entry> history = new List<Entry>();
        readonly List<TrackSignal> passed = new List<TrackSignal>();
        public IList<TrackSignal> PassedSignals { get { return passed; } }

        public TrackFollower(TrackGraph graph, int edgeId, double offset)
        {
            Graph = graph;
            EdgeId = edgeId;
            Offset = offset;
            history.Add(new Entry { EdgeId = edgeId, TravelledAtStart = -offset });
        }

        public TrackEdge Edge { get { return Graph.Edges[EdgeId]; } }

        public void Advance(double ds)
        {
            passed.Clear();
            if (ds <= 0 || AtEndOfTrack) return;
            double toMove = ds;
            bool entering = false;
            while (toMove > 1e-12)
            {
                var edge = Graph.Edges[EdgeId];
                double space = edge.Length - Offset;
                double lowerExclusive = entering ? -1.0 : Offset;
                if (toMove < space)
                {
                    CollectSignals(edge, lowerExclusive, Offset + toMove);
                    Offset += toMove;
                    Travelled += toMove;
                    return;
                }
                CollectSignals(edge, lowerExclusive, edge.Length);
                Travelled += space;
                toMove -= space;
                int next = Graph.NextEdge(EdgeId);
                if (next < 0)
                {
                    Offset = edge.Length;
                    AtEndOfTrack = true;
                    return;
                }
                EdgeId = next;
                Offset = 0;
                entering = true;
                history.Add(new Entry { EdgeId = next, TravelledAtStart = Travelled });
                if (history.Count > 16) history.RemoveAt(0);
            }
        }

        void CollectSignals(TrackEdge edge, double lowerExclusive, double upperInclusive)
        {
            foreach (var s in edge.Signals)
                if (s.Offset > lowerExclusive && s.Offset <= upperInclusive) passed.Add(s);
        }

        /// <summary>Position eines Punktes, der <paramref name="behindM"/> hinter dem Zugkopf liegt (Zugende, Wagen).</summary>
        public void LocateBehind(double behindM, out int edgeId, out double offset)
        {
            double t = Travelled - behindM;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].TravelledAtStart <= t || i == 0)
                {
                    edgeId = history[i].EdgeId;
                    offset = Math.Max(0.0, Math.Min(t - history[i].TravelledAtStart, Graph.Edges[edgeId].Length));
                    return;
                }
            }
            edgeId = EdgeId; offset = Offset;
        }

        public double GradePermilleForTrain(double lengthM)
        {
            int e; double o;
            LocateBehind(lengthM * 0.5, out e, out o);
            return 0.5 * (Edge.GradeAt(Offset) + Graph.Edges[e].GradeAt(o));
        }

        public double CurveRadiusAtHead()
        {
            return Math.Abs(Edge.SignedRadiusAt(Offset));
        }

        /// <summary>Zulaessige Hoechstgeschwindigkeit fuer den ganzen Zug (Kopf bis Ende).</summary>
        public double SpeedLimitForTrain(double lengthM)
        {
            double lim = Edge.LimitAt(Offset);
            for (int k = 1; k <= 4; k++)
            {
                int e; double o;
                LocateBehind(lengthM * k / 4.0, out e, out o);
                lim = Math.Min(lim, Graph.Edges[e].LimitAt(o));
            }
            return lim;
        }

        /// <summary>Liefert voraus liegende Signale, Geschwindigkeitswechsel, Weichen und Gleisende (nach Entfernung sortiert).</summary>
        public void ScanAhead(double range, List<TrackFeature> result)
        {
            result.Clear();
            int e = EdgeId;
            double off = Offset;
            double dist = 0;
            double lastLimit = Graph.Edges[e].LimitAt(off);
            var breakpoints = new List<double>();

            for (int guard = 0; guard < 64 && dist < range; guard++)
            {
                var edge = Graph.Edges[e];
                double avail = edge.Length - off;

                foreach (var s in edge.Signals)
                    if (s.Offset > off && dist + (s.Offset - off) <= range)
                        result.Add(new TrackFeature
                        {
                            Kind = FeatureKind.Signal, Distance = dist + (s.Offset - off), Id = s.Id,
                            Name = s.Name, Aspect = s.Aspect, SlowSpeedMs = s.SlowSpeedMs,
                        });

                breakpoints.Clear();
                foreach (var z in edge.Zones)
                {
                    if (z.Start > off && z.Start < edge.Length) breakpoints.Add(z.Start);
                    if (z.End > off && z.End < edge.Length) breakpoints.Add(z.End);
                }
                breakpoints.Sort();
                foreach (double bp in breakpoints)
                {
                    double lim = edge.LimitAt(bp + 1e-6);
                    if (Math.Abs(lim - lastLimit) > 1e-6 && dist + (bp - off) <= range)
                    {
                        result.Add(new TrackFeature { Kind = FeatureKind.SpeedLimit, Distance = dist + (bp - off), Value = lim });
                        lastLimit = lim;
                    }
                }

                dist += avail;
                if (dist > range) break;
                var node = Graph.Nodes[edge.ToNode];
                if (node.IsSwitch)
                    result.Add(new TrackFeature { Kind = FeatureKind.Switch, Distance = dist, Id = node.Id, Value = node.Diverging ? 1 : 0, Name = node.Name });

                int next = Graph.NextEdge(e);
                if (next < 0)
                {
                    result.Add(new TrackFeature { Kind = FeatureKind.EndOfTrack, Distance = dist });
                    break;
                }
                double nextLim = Graph.Edges[next].LimitAt(0.0);
                if (Math.Abs(nextLim - lastLimit) > 1e-6)
                {
                    result.Add(new TrackFeature { Kind = FeatureKind.SpeedLimit, Distance = dist, Value = nextLim });
                    lastLimit = nextLim;
                }
                e = next;
                off = 0;
            }
            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        /// <summary>Entfernung entlang der aktuell gestellten Fahrstrasse bis zu einem Punkt; Unendlich wenn nicht erreichbar.</summary>
        public double DistanceTo(int targetEdge, double targetOffset, double range)
        {
            int e = EdgeId;
            double off = Offset;
            double dist = 0;
            for (int guard = 0; guard < 64 && dist <= range; guard++)
            {
                if (e == targetEdge && targetOffset >= off) return dist + (targetOffset - off);
                dist += Graph.Edges[e].Length - off;
                int next = Graph.NextEdge(e);
                if (next < 0) break;
                e = next; off = 0;
            }
            return double.PositiveInfinity;
        }
    }
}
