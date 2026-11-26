using System;

namespace ZugSim.Core
{
    public enum WeatherPreset { Clear, Overcast, Rain, HeavyRain, Fog, Storm }

    /// <summary>Momentaufnahme des Wetters. Alle Werte 0..1.</summary>
    public sealed class WeatherState
    {
        public double Cloudiness;
        public double Rain;
        public double Fog;
        /// <summary>Laub/Glaette auf der Schiene (verschlechtert Haftung zusaetzlich).</summary>
        public double SlipperyRail;

        public WeatherState Clone()
        {
            return new WeatherState { Cloudiness = Cloudiness, Rain = Rain, Fog = Fog, SlipperyRail = SlipperyRail };
        }

        public static WeatherState FromPreset(WeatherPreset p)
        {
            switch (p)
            {
                case WeatherPreset.Overcast: return new WeatherState { Cloudiness = 0.7 };
                case WeatherPreset.Rain: return new WeatherState { Cloudiness = 0.85, Rain = 0.5, SlipperyRail = 0.2 };
                case WeatherPreset.HeavyRain: return new WeatherState { Cloudiness = 1.0, Rain = 1.0, SlipperyRail = 0.4, Fog = 0.2 };
                case WeatherPreset.Fog: return new WeatherState { Cloudiness = 0.6, Fog = 0.85 };
                case WeatherPreset.Storm: return new WeatherState { Cloudiness = 1.0, Rain = 0.9, Fog = 0.1, SlipperyRail = 0.3 };
                default: return new WeatherState { Cloudiness = 0.1 };
            }
        }

        public static WeatherState Lerp(WeatherState a, WeatherState b, double t)
        {
            return new WeatherState
            {
                Cloudiness = Units.Lerp(a.Cloudiness, b.Cloudiness, t),
                Rain = Units.Lerp(a.Rain, b.Rain, t),
                Fog = Units.Lerp(a.Fog, b.Fog, t),
                SlipperyRail = Units.Lerp(a.SlipperyRail, b.SlipperyRail, t),
            };
        }
    }

    /// <summary>Ableitung physikalisch relevanter Groessen aus dem Wetter.</summary>
    public static class WeatherModel
    {
        /// <summary>Haftbeiwert (trocken ~0.30).</summary>
        public const double DryAdhesion = 0.30;
        /// <summary>Bremshaftwert (trocken; liegt unter dem Anfahr-Haftwert).</summary>
        public const double DryBrakeAdhesion = 0.14;

        public static double AdhesionFactor(WeatherState w)
        {
            // Regen: bis -45 %, Laub/Glaette: bis -45 % (multiplikativ), Nebel (Feuchte): leicht
            double f = 1.0 - 0.45 * w.Rain;
            f *= 1.0 - 0.45 * w.SlipperyRail;
            f *= 1.0 - 0.08 * w.Fog;
            return Units.Clamp(f, 0.2, 1.0);
        }

        /// <summary>Sichtweite in Metern.</summary>
        public static double VisibilityM(WeatherState w)
        {
            double vis = 12000.0;
            vis *= 1.0 - 0.55 * w.Rain;
            // Nebel: exponentiell bis 60 m
            vis = Math.Min(vis, 60.0 + (12000.0 - 60.0) * Math.Pow(1.0 - w.Fog, 3.0));
            return Math.Max(vis, 50.0);
        }
    }

    /// <summary>Tageszeit. <see cref="TimeScale"/> beschleunigt nur die Uhr (Freie Fahrt: Tag/Nacht-Zyklus).</summary>
    public sealed class GameClock
    {
        public double Seconds;           // Sekunden seit Mitternacht, laeuft ueber 86400 hinaus weiter
        public double TimeScale = 1.0;

        public GameClock(double secondsOfDay) { Seconds = secondsOfDay; }

        public void Advance(double dt) { Seconds += dt * TimeScale; }

        public double Hour { get { return (Seconds % 86400.0) / 3600.0; } }

        /// <summary>Sonnenhoehe in Grad (vereinfacht, Aequinoktium): 06:00 = 0, 12:00 = 55, 24:00 = -55.</summary>
        public double SunElevationDeg { get { return 55.0 * Math.Sin((Hour - 6.0) / 12.0 * Math.PI); } }

        /// <summary>0 = Nacht, 1 = voller Tag.</summary>
        public double Daylight { get { return Units.SmoothStep(-6.0, 10.0, SunElevationDeg); } }

        public bool IsNight { get { return Daylight < 0.15; } }
    }

    /// <summary>Wetterverlauf mit weichen Uebergaengen und optionalem Zufallswechsel.</summary>
    public sealed class WeatherSystem
    {
        WeatherState from, to;
        double transition = 1.0, transitionDuration = 1.0;
        Random rng;
        double autoTimer;

        public WeatherState Current { get; private set; }
        public bool AutoCycle;
        public double AutoCycleMinSeconds = 240, AutoCycleMaxSeconds = 600;

        public WeatherSystem(WeatherPreset start, int seed = 1)
        {
            from = to = WeatherState.FromPreset(start);
            Current = from.Clone();
            rng = new Random(seed);
        }

        public void SetTarget(WeatherPreset p, double transitionSeconds)
        {
            from = Current.Clone();
            to = WeatherState.FromPreset(p);
            transition = 0.0;
            transitionDuration = Math.Max(0.001, transitionSeconds);
        }

        public void SetImmediate(WeatherPreset p)
        {
            from = to = WeatherState.FromPreset(p);
            Current = from.Clone();
            transition = 1.0;
        }

        public void Update(double dt)
        {
            if (transition < 1.0)
            {
                transition = Math.Min(1.0, transition + dt / transitionDuration);
                Current = WeatherState.Lerp(from, to, transition);
            }
            if (AutoCycle)
            {
                autoTimer -= dt;
                if (autoTimer <= 0)
                {
                    var values = (WeatherPreset[])Enum.GetValues(typeof(WeatherPreset));
                    SetTarget(values[rng.Next(values.Length)], 60.0);
                    autoTimer = AutoCycleMinSeconds + rng.NextDouble() * (AutoCycleMaxSeconds - AutoCycleMinSeconds);
                }
            }
        }

        public double AdhesionFactor { get { return WeatherModel.AdhesionFactor(Current); } }
        public double VisibilityM { get { return WeatherModel.VisibilityM(Current); } }
    }
}
