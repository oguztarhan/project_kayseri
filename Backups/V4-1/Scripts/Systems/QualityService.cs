using UnityEngine;

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
            switch (t)
            {
                case Tier.Low: QualitySettings.shadowDistance = 0f; break;
                case Tier.Mid: QualitySettings.shadowDistance = 30f; break;
                default: QualitySettings.shadowDistance = 60f; break;
            }
        }
    }
}
