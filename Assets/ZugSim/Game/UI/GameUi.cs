using System.Collections.Generic;
using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Career;
using ZugSim.Core.Safety;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Game
{
    /// <summary>Zeichnet Menues, HUD, Pausen- und Ergebnisbildschirm.</summary>
    public sealed class GameUi
    {
        readonly GameRoot g;
        UiToolkit ui { get { return g.Ui; } }

        struct Drop { public Vector2 Pos; public float Age, Size; }
        readonly List<Drop> drops = new List<Drop>();
        Texture2D softCircle;
        float dropAccum;
        float fpsSmoothed = 60f;
        bool showPauseMenu;
        Vector2 careerScroll;

        static readonly string[] WeatherNames = { "Klar", "Bewölkt", "Regen", "Starkregen", "Nebel", "Sturm" };

        public GameUi(GameRoot root) { g = root; }

        public void Draw()
        {
            float dt = Time.unscaledDeltaTime;
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / Mathf.Max(dt, 0.0001f), 0.05f);

            switch (g.State)
            {
                case AppScreen.MainMenu: DrawMainMenu(); break;
                case AppScreen.Career: DrawCareer(); break;
                case AppScreen.FreeRide: DrawFreeRide(); break;
                case AppScreen.Settings: DrawSettings(); break;
                case AppScreen.Playing: DrawHud(dt); break;
                case AppScreen.Result: DrawResult(); break;
            }

            if (g.Career.ShowFps || Application.isEditor)
                ui.Label(ui.R(12, 1040, 600, 30), string.Format("{0:0} FPS  {1:0.0} ms  Auflösung {2:0}%", fpsSmoothed, g.Perf.SmoothedFrameMs, g.Perf.ResolutionScale * 100f), 20, new Color(1, 1, 1, 0.55f));
        }

        // ================= Menues =================

        void Backdrop()
        {
            ui.Blocker(new Rect(0, 0, Screen.width, Screen.height));
            ui.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.35f));
        }

        void Title(string text, string sub = null)
        {
            ui.Label(ui.R(80, 50, 1200, 100), text, 84, Color.white, TextAnchor.MiddleLeft, true);
            if (sub != null) ui.Label(ui.R(84, 140, 1400, 50), sub, 32, new Color(1, 1, 1, 0.75f));
        }

        void DrawMainMenu()
        {
            Backdrop();
            Title("ZUG-SIMULATOR", "Regionalverkehr  ·  Cockpit  ·  Fahrplan");
            float x = 80, y = 300, w = 520, h = 100, gap = 24;
            if (ui.Button(ui.R(x, y, w, h), "Karriere", UiToolkit.Accent, 44)) g.State = AppScreen.Career;
            if (ui.Button(ui.R(x, y + (h + gap), w, h), "Freie Fahrt", UiToolkit.Good, 44)) g.State = AppScreen.FreeRide;
            if (ui.Button(ui.R(x, y + 2 * (h + gap), w, h), "Einstellungen", new Color(0.35f, 0.38f, 0.45f), 44)) g.State = AppScreen.Settings;
#if !UNITY_IOS
            if (ui.Button(ui.R(x, y + 3 * (h + gap), w, h), "Beenden", new Color(0.5f, 0.2f, 0.2f), 44)) Application.Quit();
#endif
            ui.Label(ui.RR(40, 1010, 900, 40), "Touch: Hebel im Cockpit ziehen  ·  Freie Fläche: umschauen  ·  Zwei Finger: Zoom", 24, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleRight);
        }

        void DrawCareer()
        {
            Backdrop();
            Title("KARRIERE", string.Format("Sterne gesamt: {0}", g.Career.TotalStars));
            if (ui.Button(ui.R(80, 960, 280, 80), "Zurück", new Color(0.35f, 0.38f, 0.45f), 36)) g.State = AppScreen.MainMenu;
            float y = 230;
            foreach (var m in MissionCatalog.All)
            {
                bool unlocked = g.Career.IsUnlocked(m);
                var rec = g.Career.Get(m.Id);
                Rect card = ui.R(80, y, 1400, 170);
                ui.Box(card, UiToolkit.Panel, new Color(1, 1, 1, 0.2f));
                ui.Label(ui.R(110, y + 12, 900, 56), m.Title, 44, unlocked ? Color.white : new Color(1, 1, 1, 0.45f), TextAnchor.MiddleLeft, true);
                string info = unlocked ? m.Description : string.Format("Gesperrt - benötigt {0} Sterne", m.RequiredStars);
                ui.Label(ui.R(110, y + 66, 1000, 96), info, 26, new Color(1, 1, 1, unlocked ? 0.8f : 0.5f), TextAnchor.UpperLeft);
                string stars = new string('★', rec != null ? rec.Stars : 0) + new string('☆', 3 - (rec != null ? rec.Stars : 0));
                ui.Label(ui.R(1130, y + 14, 330, 50), stars, 44, UiToolkit.Warn, TextAnchor.MiddleRight);
                ui.Label(ui.R(1130, y + 62, 330, 36), string.Format("{0}  ·  {1}", Units.FormatClock(m.DepartureSec).Substring(0, 5), WeatherNames[(int)m.Weather]), 24, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleRight);
                if (ui.Button(ui.R(1180, y + 102, 270, 56), unlocked ? "Fahrt starten" : "Gesperrt", UiToolkit.Accent, 28, unlocked)) g.StartMission(m);
                y += 190;
            }
        }

        void DrawFreeRide()
        {
            Backdrop();
            Title("FREIE FAHRT", "Wetter und Tageszeit frei wählen - Weichen manuell stellen");
            if (ui.Button(ui.R(80, 960, 280, 80), "Zurück", new Color(0.35f, 0.38f, 0.45f), 36)) g.State = AppScreen.MainMenu;

            ui.Label(ui.R(80, 250, 400, 40), "Wetter", 30, Color.white, TextAnchor.MiddleLeft, true);
            for (int i = 0; i < WeatherNames.Length; i++)
            {
                bool sel = (int)g.FreeWeather == i;
                if (ui.Button(ui.R(80 + (i % 3) * 270, 300 + (i / 3) * 90, 250, 74), WeatherNames[i], sel ? UiToolkit.Accent : new Color(0.25f, 0.28f, 0.34f), 30)) g.FreeWeather = (WeatherPreset)i;
            }
            ui.Label(ui.R(80, 520, 600, 40), string.Format("Startzeit: {0:00}:00 Uhr", Mathf.RoundToInt(g.FreeHour) % 24), 30, Color.white, TextAnchor.MiddleLeft, true);
            if (ui.Button(ui.R(80, 570, 120, 80), "-", new Color(0.25f, 0.28f, 0.34f), 44)) g.FreeHour = Mathf.Repeat(g.FreeHour - 1f, 24f);
            if (ui.Button(ui.R(220, 570, 120, 80), "+", new Color(0.25f, 0.28f, 0.34f), 44)) g.FreeHour = Mathf.Repeat(g.FreeHour + 1f, 24f);
            ui.Label(ui.R(80, 680, 700, 40), "Tag/Nacht-Zyklus", 30, Color.white, TextAnchor.MiddleLeft, true);
            string[] speeds = { "Echtzeit", "x30", "x120" }; float[] values = { 1f, 30f, 120f };
            for (int i = 0; i < 3; i++)
                if (ui.Button(ui.R(80 + i * 190, 730, 175, 74), speeds[i], Mathf.Approximately(g.FreeTimeScale, values[i]) ? UiToolkit.Accent : new Color(0.25f, 0.28f, 0.34f), 28)) g.FreeTimeScale = values[i];

            ui.Label(ui.R(900, 250, 600, 40), "Zug", 30, Color.white, TextAnchor.MiddleLeft, true);
            if (ui.Button(ui.R(900, 300, 520, 90), "RE-Triebzug (160 km/h)", g.FreeTrain == 0 ? UiToolkit.Accent : new Color(0.25f, 0.28f, 0.34f), 30)) g.FreeTrain = 0;
            if (ui.Button(ui.R(900, 410, 520, 90), "Hochgeschw.-Zug (250 km/h)", g.FreeTrain == 1 ? UiToolkit.Accent : new Color(0.25f, 0.28f, 0.34f), 30)) g.FreeTrain = 1;
            if (ui.Button(ui.R(900, 700, 520, 120), "Fahrt starten", UiToolkit.Good, 48)) g.StartFreeRide();
        }

        void DrawSettings()
        {
            Backdrop();
            Title("EINSTELLUNGEN");
            var c = g.Career;
            if (ui.Button(ui.R(80, 960, 280, 80), "Zurück", new Color(0.35f, 0.38f, 0.45f), 36)) { g.SaveSettings(); g.State = AppScreen.MainMenu; }

            ui.Label(ui.R(80, 210, 800, 40), "Grafikqualität", 30, Color.white, TextAnchor.MiddleLeft, true);
            string[] q = { "Niedrig", "Mittel", "Hoch" };
            for (int i = 0; i < 3; i++)
                if (ui.Button(ui.R(80 + i * 250, 260, 235, 80), q[i], c.QualityLevel == i ? UiToolkit.Accent : new Color(0.25f, 0.28f, 0.34f), 32)) { c.QualityLevel = i; g.SaveSettings(); }
            Toggle(80, 380, "Adaptive Auflösung (stabile 60 FPS)", ref c.AdaptiveResolution);
            Toggle(80, 470, "Automatische Weichenstellung (Karriere)", ref c.AutoRoute);
            Toggle(80, 560, "Sifa-Wachsamkeitstaste (Karriere)", ref c.SifaEnabled);
            Toggle(80, 650, "FPS-Anzeige", ref c.ShowFps);
            ui.Label(ui.R(80, 760, 600, 40), string.Format("Lautstärke: {0:0}%", c.Volume * 100f), 30, Color.white, TextAnchor.MiddleLeft, true);
            if (ui.Button(ui.R(80, 810, 120, 80), "-", new Color(0.25f, 0.28f, 0.34f), 44)) { c.Volume = Mathf.Clamp01(c.Volume - 0.1f); g.SaveSettings(); }
            if (ui.Button(ui.R(220, 810, 120, 80), "+", new Color(0.25f, 0.28f, 0.34f), 44)) { c.Volume = Mathf.Clamp01(c.Volume + 0.1f); g.SaveSettings(); }
        }

        void Toggle(float x, float y, string label, ref bool value)
        {
            if (ui.Button(ui.R(x, y, 120, 70), value ? "AN" : "AUS", value ? UiToolkit.Good : new Color(0.4f, 0.25f, 0.25f), 28)) value = !value;
            ui.Label(ui.R(x + 150, y, 900, 70), label, 30, Color.white, TextAnchor.MiddleLeft);
        }

        // ================= Ergebnis =================

        void DrawResult()
        {
            var r = g.LastResult;
            if (r == null) return;
            Backdrop();
            ui.Fill(ui.R(0, 0, 4000, 1080), new Color(0, 0, 0, 0.5f));
            Title(r.Passed ? "FAHRT ABGESCHLOSSEN" : "NICHT BESTANDEN", g.CurrentMission != null ? g.CurrentMission.Title : "");
            string stars = new string('★', r.Stars) + new string('☆', 3 - r.Stars);
            ui.Label(ui.R(80, 200, 600, 120), stars, 110, UiToolkit.Warn, TextAnchor.MiddleLeft);
            ui.Label(ui.R(700, 215, 700, 100), string.Format("{0:0} Punkte", r.Total), 70, Color.white, TextAnchor.MiddleLeft, true);

            float y = 360;
            Bar(80, y, "Pünktlichkeit", r.Punctuality); Bar(80, y + 70, "Haltegenauigkeit", r.Accuracy);
            Bar(80, y + 140, "Fahrkomfort", r.Comfort); Bar(80, y + 210, "Sicherheit", r.Safety);
            ui.Label(ui.R(80, y + 290, 1300, 40), string.Format("Zwangsbremsungen: {0}   Haltsignal überfahren: {1}   Zu früh abgefahren: {2}   Halte verpasst: {3}",
                r.Interventions, r.SignalViolations, r.EarlyDepartures, r.MissedStops), 26, new Color(1, 1, 1, 0.85f));
            float sy = y + 340;
            foreach (var s in r.Stops)
            {
                string line = s.Missed ? string.Format("{0}: Halt verpasst", s.Station)
                    : string.Format("{0}: {1:+0;-0;±0} s  ·  Halteabweichung {2:+0.0;-0.0;0.0} m", s.Station, s.DelaySec, s.StopErrorM);
                ui.Label(ui.R(80, sy, 1300, 40), line, 28, s.Missed ? UiToolkit.Bad : Color.white);
                sy += 44;
            }
            if (ui.Button(ui.R(1100, 780, 420, 90), "Nochmal", UiToolkit.Accent, 36)) g.RestartCurrent();
            if (ui.Button(ui.R(1100, 890, 420, 90), "Hauptmenü", new Color(0.35f, 0.38f, 0.45f), 36)) g.ToMenu();
        }

        void Bar(float x, float y, string label, double value)
        {
            ui.Label(ui.R(x, y, 420, 50), label, 32, Color.white, TextAnchor.MiddleLeft, true);
            ui.Fill(ui.R(x + 440, y + 10, 700, 34), new Color(1, 1, 1, 0.15f));
            Color c = value >= 80 ? UiToolkit.Good : (value >= 55 ? UiToolkit.Warn : UiToolkit.Bad);
            ui.Fill(ui.R(x + 440, y + 10, 700f * (float)value / 100f, 34), c);
            ui.Label(ui.R(x + 1160, y, 200, 50), string.Format("{0:0}", value), 32, Color.white, TextAnchor.MiddleLeft, true);
        }

        // ================= HUD =================

        void DrawHud(float dt)
        {
            var w = g.World;
            var t = w.Train;
            bool cab = g.IsCab;

            if (cab) DrawRain(w, dt);

            // --- Tacho-Panel (links oben)
            ui.Box(ui.R(20, 20, 330, 190), UiToolkit.Panel, new Color(1, 1, 1, 0.15f));
            double kmh = t.SpeedKmh, permit = Units.MsToKmh(w.Safety.PermittedSpeedMs);
            Color sc = kmh > permit + 1 ? UiToolkit.Bad : Color.white;
            ui.Label(ui.R(30, 22, 220, 100), string.Format("{0:0}", kmh), 96, sc, TextAnchor.MiddleRight, true);
            ui.Label(ui.R(255, 70, 90, 40), "km/h", 28, new Color(1, 1, 1, 0.7f));
            ui.Label(ui.R(30, 120, 320, 36), string.Format("zulässig {0:0} km/h", Mathf.Min((float)permit, 999f)), 28, new Color(1, 1, 1, 0.85f));
            string next = NextRestriction(w);
            ui.Label(ui.R(30, 156, 320, 36), next, 24, new Color(1, 1, 1, 0.7f));

            // --- Uhr / Fahrplan (Mitte oben)
            ui.Box(ui.RC(0, 20, 560, 150), UiToolkit.Panel, new Color(1, 1, 1, 0.15f));
            ui.Label(ui.RC(0, 24, 540, 50), Units.FormatClock(w.Clock.Seconds), 44, Color.white, TextAnchor.MiddleCenter, true);
            ui.Label(ui.RC(0, 76, 540, 40), ServiceLine1(w), 28, Color.white, TextAnchor.MiddleCenter, true);
            ui.Label(ui.RC(0, 114, 540, 50), ServiceLine2(w), 25, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter);

            // --- Kamera/Pause (rechts oben)
            if (ui.Button(ui.RR(20, 20, 200, 70), CamName(g.Cam.Mode), new Color(0.2f, 0.3f, 0.45f, 0.9f), 28)) g.Cam.Cycle();
            if (ui.Button(ui.RR(20, 100, 200, 70), "Pause", new Color(0.3f, 0.3f, 0.35f, 0.9f), 28)) { g.Paused = true; showPauseMenu = true; }

            // --- Weiche
            TrackFeature sw;
            if (w.TryGetNextSwitch(out sw) && sw.Distance < 1800)
            {
                ui.Box(ui.RR(20, 190, 330, 150), UiToolkit.Panel, new Color(1, 1, 1, 0.15f));
                ui.Label(ui.RR(30, 194, 310, 36), string.Format("Weiche in {0:0} m", sw.Distance), 28, Color.white, TextAnchor.MiddleLeft, true);
                bool div = sw.Value > 0.5;
                ui.Label(ui.RR(30, 228, 310, 36), "Stellung: " + (div ? "Abzweig (Gl. 2)" : "Gerade"), 26, div ? UiToolkit.Warn : Color.white);
                string tgt = w.Service != null && w.Service.Current != null ? " (Halt: Gl. " + w.Service.Current.Platform + ")" : "";
                if (ui.Button(ui.RR(30, 268, 310, 62), div ? "Auf Gerade stellen" : "Auf Abzweig stellen", new Color(0.2f, 0.45f, 0.6f), 26)) g.ToggleNextSwitch();
            }

            // --- Meldungen
            DrawAlerts(w);

            // --- Bedienung bei Aussenkamera
            if (!cab) DrawControls(w);
            else if (t.TractionCut && g.Throttle01 > 0.01f)
                ui.Label(ui.RC(0, 905, 1200, 40), t.Doors.IsClosedAndLocked ? "Fahrsperre: Bremse lösen" : "Fahrsperre: Türen schließen", 30, UiToolkit.Warn, TextAnchor.MiddleCenter, true);

            // --- Toasts
            float ty = 830;
            foreach (var to in g.Toasts)
            {
                float a = Mathf.Clamp01((to.Until - Time.unscaledTime) * 2f);
                var c = to.Color; c.a = a;
                ui.Box(ui.RC(0, ty, 1100, 62), new Color(0.04f, 0.05f, 0.07f, 0.85f * a), new Color(c.r, c.g, c.b, 0.9f * a), 3f);
                ui.Label(ui.RC(0, ty, 1080, 62), to.Text, 28, new Color(1, 1, 1, a), TextAnchor.MiddleCenter);
                ty -= 70;
            }

            if (showPauseMenu && g.Paused) DrawPause();
            else if (!g.Paused) showPauseMenu = false;
        }

        void DrawControls(SimulationWorld w)
        {
            float th = g.Throttle01, br = g.Brake01;
            if (ui.VSlider(ui.R(40, 480, 120, 420), ref th, w.Spec.ThrottleNotches, UiToolkit.Good, "FAHREN")) g.Throttle01 = th;
            if (ui.VSlider(ui.RR(40, 480, 120, 420), ref br, 0, UiToolkit.Bad, "BREMSE")) g.Brake01 = br;
            float bx = 220;
            if (ui.Hold(ui.R(bx, 930, 200, 90), "HORN", new Color(0.7f, 0.6f, 0.1f))) g.HudHorn = true; else g.HudHorn = g.Cab != null && g.Cab.HornButton.Held;
            if (ui.Button(ui.R(bx + 220, 930, 220, 90), "Türen auf", new Color(0.2f, 0.55f, 0.3f), 28)) g.OpenDoors();
            if (ui.Button(ui.R(bx + 460, 930, 220, 90), "Türen zu", new Color(0.6f, 0.2f, 0.2f), 28)) g.CloseDoors();
            if (ui.Button(ui.R(bx + 700, 930, 200, 90), "SIFA", new Color(0.35f, 0.35f, 0.4f), 28)) w.Safety.AcknowledgeSifa();
            if (ui.Button(ui.R(bx + 920, 930, 220, 90), "Quittieren", new Color(0.2f, 0.35f, 0.7f), 28)) g.ReleaseEmergency();
        }

        void DrawAlerts(SimulationWorld w)
        {
            var s = w.Safety;
            float y = 190;
            if (s.EmergencyBrakeActive)
            {
                ui.Box(ui.RC(0, y, 900, 80), new Color(0.6f, 0.05f, 0.05f, 0.9f), Color.white, 3f);
                ui.Label(ui.RC(0, y, 880, 80), "ZWANGSBREMSUNG: " + s.Reason + (s.CanRelease ? "  -  Quittieren möglich" : "  -  bis Stillstand warten"), 28, Color.white, TextAnchor.MiddleCenter, true);
                y += 90;
            }
            else if (s.State == SafetyState.Warning)
            {
                bool on = Mathf.Repeat(Time.unscaledTime * 3f, 1f) < 0.6f;
                ui.Box(ui.RC(0, y, 900, 64), on ? new Color(0.8f, 0.45f, 0.05f, 0.92f) : new Color(0.35f, 0.2f, 0.05f, 0.9f), Color.white, 2f);
                ui.Label(ui.RC(0, y, 880, 64), string.Format("ÜBERWACHUNG - zulässig {0:0} km/h, Geschwindigkeit verringern", Units.MsToKmh(s.PermittedSpeedMs)), 28, Color.white, TextAnchor.MiddleCenter, true);
                y += 74;
            }
            if (s.SifaWarning && !s.EmergencyBrakeActive)
            {
                ui.Box(ui.RC(0, y, 900, 64), new Color(0.7f, 0.55f, 0.05f, 0.92f), Color.white, 2f);
                ui.Label(ui.RC(0, y, 880, 64), "SIFA: Taste drücken oder Hebel bewegen!", 30, Color.white, TextAnchor.MiddleCenter, true);
            }
        }

        string NextRestriction(SimulationWorld w)
        {
            foreach (var f in w.Ahead)
            {
                if (f.Kind == FeatureKind.SpeedLimit && f.Distance < 3000)
                    return string.Format("→ {0:0} km/h in {1:0} m", Units.MsToKmh(f.Value), f.Distance);
                if (f.Kind == FeatureKind.Signal && f.Distance < 3000)
                    return string.Format("{0}: {1} in {2:0} m", f.Name, f.Aspect == SignalAspect.Halt ? "HALT" : (f.Aspect == SignalAspect.ProceedSlow ? "Langsamfahrt" : "Fahrt"), f.Distance);
                if (f.Kind == FeatureKind.EndOfTrack && f.Distance < 3000)
                    return string.Format("Gleisende in {0:0} m", f.Distance);
            }
            return "Streckenhöchstgeschwindigkeit";
        }

        string ServiceLine1(SimulationWorld w)
        {
            var svc = w.Service;
            if (svc == null) return string.Format("Freie Fahrt · Neigung {0:+0.0;-0.0;0.0} ‰", w.Follower.GradePermilleForTrain(w.Spec.LengthM));
            var stop = svc.Current;
            if (stop == null) return "Fahrt beendet";
            return svc.Phase == ServicePhase.AtPlatform ? "Halt: " + stop.Station + " (Gl. " + stop.Platform + ")" : "Nächster Halt: " + stop.Station + " (Gl. " + stop.Platform + ")";
        }

        string ServiceLine2(SimulationWorld w)
        {
            var svc = w.Service;
            if (svc == null) return Units.MsToKmh(w.Train.Speed) < 1 ? "Stillstand" : "";
            var stop = svc.Current;
            if (stop == null) return "";
            if (svc.Phase == ServicePhase.AtPlatform)
            {
                if (svc.DepartureReady) return "<color=#66ff88>ABFAHRBEREIT</color> - Türen schließen";
                if (!svc.BoardingComplete) return "Türen öffnen - Fahrgastwechsel";
                return "Abfahrt planmäßig " + Units.FormatClock(stop.DepartureSec);
            }
            double d = w.Follower.DistanceTo(stop.EdgeId, stop.TargetOffset, 30000);
            if (double.IsInfinity(d)) return "<color=#ff7766>Weiche falsch gestellt - Fahrstraße führt nicht zum Halt</color>";
            string delay = "";
            double expected = w.Clock.Seconds - stop.ArrivalSec;
            return string.Format("Soll {0}  ·  noch {1:0.0} km  ·  {2}", Units.FormatClock(stop.ArrivalSec), d / 1000.0, expected > 0 ? "<color=#ff9966>überfällig " + Mathf.RoundToInt((float)expected) + " s</color>" : "in " + Mathf.RoundToInt((float)-expected) + " s") + delay;
        }

        static string CamName(CameraMode m)
        {
            switch (m) { case CameraMode.Cab: return "Cockpit"; case CameraMode.Exterior: return "Außen"; default: return "Strecke"; }
        }

        // ================= Pause =================

        void DrawPause()
        {
            ui.Blocker(new Rect(0, 0, Screen.width, Screen.height));
            ui.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            ui.Label(ui.RC(0, 200, 800, 100), "PAUSE", 80, Color.white, TextAnchor.MiddleCenter, true);
            if (ui.Button(ui.RC(0, 340, 520, 100), "Fortsetzen", UiToolkit.Good, 44)) { g.Paused = false; showPauseMenu = false; }
            if (ui.Button(ui.RC(0, 460, 520, 100), "Neustart", UiToolkit.Accent, 44)) { g.RestartCurrent(); showPauseMenu = false; }
            if (ui.Button(ui.RC(0, 580, 520, 100), "Hauptmenü", new Color(0.5f, 0.25f, 0.25f), 44)) { g.ToMenu(); showPauseMenu = false; }
        }

        // ================= Regentropfen auf der Scheibe =================

        void DrawRain(SimulationWorld w, float dt)
        {
            if (softCircle == null) softCircle = Procedural.SoftCircle(64);
            float rain = (float)w.Weather.Current.Rain;
            if (!g.Paused)
            {
                dropAccum += rain * 22f * dt * (0.4f + (float)w.Train.Speed / 40f);
                while (dropAccum >= 1f)
                {
                    dropAccum -= 1f;
                    drops.Add(new Drop { Pos = new Vector2(Random.value * Screen.width, Random.value * Screen.height * 0.8f + Screen.height * 0.2f), Size = (10f + Random.value * 26f) * ui.S });
                }
            }
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                d.Age += g.Paused ? 0f : dt;
                d.Pos.y -= g.Paused ? 0f : dt * (20f + d.Size) * 0.6f;
                if (d.Age > 2.8f) { drops.RemoveAt(i); continue; }
                drops[i] = d;
                float a = Mathf.Clamp01(1f - d.Age / 2.8f) * 0.45f;
                var old = GUI.color;
                GUI.color = new Color(0.85f, 0.92f, 1f, a);
                GUI.DrawTexture(new Rect(d.Pos.x - d.Size * 0.5f, d.Pos.y - d.Size * 0.5f, d.Size, d.Size * 1.3f), softCircle);
                GUI.color = old;
            }
        }
    }
}
