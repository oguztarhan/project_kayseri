using UnityEngine;

namespace Game.Data
{
    /// <summary>Performance/quality tuning (GDD §14.5). Designer-editable.</summary>
    [CreateAssetMenu(fileName = "QualityConfig", menuName = "Ore Empire/Quality Config", order = 14)]
    public sealed class QualityConfig : ScriptableObject
    {
        [SerializeField] private int targetFrameRate = 60;
        [SerializeField] private bool vSync = false;

        public int TargetFrameRate => targetFrameRate;
        public bool VSync => vSync;
    }
}
