using System;
using System.Collections.Generic;
using Xunit;
using ZugSim.Core;
using ZugSim.Core.Routes;
using ZugSim.Core.Track;

public class TrackTests
{
    [Fact]
    public void FollowerCrossesEdgesAndSwitchRoutesCorrectly()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeMain, 4900);
        f.Advance(150);
        Assert.Equal(r.EdgeThrough, f.EdgeId);
        Assert.Equal(50, f.Offset, 3);

        r.Graph.Nodes[r.SwitchNode].Diverging = true;
        var g = new TrackFollower(r.Graph, r.EdgeMain, 4900);
        g.Advance(150);
        Assert.Equal(r.EdgeLoop, g.EdgeId);
    }

    [Fact]
    public void FollowerStopsAtEndOfTrack()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeEnd, 5300);
        f.Advance(500);
        Assert.True(f.AtEndOfTrack);
        Assert.Equal(5400, f.Offset, 3);
    }

    [Fact]
    public void SwitchIsLockedWhenTrainIsClose()
    {
        var r = SampleRoute.Build();
        var near = new TrackFollower(r.Graph, r.EdgeMain, 4950);
        Assert.False(r.Graph.TrySetSwitch(r.SwitchNode, true, near, 105));
        var far = new TrackFollower(r.Graph, r.EdgeMain, 3000);
        Assert.True(r.Graph.TrySetSwitch(r.SwitchNode, true, far, 105));
        Assert.True(r.Graph.Nodes[r.SwitchNode].Diverging);
        var onLoop = new TrackFollower(r.Graph, r.EdgeLoop, 50);
        Assert.False(r.Graph.TrySetSwitch(r.SwitchNode, false, onLoop, 105));
    }

    [Fact]
    public void LocateBehindWorksAcrossEdges()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeMain, 4980);
        f.Advance(40);   // jetzt 20 m auf E1
        int e; double o;
        f.LocateBehind(10, out e, out o);
        Assert.Equal(r.EdgeThrough, e); Assert.Equal(10, o, 3);
        f.LocateBehind(30, out e, out o);
        Assert.Equal(r.EdgeMain, e); Assert.Equal(4990, o, 3);
    }

    [Fact]
    public void ScanAheadFindsSignalsLimitsAndSwitch()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeMain, 4000);
        var list = new List<TrackFeature>();
        f.ScanAhead(2000, list);
        bool sig = false, lim = false, sw = false;
        foreach (var x in list)
        {
            if (x.Kind == FeatureKind.Signal && x.Name.Contains("Einfahrt")) { sig = true; Assert.Equal(800, x.Distance, 3); }
            if (x.Kind == FeatureKind.SpeedLimit && Math.Abs(x.Distance - 400) < 1) { lim = true; Assert.Equal(Units.KmhToMs(120), x.Value, 3); }
            if (x.Kind == FeatureKind.Switch) { sw = true; Assert.Equal(1000, x.Distance, 3); }
        }
        Assert.True(sig && lim && sw);
        for (int i = 1; i < list.Count; i++) Assert.True(list[i].Distance >= list[i - 1].Distance);
    }

    [Fact]
    public void DistanceToFollowsSwitchState()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeMain, 4000);
        Assert.True(double.IsInfinity(f.DistanceTo(r.EdgeLoop, 380, 8000)));
        r.Graph.Nodes[r.SwitchNode].Diverging = true;
        Assert.Equal(1000 + 380, f.DistanceTo(r.EdgeLoop, 380, 8000), 3);
    }

    [Fact]
    public void LoopGeometryMergesWithThroughTrack()
    {
        var r = SampleRoute.Build();
        var through = r.Graph.Edges[r.EdgeThrough].Geometry;
        var loop = r.Graph.Edges[r.EdgeLoop].Geometry;
        Assert.Equal(through.End.X, loop.End.X, 0);      // auf ~0.5 m genau
        Assert.Equal(through.End.Z, loop.End.Z, 0);
        // Bahnsteiggleis liegt 4.5 m neben dem Durchfahrgleis
        var a = through.Sample(380); var b = loop.Sample(380);
        double lateral = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
        Assert.InRange(lateral, 4.0, 5.0);
    }

    [Fact]
    public void GeometryClimbsOnGrade()
    {
        var r = SampleRoute.Build();
        var geo = r.Graph.Edges[r.EdgeMain].Geometry;
        Assert.Equal(0, geo.Sample(1900).Y, 3);
        Assert.True(geo.Sample(3000).Y > 10.0);      // 1000 m * 12 Promille
        Assert.True(geo.Sample(4200).Y < geo.Sample(3000).Y);
    }

    [Fact]
    public void SignalsReflectSwitchAndForcedHalt()
    {
        var r = SampleRoute.Build();
        var s = r.Graph.Signals[r.SignalDorfEntry];
        Assert.Equal(SignalAspect.Proceed, s.Aspect);
        r.Graph.Nodes[r.SwitchNode].Diverging = true; r.Graph.UpdateSignals();
        Assert.Equal(SignalAspect.ProceedSlow, s.Aspect);
        s.ForcedHalt = true; r.Graph.UpdateSignals();
        Assert.Equal(SignalAspect.Halt, s.Aspect);
        Assert.Equal(SignalAspect.Halt, r.Graph.Signals[r.SignalHbfExit].Aspect);
    }

    [Fact]
    public void FollowerReportsPassedSignals()
    {
        var r = SampleRoute.Build();
        var f = new TrackFollower(r.Graph, r.EdgeMain, 290);
        f.Advance(5);
        Assert.Empty(f.PassedSignals);
        f.Advance(10);
        Assert.Single(f.PassedSignals);
    }
}
