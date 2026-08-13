using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Systems
{
    /// <summary>
    /// Adaptive quality (GDD §14.5). Detects a device tier from RAM and applies a target frame rate +
    /// quality scaling so the 60 fps budget holds on the widest Android range.
    /// </summary>
    public sealed class QualityService
    {
        public enum Tier { Low, Mid, High }
        public Tier DeviceTier { get; }

        /// <summary>The two night-lighting extras, both read by <c>IslandGlow</c>. The ground pools
        /// are drawn on every tier — they are one additive decal per lamp and they carry most of
        /// what makes night read as night. The visible shafts are additive overdraw on top of that,
        /// and the real spot lights put a few hundred more entries through the light cluster, so
        /// they come in as the device can pay for them.</summary>
        public static bool NightBeamsAllowed { get; private set; } = true;
        public static bool NightSpotLightsAllowed { get; private set; } = true;

        public QualityService(int targetFrameRate, bool vSync)
        {
            DeviceTier = Detect();
            QualitySettings.vSyncCount = vSync ? 1 : 0;
            Application.targetFrameRate = targetFrameRate;
            ApplyTier(DeviceTier);
        }

        private static Tier Detect()
        {
            int mem = SystemInfo.systemMemorySize; // MB (0 if unknown)
            if (mem > 0 && mem < 2048) return Tier.Low;
            if (mem > 0 && mem < 4096) return Tier.Mid;
            return Tier.High;
        }

        private static void ApplyTier(Tier t)
        {
            // Shadow distance has to go through the pipeline asset: under URP the renderer reads
            // its own shadowDistance and QualitySettings.shadowDistance is ignored, so the writes
            // that used to live here did nothing at all. The island needs ~780 to be covered end
            // to end (measured: it spans 207-733 in view depth from the game camera), so the low
            // tier drops shadows entirely rather than showing half an island's worth.
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
                pipeline.shadowDistance = t == Tier.Low ? 0f : 780f;

            SetAmbientOcclusion(t != Tier.Low);
            NightBeamsAllowed = t != Tier.Low;
            NightSpotLightsAllowed = t == Tier.High;
        }

        /// <summary>Screen-space AO is the first thing to go on a low-tier device: it is a
        /// full-screen pass on a scene that is already carrying thousands of mesh instances.</summary>
        private static void SetAmbientOcclusion(bool on)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;

            var data = pipeline.rendererDataList;
            if (data == null) return;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == null) continue;
                var features = data[i].rendererFeatures;
                for (int f = 0; f < features.Count; f++)
                    if (features[f] is ScreenSpaceAmbientOcclusion) features[f].SetActive(on);
            }
        }
    }
}
