using System;
using System.Collections.Generic;
using ZugSim.Core.Service;

namespace ZugSim.Core.Career
{
    [Serializable]
    public class MissionRecord
    {
        public string Id;
        public int Stars;
        public double BestScore;
        public int Attempts;
    }

    /// <summary>Speicherstand des Karrieremodus (JsonUtility-kompatibel: oeffentliche Felder).</summary>
    [Serializable]
    public class CareerProgress
    {
        public List<MissionRecord> Records = new List<MissionRecord>();
        public int SelectedTrain;
        public int QualityLevel = 1;
        public bool AutoRoute = true;
        public bool SifaEnabled = true;
        public bool ShowFps;
        public bool AdaptiveResolution = true;
        public float Volume = 0.8f;

        public int TotalStars
        {
            get { int s = 0; foreach (var r in Records) s += r.Stars; return s; }
        }

        public MissionRecord Get(string id)
        {
            foreach (var r in Records) if (r.Id == id) return r;
            return null;
        }

        public bool IsUnlocked(MissionDefinition m) { return TotalStars >= m.RequiredStars; }

        public void Submit(string missionId, ScoreCard card)
        {
            var rec = Get(missionId);
            if (rec == null) { rec = new MissionRecord { Id = missionId }; Records.Add(rec); }
            rec.Attempts++;
            rec.Stars = Math.Max(rec.Stars, card.Stars);
            rec.BestScore = Math.Max(rec.BestScore, card.Total);
        }
    }

    public sealed class MissionDefinition
    {
        public string Id, Title, Description;
        public int RequiredStars;
        public WeatherPreset Weather;
        public double DepartureSec;
        public bool WithSignalDelay;
        public double DelayStartSec;     // Zug startet mit dieser Verspaetung (Aufholjagd)
        public double Run1 = 270, Run2 = 285;
    }

    public static class MissionCatalog
    {
        public static readonly MissionDefinition[] All =
        {
            new MissionDefinition { Id = "m1", Title = "Erste Fahrt", RequiredStars = 0, Weather = WeatherPreset.Clear,
                DepartureSec = 8 * 3600, Description = "Sonniger Morgen: Fahre den RE planmaessig von Musterstadt nach Endhausen. Stelle in Beispieldorf die Weiche auf Gleis 2." },
            new MissionDefinition { Id = "m2", Title = "Regen in Beispieldorf", RequiredStars = 2, Weather = WeatherPreset.Rain,
                DepartureSec = 8.5 * 3600, Run1 = 285, Run2 = 300, Description = "Nasse Schienen verlaengern den Bremsweg. Bremse frueher." },
            new MissionDefinition { Id = "m3", Title = "Nebel am Morgen", RequiredStars = 4, Weather = WeatherPreset.Fog,
                DepartureSec = 6.75 * 3600, Run2 = 330, WithSignalDelay = true, Description = "Sichtweite unter 200 m und ein Haltsignal in Endhausen - verlasse dich auf Signalanzeige und Geschwindigkeitsueberwachung." },
            new MissionDefinition { Id = "m4", Title = "Nachtverkehr im Sturm", RequiredStars = 6, Weather = WeatherPreset.Storm,
                DepartureSec = 22 * 3600 + 600, Run1 = 300, Run2 = 360, WithSignalDelay = true, Description = "Dunkelheit, Sturm und rutschige Schienen. Die Meisterpruefung." },
        };

        public static MissionDefinition Find(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }
    }
}
