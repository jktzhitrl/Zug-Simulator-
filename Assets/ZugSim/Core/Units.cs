using System;

namespace ZugSim.Core
{
    /// <summary>Gemeinsame Konstanten und Umrechnungen. Intern wird durchgehend in SI-Einheiten gerechnet.</summary>
    public static class Units
    {
        public const double Gravity = 9.81;

        public static double KmhToMs(double kmh) { return kmh / 3.6; }
        public static double MsToKmh(double ms) { return ms * 3.6; }

        public static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static double Clamp01(double v) { return Clamp(v, 0.0, 1.0); }

        public static double Lerp(double a, double b, double t) { return a + (b - a) * Clamp01(t); }

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        public static string FormatClock(double secondsOfDay)
        {
            int s = (int)Math.Floor(((secondsOfDay % 86400.0) + 86400.0) % 86400.0);
            return string.Format("{0:00}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
        }
    }
}
