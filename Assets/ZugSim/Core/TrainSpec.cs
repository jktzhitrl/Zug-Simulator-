namespace ZugSim.Core
{
    /// <summary>Technische Daten eines Triebfahrzeugs bzw. Triebzugs (alles SI-Einheiten).</summary>
    public sealed class TrainSpec
    {
        public string Name = "RE-Triebzug (4-teilig)";
        public double EmptyMassKg = 190000;
        public double LengthM = 105;
        public int CarCount = 4;
        public double MaxSpeedMs = Units.KmhToMs(160);

        // Antrieb
        public double MaxTractiveForceN = 190000;
        public double MaxPowerW = 2400000;
        /// <summary>Anteil der Masse auf angetriebenen Achsen (Haftung beim Anfahren).</summary>
        public double DrivenMassFraction = 0.5;
        /// <summary>Zuschlag fuer rotierende Massen.</summary>
        public double RotatingMassFactor = 1.06;

        // Laufwiderstand nach Davis: R = A + B*v + C*v^2 (N, v in m/s)
        public double DavisA = 4000;
        public double DavisB = 60;
        public double DavisC = 6.5;

        // Bremse
        public double MaxServiceDecel = 1.0;     // m/s^2, Betriebsbremse voll (trockene Schiene)
        public double EmergencyDecel = 1.2;      // m/s^2, Schnellbremse
        public double BrakeApplyTimeS = 1.5;     // Zeit bis Vollbremsstellung (Betriebsbremse)
        public double BrakeReleaseTimeS = 3.0;
        public double EmergencyApplyTimeS = 0.8;

        // Fahrschalter
        public int ThrottleNotches = 8;

        public double TotalMassKg(double passengerMassKg) { return EmptyMassKg + passengerMassKg; }

        public static TrainSpec RegionalEmu() { return new TrainSpec(); }

        public static TrainSpec HighSpeedEmu()
        {
            return new TrainSpec
            {
                Name = "Hochgeschwindigkeits-Triebzug",
                EmptyMassKg = 450000,
                LengthM = 200,
                CarCount = 8,
                MaxSpeedMs = Units.KmhToMs(250),
                MaxTractiveForceN = 300000,
                MaxPowerW = 8000000,
                DrivenMassFraction = 0.6,
                DavisA = 7000,
                DavisB = 80,
                DavisC = 7.5,
                MaxServiceDecel = 0.9,
                EmergencyDecel = 1.1,
            };
        }
    }
}
