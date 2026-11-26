using System;
using System.Collections.Generic;
using ZugSim.Core.Track;

namespace ZugSim.Core.Service
{
    public enum ServicePhase { Approaching, AtPlatform, Finished }
    public enum ServiceEventKind { Arrived, Missed, BoardingComplete, DepartureReady, Departed, EarlyDeparture, Finished }

    public struct ServiceEvent
    {
        public ServiceEventKind Kind;
        public double TimeSec;
        public string Text;
    }

    /// <summary>Fahrplan-Zustandsautomat: Anfahrt, Halt, Fahrgastwechsel, Abfahrt.</summary>
    public sealed class ServiceTracker
    {
        public const double MaxUndershootM = 60.0;
        public const double MaxOvershootM = 15.0;

        public readonly Timetable Timetable;
        readonly ScoringSystem scoring;
        readonly Queue<ServiceEvent> events = new Queue<ServiceEvent>();

        public int StopIndex { get; private set; }
        public ServicePhase Phase { get; private set; }
        public bool BoardingComplete { get; private set; }
        public bool DepartureReady { get; private set; }
        public double LastArrivalDelaySec { get; private set; }

        public ServiceTracker(Timetable timetable, ScoringSystem scoring)
        {
            Timetable = timetable;
            this.scoring = scoring;
            Phase = ServicePhase.AtPlatform;   // Startbahnsteig
            BoardingComplete = true;
        }

        public StopEntry Current { get { return StopIndex < Timetable.Stops.Count ? Timetable.Stops[StopIndex] : null; } }
        public bool IsFinished { get { return Phase == ServicePhase.Finished; } }

        public bool DrainEvent(out ServiceEvent e)
        {
            if (events.Count > 0) { e = events.Dequeue(); return true; }
            e = default(ServiceEvent);
            return false;
        }

        void Emit(ServiceEventKind k, double t, string text)
        {
            events.Enqueue(new ServiceEvent { Kind = k, TimeSec = t, Text = text });
        }

        /// <summary>Tueren duerfen nur am Bahnsteig (oder im Notfall nicht) geoeffnet werden.</summary>
        public bool CanOpenDoors(double speedMs)
        {
            return Phase == ServicePhase.AtPlatform && speedMs < 0.3;
        }

        public void Update(double dt, double nowSec, TrainPhysics train, TrackFollower follower)
        {
            var stop = Current;
            if (stop == null || Phase == ServicePhase.Finished) return;

            if (Phase == ServicePhase.Approaching)
            {
                if (stop.ExpireEdgeId >= 0 && follower.EdgeId == stop.ExpireEdgeId) { Miss(stop, nowSec); return; }
                if (follower.EdgeId == stop.EdgeId)
                {
                    double err = follower.Offset - stop.TargetOffset;
                    if (err > MaxOvershootM) { Miss(stop, nowSec); return; }
                    if (train.Speed < 0.1 && err >= -MaxUndershootM)
                    {
                        scoring.RecordArrival(stop.Station, stop.ArrivalSec, nowSec, err);
                        LastArrivalDelaySec = nowSec - stop.ArrivalSec;
                        Phase = ServicePhase.AtPlatform;
                        BoardingComplete = false;
                        DepartureReady = false;
                        Emit(ServiceEventKind.Arrived, nowSec,
                            string.Format("{0}: Ankunft {1:+0;-0;±0} s, Halteabweichung {2:+0.0;-0.0;0.0} m", stop.Station, LastArrivalDelaySec, err));
                    }
                }
                return;
            }

            // AtPlatform
            if (!BoardingComplete && train.Doors.State == DoorState.Open && train.Doors.OpenDurationS >= stop.MinDwellSec)
            {
                BoardingComplete = true;
                train.PassengerMassKg = stop.PassengerLoadAfterKg;
                Emit(ServiceEventKind.BoardingComplete, nowSec, "Fahrgastwechsel abgeschlossen");
                if (stop.IsTerminal)
                {
                    Phase = ServicePhase.Finished;
                    Emit(ServiceEventKind.Finished, nowSec, "Endstation erreicht");
                    return;
                }
            }

            bool readyNow = BoardingComplete && nowSec >= stop.DepartureSec;
            if (readyNow && !DepartureReady)
                Emit(ServiceEventKind.DepartureReady, nowSec, "Abfahrbereit");
            DepartureReady = readyNow;

            if (train.Speed > 0.5 && train.Doors.IsClosedAndLocked)
            {
                bool early = BoardingComplete && nowSec < stop.DepartureSec - 5.0;
                if (early)
                {
                    scoring.RecordEarlyDeparture();
                    Emit(ServiceEventKind.EarlyDeparture, nowSec, "Zu frueh abgefahren!");
                }
                Emit(ServiceEventKind.Departed, nowSec, "Abfahrt " + stop.Station);
                StopIndex++;
                Phase = ServicePhase.Approaching;
                DepartureReady = false;
            }
        }

        void Miss(StopEntry stop, double nowSec)
        {
            scoring.RecordMissedStop(stop.Station);
            Emit(ServiceEventKind.Missed, nowSec, "Halt verpasst: " + stop.Station);
            StopIndex++;
            if (Current == null) Phase = ServicePhase.Finished;
            else if (Current.IsOrigin) Phase = ServicePhase.AtPlatform;
        }
    }
}
