using System.Collections.Generic;
using UnityEngine;

namespace ZugSim.Game
{
    /// <summary>
    /// Erzeugt Materialien, Texturen und einfache Meshes zur Laufzeit, damit das Projekt ohne Kunst-Assets lauffaehig ist.
    /// Eigene Modelle koennen spaeter als Prefabs unter Resources/ZugSim/ abgelegt werden (siehe docs/ASSETS.md).
    /// </summary>
    public static class Procedural
    {
        static Shader lit, unlit;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static bool Urp { get { return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null; } }

        static Shader FindFirst(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
            return Shader.Find("Hidden/InternalErrorShader");
        }

        public static Material Lit(Color c, float smoothness = 0.35f, float metallic = 0f, Texture tex = null, float tiling = 1f)
        {
            string key = "L" + c + smoothness + metallic + (tex != null ? tex.GetInstanceID() : 0) + tiling;
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            if (lit == null) lit = Urp ? FindFirst("Universal Render Pipeline/Lit", "Standard") : FindFirst("Standard", "Universal Render Pipeline/Lit");
            m = new Material(lit) { name = "ZugSim_Lit" };
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling)); }
                if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", new Vector2(tiling, tiling)); }
            }
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color c, float intensity = 2f)
        {
            string key = "E" + c + intensity;
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            m = new Material(Lit(c, 0.2f)) { name = "ZugSim_Emissive" };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            cache[key] = m;
            return m;
        }

        public static Material Unlit(Color c)
        {
            string key = "U" + c;
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            if (unlit == null) unlit = Urp ? FindFirst("Universal Render Pipeline/Unlit", "Unlit/Color") : FindFirst("Unlit/Color", "Sprites/Default");
            m = new Material(unlit) { name = "ZugSim_Unlit" };
            SetColor(m, c);
            cache[key] = m;
            return m;
        }

        public static Material Particle(Color c)
        {
            var sh = Urp ? FindFirst("Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Sprites/Default") : FindFirst("Particles/Standard Unlit", "Sprites/Default");
            var m = new Material(sh) { name = "ZugSim_Particle" };
            SetColor(m, c);
            if (m.HasProperty("_Mode")) { m.SetFloat("_Mode", 2); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000; }
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0); m.renderQueue = 3000; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // ---------- Texturen ----------

        public static Texture2D Make(int w, int h, System.Func<int, int, Color> fn, bool mip = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mip) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "ZugSim_Tex" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = fn(x, y);
            t.SetPixels(px);
            t.Apply(mip, false);
            return t;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Schotterbett mit Schwellen (Laengsrichtung = V).</summary>
        static Texture2D ballastTex, grassTex;
        static readonly Dictionary<string, Texture2D> gaugeCache = new Dictionary<string, Texture2D>();

        public static Texture2D BallastTexture()
        {
            if (ballastTex != null) return ballastTex;
            return ballastTex = Make(128, 128, (x, y) =>
            {
                float n = Hash(x / 2, y / 2) * 0.25f;
                Color ballast = new Color(0.36f + n, 0.35f + n, 0.33f + n);
                float u = x / 128f, v = y / 128f;
                bool sleeperRow = (v * 4f) % 1f < 0.28f;
                bool inSleeper = u > 0.16f && u < 0.84f;
                if (sleeperRow && inSleeper) { float s = 0.38f + Hash(x, y) * 0.06f; return new Color(s, s * 0.95f, s * 0.9f); }
                return ballast;
            });
        }

        public static Texture2D GrassTexture()
        {
            if (grassTex != null) return grassTex;
            return grassTex = Make(128, 128, (x, y) =>
            {
                float n = Hash(x, y) * 0.12f + Hash(x / 4, y / 4) * 0.12f;
                return new Color(0.20f + n * 0.5f, 0.38f + n, 0.15f + n * 0.4f);
            });
        }

        public static Texture2D Noise(Color a, Color b)
        {
            return Make(64, 64, (x, y) => Color.Lerp(a, b, Hash(x, y)));
        }

        /// <summary>Zifferblatt fuer Rundinstrumente (Skala, Teilstriche) mit runtergeregelter Kreismaske.</summary>
        public static Texture2D GaugeFace(float startDeg, float endDeg, int majorTicks, int minorPerMajor, Color arcColor, float redFrom01 = 2f)
        {
            string key = startDeg + "|" + endDeg + "|" + majorTicks + "|" + minorPerMajor + "|" + arcColor + "|" + redFrom01;
            Texture2D cached;
            if (gaugeCache.TryGetValue(key, out cached) && cached != null) return cached;
            const int S = 256;
            return gaugeCache[key] = Make(S, S, (x, y) =>
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) return new Color(0, 0, 0, 0);
                if (r > 0.94f) return new Color(0.55f, 0.55f, 0.58f, 1);
                Color bg = new Color(0.04f, 0.045f, 0.05f, 1);
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;       // 0 = oben, + = rechts
                if (ang < 0) ang += 360f;
                float span = endDeg - startDeg; if (span <= 0) span += 360f;
                float a = ang - startDeg; if (a < 0) a += 360f;
                if (a <= span + 0.5f)
                {
                    float t = a / span;
                    if (r > 0.80f && r < 0.84f) return t > redFrom01 ? new Color(0.9f, 0.15f, 0.1f, 1) : arcColor * 0.8f;
                    float arcLen = r * span * Mathf.Deg2Rad;
                    float majorU = t * majorTicks;
                    float dMajor = Mathf.Abs(majorU - Mathf.Round(majorU)) / majorTicks * arcLen * S * 0.5f;
                    if (r > 0.66f && r < 0.80f && dMajor < 1.6f) return Color.white;
                    float minorU = t * majorTicks * minorPerMajor;
                    float dMinor = Mathf.Abs(minorU - Mathf.Round(minorU)) / (majorTicks * minorPerMajor) * arcLen * S * 0.5f;
                    if (r > 0.72f && r < 0.80f && dMinor < 1.0f) return new Color(0.8f, 0.8f, 0.8f, 1);
                }
                return bg;
            }, false);
        }

        public static Texture2D SoftCircle(int size = 64)
        {
            return Make(size, size, (x, y) =>
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                return new Color(1, 1, 1, a * a);
            }, false);
        }

        public static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }

        // ---------- Objekte ----------

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 localPos, float radius, float height, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 localPos, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        static Font font;
        public static Font DefaultFont
        {
            get
            {
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Roboto", "Noto Sans" }, 48);
                return font;
            }
        }

        public static TextMesh Text(string name, Transform parent, Vector3 localPos, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = DefaultFont;
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 48;
            tm.color = color;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sharedMaterial = DefaultFont.material;
            return tm;
        }
    }
}
