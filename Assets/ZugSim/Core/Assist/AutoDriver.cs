using System;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Core.Assist
{
    /// <summary>
    /// Einfacher Autopilot: haelt Fahrplan und Haltepunkte ein. Dient als Demo-Modus (Attract Mode), zum Testen
    /// des Fahrplans und als "Fahrlehrer"-Referenz. Faehrt komfortabel (begrenzte Verzoegerung/Ruck).
    /// </summary>
    public sealed class AutoDriver
    {
        public double ComfortDecel = 0.5;
        public double CruiseMarginMs = 1.5;
        public double ThrottleRatePerS = 0.5;
        public double BrakeRatePerS = 0.8;

        double throttle, brake, sifaTimer;

        public void Update(SimulationWorld w, double dt)
        {
            var t = w.Train;
            var svc = w.Service;
            double v = t.Speed;

            sifaTimer += dt;
            if (sifaTimer > 20) { w.Safety.AcknowledgeSifa(); sifaTimer = 0; }
            if (w.Safety.EmergencyBrakeActive) { w.Safety.TryRelease(); }

            // Tuersteuerung und Abfahrbereitschaft
            bool holdAtPlatform = false;
            if (svc != null && svc.Phase == ServicePhase.AtPlatform)
            {
                holdAtPlatform = true;
                if (!svc.BoardingComplete) w.RequestDoors(true);
                else if (t.Doors.State != DoorState.Closed) w.RequestDoors(false);
                else if (svc.DepartureReady && w.Graph.Signals.Count >= 0) holdAtPlatform = false;
            }
            if (svc != null && svc.IsFinished) holdAtPlatform = true;

            // Zielgeschwindigkeit aus Streckenvorschau
            double vdes = w.Follower.SpeedLimitForTrain(w.Spec.LengthM) - CruiseMarginMs;
            double a = Math.Min(ComfortDecel, 0.6 * w.AvailableServiceDecel);
            double ffBrake = 0;
            foreach (var f in w.Ahead)
            {
                double cap = double.PositiveInfinity;
                switch (f.Kind)
                {
                    case FeatureKind.SpeedLimit: cap = Math.Sqrt(Math.Pow(f.Value - CruiseMarginMs * 0.5, 2) + 2 * a * f.Distance); break;
                    case FeatureKind.Signal:
                        if (f.Aspect == SignalAspect.Halt) cap = Math.Sqrt(2 * a * Math.Max(f.Distance - 20, 0));
                        else if (f.Aspect == SignalAspect.ProceedSlow) cap = Math.Sqrt(Math.Pow(f.SlowSpeedMs - CruiseMarginMs * 0.5, 2) + 2 * a * f.Distance);
                        break;
                    case FeatureKind.EndOfTrack: cap = Math.Sqrt(2 * a * Math.Max(f.Distance - 12, 0)); break;
                }
                vdes = Math.Min(vdes, cap);
            }

            double stopDist = double.PositiveInfinity;
            if (svc != null && svc.Phase == ServicePhase.Approaching && svc.Current != null)
            {
                stopDist = w.Follower.DistanceTo(svc.Current.EdgeId, svc.Current.TargetOffset, 8000);
                if (!double.IsInfinity(stopDist))
                {
                    double d = Math.Max(stopDist, 0);
                    vdes = Math.Min(vdes, Math.Sqrt(2 * a * d));
                    if (v > 0.4 && v >= 0.8 * Math.Sqrt(2 * a * d)) ffBrake = (v * v) / (2.0 * Math.Max(d, 1.0)) / w.AvailableServiceDecel;
                }
            }
            if (holdAtPlatform) vdes = 0;

            double err = vdes - v;
            double wantThrottle = 0, wantBrake = 0;
            if (holdAtPlatform && v < 0.2) { wantBrake = 0.4; }
            else if (err > 0.3 && !(ffBrake > 0.25)) wantThrottle = Units.Clamp01(err * 0.35);
            if (err < -0.2) wantBrake = Math.Max(wantBrake, Units.Clamp01(-err * 0.35));
            if (ffBrake > 0.05 && v > 0.4) wantBrake = Math.Max(wantBrake, Units.Clamp01(ffBrake * 1.08));
            if (!double.IsInfinity(stopDist) && stopDist < 3.0 && v < 0.8) wantBrake = Math.Max(wantBrake, 0.5);
            if (wantBrake > 0.02) wantThrottle = 0;

            throttle = Slew(throttle, wantThrottle, ThrottleRatePerS * dt);
            brake = Slew(brake, wantBrake, BrakeRatePerS * dt);
            w.Input.Throttle = throttle;
            w.Input.Brake = brake;
            w.Input.EmergencyBrake = false;
        }

        static double Slew(double cur, double target, double maxDelta)
        {
            double d = target - cur;
            return Math.Abs(d) <= maxDelta ? target : cur + Math.Sign(d) * maxDelta;
        }
    }
}
