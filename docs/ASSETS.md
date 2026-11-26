# Eigene Assets einbinden (Weg zur hochwertigen Grafik)

Das Projekt läuft vollständig mit prozeduralen Platzhaltern, damit es sofort startbar und testbar ist. Für die
finale Grafikqualität werden diese Platzhalter durch eigene Modelle, Texturen und Audio ersetzt. Der Code ist
darauf vorbereitet; die Simulation ändert sich dabei nicht.

## Bereits vorgesehene Austauschpunkte

| Was | Wie |
|---|---|
| **Wagen** | Prefab `Assets/Resources/ZugSim/TrainCar.prefab` (Pivot = Wagenmitte, +Z = Fahrtrichtung, Länge ≈ `Zuglänge / Wagenzahl`) – ersetzt den Platzhalter-Wagenkasten. |
| **Kopfwagen** | Prefab `Assets/Resources/ZugSim/HeadCar.prefab` – wird im Cockpitblick automatisch ausgeblendet. |
| **Render-Pipeline** | URP-Asset zuweisen: `Procedural` erkennt die Pipeline und nutzt URP-Shader. Für Materialien mit PBR-Texturen eigene Materialien verwenden. |
| **Fahrzeugdaten** | `TrainSpec` (Masse, Leistung, Zugkraft, Bremsverzögerung, Länge) – weitere Fahrzeuge als statische Factory-Methoden ergänzen. |
| **Strecken** | `Routes/SampleRoute.cs` zeigt den Aufbau (Kanten, Weichen, Signale, Fahrplan). Weitere Strecken analog anlegen oder aus Daten (JSON) erzeugen. |

## Empfohlene nächste Schritte für Produktionsqualität

1. **Fahrzeug:** Modell mit separaten Meshes für Drehgestelle (für spätere Animation), Innenraum-Cockpit als Prefab
   mit den Skripten `Lever3D`, `PushButton3D`, `Gauge3D`, `Lamp3D` an den passenden Objekten (Collider = Touch-Fläche, ≥ 8 cm Durchmesser).
2. **Strecke:** `TrackView` durch Spline-basierte Gleis-Prefabs (Schotter, Schwellen, Schienen) ersetzen; die
   Positionen liefert unverändert `TrackGraph.PoseAt(edge, offset)`.
3. **Umgebung:** Gelände (Terrain/Meshes), Vegetation mit GPU-Instancing, Bahnhofsgebäude, Oberleitung als Prefabs.
4. **Licht:** Baked Lighting für statische Objekte, eine echte Sonne + Reflection Probes; Wetter-Shader (Nässe) über
   `WeatherState` (Regen, Nebel, Glätte) steuern.
5. **Audio:** Aufnahmen für Fahrmotor (Loops je Fahrstufe), Rollgeräusch, Bremse, Horn, Türen, Ansagen; `ProceduralAudio` als Fallback behalten.
6. **Performance-Budget auf Tablets:** < 1,5 Mio. Dreiecke sichtbar, < 150 Draw Calls, Texturen ASTC 6x6, LOD-Gruppen, Atlas-Materialien.
