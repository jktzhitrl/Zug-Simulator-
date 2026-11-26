using UnityEngine;

namespace ZugSim.Game
{
    /// <summary>
    /// 60-FPS-Ziel auf Tablets: Frame-Limit, Qualitaetsstufen und adaptive Aufloesung (Dynamic Resolution).
    /// Faellt die Frametime dauerhaft ueber das Budget, sinkt die Render-Aufloesung schrittweise, bei Reserve steigt sie wieder.
    /// </summary>
    public sealed class PerformanceManager : MonoBehaviour
    {
        public const float TargetFps = 60f;

        public float ResolutionScale { get; private set; }
        public float SmoothedFrameMs { get; private set; }
        public bool Adaptive = true;

        Camera cam;
        float minScale = 0.7f;
        float timer;
        int level = 1;

        public static PerformanceManager Create(Transform parent, Camera cam)
        {
            var go = new GameObject("Performance");
            go.transform.SetParent(parent, false);
            var pm = go.AddComponent<PerformanceManager>();
            pm.cam = cam;
            pm.ResolutionScale = 1f;
            pm.SmoothedFrameMs = 16.7f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = (int)TargetFps;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (cam != null) cam.allowDynamicResolution = true;
            return pm;
        }

        public int Level { get { return level; } }

        /// <summary>0 = Niedrig (aeltere Geraete), 1 = Mittel, 2 = Hoch (iPad Pro / Flaggschiff).</summary>
        public void ApplyLevel(int lvl)
        {
            level = Mathf.Clamp(lvl, 0, 2);
            switch (level)
            {
                case 0:
                    QualitySettings.shadows = ShadowQuality.Disable; QualitySettings.shadowDistance = 0f;
                    QualitySettings.lodBias = 0.6f; QualitySettings.antiAliasing = 0; QualitySettings.pixelLightCount = 1;
                    QualitySettings.skinWeights = SkinWeights.OneBone;
                    minScale = 0.6f;
                    break;
                case 1:
                    QualitySettings.shadows = ShadowQuality.HardOnly; QualitySettings.shadowDistance = 70f;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.lodBias = 1f; QualitySettings.antiAliasing = 2; QualitySettings.pixelLightCount = 2;
                    minScale = 0.7f;
                    break;
                default:
                    QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 130f;
                    QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.shadowCascades = 4;
                    QualitySettings.lodBias = 1.5f; QualitySettings.antiAliasing = 4; QualitySettings.pixelLightCount = 4;
                    minScale = 0.8f;
                    break;
            }
        }

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            SmoothedFrameMs = Mathf.Lerp(SmoothedFrameMs, Mathf.Min(ms, 100f), 0.05f);
            if (!Adaptive) return;
            timer += Time.unscaledDeltaTime;
            if (timer < 0.5f) return;
            timer = 0f;
            float budget = 1000f / TargetFps;
            if (SmoothedFrameMs > budget * 1.12f) ResolutionScale = Mathf.Max(minScale, ResolutionScale - 0.05f);
            else if (SmoothedFrameMs < budget * 0.85f) ResolutionScale = Mathf.Min(1f, ResolutionScale + 0.025f);
            ScalableBufferManager.ResizeBuffers(ResolutionScale, ResolutionScale);
        }
    }
}
