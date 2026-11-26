# Architektur

## Schichten

```
┌──────────────── Unity-Schicht (Assets/ZugSim/Game) ────────────────┐
│ GameRoot ─ GameUi/UiToolkit ─ InteractionRouter (Touch) ─ CabView   │
│ TrackView · TrainView · EnvironmentView · CameraRig · ProceduralAudio│
└───────────────▲──────────────────────────────┬──────────────────────┘
                │ liest Zustand                 │ schreibt DriverInput
┌───────────────┴──────────────────────────────▼──────────────────────┐
│ SimulationWorld  (Assets/ZugSim/Core, kein UnityEngine)              │
│ TrainPhysics · BrakeSystem · DoorSystem · TrackGraph/TrackFollower   │
│ SafetyMonitor (PZB/Sifa) · ServiceTracker · ScoringSystem · Weather  │
└──────────────────────────────────────────────────────────────────────┘
```

Der **Core** ist deterministisch, rechnet in SI-Einheiten und wird mit `SimulationWorld.Step(dt)` getrieben
(`dt` ≤ 50 ms; die Physik unterteilt intern in 20-ms-Schritte). Er lässt sich ohne Unity testen – genau das tun
die xUnit-Tests (inkl. Autopilot-Durchläufe aller Karrieremissionen).

## Fahrphysik (`TrainPhysics`)

* **Zugkraft:** `F = Fahrstufe · min(F_max, P_max / v)`, begrenzt durch den Kraftschluss `μ · m_Antrieb · g`
  (Haftwert 0,30 trocken, wetterabhängig).
* **Laufwiderstand (Davis):** `R = A + B·v + C·v²`, dazu Steigung `m·g·i/1000` (i in ‰) und Kurvenwiderstand
  nach Röckl (`650/(R−55)` ‰ für R ≥ 300 m).
* **Bremse:** Druckluft-Modell mit Ansprech- und Lösezeit (Zylinderfüllung 0…1), Betriebsbremse (≈ 1,0 m/s²) und
  Schnellbremse (≈ 1,2 m/s²). Bremshaftwert 0,14 trocken; Regen, Laub und Nebel senken ihn und verlängern den Bremsweg.
  Die Hauptluftleitung (5,0 → 3,5 → 0 bar) und der Bremszylinderdruck werden an den Cockpit-Manometern angezeigt.
* **Verriegelungen:** Traktion gesperrt bei angelegter Bremse, offenen Türen oder Zwangsbremsung; Türen öffnen nur im Stand.
* Stillstand: Parkbremse hält den Zug (kein Zurückrollen).

## Gleisnetz und Weichen

`TrackGraph` ist ein gerichteter Graph aus Knoten und Kanten. Eine Kante hat Länge, Neigungsprofil, Kurven,
Geschwindigkeitszonen und Signale. Aus Kurven und Neigungen wird die **3D-Geometrie** (`EdgeGeometry`) abgeleitet –
Physik und Darstellung können deshalb nicht auseinanderlaufen. Weichen sind Knoten mit zwei Ausgängen
(gerade/Abzweig); sie sind verriegelt, wenn der Zug sie in weniger als 120 m anfährt oder befährt.
`TrackFollower` trägt die Zugposition (inkl. Historie für Zugende und Wagen) und liefert die Streckenvorschau
(Signale, Geschwindigkeitswechsel, Weichen, Gleisende).

## Zugsicherung

`SafetyMonitor` überwacht eine **Bremskurve** zu jedem Ziel voraus (Geschwindigkeitsabfall, Halt zeigendes Signal,
Langsamfahrt, Gleisende; Verzögerungsannahme 0,7 m/s², witterungsabhängig reduziert). Überschreitung → Warnung,
bei deutlicher oder anhaltender Überschreitung **Zwangsbremsung** bis Stillstand und Quittieren. Überfahren eines
Haltsignals löst Zwangsbremsung aus und zählt als schwerer Verstoß. Dazu eine **Sifa** (30 s ohne Bedienung → Warnton → Zwangsbremsung).

## Fahrplan und Wertung

`Timetable` / `StopEntry` (Halteposition auf einer Kante, Ankunft/Abfahrt, Mindesthaltezeit) → `ServiceTracker`
(Anfahrt → Halt → Fahrgastwechsel → Abfahrbereit → Abfahrt, verpasste Halte, zu frühe Abfahrt) →
`ScoringSystem` (Punkte je Halt, Komfort aus Ruck und Beschleunigung, Sicherheit). Siehe README für die Gewichte.

## Unity-Schicht

* **Start:** `GameRoot` initialisiert sich per `RuntimeInitializeOnLoadMethod` – keine Szene nötig.
* **Eingabe:** `PointerSource` (Multi-Touch, alt/neu Input System) → `InteractionRouter` verteilt Zeiger an HUD,
  3D-Bedienelemente (`Lever3D`, `PushButton3D` per Raycast) oder die Kamera (Look/Pinch).
* **Darstellung:** `TrackView` baut Gleise/Gelände/Masten/Signale/Bahnsteige als wenige Chunk-Meshes;
  `TrainView` setzt Wagen über Drehgestell-Positionen auf das Gleis; `EnvironmentView` steuert Sonne/Mond, Himmel,
  Nebel, Regen und Blitze aus Uhrzeit und Wetter.
* **UI:** IMGUI-Baukasten (`UiToolkit`), Eingabe ausschließlich über die gemeinsamen Zeiger – dadurch Multi-Touch-fähig
  (z. B. Fahrschalter und Bremse gleichzeitig).
* **Audio:** `ProceduralAudio` synthetisiert Motor, Rollen, Stöße, Bremse, Wind, Regen, Horn, Warntöne in `OnAudioFilterRead`.

## Performance (60 FPS auf Tablets)

* Frame-Limit 60, VSync aus (iOS: Display-Link), `Screen.sleepTimeout = NeverSleep`.
* `PerformanceManager`: drei Qualitätsstufen (Schatten, LOD-Bias, MSAA) und **adaptive Auflösung** über
  `ScalableBufferManager` (senkt bei Frametime > 18,7 ms, erhöht bei Reserve).
* Statische Geometrie in Chunks (≈ 240 m) mit wenigen Materialien; keine per-Frame-Allokationen im Simulationspfad.
* Empfehlungen für Produktions-Assets: URP, Texturen ASTC, Baked Lighting für statische Szenen, LODs, GPU-Instancing für Vegetation.
