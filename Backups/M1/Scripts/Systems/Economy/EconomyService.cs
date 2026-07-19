using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Pure economy math (no Unity dependency, so it is unit-testable). Values come from the
    /// EconomyConfig SO at construction time. Upgrade cost grows geometrically per level;
    /// ore-tier value grows geometrically per tier (GDD §5).
    /// </summary>
    public sealed class EconomyService
    {
        public double CostGrowth { get; }
        public double TierValueMultiplier { get; }

        public EconomyService(double costGrowth, double tierValueMultiplier)
        {
            CostGrowth = costGrowth;
            TierValueMultiplier = tierValueMultiplier;
        }

        /// <summary>Cost to take a station from <paramref name="level"/> to level+1.</summary>
        public BigDouble UpgradeCost(double baseCost, int level)
            => BigDouble.Pow(CostGrowth, level) * baseCost;

        /// <summary>Value multiplier for an ore/product tier (0 = the first tier).</summary>
        public double TierValue(int tierIndex)
            => Math.Pow(TierValueMultiplier, tierIndex);
    }
}
