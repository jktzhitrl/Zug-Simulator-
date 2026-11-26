using System.Collections.Generic;

namespace ZugSim.Core.Service
{
    public sealed class StopEntry
    {
        public string Station;
        public int EdgeId;
        /// <summary>Soll-Halteposition des Zugkopfes (Halttafel) auf der Kante.</summary>
        public double TargetOffset;
        /// <summary>Planmaessige Ankunft in Sekunden seit Mitternacht (beim Starthalt ungenutzt).</summary>
        public double ArrivalSec;
        public double DepartureSec;
        public double MinDwellSec = 30;
        public bool IsOrigin;
        public bool IsTerminal;
        /// <summary>Betritt der Zug diese Kante ohne gehalten zu haben, gilt der Halt als verpasst (-1 = nicht genutzt).</summary>
        public int ExpireEdgeId = -1;
        public double PassengerLoadAfterKg = 12000;
        public string Platform = "1";
    }

    public sealed class Timetable
    {
        public string Name;
        public readonly List<StopEntry> Stops = new List<StopEntry>();
        public double StartTimeSec { get { return Stops.Count > 0 ? Stops[0].DepartureSec - 120.0 : 0.0; } }
    }
}
