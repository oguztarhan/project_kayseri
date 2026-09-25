using System;

namespace Game.Core
{
    /// <summary>
    /// Turns a bench saved under the two old tracks (speed and value, 1-21 each) into one mastery level.
    ///
    /// A bench gets the HIGHER of two levels. The first is what its old upgrades cost, spent again on the new curve,
    /// with the leftover refunded as cash. The second is the lowest level that earns at least what the bench earned
    /// before. So nobody loses income, and nobody loses what they paid. Old 21/21 pickaxe → level 29.
    ///
    /// The old economy is written down here as constants on purpose. Nothing tunes it any more; it only describes
    /// what an old save bought, and it must not move if the live tuning does.
    /// </summary>
    public static class MiningShopLevelMigration
    {
        /// <summary>A business whose LevelSchema is below this still carries the old tracks.</summary>
        public const int Schema = 1;
        public const int LegacyMaxLevel = 21;
        private const double LegacySpeedPerLevel = 0.1d;
        private const double LegacyValuePerLevel = 0.15d;
        private const double LegacyUpgradeBaseCost = 40d;
        private const double LegacyUpgradeCostGrowth = 1.18d;
        private static readonly double[] LegacyCraftSeconds = { 10d, 20d, 40d, 60d };
        private static readonly double[] LegacyUnitPrice = { 20d, 60d, 150d, 360d };

        public static bool LegacyLevelsValid(int speedLevel, int valueLevel) =>
            speedLevel >= 1 && speedLevel <= LegacyMaxLevel && valueLevel >= 1 && valueLevel <= LegacyMaxLevel;

        /// <summary>Cash the player paid for both tracks. Every bench shared one upgrade curve.</summary>
        public static double LegacySpend(int speedLevel, int valueLevel) => TrackSpend(speedLevel) + TrackSpend(valueLevel);

        /// <summary>What the bench earned per second under the old tracks.</summary>
        public static double LegacyIncomePerSecond(int productIndex, int speedLevel, int valueLevel)
        {
            double price = LegacyUnitPrice[productIndex] * (1d + LegacyValuePerLevel * (valueLevel - 1));
            double craft = LegacyCraftSeconds[productIndex] / (1d + LegacySpeedPerLevel * (speedLevel - 1));
            return price / craft;
        }

        /// <summary>
        /// The new level for one bench, and the cash to give back. The costs, price and craft time are the live tuning
        /// for the bench at <paramref name="productIndex"/>.
        /// </summary>
        public static int Level(int productIndex, int speedLevel, int valueLevel, double firstLevelCost, double basePrice,
            double baseCraftSeconds, in BenchMastery.Tuning mastery, out double refund)
        {
            if (productIndex < 0 || productIndex >= LegacyCraftSeconds.Length) throw new ArgumentOutOfRangeException(nameof(productIndex));
            if (!LegacyLevelsValid(speedLevel, valueLevel)) throw new ArgumentOutOfRangeException(nameof(speedLevel));

            double spent = LegacySpend(speedLevel, valueLevel);
            int bought = BenchMastery.MinLevel + BenchMastery.AffordableLevels(firstLevelCost, BenchMastery.MinLevel, spent,
                BenchMastery.MaxLevel - BenchMastery.MinLevel, mastery);

            double earned = LegacyIncomePerSecond(productIndex, speedLevel, valueLevel);
            int floor = BenchMastery.MinLevel;
            while (floor < BenchMastery.MaxLevel &&
                   BenchMastery.IncomePerSecond(basePrice, baseCraftSeconds, floor, mastery) < earned) floor++;

            int level = Math.Max(bought, floor);
            refund = Math.Max(0d, spent - BenchMastery.CostOfLevels(firstLevelCost, BenchMastery.MinLevel,
                level - BenchMastery.MinLevel, mastery));
            return level;
        }

        private static double TrackSpend(int level)
        {
            double spent = 0d;
            for (int k = 1; k < level; k++) spent += Math.Ceiling(LegacyUpgradeBaseCost * Math.Pow(LegacyUpgradeCostGrowth, k - 1));
            return spent;
        }
    }
}
