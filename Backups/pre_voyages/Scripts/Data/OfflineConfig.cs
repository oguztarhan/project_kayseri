using UnityEngine;

namespace Game.Data
{
    /// <summary>Offline-earnings tuning (GDD §7). Designer-editable.</summary>
    [CreateAssetMenu(fileName = "OfflineConfig", menuName = "Ore Empire/Offline Config", order = 10)]
    public sealed class OfflineConfig : ScriptableObject
    {
        [SerializeField] private bool enabled = true;
        [SerializeField, Range(0f, 1f)] private float efficiency = 0.5f;
        [SerializeField] private float capHours = 2f;

        public bool Enabled => enabled;
        public double Efficiency => efficiency;
        public long CapSeconds => (long)(capHours * 3600f);
    }
}
