using UnityEngine;
using UnityEngine.Rendering;
using ZugSim.Core;

namespace ZugSim.Game
{
    /// <summary>Sonne/Mond, prozeduraler Himmel, Nebel, Regen, Gewitterblitze - gesteuert von Uhrzeit und Wetter der Simulation.</summary>
    public sealed class EnvironmentView : MonoBehaviour
    {
        Light sun, moon;
        Material skyMat;
        ParticleSystem rain;
        Camera cam;
        float lightning;
        float lightningTimer = 6f;
        float ambientTimer;
        System.Random rng = new System.Random(3);

        public bool NeedsHeadlights { get; private set; }
        public float Daylight { get; private set; }

        public static EnvironmentView Build(Transform parent, Camera cam)
        {
            var go = new GameObject("Environment");
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<EnvironmentView>();
            e.cam = cam;

            e.sun = NewLight("Sun", go.transform, LightShadows.Soft);
            e.moon = NewLight("Moon", go.transform, LightShadows.None);
            e.moon.color = new Color(0.55f, 0.65f, 1f);

            var sky = Shader.Find("Skybox/Procedural");
            if (sky != null)
            {
                e.skyMat = new Material(sky) { name = "ZugSim_Sky" };
                RenderSettings.skybox = e.skyMat;
            }
            RenderSettings.sun = e.sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;

            e.rain = BuildRain(go.transform);
            return e;
        }

        static Light NewLight(string name, Transform parent, LightShadows shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = shadows;
            l.shadowStrength = 0.85f;
            return l;
        }

        static ParticleSystem BuildRain(Transform parent)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.9f;
            main.startSpeed = 0f;
            main.startSize = 0.035f;
            main.maxParticles = 3500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.8f, 0.85f, 0.95f, 0.45f);
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(36f, 0.5f, 36f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(-28f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 5f; r.velocityScale = 0f;
            r.sharedMaterial = Procedural.Particle(new Color(0.8f, 0.85f, 0.95f, 0.5f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        public void UpdateFrom(SimulationWorld w, float dt)
        {
            var wx = w.Weather.Current;
            double elev = w.Clock.SunElevationDeg;
            double day = w.Clock.Daylight;
            Daylight = (float)day;
            float cloud = (float)wx.Cloudiness, fog = (float)wx.Fog, rainI = (float)wx.Rain;
            float vis = (float)w.Weather.VisibilityM;

            // Sonne / Mond
            float az = 140f + (float)(w.Clock.Hour - 12.0) * 6f;
            sun.transform.rotation = Quaternion.Euler(Mathf.Max((float)elev, 2f), az, 0);
            float sunI = Mathf.Clamp01(Mathf.InverseLerp(-1f, 12f, (float)elev)) * 1.35f * (1f - 0.65f * cloud);
            sun.intensity = sunI;
            sun.color = Color.Lerp(new Color(1f, 0.55f, 0.3f), new Color(1f, 0.97f, 0.9f), Mathf.Clamp01((float)elev / 25f));
            sun.enabled = sunI > 0.01f;
            moon.transform.rotation = Quaternion.Euler(Mathf.Max((float)(-elev), 15f), az + 180f, 0);
            moon.intensity = (1f - (float)day) * 0.18f * (1f - 0.7f * cloud);
            moon.enabled = moon.intensity > 0.01f;

            // Himmel und Umgebungslicht
            Color dayAmbSky = Color.Lerp(new Color(0.50f, 0.62f, 0.85f), new Color(0.45f, 0.48f, 0.52f), cloud);
            Color nightAmbSky = new Color(0.025f, 0.035f, 0.07f);
            Color ambSky = Color.Lerp(nightAmbSky, dayAmbSky, (float)day);
            lightning = Mathf.Max(0f, lightning - dt * 4f);
            float flash = lightning * 2.5f;
            RenderSettings.ambientSkyColor = ambSky * (1f + flash);
            RenderSettings.ambientEquatorColor = ambSky * 0.7f * (1f + flash);
            RenderSettings.ambientGroundColor = ambSky * 0.35f;
            if (skyMat != null)
            {
                skyMat.SetFloat("_Exposure", Mathf.Lerp(0.04f, 1.25f, (float)day) * (1f - 0.45f * cloud));
                skyMat.SetFloat("_AtmosphereThickness", 1f + cloud * 1.3f);
                skyMat.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.55f, 0.55f, 0.58f), cloud));
                skyMat.SetFloat("_SunSize", 0.04f * (1f - cloud));
            }

            // Nebel
            Color fogDay = Color.Lerp(new Color(0.72f, 0.80f, 0.90f), new Color(0.62f, 0.64f, 0.67f), Mathf.Max(cloud * 0.8f, fog));
            Color fogCol = Color.Lerp(new Color(0.02f, 0.025f, 0.04f), fogDay, (float)day * 0.95f + 0.05f);
            fogCol += Color.white * (flash * 0.3f);
            RenderSettings.fogColor = fogCol;
            RenderSettings.fogDensity = 3.0f / Mathf.Max(vis, 60f);
            if (cam != null)
            {
                bool solid = vis < 1800f;
                cam.clearFlags = solid || skyMat == null ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                cam.backgroundColor = fogCol;
                cam.farClipPlane = Mathf.Clamp(vis * 2.5f, 400f, 6000f);
            }

            // Regen
            if (rain != null)
            {
                var em = rain.emission;
                em.rateOverTime = rainI * 2600f;
                if (cam != null) rain.transform.position = cam.transform.position + Vector3.up * 14f;
            }

            // Gewitter
            if (rainI > 0.8f && cloud > 0.9f)
            {
                lightningTimer -= dt;
                if (lightningTimer <= 0) { lightning = 1f; lightningTimer = 5f + (float)rng.NextDouble() * 14f; }
            }

            NeedsHeadlights = day < 0.5 || vis < 2500f || rainI > 0.6f;
        }
    }
}
