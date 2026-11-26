# Zug-Simulator

Realistischer 3D-Zugsimulator für **Tablets (iPadOS / Android)** auf Basis von **Unity 6 (LTS)**.
Fahrphysik mit Zugkraftkennlinie und Bremswegen, Cockpit mit Touch-Hebeln, dynamisches Wetter, Tag/Nacht,
Karrieremodus mit Fahrplänen und Pünktlichkeitswertung sowie freie Fahrt.

> **Status (ehrlich):** Der Simulationskern ist vollständig getestet (36 automatische Tests, ein Autopilot
> fährt alle Karrieremissionen fehlerfrei). Die Unity-Schicht (Darstellung, Cockpit, UI, Audio) wurde
> gegen die echten UnityEngine-Assemblies **kompiliert**, aber noch **nicht im Unity-Editor oder auf einem
> Tablet ausgeführt** (kein Unity in der Entwicklungsumgebung). Grafik und Ton sind **prozedurale Platzhalter**;
> für die „hochwertige, moderne 3D-Grafik“ werden eigene Assets eingebunden (siehe [docs/ASSETS.md](docs/ASSETS.md)).
> Die 60-FPS-Mechanismen (Frame-Limit, Qualitätsstufen, adaptive Auflösung) sind eingebaut, aber nicht auf Geräten vermessen.

## Schnellstart

1. **Unity Hub** → Unity **6000.0 LTS** installieren (mit *Android Build Support* und/oder *iOS Build Support*).
2. Dieses Repository als Projekt öffnen (Ordner mit `Assets/` und `Packages/`). Beim ersten Öffnen werden das
   Input-System-Paket aufgelöst und Skripte kompiliert.
3. Menü **ZugSim → Projekt für Tablets einrichten** (Shader, Player-Einstellungen, Startszene `Assets/Scenes/Main.unity`).
4. **Play** drücken – das Spiel baut sich vollständig zur Laufzeit auf (jede Szene reicht, auch eine leere).
5. Build: **ZugSim → Build → Android (APK)** bzw. **iPadOS (Xcode-Projekt)** – Details in [docs/BUILD.md](docs/BUILD.md).

## Bedienung

| Aktion | Touch | Tastatur (Editor / BT-Tastatur) |
|---|---|---|
| Fahrschalter (8 Stufen) | Hebel links im Cockpit nach oben/unten ziehen | `W` / `S` |
| Bremse (Betriebsbremse → Schnellbremse am Anschlag) | Hebel daneben ziehen | `D` / `A`, `Leertaste` = Schnellbremse |
| Horn (Zweiklang) | Taster „HORN“ halten | `H` |
| Türen | Taster „TÜR AUF/ZU“ | `T` |
| Sifa quittieren | Taster „SIFA“ (oder Hebel bewegen) | `Strg` |
| Zwangsbremsung quittieren | Taster „FREI“ (im Stillstand) | – |
| Umschauen / Orbit | Finger auf freier Fläche ziehen | Maus ziehen |
| Zoom | Zwei-Finger-Pinch | Mausrad |
| Kamera wechseln | Button oben rechts (Cockpit → Außen → Strecke) | – |
| Weiche stellen | Button „Weiche in … m“ (rechts im HUD) | – |

Tipps: Traktion ist gesperrt, solange die Bremse angelegt oder die Türen offen sind. Bei Regen und Laub verlängern
sich die Bremswege deutlich. Im Karrieremodus stellt der Fahrdienstleiter die Weichen automatisch (abschaltbar in den Einstellungen).

## Spielmodi

* **Karriere** – vier Missionen (Sonne, Regen, Nebel mit Haltsignal, Nachtfahrt im Sturm). Bewertet werden
  **Pünktlichkeit** (Ankunft/Abfahrt, 40 %), **Haltegenauigkeit** am Bahnsteig (25 %), **Fahrkomfort** (Ruck/Beschleunigung, 20 %)
  und **Sicherheit** (Zwangsbremsungen, überfahrene Haltsignale, 15 %). 3 Sterne ab 90 Punkten; Sterne schalten weitere Missionen frei.
* **Freie Fahrt** – Wetter, Tageszeit, Zeitraffer des Tag/Nacht-Zyklus und Zugtyp frei wählbar; Weichen manuell.

## Projektstruktur

```
Assets/ZugSim/
  Core/      Reiner C#-Simulationskern ohne UnityEngine (physik, Gleisnetz, Sicherheit, Fahrplan, Wertung)
  Game/      Unity-Schicht: Darstellung, Cockpit, Kamera, Umwelt, Audio, UI, Spielablauf
  Editor/    Projekt-Setup und Build-Skripte (Menü + Kommandozeile/CI)
tests/ZugSim.Core.Tests/       xUnit-Tests (kompiliert den Core direkt aus Assets/)
tools/UnityCompileCheck/       Compile-Check der Unity-Schicht ohne Unity-Editor
docs/                          Architektur, Build-Anleitung, Asset-Integration
```

Tests ausführen (.NET 8 SDK genügt, kein Unity nötig):

```bash
dotnet test tests/ZugSim.Core.Tests
dotnet build tools/UnityCompileCheck   # prüft die Unity-Schicht gegen UnityEngine 2021.3 (Modul-Pakete von NuGet)
```

Weiterführend: [Architektur & Physik](docs/ARCHITECTURE.md) · [Build-Anleitung](docs/BUILD.md) · [Eigene Assets einbinden](docs/ASSETS.md)
