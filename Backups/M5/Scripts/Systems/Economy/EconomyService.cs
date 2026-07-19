using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Pure economy math (no Unity dependency, unit-testable). Values come from the EconomyConfig SO.
    /// Upgrade cost grows geometrically per level; ore-tier value geometrically per tier (GDD §5).
    /// </summary>
    public sealed class EconomyService
    {
        public double CostGrowth { get; }
        public double TierValueMultiplier { get; }
        public double ManagerBonus { get; }       // e.g. 1.0 = +100% (x2) rate when a manager is hired
        public double ManagerCostBase { get; }

        public EconomyService(double costGrowth, double tierValueMultiplier, double managerBonus = 1d, double managerCostBase = 500d)
        {
            CostGrowth = costGrowth;
            TierValueMultiplier = tierValueMultiplier;
            ManagerBonus = managerBonus;
            ManagerCostBase = managerCostBase;
        }

        /// <summary>Cost to take a station from <paramref name="level"/> to level+1.</summary>
        public BigDouble UpgradeCost(double baseCost, int level) => BigDouble.Pow(CostGrowth, level) * baseCost;

        /// <summary>Value multiplier for an ore/product tier (0 = the first tier).</summary>
        public double TierValue(int tierIndex) => Math.Pow(TierValueMultiplier, tierIndex);
    }
}
