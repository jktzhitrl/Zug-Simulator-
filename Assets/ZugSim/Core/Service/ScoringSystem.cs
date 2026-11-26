using System;
using System.Collections.Generic;
using ZugSim.Core.Safety;

namespace ZugSim.Core.Service
{
    public sealed class StopResult
    {
        public string Station;
        public bool Missed;
        public double DelaySec;
        public double StopErrorM;
        public double PunctualityScore;
        public double AccuracyScore;
        public bool EarlyDeparture;
    }

    public sealed class ScoreCard
    {
        public double Punctuality, Accuracy, Comfort, Safety, Total;
        public int Stars;
        public bool Passed;
        public int Interventions, SignalViolations, EarlyDepartures, MissedStops;
        public List<StopResult> Stops = new List<StopResult>();
    }

    /// <summary>Bewertung einer Fahrt: Puenktlichkeit, Haltegenauigkeit, Fahrkomfort (Ruck) und Sicherheit.</summary>
    public sealed class ScoringSystem
    {
        public const double ComfortJerkLimit = 0.8;     // m/s^3
        public const double ComfortAccelLimit = 1.2;    // m/s^2

        public readonly List<StopResult> Results = new List<StopResult>();

        double smoothedAccel, prevSmoothedAccel;
        double movingTime, discomfort;
        public double CurrentJerk { get; private set; }
        public int EarlyDepartures { get; private set; }

        public void UpdateComfort(double dt, double speedMs, double accel)
        {
            double k = 1.0 - Math.Exp(-dt / 0.4);
            smoothedAccel += (accel - smoothedAccel) * k;
            CurrentJerk = (smoothedAccel - prevSmoothedAccel) / dt;
            prevSmoothedAccel = smoothedAccel;
            if (speedMs < 0.3 && Math.Abs(accel) < 0.05) return;
            movingTime += dt;
            discomfort += dt * (Math.Max(0, Math.Abs(CurrentJerk) - ComfortJerkLimit)
                              + 2.0 * Math.Max(0, Math.Abs(smoothedAccel) - ComfortAccelLimit));
        }

        public static double PunctualityScoreFor(double delaySec)
        {
            if (delaySec > 30) return Units.Clamp(100.0 - (delaySec - 30.0) / 270.0 * 100.0, 0, 100);
            if (delaySec < -30) return Units.Clamp(100.0 - (-delaySec - 30.0) * 0.5, 0, 100);
            return 100.0;
        }

        public static double AccuracyScoreFor(double errorM)
        {
            // errorM > 0 = ueberfahren (wiegt doppelt)
            double e = errorM > 0 ? errorM * 2.0 : -errorM;
            if (e <= 2.0) return 100.0;
            return Units.Clamp(100.0 - (e - 2.0) / 28.0 * 100.0, 0, 100);
        }

        public void RecordArrival(string station, double scheduledSec, double actualSec, double errorM)
        {
            double delay = actualSec - scheduledSec;
            Results.Add(new StopResult
            {
                Station = station, DelaySec = delay, StopErrorM = errorM,
                PunctualityScore = PunctualityScoreFor(delay), AccuracyScore = AccuracyScoreFor(errorM),
            });
        }

        public void RecordMissedStop(string station)
        {
            Results.Add(new StopResult { Station = station, Missed = true });
        }

        public void RecordEarlyDeparture()
        {
            EarlyDepartures++;
            if (Results.Count > 0) Results[Results.Count - 1].EarlyDeparture = true;
        }

        public ScoreCard Compute(SafetyMonitor safety)
        {
            var card = new ScoreCard
            {
                Interventions = safety.Interventions,
                SignalViolations = safety.SignalViolations,
                EarlyDepartures = EarlyDepartures,
            };
            double punct = 0, acc = 0; int n = 0;
            foreach (var r in Results)
            {
                if (r.Missed) { card.MissedStops++; n++; continue; }
                double p = r.PunctualityScore - (r.EarlyDeparture ? 40.0 : 0.0);
                punct += Math.Max(0, p); acc += r.AccuracyScore; n++;
            }
            card.Stops = new List<StopResult>(Results);
            card.Punctuality = n > 0 ? punct / n : 100.0;
            card.Accuracy = n > 0 ? acc / n : 100.0;
            double avgExcess = movingTime > 1 ? discomfort / movingTime : 0.0;
            card.Comfort = Units.Clamp(100.0 * (1.0 - avgExcess / 0.25), 0, 100);
            card.Safety = Units.Clamp(100.0 - 25.0 * safety.Interventions - 50.0 * safety.SignalViolations, 0, 100);
            card.Total = 0.40 * card.Punctuality + 0.25 * card.Accuracy + 0.20 * card.Comfort + 0.15 * card.Safety;
            card.Passed = safety.SignalViolations == 0 && card.MissedStops == 0 && card.Total >= 55;
            card.Stars = !card.Passed ? 0 : (card.Total >= 90 ? 3 : (card.Total >= 75 ? 2 : 1));
            return card;
        }
    }
}
