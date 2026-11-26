using System;
using System.Collections.Generic;
using ZugSim.Core.Career;
using ZugSim.Core.Routes;
using ZugSim.Core.Safety;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Core
{
    /// <summary>
    /// Fasst die gesamte Simulation zusammen. Wird in einem festen Zeitschritt (z. B. 50 Hz) mit <see cref="Step"/> gefahren.
    /// Die Unity-Schicht liest nur den Zustand und schreibt <see cref="Input"/>.
    /// </summary>
    public sealed class SimulationWorld
    {
        public const double FixedStep = 0.02;

        public readonly TrainSpec Spec;
        public readonly RouteData Route;
        public readonly TrackGraph Graph;
        public readonly TrainPhysics Train;
        public readonly TrackFollower Follower;
        public readonly SafetyMonitor Safety = new SafetyMonitor();
        public readonly GameClock Clock;
        public readonly WeatherSystem Weather;
        public readonly ScoringSystem Scoring = new ScoringSystem();

        public Timetable Timetable { get; private set; }
        public ServiceTracker Service { get; private set; }
        public string MissionId { get; private set; }

        public DriverInput Input;
        public bool HornActive;
        /// <summary>Automatische Weichenstellung gemaess Fahrplan (Fahrdienstleiter).</summary>
        public bool AutoRoute = true;

        public readonly List<TrackFeature> Ahead = new List<TrackFeature>();

        List<SignalEvent> script = new List<SignalEvent>();
        int scriptIndex;
        int aheadCounter;
        DriverInput lastInput;

        public SimulationWorld(TrainSpec spec, RouteData route, double startSecondsOfDay, WeatherPreset weather)
        {
            Spec = spec;
            Route = route;
            Graph = route.Graph;
            Train = new TrainPhysics(spec);
            Follower = new TrackFollower(Graph, route.StartEdge, route.StartOffset);
            Clock = new GameClock(startSecondsOfDay);
            Weather = new WeatherSystem(weather);
            Graph.UpdateSignals();
        }

        public static SimulationWorld CreateFreeRide(TrainSpec spec, double startHour, WeatherPreset weather, double timeScale = 30.0)
        {
            var route = SampleRoute.Build();
            var w = new SimulationWorld(spec, route, startHour * 3600.0, weather);
            w.Clock.TimeScale = timeScale;
            w.Weather.AutoCycle = false;
            w.AutoRoute = false;
            w.Safety.SifaEnabled = false;
            foreach (var sig in route.Graph.Signals) sig.ForcedHalt = false;
            route.Graph.UpdateSignals();
            return w;
        }

        public static SimulationWorld CreateMission(MissionDefinition m, TrainSpec spec = null)
        {
            var route = SampleRoute.Build();
            var tt = SampleRoute.BuildTimetable(route, m.DepartureSec, m.Run1, m.Run2);
            var w = new SimulationWorld(spec ?? TrainSpec.RegionalEmu(), route, tt.StartTimeSec, m.Weather);
            w.MissionId = m.Id;
            w.Timetable = tt;
            w.Service = new ServiceTracker(tt, w.Scoring);
            w.script = SampleRoute.BuildSignalScript(route, tt, m.WithSignalDelay);
            w.Train.PassengerMassKg = tt.Stops[0].PassengerLoadAfterKg;
            w.ApplySignalScript();
            return w;
        }

        void ApplySignalScript()
        {
            while (scriptIndex < script.Count && script[scriptIndex].TimeSec <= Clock.Seconds)
            {
                Graph.Signals[script[scriptIndex].SignalId].ForcedHalt = script[scriptIndex].ForcedHalt;
                scriptIndex++;
            }
            Graph.UpdateSignals();
        }

        public bool RequestDoors(bool open)
        {
            if (!open) { Train.Doors.RequestClose(); return true; }
            bool allowed = Service == null ? Train.Speed < 0.3 : Service.CanOpenDoors(Train.Speed);
            return allowed && Train.Doors.RequestOpen(Train.Speed);
        }

        public bool TrySetSwitch(int nodeId, bool diverging)
        {
            return Graph.TrySetSwitch(nodeId, diverging, Follower, Spec.LengthM);
        }

        public bool TryReleaseEmergency() { return Safety.TryRelease(); }

        public ScoreCard FinalScore { get { return Scoring.Compute(Safety); } }

        /// <summary>Aktuell nutzbare Betriebsbremsverzoegerung (witterungsabhaengig).</summary>
        public double AvailableServiceDecel
        {
            get { return Math.Min(Spec.MaxServiceDecel, TrainPhysics.AvailableDecel(Weather.AdhesionFactor)); }
        }

        /// <summary>Zug steht am Ende der Strecke (Prellbock-Bereich, letzte 120 m).</summary>
        public bool IsAtTerminus
        {
            get
            {
                if (Train.Speed > 0.05) return false;
                if (Follower.AtEndOfTrack) return true;
                return Graph.NextEdge(Follower.EdgeId) < 0 && Follower.Edge.Length - Follower.Offset < 120.0;
            }
        }

        public bool IsFinished { get { return Service != null && Service.IsFinished; } }

        void RouteAssist()
        {
            if (!AutoRoute || Service == null || Service.Current == null) return;
            int target = Service.Current.EdgeId;
            foreach (var f in Ahead)
            {
                if (f.Kind != FeatureKind.Switch || f.Distance > 1500) continue;
                var node = Graph.Nodes[f.Id];
                bool straightOk = Graph.CanReach(node.Outgoing[0], target);
                bool divergingOk = Graph.CanReach(node.Outgoing[1], target);
                if (straightOk == divergingOk) continue;
                Graph.TrySetSwitch(node.Id, divergingOk, Follower, Spec.LengthM);
            }
        }

        public void Step(double dt)
        {
            Weather.Update(dt);
            Clock.Advance(dt);
            ApplySignalScript();

            if (Math.Abs(Input.Throttle - lastInput.Throttle) > 0.01 || Math.Abs(Input.Brake - lastInput.Brake) > 0.01)
                Safety.NotifyDriverActivity();
            lastInput = Input;

            if ((aheadCounter++ % 5) == 0)
            {
                Follower.ScanAhead(3000.0, Ahead);
                RouteAssist();
            }

            double adh = Weather.AdhesionFactor;
            double availDecel = Math.Min(Spec.MaxServiceDecel, TrainPhysics.AvailableDecel(adh));
            Safety.CurveDecel = Math.Min(0.7, 0.9 * availDecel);

            var cond = new TrackConditions
            {
                GradePermille = Follower.GradePermilleForTrain(Spec.LengthM),
                CurveRadiusM = Follower.CurveRadiusAtHead(),
            };
            Train.Step(dt, Input, Safety.EmergencyBrakeActive, cond, adh);
            Follower.Advance(Train.Speed * dt);

            if (Follower.AtEndOfTrack && Train.Speed > 0.0)
            {
                if (Train.Speed > 1.5) Safety.Intervene("Gleisende überfahren");
                Train.Speed = 0.0;
                Train.Acceleration = 0.0;
            }

            Safety.Update(dt, Train.Speed, Follower, Spec.LengthM);
            if (Service != null) Service.Update(dt, Clock.Seconds, Train, Follower);
            Scoring.UpdateComfort(dt, Train.Speed, Train.Acceleration);
        }

        /// <summary>Naechste Weiche voraus (fuer HUD/Touch-Bedienung) oder null.</summary>
        public bool TryGetNextSwitch(out TrackFeature feature)
        {
            foreach (var f in Ahead)
                if (f.Kind == FeatureKind.Switch) { feature = f; return true; }
            feature = default(TrackFeature);
            return false;
        }
    }
}
