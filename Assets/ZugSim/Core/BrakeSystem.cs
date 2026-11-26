using System;

namespace ZugSim.Core
{
    /// <summary>
    /// Vereinfachte Druckluftbremse mit Ansprech- und Loesezeiten.
    /// <see cref="Fill"/> ist der normierte Bremszylinderdruck 0..1, wobei 1 = Schnellbremse.
    /// </summary>
    public sealed class BrakeSystem
    {
        readonly TrainSpec spec;

        public double Fill { get; private set; }
        public bool EmergencyActive { get; private set; }

        public BrakeSystem(TrainSpec spec) { this.spec = spec; }

        /// <summary>Anteil der Betriebsbremse voll an der Schnellbremse.</summary>
        double ServiceRatio { get { return spec.MaxServiceDecel / spec.EmergencyDecel; } }

        public void Update(double dt, double lever, bool emergency)
        {
            EmergencyActive = emergency;
            double target = emergency ? 1.0 : Units.Clamp01(lever) * ServiceRatio;
            double rate;
            if (emergency) rate = 1.0 / spec.EmergencyApplyTimeS;
            else if (target > Fill) rate = ServiceRatio / spec.BrakeApplyTimeS;
            else rate = 1.0 / spec.BrakeReleaseTimeS;

            double delta = target - Fill;
            double maxStep = rate * dt;
            Fill += Math.Abs(delta) <= maxStep ? delta : Math.Sign(delta) * maxStep;
        }

        /// <summary>Hauptluftleitung in bar (5.0 geloest, 3.5 Vollbremsung, 0 Schnellbremsung).</summary>
        public double MainPipeBar
        {
            get
            {
                double r = ServiceRatio;
                if (Fill <= r) return 5.0 - 1.5 * (Fill / r);
                return 3.5 * (1.0 - (Fill - r) / (1.0 - r));
            }
        }

        /// <summary>Bremszylinderdruck in bar (max. 3.8).</summary>
        public double CylinderBar { get { return 3.8 * Fill; } }

        /// <summary>Bremskraft in N, begrenzt durch die Kraftschlussgrenze (alle Achsen gebremst).</summary>
        public double BrakeForceN(double massKg, double adhesionCoefficient)
        {
            double wanted = Fill * massKg * spec.EmergencyDecel;
            double limit = 0.95 * adhesionCoefficient * massKg * Units.Gravity;
            return Math.Min(wanted, limit);
        }
    }

    public enum DoorState { Closed, Opening, Open, Closing }

    /// <summary>Tueren inkl. Zugsicherung: Traktion nur bei geschlossenen Tueren, Oeffnen nur im Stand.</summary>
    public sealed class DoorSystem
    {
        public DoorState State { get; private set; }
        /// <summary>0 = zu, 1 = ganz offen.</summary>
        public double Openness { get; private set; }
        public double OpenTimeS = 3.0;
        public double CloseTimeS = 4.0;
        /// <summary>Zeit seit die Tueren vollstaendig offen sind.</summary>
        public double OpenDurationS { get; private set; }

        public bool IsClosedAndLocked { get { return State == DoorState.Closed; } }
        public bool ClosingWarning { get { return State == DoorState.Closing; } }

        public bool RequestOpen(double speedMs)
        {
            if (speedMs > 0.3) return false;
            if (State == DoorState.Closed || State == DoorState.Closing) State = DoorState.Opening;
            return true;
        }

        public void RequestClose()
        {
            if (State == DoorState.Open || State == DoorState.Opening) State = DoorState.Closing;
        }

        public void Update(double dt)
        {
            switch (State)
            {
                case DoorState.Opening:
                    Openness = Math.Min(1.0, Openness + dt / OpenTimeS);
                    if (Openness >= 1.0) { State = DoorState.Open; OpenDurationS = 0; }
                    break;
                case DoorState.Closing:
                    Openness = Math.Max(0.0, Openness - dt / CloseTimeS);
                    if (Openness <= 0.0) { State = DoorState.Closed; OpenDurationS = 0; }
                    break;
                case DoorState.Open:
                    OpenDurationS += dt;
                    break;
            }
        }
    }
}
