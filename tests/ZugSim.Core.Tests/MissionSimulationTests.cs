using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using ZugSim.Core;
using ZugSim.Core.Assist;
using ZugSim.Core.Career;
using ZugSim.Core.Service;

/// <summary>End-to-End: Der Autopilot muss alle Missionen planmaessig meistern (belegt, dass der Fahrplan fahrbar ist).</summary>
public class MissionSimulationTests
{
    readonly ITestOutputHelper output;
    public MissionSimulationTests(ITestOutputHelper output) { this.output = output; }

    ScoreCard Drive(MissionDefinition m, out SimulationWorld world)
    {
        var w = SimulationWorld.CreateMission(m);
        var driver = new AutoDriver();
        double t = 0;
        while (!w.IsFinished && t < 3600)
        {
            driver.Update(w, SimulationWorld.FixedStep);
            w.Step(SimulationWorld.FixedStep);
            ServiceEvent e;
            while (w.Service.DrainEvent(out e)) output.WriteLine(Units.FormatClock(e.TimeSec) + "  " + e.Text);
            t += SimulationWorld.FixedStep;
        }
        world = w;
        var card = w.FinalScore;
        output.WriteLine(string.Format("{0}: total {1:0.0} (P {2:0} A {3:0} C {4:0} S {5:0}) stars {6}, interventions {7}, t={8:0}s",
            m.Id, card.Total, card.Punctuality, card.Accuracy, card.Comfort, card.Safety, card.Stars, card.Interventions, t));
        foreach (var s in card.Stops) output.WriteLine(string.Format("  {0}: delay {1:0.0}s err {2:0.0}m missed={3}", s.Station, s.DelaySec, s.StopErrorM, s.Missed));
        return card;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AutopilotCompletesMissionWithStars(int index)
    {
        SimulationWorld w;
        var card = Drive(MissionCatalog.All[index], out w);
        Assert.True(w.IsFinished, "Fahrt nicht beendet");
        Assert.Equal(0, card.Interventions);
        Assert.Equal(0, card.SignalViolations);
        Assert.Equal(0, card.MissedStops);
        Assert.True(card.Stars >= 2, "Sterne: " + card.Stars + ", Punkte " + card.Total);
    }

    [Fact]
    public void WithoutAutoRouteTheDriverMustSetTheSwitch()
    {
        var w = SimulationWorld.CreateMission(MissionCatalog.All[0]);
        w.AutoRoute = false;
        var driver = new AutoDriver();
        double t = 0;
        while (!w.IsFinished && t < 1800 && w.Service.StopIndex < 2)
        {
            driver.Update(w, SimulationWorld.FixedStep);
            w.Step(SimulationWorld.FixedStep);
            t += SimulationWorld.FixedStep;
        }
        // Weiche blieb gerade -> Halt in Beispieldorf verpasst
        Assert.True(w.FinalScore.MissedStops >= 1);
    }
}
