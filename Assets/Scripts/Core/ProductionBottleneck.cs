namespace Game.Core
{
    /// <summary>
    /// Chooses the production-chain row that is currently holding an island back.
    /// Kept as pure maths so the report and its tests cannot drift apart.
    /// </summary>
    public static class ProductionBottleneck
    {
        public const int Unknown = -1;
        public const int Source = 0;
        public const int OreFleet = 2;
        public const int Smelter = 3;
        public const int CargoFleet = 4;
        public const int Market = 5;

        private const double FullFraction = 0.90d;
        private const double MeaningfulRateRatio = 0.80d;

        public static int Find(
            bool flowReady,
            double oreMinedPerMinute,
            double oreHauledPerMinute,
            double barsRefinedPerMinute,
            double barsDeliveredPerMinute,
            double storageFraction,
            double refineQueue,
            double sixSecondsOfSmelting,
            double barStorageFraction,
            double marketOverflowSeconds)
        {
            if (!flowReady) return Unknown;

            // Back-pressure is the strongest signal. Read from the counter backwards so the
            // downstream cause wins over every full pile it creates upstream.
            if (marketOverflowSeconds > 3d) return Market;
            if (barStorageFraction >= FullFraction) return CargoFleet;
            if (sixSecondsOfSmelting > 0d && refineQueue >= sixSecondsOfSmelting) return Smelter;
            if (storageFraction >= FullFraction) return OreFleet;

            // A pile need not reach 90% before a real restriction becomes visible. The old report
            // ignored these meters entirely and therefore labelled every such island "Mine".
            // Require both readings to be positive: during initial pipe fill, zero means "not yet
            // observed", not that the unopened downstream station is definitely the bottleneck.
            if (ClearlySlower(barsDeliveredPerMinute, barsRefinedPerMinute)) return CargoFleet;
            if (ClearlySlower(barsRefinedPerMinute, oreHauledPerMinute)) return Smelter;
            if (ClearlySlower(oreHauledPerMinute, oreMinedPerMinute)) return OreFleet;

            // With no back-pressure and no measured loss between stages, the chain is genuinely
            // supply-limited. The source row groups the mine and its railway on this report.
            return Source;
        }

        private static bool ClearlySlower(double downstream, double upstream)
            => upstream > 0d && downstream > 0d && downstream < upstream * MeaningfulRateRatio;
    }
}
