using System.Collections.Generic;
using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Assist;
using ZugSim.Core.Career;
using ZugSim.Core.Service;
using ZugSim.Core.Track;

namespace ZugSim.Game
{
    public enum AppScreen { MainMenu, Career, FreeRide, Settings, Playing, Result }

    /// <summary>
    /// Einstiegspunkt und Spielablauf. Startet automatisch mit jeder Szene (RuntimeInitializeOnLoadMethod),
    /// baut Strecke, Zug, Cockpit, Umwelt, Audio und UI zur Laufzeit auf.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || FindFirstObjectByType<GameRoot>() != null) return;
            new GameObject("ZugSim").AddComponent<GameRoot>();
        }

        // ---- Zustand fuer UI ----
        public AppScreen State = AppScreen.MainMenu;
        public bool Paused;
        public SimulationWorld World;
        public CareerProgress Career;
        public MissionDefinition CurrentMission;
        public ScoreCard LastResult;
        public CameraRig Cam;
        public PerformanceManager Perf;
        public UiToolkit Ui = new UiToolkit();
        public float Throttle01, Brake01;
        public bool HudHorn;
        public readonly List<Toast> Toasts = new List<Toast>();
        public WeatherPreset FreeWeather = WeatherPreset.Clear;
        public float FreeHour = 10f;
        public int FreeTrain;
        public float FreeTimeScale = 30f;

        public struct Toast { public string Text; public float Until; public Color Color; }

        TrainView trainView;
        TrackView trackView;
        EnvironmentView env;
        ProceduralAudio audioSynth;
        InteractionRouter router;
        Transform worldRoot;
        AutoDriver attract = new AutoDriver();
        GameUi gameUi;
        float attractRestart;
        bool endToastShown;
        float lastHiss, prevFill;

        public CabView Cab { get { return trainView != null ? trainView.Cab : null; } }
        public bool IsCab { get { return Cam != null && Cam.Mode == CameraMode.Cab; } }
        public bool InMenu { get { return State != AppScreen.Playing && State != AppScreen.Result; } }

        // ================= Lebenszyklus =================

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.gameObject != gameObject) c.enabled = false;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) l.enabled = false;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) l.enabled = false;

            Career = SaveSystem.Load();
            Cam = CameraRig.Build(transform);
            env = EnvironmentView.Build(transform, Cam.Cam);
            Perf = PerformanceManager.Create(transform, Cam.Cam);
            Perf.ApplyLevel(Career.QualityLevel);
            Perf.Adaptive = Career.AdaptiveResolution;

            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(transform, false);
            audioGo.AddComponent<AudioSource>();
            audioSynth = audioGo.AddComponent<ProceduralAudio>();
            audioSynth.Master = Career.Volume;

            router = new InteractionRouter
            {
                Cam = Cam.Cam,
                HudCapture = Ui.TryCapture,
                HudEvent = Ui.OnPointer,
                OnLook = d => { if (State == AppScreen.Playing && !Paused) Cam.Look(d); },
                OnPinch = a => { if (State == AppScreen.Playing && !Paused) Cam.Pinch(a); },
            };
            gameUi = new GameUi(this);
            StartAttract();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && State == AppScreen.Playing) Paused = true;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            router.Update();
            if (World == null) return;

            if (State == AppScreen.Playing)
            {
                if (!Paused)
                {
                    HandleControls();
                    World.Step(dt);
                    PollEvents();
                    if (World.IsFinished) Finish();
                    else if (World.IsAtTerminus && World.Service == null && !endToastShown)
                    {
                        endToastShown = true;
                        ShowToast("Gleisende erreicht - Neustart im Pause-Menue", UiToolkit.Warn);
                    }
                }
            }
            else if (State != AppScreen.Result)
            {
                attract.Update(World, dt);
                World.Step(dt);
                attractRestart = World.IsAtTerminus ? attractRestart + dt : 0f;
                if (attractRestart > 6f) StartAttract();
            }
            Toasts.RemoveAll(t => Time.unscaledTime > t.Until);
        }

        void LateUpdate()
        {
            if (World == null || trainView == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool cab = State == AppScreen.Playing && Cam.Mode == CameraMode.Cab;
            Cam.AutoOrbit = InMenu;
            if (InMenu) Cam.Mode = CameraMode.Exterior;
            trainView.UpdateFromWorld(World, cab, dt);
            trackView.UpdateSignals();
            env.UpdateFrom(World, dt);
            trainView.SetLights(env.NeedsHeadlights, 1f);
            Cam.UpdateRig(trainView, World, dt);
            if (cab) trainView.Cab.UpdateInstruments(World, Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.5f ? 1f : 0f);

            // Ton
            float fill = (float)World.Train.Brakes.Fill;
            float released = Mathf.Max(0f, prevFill - fill) * 30f;
            prevFill = fill;
            lastHiss = Mathf.Lerp(lastHiss, Mathf.Clamp01(released), 0.2f);
            audioSynth.SetHiss(lastHiss);
            bool horn = World.HornActive && State == AppScreen.Playing && !Paused;
            audioSynth.Configure(World, cab, horn);
            audioSynth.Master = Paused ? 0f : Career.Volume * (InMenu ? 0.35f : 1f);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            Ui.BeginFrame();
            if (World != null) gameUi.Draw();
        }

        // ================= Spielstart =================

        void ClearWorld()
        {
            if (worldRoot != null) Destroy(worldRoot.gameObject);
            trainView = null; trackView = null;
            Ui.ClearTransient();
            router.ReleaseAll();
        }

        void BuildWorld(SimulationWorld w)
        {
            ClearWorld();
            World = w;
            worldRoot = new GameObject("World").transform;
            worldRoot.SetParent(transform, false);
            trackView = TrackView.Build(worldRoot, w.Route, w.Timetable);
            trainView = TrainView.Build(worldRoot, w.Spec, w.Graph);
            var cab = trainView.Cab;
            cab.HornButton.Pressed += () => HudHorn = true;
            cab.HornButton.Released += () => HudHorn = false;
            cab.DoorOpenButton.Pressed += OpenDoors;
            cab.DoorCloseButton.Pressed += CloseDoors;
            cab.SifaButton.Pressed += () => World.Safety.AcknowledgeSifa();
            cab.ReleaseButton.Pressed += ReleaseEmergency;
            endToastShown = false;
            attractRestart = 0f;
            Toasts.Clear();
        }

        public void StartAttract()
        {
            var w = SimulationWorld.CreateFreeRide(TrainSpec.RegionalEmu(), 9.0, WeatherPreset.Overcast, 1.0);
            w.AutoRoute = false;
            BuildWorld(w);
            Cam.SetMode(CameraMode.Exterior);
            State = AppScreen.MainMenu;
            Paused = false;
        }

        public void StartMission(MissionDefinition m)
        {
            CurrentMission = m;
            var w = SimulationWorld.CreateMission(m, TrainSpec.RegionalEmu());
            w.AutoRoute = Career.AutoRoute;
            w.Safety.SifaEnabled = Career.SifaEnabled;
            BeginPlaying(w);
        }

        public void StartFreeRide()
        {
            CurrentMission = null;
            var spec = FreeTrain == 1 ? TrainSpec.HighSpeedEmu() : TrainSpec.RegionalEmu();
            var w = SimulationWorld.CreateFreeRide(spec, FreeHour, FreeWeather, FreeTimeScale);
            BeginPlaying(w);
        }

        void BeginPlaying(SimulationWorld w)
        {
            BuildWorld(w);
            Throttle01 = 0f; Brake01 = 0.75f;
            Cam.SetMode(CameraMode.Cab);
            State = AppScreen.Playing;
            Paused = false;
            if (CurrentMission != null) ShowToast(CurrentMission.Title + ": " + CurrentMission.Description, UiToolkit.Accent, 9f);
        }

        public void RestartCurrent()
        {
            if (CurrentMission != null) StartMission(CurrentMission); else StartFreeRide();
        }

        public void ToMenu()
        {
            State = AppScreen.MainMenu;
            StartAttract();
        }

        void Finish()
        {
            LastResult = World.FinalScore;
            if (CurrentMission != null)
            {
                Career.Submit(CurrentMission.Id, LastResult);
                SaveSystem.Save(Career);
            }
            State = AppScreen.Result;
        }

        public void SaveSettings()
        {
            Career.Volume = Mathf.Clamp01(Career.Volume);
            Perf.ApplyLevel(Career.QualityLevel);
            Perf.Adaptive = Career.AdaptiveResolution;
            if (!Career.AdaptiveResolution) ScalableBufferManager.ResizeBuffers(1f, 1f);
            SaveSystem.Save(Career);
        }

        // ================= Steuerung =================

        void HandleControls()
        {
            var cab = trainView.Cab;
            bool cabView = IsCab;
            int notches = World.Spec.ThrottleNotches;

            var k = KeyboardControls.Poll();
            if (k.ThrottleUp) Throttle01 = Mathf.Min(1f, Throttle01 + 1f / notches);
            if (k.ThrottleDown) Throttle01 = Mathf.Max(0f, Throttle01 - 1f / notches);
            if (k.BrakeUp) Brake01 = Mathf.Min(1f, Brake01 + 0.1f);
            if (k.BrakeDown) Brake01 = Mathf.Max(0f, Brake01 - 0.1f);
            if (k.Emergency) Brake01 = 1f;
            if (k.Sifa) World.Safety.AcknowledgeSifa();
            if (k.Doors) { if (World.Train.Doors.State == DoorState.Closed || World.Train.Doors.State == DoorState.Closing) OpenDoors(); else CloseDoors(); }

            if (cabView)
            {
                if (cab.ThrottleLever.Dragging) Throttle01 = cab.ThrottleLever.Value;
                if (cab.BrakeLever.Dragging) Brake01 = cab.BrakeLever.Value;
            }
            cab.SetLevers(Throttle01, Brake01);

            var input = new DriverInput { Throttle = Throttle01 };
            input.EmergencyBrake = Brake01 >= 0.97f;
            input.Brake = Mathf.Clamp01(Brake01 / 0.85f);
            World.Input = input;
            World.HornActive = HudHorn || k.Horn || (cab.HornButton != null && cab.HornButton.Held);
        }

        public void OpenDoors()
        {
            if (World.RequestDoors(true)) audioSynth.TriggerChime();
            else ShowToast(World.Service != null ? "Türen nur im Stand am Bahnsteig öffnen" : "Türen nur im Stand öffnen", UiToolkit.Warn);
        }

        public void CloseDoors() { World.RequestDoors(false); }

        public void ReleaseEmergency()
        {
            if (World.TryReleaseEmergency()) { Brake01 = 0.75f; Throttle01 = 0f; ShowToast("Zwangsbremsung quittiert", UiToolkit.Good); }
            else ShowToast("Quittieren erst im Stillstand möglich", UiToolkit.Warn);
        }

        public void ToggleNextSwitch()
        {
            TrackFeature f;
            if (!World.TryGetNextSwitch(out f)) return;
            bool wantDiverging = f.Value < 0.5;
            if (!World.TrySetSwitch(f.Id, wantDiverging)) ShowToast("Weiche verriegelt - Zug zu nah", UiToolkit.Warn);
        }

        void PollEvents()
        {
            if (World.Service == null) return;
            ServiceEvent e;
            while (World.Service.DrainEvent(out e))
            {
                Color c = e.Kind == ServiceEventKind.Missed || e.Kind == ServiceEventKind.EarlyDeparture ? UiToolkit.Bad
                        : (e.Kind == ServiceEventKind.DepartureReady || e.Kind == ServiceEventKind.Arrived ? UiToolkit.Good : UiToolkit.Accent);
                ShowToast(e.Text, c);
                if (e.Kind == ServiceEventKind.Arrived || e.Kind == ServiceEventKind.DepartureReady) audioSynth.TriggerChime();
            }
        }

        public void ShowToast(string text, Color color, float seconds = 4.5f)
        {
            Toasts.Add(new Toast { Text = text, Color = color, Until = Time.unscaledTime + seconds });
            if (Toasts.Count > 4) Toasts.RemoveAt(0);
        }
    }
}
