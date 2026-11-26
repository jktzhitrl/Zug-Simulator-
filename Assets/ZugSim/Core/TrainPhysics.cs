using System;

namespace ZugSim.Core
{
    public struct DriverInput
    {
        /// <summary>Fahrschalter 0..1 (Leistungsanforderung).</summary>
        public double Throttle;
        /// <summary>Bremshebel 0..1 (Betriebsbremse).</summary>
        public double Brake;
        /// <summary>Schnellbremse vom Fahrer.</summary>
        public bool EmergencyBrake;
    }

    public struct TrackConditions
    {
        /// <summary>Neigung in Promille (positiv = Steigung).</summary>
        public double GradePermille;
        /// <summary>Kurvenradius in m; 0 = Gerade.</summary>
        public double CurveRadiusM;
    }

    /// <summary>
    /// Laengsdynamik eines Zuges: Zugkraftkennlinie mit Leistungsgrenze und Kraftschluss,
    /// Davis-Laufwiderstand, Steigung, Kurve und Bremse. Fahrt nur vorwaerts (Parkbremse haelt im Stand).
    /// </summary>
    public sealed class TrainPhysics
    {
        public const double MaxSubStep = 0.02;

        public readonly TrainSpec Spec;
        public readonly BrakeSystem Brakes;
        public readonly DoorSystem Doors = new DoorSystem();

        public double PassengerMassKg;
        public double Speed;            // m/s
        public double Odometer;         // m
        public double Acceleration;     // m/s^2
        public double TractiveForceN;
        public double ResistanceForceN;
        public double BrakeForceN;
        public bool TractionCut;
        public double ThrottleApplied;

        public TrainPhysics(TrainSpec spec)
        {
            Spec = spec;
            Brakes = new BrakeSystem(spec);
        }

        public double MassKg { get { return Spec.TotalMassKg(PassengerMassKg); } }
        public double SpeedKmh { get { return Units.MsToKmh(Speed); } }

        public static double CurveResistancePermille(double radiusM)
        {
            if (radiusM <= 0 || double.IsInfinity(radiusM)) return 0;
            radiusM = Math.Abs(radiusM);
            if (radiusM >= 300) return 650.0 / (radiusM - 55.0);
            return 500.0 / Math.Max(radiusM - 30.0, 20.0);
        }

        public double RunningResistanceN(double v)
        {
            return Spec.DavisA + Spec.DavisB * v + Spec.DavisC * v * v;
        }

        public void Step(double dt, DriverInput input, bool safetyEmergency, TrackConditions track, double adhesionFactor)
        {
            while (dt > 1e-9)
            {
                double h = Math.Min(dt, MaxSubStep);
                SubStep(h, input, safetyEmergency, track, adhesionFactor);
                dt -= h;
            }
        }

        void SubStep(double dt, DriverInput input, bool safetyEmergency, TrackConditions track, double adhesionFactor)
        {
            double m = MassKg;
            double mu = WeatherModel.DryAdhesion * adhesionFactor;
            double muBrake = WeatherModel.DryBrakeAdhesion * adhesionFactor;
            bool emergency = input.EmergencyBrake || safetyEmergency;

            Brakes.Update(dt, input.Brake, emergency);
            Doors.Update(dt);

            // Traktion
            double thr = Units.Clamp01(input.Throttle);
            TractionCut = !Doors.IsClosedAndLocked || emergency || Brakes.Fill > 0.03 || Speed >= Spec.MaxSpeedMs;
            double tractive = 0.0;
            if (!TractionCut && thr > 0.0)
            {
                double fPower = Speed > 0.5 ? Spec.MaxPowerW / Speed : double.MaxValue;
                tractive = thr * Math.Min(Spec.MaxTractiveForceN, fPower);
                tractive = Math.Min(tractive, mu * Spec.DrivenMassFraction * m * Units.Gravity);
            }
            ThrottleApplied = TractionCut ? 0.0 : thr;
            TractiveForceN = tractive;

            // Widerstaende
            double gradeForce = m * Units.Gravity * track.GradePermille / 1000.0;
            double curveForce = m * Units.Gravity * CurveResistancePermille(track.CurveRadiusM) / 1000.0;
            double brakeForce = Brakes.BrakeForceN(m, muBrake);
            BrakeForceN = brakeForce;

            double drive = tractive - gradeForce - curveForce;
            double effMass = m * Spec.RotatingMassFactor;
            double newSpeed;
            double accel;

            if (Speed <= 0.01)
            {
                double hold = Spec.DavisA + brakeForce;
                if (drive > hold)
                {
                    accel = (drive - hold) / effMass;
                    newSpeed = Speed + accel * dt;
                }
                else
                {
                    accel = 0.0;
                    newSpeed = 0.0;
                }
                ResistanceForceN = Spec.DavisA + gradeForce + curveForce;
            }
            else
            {
                double run = RunningResistanceN(Speed);
                ResistanceForceN = run + gradeForce + curveForce;
                double net = drive - run - brakeForce;
                accel = net / effMass;
                newSpeed = Speed + accel * dt;
                if (newSpeed <= 0.0)
                {
                    newSpeed = 0.0;
                    accel = (newSpeed - Speed) / dt;
                }
            }

            Odometer += 0.5 * (Speed + newSpeed) * dt;
            Speed = newSpeed;
            Acceleration = accel;
        }

        /// <summary>Maximal uebertragbare Bremsverzoegerung bei gegebener Haftung.</summary>
        public static double AvailableDecel(double adhesionFactor)
        {
            return 0.95 * WeatherModel.DryBrakeAdhesion * adhesionFactor * Units.Gravity;
        }

        /// <summary>Naeherung des Bremsweges (Betriebsbremse voll, konstante Verzoegerung inkl. Ansprechzeit).</summary>
        public double EstimatedServiceBrakingDistance(double adhesionFactor, double gradePermille)
        {
            double decel = Math.Min(Spec.MaxServiceDecel, AvailableDecel(adhesionFactor))
                           - Units.Gravity * gradePermille / 1000.0;
            decel = Math.Max(decel, 0.1);
            return Speed * Spec.BrakeApplyTimeS * 0.5 + Speed * Speed / (2.0 * decel);
        }
    }
}
