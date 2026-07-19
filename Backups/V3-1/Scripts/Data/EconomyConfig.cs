using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Global economy tuning. Every value is designer-editable in the Inspector (GDD §5, §16).
    /// Create the asset via: Assets &gt; Create &gt; Ore Empire &gt; Economy Config.
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "Ore Empire/Economy Config", order = 0)]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Header("Currency start")]
        [SerializeField] private double startingCash = 0d;
        [SerializeField] private long startingGems = 0;

        [Header("Upgrade cost curve  (cost = base * growth^level)")]
        [SerializeField] private double baseUpgradeCost = 10d;
        [SerializeField] private double costGrowth = 1.09d;

        [Header("Ore economy")]
        [SerializeField] private double tierValueMultiplier = 3.2d;

        [Header("Managers (GDD 6)")]
        [SerializeField] private double managerBonus = 1.0d;       // +100% station rate (x2)
        [SerializeField] private double managerCostBase = 500d;

        public double StartingCash => startingCash;
        public long StartingGems => startingGems;
        public double BaseUpgradeCost => baseUpgradeCost;
        public double CostGrowth => costGrowth;
        public double TierValueMultiplier => tierValueMultiplier;
        public double ManagerBonus => managerBonus;
        public double ManagerCostBase => managerCostBase;
    }
}
