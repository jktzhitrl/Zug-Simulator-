using System;
using System.Collections.Generic;
using ZugSim.Core.Track;

namespace ZugSim.Core.Safety
{
    public enum SafetyState { Normal, Warning, Intervention }

    /// <summary>
    /// Vereinfachte Zugbeeinflussung (PZB/LZB-aehnlich) mit Bremskurven-Ueberwachung plus Sicherheitsfahrschaltung (Sifa).
    /// Ueberschreitet der Fahrer die zulaessige Geschwindigkeitskurve, kommt erst eine Warnung, dann Zwangsbremsung.
    /// </summary>
    public sealed class SafetyMonitor
    {
        // Parameter
        public double CurveDecel = 0.7;              // m/s^2, angenommene Bremsverzoegerung der Ueberwachungskurve
        public double StopMargin = 8.0;              // m vor dem Halt zeigenden Signal
        public double ReleaseSpeedMs = 1.5;          // Restgeschwindigkeit bis zum Signal
        public double WarningExcessMs = 0.3;
        public double InterventionExcessMs = 2.0;
        public double WarningToInterventionS = 6.0;
        public double SifaPeriodS = 30.0;
        public double SifaWarningS = 4.0;            // Zeit Warnton bis Zwangsbremsung
        public bool SifaEnabled = true;

        // Zustand
        public SafetyState State { get; private set; }
        public string Reason { get; private set; }
        public double PermittedSpeedMs { get; private set; }
        public bool EmergencyBrakeActive { get; private set; }
        public bool CanRelease { get; private set; }
        public int Interventions { get; private set; }
        public int SignalViolations { get; private set; }
        public bool SifaWarning { get; private set; }
        public double SifaTimer { get; private set; }

        double warningTime;
        double standstillTime;
        readonly List<TrackFeature> features = new List<TrackFeature>();

        public double NextTargetDistanceM { get; private set; }

        public void NotifyDriverActivity() { SifaTimer = 0; SifaWarning = false; }
        public void AcknowledgeSifa() { NotifyDriverActivity(); }

        /// <summary>Quittieren nach Zwangsbremsung. Nur im Stillstand moeglich.</summary>
        public bool TryRelease()
        {
            if (!EmergencyBrakeActive || !CanRelease) return false;
            EmergencyBrakeActive = false;
            CanRelease = false;
            State = SafetyState.Normal;
            Reason = null;
            warningTime = 0;
            NotifyDriverActivity();
            return true;
        }

        public void Intervene(string reason) { Trigger(reason); }

        void Trigger(string reason)
        {
            if (EmergencyBrakeActive) return;
            Interventions++;
            EmergencyBrakeActive = true;
            CanRelease = false;
            standstillTime = 0;
            State = SafetyState.Intervention;
            Reason = reason;
        }

        public void Update(double dt, double speedMs, TrackFollower follower, double trainLengthM)
        {
            // Haltsignal ueberfahren?
            foreach (var s in follower.PassedSignals)
            {
                if (s.Aspect == SignalAspect.Halt)
                {
                    SignalViolations++;
                    Trigger("Haltsignal überfahren: " + s.Name);
                }
            }

            // Zulaessige Geschwindigkeit
            double permitted = follower.SpeedLimitForTrain(trainLengthM);
            NextTargetDistanceM = double.PositiveInfinity;
            follower.ScanAhead(2500.0, features);
            foreach (var f in features)
            {
                double curve = double.PositiveInfinity;
                switch (f.Kind)
                {
                    case FeatureKind.SpeedLimit:
                        curve = Math.Sqrt(f.Value * f.Value + 2.0 * CurveDecel * f.Distance);
                        break;
                    case FeatureKind.Signal:
                        if (f.Aspect == SignalAspect.Halt)
                        {
                            curve = Math.Max(ReleaseSpeedMs, Math.Sqrt(2.0 * CurveDecel * Math.Max(f.Distance - StopMargin, 0.0)));
                            NextTargetDistanceM = Math.Min(NextTargetDistanceM, f.Distance);
                        }
                        else if (f.Aspect == SignalAspect.ProceedSlow)
                            curve = Math.Sqrt(f.SlowSpeedMs * f.SlowSpeedMs + 2.0 * CurveDecel * f.Distance);
                        break;
                    case FeatureKind.EndOfTrack:
                        curve = Math.Max(1.0, Math.Sqrt(2.0 * CurveDecel * Math.Max(f.Distance - 4.0, 0.0)));
                        NextTargetDistanceM = Math.Min(NextTargetDistanceM, f.Distance);
                        break;
                }
                if (curve < permitted) permitted = curve;
            }
            PermittedSpeedMs = permitted;

            // Zwangsbremsung aktiv: auf Stillstand warten
            if (EmergencyBrakeActive)
            {
                if (speedMs < 0.05) { standstillTime += dt; if (standstillTime > 2.0) CanRelease = true; }
                return;
            }

            double excess = speedMs - permitted;
            if (excess > InterventionExcessMs) Trigger("Überwachungskurve überschritten");
            else if (excess > WarningExcessMs)
            {
                State = SafetyState.Warning;
                warningTime += dt;
                if (warningTime > WarningToInterventionS) Trigger("Warnung nicht beachtet");
            }
            else
            {
                if (State == SafetyState.Warning) State = SafetyState.Normal;
                warningTime = Math.Max(0, warningTime - dt);
            }

            // Sifa
            if (SifaEnabled && speedMs > 1.0)
            {
                SifaTimer += dt;
                if (SifaTimer > SifaPeriodS) SifaWarning = true;
                if (SifaTimer > SifaPeriodS + SifaWarningS) Trigger("Sifa nicht betätigt");
            }
            else if (speedMs <= 1.0) { SifaTimer = 0; SifaWarning = false; }
        }
    }
}
