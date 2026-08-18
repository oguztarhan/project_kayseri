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

        /// <summary>
        /// How many seconds of the trailing minute a buffer has to have spent backed up before it
        /// counts. A tenth of the window: long enough that one truck arriving late is not a verdict,
        /// short enough that a pile which genuinely cannot be cleared always trips it.
        /// </summary>
        private const double BackedUpSeconds = 6d;

        private const double MeaningfulRateRatio = 0.80d;

        /// <summary>
        /// The buffer arguments are DURATIONS, not levels — how long each pile spent at its ceiling,
        /// in seconds. Reading the level instead was what made this report answer "Mine" to almost
        /// every island: every pile in the chain is a sawtooth, sitting at its ceiling until a truck
        /// takes a load out of it, and a screen sampling once every quarter second lands in the
        /// trough as often as on the peak. One missed peak and a genuinely blocked island fell all
        /// the way through to the supply-limited fallback at the bottom.
        /// </summary>
        public static int Find(
            bool flowReady,
            double oreMinedPerMinute,
            double oreHauledPerMinute,
            double barsRefinedPerMinute,
            double barsDeliveredPerMinute,
            double yardFullSeconds,
            double furnaceQueueSeconds,
            double barStoreFullSeconds,
            double marketOverflowSeconds)
        {
            if (!flowReady) return Unknown;

            // Back-pressure is the strongest signal. Read from the counter backwards so the
            // downstream cause wins over every full pile it creates upstream.
            if (marketOverflowSeconds > 3d) return Market;
            if (barStoreFullSeconds >= BackedUpSeconds) return CargoFleet;
            if (furnaceQueueSeconds >= BackedUpSeconds) return Smelter;
            if (yardFullSeconds >= BackedUpSeconds) return OreFleet;

            // A pile need not reach its ceiling before a real restriction becomes visible. The old
            // report ignored these meters entirely and therefore labelled every such island "Mine".
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
