using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Per-station tunables (mine / storage / market …). Designer-editable (GDD §16). Rate and
    /// capacity scale with the station's upgrade level; the upgrade price uses the global cost
    /// curve in EconomyService seeded by <see cref="BaseUpgradeCost"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "StationConfig", menuName = "Ore Empire/Station Config", order = 2)]
    public sealed class StationConfig : ScriptableObject
    {
        [SerializeField] private string displayName = "Station";

        [Header("Throughput")]
        [Tooltip("Units per second at level 0 (mine = produced, market = sold).")]
        [SerializeField] private double baseRate = 2d;
        [Tooltip("Fractional rate gain per level, e.g. 0.15 = +15% of base per level.")]
        [SerializeField] private double ratePerLevel = 0.20d;

        [Header("Buffer capacity")]
        [SerializeField] private double baseCapacity = 50d;
        [SerializeField] private double capacityPerLevel = 0.25d;

        [Header("Upgrades")]
        [SerializeField] private double baseUpgradeCost = 10d;

        public string DisplayName => displayName;
        public double BaseUpgradeCost => baseUpgradeCost;

        public double RateAtLevel(int level) => baseRate * (1d + ratePerLevel * level);
        public double CapacityAtLevel(int level) => baseCapacity * (1d + capacityPerLevel * level);
    }
}
