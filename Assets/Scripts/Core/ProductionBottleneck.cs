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

        /// <summary>
        /// The market's own patience. Shorter than <see cref="BackedUpSeconds"/> because an overflowing
        /// counter is the end of the chain: nothing downstream of it can be the real cause, so there is
        /// nothing to wait for a second opinion about.
        /// </summary>
        private const double MarketOverflowSeconds = 3d;

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

            // Back-pressure is the strongest signal, and it is the whole of Blocked.
            int wall = Blocked(yardFullSeconds, furnaceQueueSeconds,
                               barStoreFullSeconds, marketOverflowSeconds);
            if (wall != Unknown) return wall;

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

        /// <summary>
        /// The back-pressure half of <see cref="Find"/> on its own: the stage a full pile is pointing
        /// at, or <see cref="Unknown"/> when nothing on the island is backed up.
        ///
        /// WHY IT IS SPLIT OUT. Find always names a stage, because a screen that asked "what is the
        /// wall?" needs an answer even on a chain that is running perfectly - there it says Source,
        /// meaning supply-limited. That is the right answer for a report and the wrong one for a
        /// badge: it would be lit on every healthy island forever, which is how a warning stops being
        /// read. Back-pressure is different in kind. A pile at its ceiling means throughput the player
        /// has ALREADY PAID FOR is going to waste, and that is worth interrupting them for.
        ///
        /// Read from the market backwards, for the reason Find is: a yard is only ever full because
        /// the leg after it cannot clear it, so the downstream cause has to win over every full pile
        /// it creates behind itself.
        /// </summary>
        public static int Blocked(double yardFullSeconds, double furnaceQueueSeconds,
                                  double barStoreFullSeconds, double marketOverflowSeconds)
        {
            if (marketOverflowSeconds > MarketOverflowSeconds) return Market;
            if (barStoreFullSeconds >= BackedUpSeconds) return CargoFleet;
            if (furnaceQueueSeconds >= BackedUpSeconds) return Smelter;
            if (yardFullSeconds >= BackedUpSeconds) return OreFleet;
            return Unknown;
        }

        /// <summary>
        /// The <see cref="IslandEconomy.Stations"/> index a report row belongs to, or -1 for a row
        /// that is not one station.
        ///
        /// The two numberings are NOT the same and must not be assumed to be: these rows are what the
        /// chain report draws, which groups the mine and its railway on one line and gives the storage
        /// shed a line of its own, so row 2 is ORE TRUCKS while station 2 is STORAGE. Anything naming
        /// a row out loud has to come through here.
        /// </summary>
        public static int StationOf(int row)
        {
            switch (row)
            {
                case Source:     return 0;   // MINE - the row covers the mine and the train
                case OreFleet:   return 3;   // ORE TRUCKS
                case Smelter:    return 4;   // SMELTER
                case CargoFleet: return 5;   // CARGO TRUCKS
                case Market:     return 6;   // MARKET
                default:         return -1;
            }
        }
    }
}
