using System;
using Xunit;
using ZugSim.Core;

public class PhysicsTests
{
    static TrainPhysics NewTrain(double pax = 0) { var t = new TrainPhysics(TrainSpec.RegionalEmu()); t.PassengerMassKg = pax; return t; }

    static void Run(TrainPhysics t, double seconds, DriverInput input, TrackConditions track = default(TrackConditions), double adhesion = 1.0)
    {
        for (double s = 0; s < seconds; s += 0.02) t.Step(0.02, input, false, track, adhesion);
    }

    [Fact]
    public void FullThrottleAcceleratesAndRespectsMaxSpeed()
    {
        var t = NewTrain(15000);
        Run(t, 20, new DriverInput { Throttle = 1 });
        Assert.InRange(t.Acceleration, 0.0, 1.2);
        Assert.InRange(t.SpeedKmh, 40, 90);   // nach 20 s deutlich in Fahrt
        Run(t, 600, new DriverInput { Throttle = 1 });
        Assert.True(t.Speed <= t.Spec.MaxSpeedMs + 0.5);
        Assert.InRange(t.SpeedKmh, 140, 161);
    }

    [Fact]
    public void StartingAccelerationIsInRealisticRange()
    {
        var t = NewTrain(15000);
        Run(t, 3, new DriverInput { Throttle = 1 });
        // 190 kN / (205 t * 1.06) ~ 0.87 m/s^2
        Assert.InRange(t.Acceleration, 0.6, 1.0);
    }

    [Fact]
    public void BrakingDistanceFrom100KmhIsPlausible()
    {
        var t = NewTrain(15000);
        t.Speed = Units.KmhToMs(100);
        double start = t.Odometer;
        Run(t, 60, new DriverInput { Brake = 1 });
        double dist = t.Odometer - start;
        Assert.Equal(0.0, t.Speed, 3);
        Assert.InRange(dist, 330, 480);   // v^2/2a = 386 m + Ansprechzeit
    }

    [Fact]
    public void EmergencyBrakeIsShorterThanServiceBrake()
    {
        var a = NewTrain(); a.Speed = Units.KmhToMs(120);
        var b = NewTrain(); b.Speed = Units.KmhToMs(120);
        for (int i = 0; i < 4000; i++)
        {
            a.Step(0.02, new DriverInput { Brake = 1 }, false, default(TrackConditions), 1.0);
            b.Step(0.02, new DriverInput { EmergencyBrake = true }, false, default(TrackConditions), 1.0);
        }
        Assert.True(b.Odometer < a.Odometer);
    }

    [Fact]
    public void RainLengthensBrakingDistance()
    {
        var dry = NewTrain(); dry.Speed = Units.KmhToMs(160);
        var wet = NewTrain(); wet.Speed = Units.KmhToMs(160);
        double adhesionWet = WeatherModel.AdhesionFactor(WeatherState.FromPreset(WeatherPreset.Storm));
        for (int i = 0; i < 6000; i++)
        {
            dry.Step(0.02, new DriverInput { Brake = 1 }, false, default(TrackConditions), 1.0);
            wet.Step(0.02, new DriverInput { Brake = 1 }, false, default(TrackConditions), adhesionWet);
        }
        Assert.True(wet.Odometer > dry.Odometer * 1.1, "dry " + dry.Odometer + " wet " + wet.Odometer);
    }

    [Fact]
    public void UphillSlowsAndDownhillSpeedsUp()
    {
        var flat = NewTrain(); var up = NewTrain(); var down = NewTrain();
        flat.Speed = up.Speed = down.Speed = Units.KmhToMs(80);
        for (int i = 0; i < 1500; i++)
        {
            flat.Step(0.02, default(DriverInput), false, new TrackConditions { GradePermille = 0 }, 1);
            up.Step(0.02, default(DriverInput), false, new TrackConditions { GradePermille = 20 }, 1);
            down.Step(0.02, default(DriverInput), false, new TrackConditions { GradePermille = -20 }, 1);
        }
        Assert.True(up.Speed < flat.Speed);
        Assert.True(down.Speed > flat.Speed);
    }

    [Fact]
    public void TrainDoesNotRollAwayOnSlopeWithBrakeApplied()
    {
        var t = NewTrain();
        Run(t, 10, new DriverInput { Brake = 1 }, new TrackConditions { GradePermille = 25 });
        Assert.Equal(0.0, t.Speed, 6);
    }

    [Fact]
    public void TractionIsCutWhileDoorsOpenAndWhileBraking()
    {
        var t = NewTrain();
        t.Doors.RequestOpen(0);
        Run(t, 6, new DriverInput { Throttle = 1 });
        Assert.Equal(0.0, t.Speed, 6);
        Assert.True(t.TractionCut);

        var u = NewTrain();
        Run(u, 5, new DriverInput { Throttle = 1, Brake = 0.5 });
        Assert.Equal(0.0, u.Speed, 6);
    }

    [Fact]
    public void DoorsCannotOpenWhileMoving()
    {
        var t = NewTrain(); t.Speed = 10;
        Assert.False(t.Doors.RequestOpen(t.Speed));
        t.Speed = 0;
        Assert.True(t.Doors.RequestOpen(t.Speed));
        Run(t, 4, default(DriverInput));
        Assert.Equal(DoorState.Open, t.Doors.State);
        t.Doors.RequestClose();
        Run(t, 5, default(DriverInput));
        Assert.Equal(DoorState.Closed, t.Doors.State);
    }

    [Fact]
    public void BrakePipePressureGaugeFollowsLever()
    {
        var t = NewTrain();
        Run(t, 0.5, new DriverInput { Brake = 0.0 });
        Assert.Equal(5.0, t.Brakes.MainPipeBar, 2);
        Run(t, 10, new DriverInput { Brake = 1.0 });
        Assert.Equal(3.5, t.Brakes.MainPipeBar, 2);
        Run(t, 3, new DriverInput { EmergencyBrake = true });
        Assert.Equal(0.0, t.Brakes.MainPipeBar, 1);
    }

    [Fact]
    public void WeatherModelProducesSensibleValues()
    {
        Assert.Equal(1.0, WeatherModel.AdhesionFactor(WeatherState.FromPreset(WeatherPreset.Clear)), 3);
        Assert.True(WeatherModel.AdhesionFactor(WeatherState.FromPreset(WeatherPreset.HeavyRain)) < 0.7);
        Assert.True(WeatherModel.VisibilityM(WeatherState.FromPreset(WeatherPreset.Fog)) < 400);
        Assert.True(WeatherModel.VisibilityM(WeatherState.FromPreset(WeatherPreset.Clear)) > 8000);
        var clock = new GameClock(12 * 3600);
        Assert.True(clock.Daylight > 0.95);
        clock.Seconds = 0;
        Assert.True(clock.IsNight);
        Assert.Equal("08:05:30", Units.FormatClock(8 * 3600 + 330));
    }

    [Fact]
    public void WeatherTransitionIsSmooth()
    {
        var w = new WeatherSystem(WeatherPreset.Clear);
        w.SetTarget(WeatherPreset.HeavyRain, 10);
        for (int i = 0; i < 250; i++) w.Update(0.02);
        Assert.InRange(w.Current.Rain, 0.3, 0.7);
        for (int i = 0; i < 400; i++) w.Update(0.02);
        Assert.Equal(1.0, w.Current.Rain, 3);
    }
}
