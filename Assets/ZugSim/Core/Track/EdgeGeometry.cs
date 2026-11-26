using System;
using System.Collections.Generic;

namespace ZugSim.Core.Track
{
    /// <summary>Pose im Raum. Heading: 0 = +Z (Norden), waechst Richtung +X (wie Unity-Yaw). Y = Hoehe.</summary>
    public struct Pose
    {
        public double X, Y, Z, Heading, Pitch;
        public Pose(double x, double y, double z, double heading, double pitch)
        { X = x; Y = y; Z = z; Heading = heading; Pitch = pitch; }
    }

    /// <summary>
    /// Raeumliche Gleisgeometrie einer Kante, abgeleitet aus Kurven (Radius) und Neigungen.
    /// So bleiben Physik und Darstellung automatisch konsistent.
    /// </summary>
    public sealed class EdgeGeometry
    {
        public const double Step = 2.0;
        readonly Pose[] samples;
        readonly double[] offsets;
        readonly double length;

        EdgeGeometry(Pose[] samples, double[] offsets, double length)
        { this.samples = samples; this.offsets = offsets; this.length = length; }

        public Pose Start { get { return samples[0]; } }
        public Pose End { get { return samples[samples.Length - 1]; } }

        public static EdgeGeometry Build(TrackEdge edge, Pose start)
        {
            // Stuetzstellen: regelmaessiges Raster plus alle Kurven-/Neigungsgrenzen (damit Integration exakt bleibt)
            var pos = new List<double>();
            int n = Math.Max(1, (int)Math.Ceiling(edge.Length / Step));
            for (int i = 0; i <= n; i++) pos.Add(Math.Min(i * Step, edge.Length));
            foreach (var c in edge.Curves) { pos.Add(c.Start); pos.Add(c.End); }
            foreach (var g in edge.Grades) pos.Add(g.Start);
            pos.Sort();
            var offs = new List<double>();
            foreach (double p in pos)
            {
                if (p < 0 || p > edge.Length) continue;
                if (offs.Count > 0 && p - offs[offs.Count - 1] < 1e-9) continue;
                offs.Add(p);
            }
            if (offs[offs.Count - 1] < edge.Length - 1e-9) offs.Add(edge.Length);

            var s = new Pose[offs.Count];
            s[0] = start;
            s[0].Pitch = Math.Atan(edge.GradeAt(0) / 1000.0);
            double x = start.X, y = start.Y, z = start.Z, h = start.Heading;
            for (int i = 1; i < offs.Count; i++)
            {
                double prev = offs[i - 1], cur = offs[i];
                double ds = cur - prev;
                double mid = 0.5 * (prev + cur);
                double r = edge.SignedRadiusAt(mid);
                double dh = Math.Abs(r) > 1e-6 ? ds / r : 0.0;
                double pitch = Math.Atan(edge.GradeAt(mid) / 1000.0);
                double hm = h + 0.5 * dh;
                x += ds * Math.Cos(pitch) * Math.Sin(hm);
                z += ds * Math.Cos(pitch) * Math.Cos(hm);
                y += ds * Math.Sin(pitch);
                h += dh;
                s[i] = new Pose(x, y, z, h, pitch);
            }
            return new EdgeGeometry(s, offs.ToArray(), edge.Length);
        }

        public Pose Sample(double offset)
        {
            offset = Units.Clamp(offset, 0.0, length);
            int i = Array.BinarySearch(offsets, offset);
            if (i >= 0) return samples[i];
            i = ~i - 1;
            if (i >= samples.Length - 1) return samples[samples.Length - 1];
            double f = (offset - offsets[i]) / (offsets[i + 1] - offsets[i]);
            Pose a = samples[i], b = samples[i + 1];
            return new Pose(
                a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f,
                a.Heading + (b.Heading - a.Heading) * f, a.Pitch + (b.Pitch - a.Pitch) * f);
        }
    }
}
