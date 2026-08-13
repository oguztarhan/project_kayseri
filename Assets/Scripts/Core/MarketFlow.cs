using System;

namespace Game.Core
{
    /// <summary>
    /// The market yard as pure maths: how fast a yard sells, given who is working in it.
    ///
    /// This is the market's counterpart to <see cref="IslandEconomy"/>, and it exists for the same
    /// reason — the island's income used to be a thing you could only discover by playing, and the
    /// yard would inherit that problem the moment it became the place cash is made. Everything
    /// closed-form lives here so a service, a test, and the offline grant all read one copy of it.
    ///
    /// THE ONE IDEA: a yard has three jobs — carry the bars to the counter, serve the queue, collect
    /// the cash off the floor — and a chain runs at the speed of its slowest link. So the yard's
    /// throughput is the MINIMUM of the three job rates, never the average:
    ///
    ///     nobody hired                     the AFK trickle  -> 0.15
    ///     two jobs hired, one not          the gap throttles -> 0.15
    ///     all three hired at max level                       -> 1.00, forever
    ///
    /// The last row is the promise the whole feature rests on: a finished yard never needs another
    /// visit. It is true here by construction — <see cref="HireStep"/> is derived so that a hire at
    /// <see cref="MaxHireLevel"/> is exactly 1.0, rather than a hand-picked number that happens to
    /// round to it.
    ///
    /// WHAT IS DELIBERATELY NOT HERE: the player. This used to carry a "playerPresent" term worth a
    /// flat 0.8, which was a stand-in for work nothing in the game was doing yet. Now the yard has a
    /// floor to walk and bars to pick up, that stand-in would be paying him twice — once for standing
    /// there and once for the load on his back. So this is the rate the yard manages ON ITS OWN, and
    /// what the player carries is added to it, a bar at a time, by the hands that carried it.
    ///
    /// Rates and capacities are expressed as FRACTIONS OF WHAT THE ISLAND DELIVERS, never as absolute
    /// bars per second. The ore ladder multiplies every island's output by 3.2 per tier, so any
    /// absolute number here would be correct on coal and meaningless by diamond. Relative numbers make
    /// a new island's yard a palette row rather than a balance pass.
    /// </summary>
    public static class MarketFlow
    {
        /// <summary>Job indices. Saved games address hires by number, so these must never be reordered.</summary>
        public const int Carry = 0, Serve = 1, Collect = 2, JobCount = 3;

        /// <summary>A hire's level. 0 in a save row means "nobody does this job"; 1..5 is a hire.</summary>
        public const int MaxHireLevel = 5;

        /// <summary>Yard upgrade ceilings: deposit pads on the floor, and places in the queue.</summary>
        public const int MaxDepositSlots = 4, MaxQueueSlots = 6;

        /// <summary>
        /// What an unworked job still manages — the AFK trickle. Not zero: an island whose yard is
        /// unattended has to keep paying something, or closing the app on an unfinished yard would
        /// stop the empire dead and the offline grant would have nothing to grant.
        /// </summary>
        public const double IdleTrickle = 0.15d;

        /// <summary>What a freshly hired worker manages before any levels. Better than the trickle, well short of full.</summary>
        public const double HireBase = 0.45d;

        /// <summary>Derived, not chosen: the step that lands level <see cref="MaxHireLevel"/> exactly on 1.0.</summary>
        public static double HireStep => (1d - HireBase) / (MaxHireLevel - 1);

        /// <summary>
        /// What a yard with ONE place in the queue can move: exactly what its island delivers.
        ///
        /// This is parity on purpose, and it was 0.25 first, which was wrong. Queue length and staffing
        /// are two separate throttles, and starting the queue below parity multiplied them together —
        /// a brand-new coal yard came out at 0.25 x 0.15, under four percent of what the island used to
        /// earn, and the opening hour of the game with it. Staffing is the throttle this feature is
        /// about; the queue is headroom for an island that has grown since.
        /// </summary>
        public const double QueueParityShare = 1d;

        /// <summary>Every place after the first, as a share of delivery. Six slots is 2.25x headroom.</summary>
        public const double SupplySharePerExtraQueueSlot = 0.25d;

        /// <summary>How long a deposit pad can hold: minutes of the island's own delivery rate.</summary>
        public const double StockMinutesPerDepositSlot = 3d;

        // ------------------------------------------------------------------ jobs
        /// <summary>
        /// One job's rate, 0..1. A hire works at its level; a job nobody has been hired for still
        /// manages the trickle, because an unattended yard has to keep paying something.
        /// </summary>
        public static double JobRate(int hireLevel)
        {
            if (hireLevel <= 0) return IdleTrickle;
            int level = hireLevel > MaxHireLevel ? MaxHireLevel : hireLevel;
            return Clamp01(HireBase + HireStep * (level - 1));
        }

        /// <summary>
        /// The share of its capacity the yard turns over on its own — the slowest of the three jobs.
        /// Whatever the player carries by hand is on top of this, and is not modelled here.
        /// </summary>
        public static double ServiceRate(int[] hireLevels)
        {
            double slowest = 1d;
            for (int j = 0; j < JobCount; j++)
            {
                int level = (hireLevels != null && j < hireLevels.Length) ? hireLevels[j] : 0;
                double rate = JobRate(level);
                if (rate < slowest) slowest = rate;
            }
            return slowest;
        }

        /// <summary>
        /// True once the yard runs itself: every job hired and levelled out. This is the flag the
        /// design hangs on — a maxed yard sells at full speed whether or not the player ever returns.
        /// </summary>
        public static bool IsMaxed(int[] hireLevels)
        {
            if (hireLevels == null || hireLevels.Length < JobCount) return false;
            for (int j = 0; j < JobCount; j++)
                if (hireLevels[j] < MaxHireLevel) return false;
            return true;
        }

        // -------------------------------------------------------------- capacity
        /// <summary>Bars per second the counter could move at rate 1.0, given how long the queue is.</summary>
        public static double SellCapacityPerSecond(double supplyPerSecond, int queueSlots)
        {
            if (supplyPerSecond <= 0d) return 0d;
            int slots = Clamp(queueSlots, 1, MaxQueueSlots);
            return supplyPerSecond * (QueueParityShare + SupplySharePerExtraQueueSlot * (slots - 1));
        }

        /// <summary>Bars the deposit pads can hold before the yard is full and deliveries start spilling.</summary>
        public static double StockCapacity(double supplyPerSecond, int depositSlots)
        {
            if (supplyPerSecond <= 0d) return 0d;
            int slots = Clamp(depositSlots, 1, MaxDepositSlots);
            return supplyPerSecond * 60d * StockMinutesPerDepositSlot * slots;
        }

        // ----------------------------------------------------------------- flow
        /// <summary>
        /// Bars sold in one tick: what the counter can move, limited by what is actually on the pad.
        /// </summary>
        public static double SoldInTick(double stock, double sellCapacityPerSecond, double serviceRate, double seconds)
        {
            if (stock <= 0d || sellCapacityPerSecond <= 0d || serviceRate <= 0d || seconds <= 0d) return 0d;
            double want = sellCapacityPerSecond * serviceRate * seconds;
            return want < stock ? want : stock;
        }

        /// <summary>
        /// Adds a delivery to the pad, reporting what did not fit. The overflow is not a rounding
        /// detail — a yard that has been full for hours is the signal the player is meant to act on,
        /// and the caller needs to be able to say so.
        /// </summary>
        public static double AddStock(double stock, double bars, double capacity, out double overflow)
        {
            overflow = 0d;
            if (bars <= 0d) return stock;
            double total = stock + bars;
            if (capacity > 0d && total > capacity)
            {
                overflow = total - capacity;
                return capacity;
            }
            return total;
        }

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
