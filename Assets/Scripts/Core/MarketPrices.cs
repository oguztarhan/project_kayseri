using System;

namespace Game.Core
{
    /// <summary>
    /// What a yard charges for each of its upgrades.
    ///
    /// Prices are quoted in MINUTES OF THE ISLAND'S CAPPED INCOME, never in currency. The ore ladder
    /// multiplies what an island earns by 3.2 a tier, so an absolute price would be a fortune on coal
    /// and a rounding error by diamond, and every new island would need its own table. A price in
    /// minutes is the same sentence on all eight: "this costs about two minutes of a finished
    /// island's takings", and it stays true when the economy is re-solved around it.
    ///
    /// It reads against the CAP rather than against what the yard is actually earning on purpose. A
    /// bare yard makes 15% of its ceiling, so a pad priced at two capped minutes is thirteen real
    /// ones — enough to feel like a decision, short enough that the first hire is reachable in one
    /// visit rather than one evening. Pricing off live income instead would make upgrades cheapest
    /// exactly when the yard is worst, which is the wrong way round.
    ///
    /// THE NUMBERS BELOW ARE A FIRST PASS. They are shaped right — hires dominate, slots are cheap
    /// early, everything compounds — but they have not been through the ladder solver the island
    /// economy was tuned with, and they should be before this ships.
    /// </summary>
    public static class MarketPrices
    {
        /// <summary>Slots are counted from one — a yard always has a pad and a place in the line.</summary>
        public static int MinLevel(YardUpgrade kind)
            => kind == YardUpgrade.DepositSlot || kind == YardUpgrade.QueueSlot ? 1 : 0;

        /// <summary>How far each track goes. Past this the pad reads as finished and takes no more money.</summary>
        public static int MaxLevel(YardUpgrade kind)
        {
            switch (kind)
            {
                case YardUpgrade.DepositSlot: return MarketFlow.MaxDepositSlots;
                case YardUpgrade.QueueSlot: return MarketFlow.MaxQueueSlots;
                case YardUpgrade.CarryCapacity: return MaxCarryLevel;
                default: return MarketFlow.MaxHireLevel;      // the three hires
            }
        }

        /// <summary>How many times the player's own back can be upgraded.</summary>
        public const int MaxCarryLevel = 8;

        public static bool IsMaxed(YardUpgrade kind, int level) => level >= MaxLevel(kind);

        /// <summary>
        /// Minutes of capped income the FIRST step on each track costs, indexed by
        /// <see cref="YardUpgrade"/>. The three hires are the expensive things in a yard because they
        /// are the only ones that end the visit — a slot makes a yard better, a hire makes it optional.
        /// </summary>
        private static readonly double[] FirstStepMinutes =
        {
            2.5d,    // DepositSlot   — buys buffer, so it matters most to a player who leaves
            1.2d,    // QueueSlot     — the cheapest way to make a yard feel busier
            1.8d,    // HireCarry     — the job the player does most of, so the first one to want gone
            2.2d,    // HireServe
            2.0d,    // HireCollect
            0.8d,    // CarryCapacity — bought early and often; it is the one that makes running fun
        };

        /// <summary>What each further step multiplies the last one by.</summary>
        private static readonly double[] Growth =
        {
            1.75d,   // DepositSlot   — only three ever bought, so it may climb steeply
            1.55d,   // QueueSlot
            1.60d,   // HireCarry
            1.60d,   // HireServe
            1.60d,   // HireCollect
            1.45d,   // CarryCapacity — eight steps, so the gentlest curve of the six
        };

        /// <summary>
        /// The price of the NEXT step on a track, in currency. Returns 0 for a finished track, which
        /// callers read as "nothing left to sell here".
        /// </summary>
        public static double Cost(YardUpgrade kind, int currentLevel, double islandIncomeCapPerMinute)
        {
            if (islandIncomeCapPerMinute <= 0d) return 0d;
            if (IsMaxed(kind, currentLevel)) return 0d;

            int index = (int)kind;
            if (index < 0 || index >= FirstStepMinutes.Length) return 0d;

            int steps = currentLevel - MinLevel(kind);
            if (steps < 0) steps = 0;
            return islandIncomeCapPerMinute * FirstStepMinutes[index] * Math.Pow(Growth[index], steps);
        }

        /// <summary>
        /// Everything left on one track, for the "what will finishing this yard cost me" readouts the
        /// upgrade screens want. Same shape as <c>IslandEconomy.CostToMax</c>, for the same reason.
        /// </summary>
        public static double CostToMax(YardUpgrade kind, int currentLevel, double islandIncomeCapPerMinute)
        {
            double total = 0d;
            for (int level = currentLevel; level < MaxLevel(kind); level++)
                total += Cost(kind, level, islandIncomeCapPerMinute);
            return total;
        }
    }
}
