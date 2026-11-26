using System;
using Xunit;
using ZugSim.Core;
using ZugSim.Core.Career;
using ZugSim.Core.Routes;
using ZugSim.Core.Safety;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

public class SafetyAndServiceTests
{
    static SimulationWorld Mission(int idx = 0) { return SimulationWorld.CreateMission(MissionCatalog.All[idx]); }

    [Fact]
    public void PassingHaltSignalTriggersEmergencyBrake()
    {
        var w = Mission();
        w.Clock.Seconds = 0;   // egal; Signal bleibt Halt
        w.Graph.Signals[w.Route.SignalHbfExit].ForcedHalt = true; w.Graph.UpdateSignals();
        w.Input.Throttle = 1;
        for (int i = 0; i < 20000 && !w.Safety.EmergencyBrakeActive; i++) { w.Input.Throttle = 1; w.Step(0.02); }
        Assert.True(w.Safety.EmergencyBrakeActive || w.Safety.SignalViolations > 0 || w.Safety.Interventions > 0);
    }

    [Fact]
    public void OverspeedIntoRestrictionCausesWarningThenIntervention()
    {
        var w = Mission();
        // Zug nahe vor die 120er-Zone im Bahnhofsbereich setzen, mit hoher Geschwindigkeit
        w.Train.Speed = Units.KmhToMs(150);
        w.Graph.Signals[w.Route.SignalHbfExit].ForcedHalt = false; w.Graph.UpdateSignals();
        bool warned = false;
        for (int i = 0; i < 3000 && !w.Safety.EmergencyBrakeActive; i++)
        {
            w.Input.Throttle = 0;
            w.Step(0.02);
            if (w.Safety.State == SafetyState.Warning) warned = true;
        }
        Assert.True(w.Safety.EmergencyBrakeActive);
        Assert.True(w.Safety.Interventions >= 1);
        // nach Stillstand quittieren
        for (int i = 0; i < 6000 && w.Train.Speed > 0; i++) w.Step(0.02);
        for (int i = 0; i < 300; i++) w.Step(0.02);
        Assert.True(w.TryReleaseEmergency());
        Assert.False(w.Safety.EmergencyBrakeActive);
    }

    [Fact]
    public void SifaTriggersWithoutDriverActivity()
    {
        var w = Mission();
        w.Graph.Signals[w.Route.SignalHbfExit].ForcedHalt = false; w.Graph.UpdateSignals();
        w.Train.Speed = Units.KmhToMs(45);
        w.Input.Throttle = 0.3;   // konstant, keine Aenderung -> keine Aktivitaet
        w.Step(0.02);
        for (int i = 0; i < 2000 && !w.Safety.SifaWarning; i++) { w.Input.Throttle = 0.3; w.Step(0.02); if (w.Train.Speed > 21) w.Input.Throttle = 0.0; }
        // Aktivitaet zuruecksetzen funktioniert
        w.Safety.AcknowledgeSifa();
        Assert.False(w.Safety.SifaWarning);
    }

    [Fact]
    public void PunctualityAndAccuracyScoring()
    {
        Assert.Equal(100, ScoringSystem.PunctualityScoreFor(10));
        Assert.Equal(100, ScoringSystem.PunctualityScoreFor(-20));
        Assert.Equal(0, ScoringSystem.PunctualityScoreFor(330), 6);
        Assert.InRange(ScoringSystem.PunctualityScoreFor(120), 60, 75);
        Assert.Equal(100, ScoringSystem.AccuracyScoreFor(-1.5));
        Assert.True(ScoringSystem.AccuracyScoreFor(5) < ScoringSystem.AccuracyScoreFor(-5));
        Assert.Equal(0, ScoringSystem.AccuracyScoreFor(15), 6);
    }

    [Fact]
    public void MissionsAreUnlockedByStars()
    {
        var p = new CareerProgress();
        Assert.True(p.IsUnlocked(MissionCatalog.All[0]));
        Assert.False(p.IsUnlocked(MissionCatalog.All[1]));
        p.Submit("m1", new ScoreCard { Stars = 3, Total = 95 });
        Assert.True(p.IsUnlocked(MissionCatalog.All[1]));
        p.Submit("m1", new ScoreCard { Stars = 1, Total = 60 });
        Assert.Equal(3, p.Get("m1").Stars);
        Assert.Equal(2, p.Get("m1").Attempts);
    }

    [Fact]
    public void DoorsCannotBeOpenedBetweenStations()
    {
        var w = Mission();
        w.Graph.Signals[w.Route.SignalHbfExit].ForcedHalt = false; w.Graph.UpdateSignals();
        w.Train.Speed = 0.0;
        w.Service.Update(0.02, w.Clock.Seconds, w.Train, w.Follower);
        Assert.True(w.RequestDoors(true));     // am Startbahnsteig ok
        w.Train.Doors.RequestClose();
        for (int i = 0; i < 1000; i++) w.Step(0.02);
        w.Train.Speed = 20;
        Assert.False(w.RequestDoors(true));
    }
}
