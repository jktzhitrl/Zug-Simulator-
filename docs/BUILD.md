# Installations- und Build-Anleitung

## Voraussetzungen

| | Android | iPadOS |
|---|---|---|
| Unity | 6000.0 LTS mit *Android Build Support* (+ OpenJDK, SDK & NDK) | 6000.0 LTS mit *iOS Build Support* |
| Rechner | Windows / macOS / Linux | **macOS** mit aktuellem **Xcode** |
| Konto | – (Play Console nur für Store-Release) | Apple-Developer-Konto (für Gerätetest reicht ein kostenloses Konto) |
| Zielgeräte | Android 8.0+ (API 26), ARM64, Vulkan oder GLES3; empfohlen Snapdragon 8 Gen 1 / Tensor G2 oder neuer | iPadOS 15+, empfohlen A14 / M1 oder neuer |

## Projekt einrichten

1. Repository klonen und in Unity Hub **Add project from disk** wählen.
2. Beim ersten Öffnen werden die Pakete aus `Packages/manifest.json` geladen (Input System). Die Meldung zum
   Aktivieren des neuen Input Systems ggf. bestätigen (Editor-Neustart). Beide Eingabe-Backends werden unterstützt.
3. **ZugSim → Projekt für Tablets einrichten** ausführen:
   * trägt die zur Laufzeit benötigten Shader unter *Always Included Shaders* ein (sonst fehlen sie im Build),
   * setzt Querformat, IL2CPP, ARM64, Vulkan/GLES3 bzw. Metal, Bundle-ID `com.zugsim.trainsimulator`,
   * legt `Assets/Scenes/Main.unity` an und trägt sie in die Build Settings ein.
4. **Play** im Editor: Maus ersetzt Touch, Tastatursteuerung siehe README.

## Android

* Menü **ZugSim → Build → Android (APK)** → `Builds/ZugSim.apk`; per `adb install -r Builds/ZugSim.apk` aufs Gerät
  (USB-Debugging aktivieren).
* Play Store: **Android (AAB)**. Vor dem Release einen eigenen Keystore in *Project Settings → Player → Publishing Settings* hinterlegen.
* Kommandozeile / CI:

```bash
Unity -batchmode -nographics -quit -projectPath . \
  -executeMethod ZugSim.EditorTools.BuildScript.BuildAndroid -buildOutput Builds/ZugSim.apk
```

## iPadOS

1. **ZugSim → Build → iPadOS (Xcode-Projekt)** → `Builds/iOS/`.
2. `Builds/iOS/Unity-iPhone.xcodeproj` in Xcode öffnen, unter *Signing & Capabilities* das eigene Team wählen und
   die Bundle-ID bei Bedarf anpassen.
3. iPad per Kabel verbinden, als Ziel wählen, **Run** (Release-Konfiguration für Performance-Tests verwenden).
4. Für TestFlight/App Store: *Product → Archive*.

```bash
Unity -batchmode -nographics -quit -projectPath . \
  -executeMethod ZugSim.EditorTools.BuildScript.BuildIos -buildOutput Builds/iOS
```

## Performance prüfen (60 FPS)

* In **Einstellungen → FPS-Anzeige** aktivieren: zeigt FPS, Frametime und die aktuelle Render-Auflösung.
* Qualitätsstufe passend wählen (Niedrig für ältere Geräte, Hoch für iPad Pro/Flaggschiff-Android). Die adaptive
  Auflösung hält die Frametime automatisch unter ~16,7 ms.
* Profiling: *Window → Analysis → Profiler* mit *Autoconnect Profiler* im Development-Build; Xcode *Instruments* (Metal System Trace) auf dem iPad.
* Empfohlen für Produktionsgrafik: Universal Render Pipeline (Paket installieren, URP-Asset anlegen und in
  *Project Settings → Graphics* zuweisen – die Materialerzeugung erkennt URP automatisch).

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| Rosa Materialien im Build | Shader fehlen: *ZugSim → Projekt für Tablets einrichten* erneut ausführen (Always Included Shaders). |
| `Unity.InputSystem` nicht gefunden | Package Manager → *Input System* installieren (steht in `Packages/manifest.json`). |
| Kein Ton / Ton knackt | Audio-Puffer in *Project Settings → Audio* auf *Best Latency* bzw. *Good Latency* stellen. |
| Touch reagiert nicht | *Project Settings → Player → Active Input Handling* auf *Both* oder *Input System Package* stellen. |
