using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZugSim.Game
{
    /// <summary>
    /// Minimaler Touch-UI-Baukasten auf IMGUI-Basis. Gezeichnet wird in OnGUI (Repaint); Eingaben kommen
    /// ueber die einheitlichen Zeiger (PointerSource), damit Multi-Touch auf Tablets zuverlaessig funktioniert.
    /// </summary>
    public sealed class UiToolkit
    {
        struct Hit { public int Id; public Rect Rect; public bool Slider; }

        public float S = 1f;          // Skalierung relativ zu 1080 px Bildschirmhoehe
        public float VW = 1920f, VH = 1080f;

        List<Hit> hits = new List<Hit>();
        List<Hit> building = new List<Hit>();
        readonly Dictionary<int, int> down = new Dictionary<int, int>();   // pointerId -> widgetId
        readonly HashSet<int> taps = new HashSet<int>();
        readonly Dictionary<int, float> sliderValues = new Dictionary<int, float>();
        readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        Texture2D white;

        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.1f, 0.78f);
        public static readonly Color Accent = new Color(0.15f, 0.55f, 0.95f, 1f);
        public static readonly Color Good = new Color(0.2f, 0.75f, 0.35f, 1f);
        public static readonly Color Bad = new Color(0.9f, 0.25f, 0.2f, 1f);
        public static readonly Color Warn = new Color(1f, 0.7f, 0.1f, 1f);

        public void BeginFrame()
        {
            S = Mathf.Max(Screen.height, 1) / 1080f;
            VW = Screen.width / S; VH = 1080f;
            if (white == null) white = Texture2D.whiteTexture;
            var t = hits; hits = building; building = t; building.Clear();
        }

        public Rect R(float x, float y, float w, float h) { return new Rect(x * S, y * S, w * S, h * S); }
        /// <summary>Rechtsbuendig (x ab rechtem Rand).</summary>
        public Rect RR(float xFromRight, float y, float w, float h) { return new Rect((VW - xFromRight - w) * S, y * S, w * S, h * S); }
        public Rect RC(float cx, float y, float w, float h) { return new Rect((VW * 0.5f + cx - w * 0.5f) * S, y * S, w * S, h * S); }

        // ---------- Zeichnen ----------

        public void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

        public void Box(Rect r, Color c, Color border, float bw = 2f)
        {
            Fill(r, border);
            float b = bw * S;
            Fill(new Rect(r.x + b, r.y + b, r.width - 2 * b, r.height - 2 * b), c);
        }

        GUIStyle Style(int size, TextAnchor anchor, bool bold)
        {
            int key = size * 100 + (int)anchor * 2 + (bold ? 1 : 0);
            GUIStyle st;
            if (!styles.TryGetValue(key, out st))
            {
                st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, richText = true, wordWrap = true, clipping = TextClipping.Overflow };
                st.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                styles[key] = st;
            }
            return st;
        }

        public void Label(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false)
        {
            var st = Style(Mathf.RoundToInt(size * S), anchor, bold);
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f * color.a);
            GUI.Label(new Rect(r.x + S, r.y + S, r.width, r.height), text.IndexOf("<color", StringComparison.Ordinal) >= 0 ? System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "") : text, st);
            GUI.color = color;
            GUI.Label(r, text, st);
            GUI.color = old;
        }

        // ---------- Widgets ----------

        int IdFor(string key, Rect r) { return key.GetHashCode() * 31 + Mathf.RoundToInt(r.x) * 7 + Mathf.RoundToInt(r.y) * 13 + 1; }

        public void Blocker(Rect r) { building.Add(new Hit { Id = 0, Rect = r }); }

        public bool Button(Rect r, string label, Color bg, float size = 34f, bool enabled = true)
        {
            int id = IdFor(label, r);
            if (enabled) building.Add(new Hit { Id = id, Rect = r }); else building.Add(new Hit { Id = 0, Rect = r });
            bool pressed = IsDown(id);
            Color c = enabled ? (pressed ? Color.Lerp(bg, Color.white, 0.35f) : bg) : new Color(0.25f, 0.25f, 0.28f, 0.8f);
            Box(r, c, new Color(1, 1, 1, 0.25f), 2f);
            Label(r, label, size, enabled ? Color.white : new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter, true);
            return enabled && taps.Remove(id);
        }

        /// <summary>Taste, die waehrend des Haltens true liefert (z. B. Horn).</summary>
        public bool Hold(Rect r, string label, Color bg, float size = 30f)
        {
            int id = IdFor(label, r);
            building.Add(new Hit { Id = id, Rect = r });
            bool held = IsDown(id);
            Box(r, held ? Color.Lerp(bg, Color.white, 0.4f) : bg, new Color(1, 1, 1, 0.25f), 2f);
            Label(r, label, size, Color.white, TextAnchor.MiddleCenter, true);
            return held;
        }

        /// <summary>Senkrechter Schieber; Fingerposition bestimmt den Wert direkt. Rasterung ueber <paramref name="notches"/>.</summary>
        public bool VSlider(Rect r, ref float value, int notches, Color fill, string caption)
        {
            int id = IdFor("slider" + caption, r);
            building.Add(new Hit { Id = id, Rect = r, Slider = true });
            bool changed = false;
            float v;
            if (IsDown(id) && sliderValues.TryGetValue(id, out v))
            {
                if (notches > 0) v = Mathf.Round(v * notches) / notches;
                if (Mathf.Abs(v - value) > 1e-4f) { value = v; changed = true; }
            }
            Box(r, new Color(0.04f, 0.05f, 0.07f, 0.85f), new Color(1, 1, 1, 0.3f), 2f);
            float pad = 6f * S;
            float h = (r.height - 2 * pad) * value;
            Fill(new Rect(r.x + pad, r.yMax - pad - h, r.width - 2 * pad, h), new Color(fill.r, fill.g, fill.b, 0.75f));
            if (notches > 0)
                for (int i = 0; i <= notches; i++)
                {
                    float y = r.yMax - pad - (r.height - 2 * pad) * i / notches;
                    Fill(new Rect(r.x + pad, y - S, r.width - 2 * pad, 2f * S), new Color(1, 1, 1, 0.35f));
                }
            float ty = r.yMax - pad - (r.height - 2 * pad) * value;
            Fill(new Rect(r.x, ty - 14f * S, r.width, 28f * S), Color.Lerp(fill, Color.white, 0.5f));
            Label(new Rect(r.x - 30 * S, r.yMax + 2 * S, r.width + 60 * S, 34 * S), caption, 24f, Color.white, TextAnchor.UpperCenter, true);
            return changed;
        }

        bool IsDown(int id) { foreach (var kv in down) if (kv.Value == id) return true; return false; }

        // ---------- Eingabe (vom InteractionRouter) ----------

        Vector2 ToGui(Vector2 screenPos) { return new Vector2(screenPos.x, Screen.height - screenPos.y); }

        public bool TryCapture(PointerInfo p)
        {
            Vector2 g = ToGui(p.Position);
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                if (!hits[i].Rect.Contains(g)) continue;
                if (hits[i].Id != 0) { down[p.Id] = hits[i].Id; if (hits[i].Slider) UpdateSlider(hits[i], g); }
                return true;
            }
            return false;
        }

        public void OnPointer(PointerInfo p)
        {
            int id;
            if (!down.TryGetValue(p.Id, out id)) return;
            Vector2 g = ToGui(p.Position);
            foreach (var h in hits)
            {
                if (h.Id != id) continue;
                if (h.Slider) { UpdateSlider(h, g); break; }
                if (p.Phase == PointerPhase.Ended && h.Rect.Contains(g)) taps.Add(id);
                break;
            }
            if (p.Phase == PointerPhase.Ended) down.Remove(p.Id);
        }

        void UpdateSlider(Hit h, Vector2 g)
        {
            float pad = 6f * S;
            sliderValues[h.Id] = Mathf.Clamp01((h.Rect.yMax - pad - g.y) / (h.Rect.height - 2 * pad));
        }

        public void ClearTransient() { taps.Clear(); down.Clear(); }
    }
}
