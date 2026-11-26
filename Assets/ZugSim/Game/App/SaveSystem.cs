using System;
using System.IO;
using UnityEngine;
using ZugSim.Core.Career;

namespace ZugSim.Game
{
    /// <summary>Speichert den Karrierefortschritt als JSON im persistenten App-Verzeichnis.</summary>
    public static class SaveSystem
    {
        static string PathFile { get { return Path.Combine(Application.persistentDataPath, "career.json"); } }

        public static CareerProgress Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var p = JsonUtility.FromJson<CareerProgress>(File.ReadAllText(PathFile));
                    if (p != null) { if (p.Records == null) p.Records = new System.Collections.Generic.List<MissionRecord>(); return p; }
                }
            }
            catch (Exception e) { Debug.LogWarning("Speicherstand konnte nicht gelesen werden: " + e.Message); }
            return new CareerProgress();
        }

        public static void Save(CareerProgress p)
        {
            try { File.WriteAllText(PathFile, JsonUtility.ToJson(p, true)); }
            catch (Exception e) { Debug.LogWarning("Speichern fehlgeschlagen: " + e.Message); }
        }
    }
}
