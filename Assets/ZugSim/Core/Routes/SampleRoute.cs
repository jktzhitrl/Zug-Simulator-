using System;
using System.Collections.Generic;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Core.Routes
{
    /// <summary>Szenario-Ereignis: setzt/loest einen erzwungenen Halt an einem Signal zu einer Uhrzeit.</summary>
    public sealed class SignalEvent
    {
        public double TimeSec;
        public int SignalId;
        public bool ForcedHalt;
    }

    public sealed class RouteData
    {
        public string Name;
        public TrackGraph Graph;
        public int StartEdge;
        public double StartOffset;
        public int SwitchNode = -1;
        public int SignalHbfExit, SignalDorfEntry, SignalDorfExit, SignalEndEntry;
        public int EdgeMain, EdgeThrough, EdgeLoop, EdgeEnd;
    }

    /// <summary>
    /// Fiktive Regionalstrecke "Musterstadt Hbf - Beispieldorf - Endhausen" (ca. 11,8 km) mit Steigungen, Kurven,
    /// Geschwindigkeitswechseln, einer Weiche (Durchfahrgleis oder Gleis 2 mit Bahnsteig) und Signalen.
    /// </summary>
    public static class SampleRoute
    {
        public static RouteData Build()
        {
            var g = new TrackGraph();
            var nHbf = g.AddNode("Musterstadt Hbf");
            var nSwitch = g.AddNode("Weiche Beispieldorf");
            var nMerge = g.AddNode("Zusammenfuehrung Beispieldorf");
            var nEnd = g.AddNode("Endhausen Gleisende");

            // E0: Hbf -> Weiche (5000 m)
            var e0 = g.AddEdge(nHbf.Id, nSwitch.Id, 5000, 160, "Hauptstrecke Nord");
            e0.Limit(0, 600, 80);                    // Bahnhofsbereich
            e0.Curve(1800, 2600, 700);               // Rechtskurve
            e0.Limit(1800, 2600, 120);
            e0.Curve(3300, 3900, -900);              // Linkskurve
            e0.Grade(2000, 12).Grade(3000, -8).Grade(4200, 0);
            e0.Limit(4400, 5000, 120);               // Einfahrt Beispieldorf

            // E1: Durchfahrgleis (gerade, 600 m); E2: Gleis 2 mit Bahnsteig (Abzweig)
            const double ls = 600.0, radius = 500.0, lateral = 4.5;
            double theta = Math.Acos(1.0 - lateral / (2.0 * radius));
            double arc = radius * theta;
            double lp = ls - 4.0 * radius * Math.Sin(theta);
            var e1 = g.AddEdge(nSwitch.Id, nMerge.Id, ls, 120, "Durchfahrgleis");
            var e2 = g.AddEdge(nSwitch.Id, nMerge.Id, 4.0 * arc + lp, 60, "Gleis 2");
            e2.Curve(0, arc, radius).Curve(arc, 2 * arc, -radius)
              .Curve(2 * arc + lp, 3 * arc + lp, -radius).Curve(3 * arc + lp, 4 * arc + lp, radius);

            // E3: Beispieldorf -> Endhausen (5400 m)
            var e3 = g.AddEdge(nMerge.Id, nEnd.Id, 5400, 160, "Hauptstrecke Sued");
            e3.Limit(0, 300, 80).Limit(4600, 5400, 80);
            e3.Curve(1500, 2300, -800).Curve(3600, 4400, 1000);
            e3.Grade(800, -6).Grade(2600, 4).Grade(4000, 0);

            // Signale
            var sHbf = g.AddSignal(e0.Id, 300, "Hbf Ausfahrt");
            sHbf.ForcedHalt = true;                  // wird per Szenario zur Abfahrtszeit geloest
            var sDorfIn = g.AddSignal(e0.Id, 4800, "Beispieldorf Einfahrt", nSwitch.Id);
            var sDorfOut = g.AddSignal(e2.Id, 540, "Beispieldorf Ausfahrt Gl. 2");
            var sEnd = g.AddSignal(e3.Id, 4950, "Endhausen Einfahrt");
            g.UpdateSignals();

            g.BuildGeometry(nHbf.Id, new TrackPose(0, 0, 0, 0, 0));

            return new RouteData
            {
                Name = "Musterstadt Hbf - Endhausen",
                Graph = g,
                StartEdge = e0.Id,
                StartOffset = 150,
                SwitchNode = nSwitch.Id,
                SignalHbfExit = sHbf.Id, SignalDorfEntry = sDorfIn.Id, SignalDorfExit = sDorfOut.Id, SignalEndEntry = sEnd.Id,
                EdgeMain = e0.Id, EdgeThrough = e1.Id, EdgeLoop = e2.Id, EdgeEnd = e3.Id,
            };
        }

        /// <summary>Fahrplan fuer die Beispielstrecke. <paramref name="departureSec"/> = planmaessige Abfahrt in Musterstadt.</summary>
        public static Timetable BuildTimetable(RouteData r, double departureSec, double runBetweenStops1 = 270, double runBetweenStops2 = 285)
        {
            var t = new Timetable { Name = "RE 4711 Musterstadt - Endhausen" };
            t.Stops.Add(new StopEntry
            {
                Station = "Musterstadt Hbf", EdgeId = r.EdgeMain, TargetOffset = r.StartOffset,
                DepartureSec = departureSec, IsOrigin = true, Platform = "3", PassengerLoadAfterKg = 18000,
            });
            double arr1 = departureSec + runBetweenStops1;
            t.Stops.Add(new StopEntry
            {
                Station = "Beispieldorf", EdgeId = r.EdgeLoop, TargetOffset = 380, ArrivalSec = arr1,
                DepartureSec = arr1 + 60, MinDwellSec = 25, ExpireEdgeId = r.EdgeEnd, Platform = "2",
                PassengerLoadAfterKg = 14000,
            });
            double arr2 = arr1 + 60 + runBetweenStops2;
            t.Stops.Add(new StopEntry
            {
                Station = "Endhausen", EdgeId = r.EdgeEnd, TargetOffset = 5330, ArrivalSec = arr2,
                DepartureSec = arr2 + 120, MinDwellSec = 15, IsTerminal = true, Platform = "1",
                PassengerLoadAfterKg = 0,
            });
            return t;
        }

        public static List<SignalEvent> BuildSignalScript(RouteData r, Timetable t, bool withDelayAtDorf)
        {
            var list = new List<SignalEvent>
            {
                // Abfahrt Musterstadt: Ausfahrsignal wird kurz vor Abfahrt freigegeben
                new SignalEvent { TimeSec = 0, SignalId = r.SignalHbfExit, ForcedHalt = true },
                new SignalEvent { TimeSec = t.Stops[0].DepartureSec - 15, SignalId = r.SignalHbfExit, ForcedHalt = false },
                // Beispieldorf: Ausfahrsignal Gl. 2 bleibt bis kurz vor planmaessiger Abfahrt auf Halt
                new SignalEvent { TimeSec = 0, SignalId = r.SignalDorfExit, ForcedHalt = true },
                new SignalEvent { TimeSec = t.Stops[1].DepartureSec - 8, SignalId = r.SignalDorfExit, ForcedHalt = false },
            };
            if (withDelayAtDorf)
            {
                // Gegenverkehr: Einfahrsignal Endhausen haelt den Zug zeitweise auf
                list.Add(new SignalEvent { TimeSec = t.Stops[1].DepartureSec + 60, SignalId = r.SignalEndEntry, ForcedHalt = true });
                list.Add(new SignalEvent { TimeSec = t.Stops[1].DepartureSec + 230, SignalId = r.SignalEndEntry, ForcedHalt = false });
            }
            list.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
            return list;
        }
    }
}
