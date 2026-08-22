using System;

namespace Game.Core
{
    /// <summary>
    /// What a yard charges for each of its upgrades.
    ///
    /// A yard's WHOLE price list is one budget, split six ways, and the budget is a share of what the
    /// ISLAND'S OWN upgrade tree costs — with a ceiling in minutes of capped income so the late
    /// islands cannot run away. Everything below divides that budget up; no track carries an absolute
    /// number, and adding a ninth island needs no entry here at all.
    ///
    /// WHY NOT MINUTES OF CAPPED INCOME, which is what this used to be. It looked island-independent
    /// and it was not. The ore ladder multiplies income by 3.2 a tier, so quoting prices in minutes
    /// does hold the yard's PAYBACK constant — but it says nothing about whether the yard is a
    /// sensible thing to buy compared with upgrading the island it stands on, and that ratio is not
    /// constant at all. Measured across the eight:
    ///
    ///     island tree, in minutes of that island's capped income
    ///     coal 32   copper 430   iron 944   silver 1592   gold 2401   ruby 3320   emerald 3930   diamond 4158
    ///
    /// A flat 160 minutes of yard against that made the coal market FIVE TIMES the price of finishing
    /// the entire coal island, and four per cent of finishing the diamond one. The early islands —
    /// the ones a player actually meets — were the badly broken end.
    ///
    /// So the budget is <see cref="YardBudget"/>: a share of the island's tree, capped. The share is
    /// what keeps a yard proportionate to the island it is in; the cap is what stops the late islands,
    /// whose trees are deliberately enormous, pricing a market at days of income.
    ///
    /// It reads against the CAP rather than against what the yard is actually earning on purpose. A
    /// bare yard makes 15% of its ceiling, so a fully hired one is worth 6.7x — pricing off live
    /// income would make the upgrade cheapest exactly when the yard is worst, which is the wrong way
    /// round.
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
        /// What share of the ISLAND'S OWN upgrade tree a complete market yard costs.
        ///
        /// An eighth, and the number comes from what the yard is worth rather than from taste: hiring
        /// out all three jobs takes a yard from the 0.15 idle trickle to 1.0, so a finished yard is
        /// a 6.7x multiplier on that island's income. The island's own tree is worth far more than
        /// that — coal goes from 360 a minute to 102,000 — so the yard has to be a fraction of it or
        /// it is a trap, and a large enough fraction to be a decision or it is free.
        /// </summary>
        private const double TreeShare = 0.13d;

        /// <summary>
        /// The ceiling, in minutes of the island's capped income, whatever the tree says.
        ///
        /// The late islands' trees are meant to be long grinds — diamond's is four thousand minutes of
        /// its own ceiling — and an eighth of that is still most of a day's production for a building
        /// the player has already learned. This is what binds from iron upward.
        ///
        /// An hour, which is what makes the FIRST pad in any yard a few minutes' work rather than a
        /// wait: the cheapest step is about a sixtieth of the budget, so no yard on the ladder opens
        /// with a price the player has to go away and come back for. A finished yard pays itself back
        /// in a bit over an hour of the extra income it unlocks, everywhere above copper.
        /// </summary>
        private const double CeilingMinutes = 60d;

        /// <summary>
        /// What the whole yard costs, before it is split between the six tracks.
        ///
        /// A tree cost of zero means the island has not reported one — an old save, a test stub, or
        /// the first frames before an island's economy exists. That falls back to the ceiling rather
        /// than to nothing, because a yard that is briefly free is a worse bug than one that is
        /// briefly dear.
        /// </summary>
        public static double YardBudget(double islandIncomeCapPerMinute, double islandTreeCost)
        {
            if (islandIncomeCapPerMinute <= 0d) return 0d;
            double ceiling = islandIncomeCapPerMinute * CeilingMinutes;
            if (islandTreeCost <= 0d) return ceiling;
            double share = islandTreeCost * TreeShare;
            return share < ceiling ? share : ceiling;
        }

        /// <summary>
        /// How the yard's budget is divided between its six tracks. Sums to one — these are shares of
        /// the whole, not prices, so re-weighting one means re-weighting another and the yard's total
        /// never moves by accident.
        ///
        /// The three hires take three fifths of it between them, because they are the only upgrades
        /// that end the visit: a slot makes a yard better, a hire makes it optional.
        /// </summary>
        private static readonly double[] TrackShare =
        {
            0.13d,   // DepositSlot   — buys buffer, so it matters most to a player who leaves
            0.12d,   // QueueSlot     — the cheapest way to make a yard feel busier
            0.19d,   // HireCarry     — the job the player does most of, so the first one to want gone
            0.21d,   // HireServe
            0.20d,   // HireCollect
            0.15d,   // CarryCapacity — bought early and often; it is the one that makes running fun
        };

        /// <summary>
        /// How steeply a track's own price climbs. This no longer decides what a track COSTS — the
        /// share above does — only how its cost is spread across its steps, so a steeper curve makes
        /// the first step cheaper and the last one dearer without changing the total.
        /// </summary>
        private static readonly double[] Growth =
        {
            1.55d,   // DepositSlot   — only three ever bought, so it may climb steeply
            1.38d,   // QueueSlot
            1.42d,   // HireCarry
            1.42d,   // HireServe
            1.42d,   // HireCollect
            1.30d,   // CarryCapacity — eight steps, so the gentlest curve of the six
        };

        /// <summary>
        /// The sum of a track's growth curve over all its steps. Dividing by this is what turns a
        /// share of the budget into a price per step: the steps of one track always add back up to
        /// exactly that track's share, however the curve is shaped.
        /// </summary>
        private static double CurveTotal(YardUpgrade kind)
        {
            int index = (int)kind;
            int steps = MaxLevel(kind) - MinLevel(kind);
            double total = 0d, term = 1d;
            for (int i = 0; i < steps; i++) { total += term; term *= Growth[index]; }
            return total;
        }

        /// <summary>
        /// The price of the NEXT step on a track, in currency. Returns 0 for a finished track, which
        /// callers read as "nothing left to sell here".
        ///
        /// <paramref name="islandTreeCost"/> may be left out, and everything still works off the
        /// ceiling alone — see <see cref="YardBudget"/>. It is optional rather than required so that
        /// a caller which genuinely has no island behind it (a test, a save from before islands
        /// reported their trees) is not forced to invent a number.
        /// </summary>
        public static double Cost(YardUpgrade kind, int currentLevel, double islandIncomeCapPerMinute,
                                  double islandTreeCost = 0d)
        {
            if (IsMaxed(kind, currentLevel)) return 0d;

            int index = (int)kind;
            if (index < 0 || index >= TrackShare.Length) return 0d;

            double budget = YardBudget(islandIncomeCapPerMinute, islandTreeCost);
            if (budget <= 0d) return 0d;

            double curve = CurveTotal(kind);
            if (curve <= 0d) return 0d;

            int steps = currentLevel - MinLevel(kind);
            if (steps < 0) steps = 0;
            return budget * TrackShare[index] * Math.Pow(Growth[index], steps) / curve;
        }

        /// <summary>
        /// Everything left on one track, for the "what will finishing this yard cost me" readouts the
        /// upgrade screens want. Same shape as <c>IslandEconomy.CostToMax</c>, for the same reason.
        /// </summary>
        public static double CostToMax(YardUpgrade kind, int currentLevel,
                                       double islandIncomeCapPerMinute, double islandTreeCost = 0d)
        {
            double total = 0d;
            for (int level = currentLevel; level < MaxLevel(kind); level++)
                total += Cost(kind, level, islandIncomeCapPerMinute, islandTreeCost);
            return total;
        }
    }
}
