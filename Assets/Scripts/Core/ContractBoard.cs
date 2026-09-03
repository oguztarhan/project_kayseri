namespace Game.Core
{
    /// <summary>
    /// How one port job is sized and priced (GDD §9).
    ///
    /// This is the whole of the board's arithmetic and none of its state. It lives here rather than
    /// inside ContractService for two reasons. The first is that every path that puts a job on the table
    /// has to produce it the same way — cutting a whole board when the ship docks, replacing a single
    /// card, refilling one that was taken — and three copies of the sizing rule would drift the moment
    /// one of them was tuned. The second is that a job is pure arithmetic over four numbers and testing
    /// it should not require a Unity assembly.
    ///
    /// Nothing here reads a clock, a wallet or a random number. A <see cref="Cut"/> is a function of its
    /// arguments and nothing else, which is what makes a board reproducible: hand it the meter the board
    /// was cut against and it gives back the same job it gave the first time, on any device, in any
    /// session, however long the ship has been sitting at the pier.
    /// </summary>
    public static class ContractBoard
    {
        /// <summary>What one difficulty asks for and pays, as multiples of NORMAL.</summary>
        public struct Tier
        {
            public float Rate;       // demanded throughput as a multiple of what the empire measures
            public float Minutes;    // the window
            public float Pay;        // payout as a multiple of the NORMAL payout
            public long Gems;
        }

        /// <summary>
        /// The empire as the board saw it at the moment it was cut, plus the streak multiplier. Frozen
        /// for the life of the board: the meter keeps moving while the ship waits, and a card that was
        /// priced off a later reading than the one beside it would make the three cards incomparable.
        /// </summary>
        public struct Meter
        {
            public double ProcPerMinute;
            public double CashPerMinute;
            public double Difficulty;
        }

        /// <summary>
        /// The opening-minutes floors. Before the meter has anything in it every job would ask for zero
        /// units and pay zero cash, so these carry the first few contracts of a new save.
        /// </summary>
        public struct Floors
        {
            public double Units;
            public double Cash;
            public double RewardFraction;
            public float NormalMinutes;
        }

        /// <summary>One job, ready to go on a card.</summary>
        public struct Terms
        {
            public double Units;
            public float Seconds;
            public double Cash;
            public long Gems;
        }

        /// <summary>
        /// Sizes and prices a single job. Pure: same arguments, same job, always.
        /// <paramref name="windowScale"/> is 1 for a rolled card and the swap's shape for a swapped one;
        /// it has already been applied to <c>tier.Minutes</c> by the caller and is passed again only so
        /// the cash floor can follow it. The units floor follows the window by construction, but the
        /// cash floor is a flat number — left alone, a shorter swap on a floor-priced board keeps the
        /// whole floor for less of the player's time, and that is the raise a swap must never be.
        /// </summary>
        public static Terms Cut(Tier tier, Meter meter, Floors floors, float windowScale = 1f)
        {
            double units = meter.ProcPerMinute * tier.Minutes * tier.Rate * meter.Difficulty;

            // The floor is scaled by the tier so the three cards do not collapse into the same number
            // before the meter has anything in it.
            double floor = floors.Units * tier.Rate
                         * (floors.NormalMinutes > 0f ? tier.Minutes / floors.NormalMinutes : 1f);
            if (units < floor) units = floor;

            double cash = meter.CashPerMinute * tier.Minutes * floors.RewardFraction * tier.Pay;
            double cashFloor = floors.Cash * tier.Pay * windowScale;
            if (cash < cashFloor) cash = cashFloor;

            return new Terms
            {
                Units = RoundNice(units),
                Seconds = tier.Minutes * 60f,
                Cash = cash,
                Gems = tier.Gems,
            };
        }

        /// <summary>
        /// Whether a board cut against <paramref name="boardProcPerMinute"/> has been left behind by an
        /// empire now running at <paramref name="liveProcPerMinute"/>.
        ///
        /// This is deliberately a RATIO and not an age. A board goes stale because the player got
        /// stronger, not because time passed: an hour away from a maxed-out island leaves the numbers
        /// exactly as good as they were, while ten minutes spent buying upgrades can leave them
        /// laughable. Measuring the thing that actually went wrong also means there is no device clock
        /// in the decision, so there is nothing here to gain by moving one.
        /// </summary>
        public static bool IsStale(double liveProcPerMinute, double boardProcPerMinute, double factor)
        {
            if (factor <= 1d) return false;
            if (boardProcPerMinute <= 0d) return false;   // nothing to compare against yet
            return liveProcPerMinute >= boardProcPerMinute * factor;
        }

        /// <summary>
        /// The shapes a swapped card can take, as multiples of the tier's authored window. A swap keeps
        /// the tier — its rate, its pay-per-minute, its gems — and changes only how long the player is
        /// signing up for, with the units and the cash following the window. That is the one lever that
        /// gives a real choice without touching balance: a card that pays more per minute than the one
        /// it replaced would make the swap button a raise, and every player would press it every time.
        ///
        /// None of these is 1.0 on purpose. A swap that could hand back the card it replaced is a swap
        /// the player will believe was ignored.
        /// </summary>
        private static readonly float[] WindowScales = { 0.7f, 0.85f, 1.15f, 1.3f };

        /// <summary>
        /// Picks the window for a swapped card from <paramref name="seed"/> — the new card's id, which
        /// is persisted, so a swapped card can be re-cut from the save exactly like a rolled one.
        /// <paramref name="current"/> is the scale already on the card being replaced (1.0 for an
        /// unswapped card) and is stepped past, so two swaps in a row can never produce the same job.
        /// </summary>
        public static float WindowScale(int seed, float current)
        {
            int i = (int)(Hash(seed) % (uint)WindowScales.Length);
            if (System.Math.Abs(WindowScales[i] - current) < 0.001f) i = (i + 1) % WindowScales.Length;
            return WindowScales[i];
        }

        /// <summary>Knuth's multiplicative hash with a fold, so consecutive ids do not walk the table.</summary>
        private static uint Hash(int seed)
        {
            uint h = unchecked((uint)seed * 2654435761u);
            return h ^ (h >> 16);
        }

        /// <summary>
        /// Two significant digits — "50K", not "50,432". A contract is a headline the player reads in a
        /// second; the precision the meter produces would be noise on the card and noise in the bar.
        /// </summary>
        public static double RoundNice(double v)
        {
            if (v <= 0d) return 0d;
            if (v < 10d) return System.Math.Ceiling(v);
            double mag = System.Math.Pow(10d, System.Math.Floor(System.Math.Log10(v)) - 1d);
            return System.Math.Round(v / mag) * mag;
        }
    }
}
