using System;

namespace Game.Core
{
    /// <summary>
    /// What an island actually earns, and how long the ladder therefore takes.
    ///
    /// Income cannot be derived: it comes out of trains and trucks driving real routes and
    /// stalling on each other. So it is MEASURED — the table below is a recording of the
    /// coal island at eleven points along a balanced build, taken with the editor probe
    /// (Kayseri/Economy). Everything else here is exact arithmetic on top of it.
    ///
    /// That split is the point. The one squishy input is a table of numbers with a date on
    /// it, and the pacing consequences are then computed rather than guessed. Re-run the
    /// probe after changing any rate and paste the new table in.
    /// </summary>
    public static class EconomyCurve
    {
        /// <summary>
        /// Coal island, $/min at a uniform level across every axis, no ghost buildings.
        /// Measured 2026-08-04, EconomyProbe at x8 with a 150-second settle.
        ///
        /// The settle is why these differ from a first pass that read 540 at level 0:
        /// CoalOperation.WarmStart pre-fills the yards, and until that buffer drains the
        /// island sells faster than it can produce. Level 0 was 50% high, and the bias
        /// tapered as levels rose, so it tilted the whole curve rather than just offsetting
        /// it. Cross-checked against the live meter left running for three minutes: 360.
        ///
        /// Individual points carry about +/-10%: a truck sells every ~25 s and the game's
        /// own meter averages 60 s, so a single reading covers barely two cycles. The shape
        /// is sound; do not read meaning into one sample moving.
        /// </summary>
        public static readonly int[] SampleLevel = { 0, 1, 2, 3, 5, 8, 12, 18, 25, 35, 50 };
        public static readonly double[] SamplePerMin =
            { 360, 1174, 1911, 2758, 3114, 1813, 4332, 7984, 17718, 35296, 88037 };

        /// <summary>
        /// KNOWN DEFECT, and it is in the numbers above rather than hidden: level 8 measures
        /// 1813 against level 5's 3114. Buying upgrades around there makes the player POORER
        /// for a while. It reproduces across runs and across fleet caps, so it is not noise,
        /// and it shares a root cause with the truck-count cliff described in
        /// IslandEconomy.MaxLevel — the haulage fleet destabilises throughput at certain
        /// sizes. The curve is recorded as measured rather than smoothed, because a balance
        /// solved against invented numbers would be worse than one solved against ugly ones.
        /// </summary>
        public const int DipLevel = 8;

        /// <summary>
        /// What all ten ghost buildings are worth, measured at level 50: 88037 -> 102626.
        /// 17%, up from 3% before the haulage fleet cap was raised — with cargo capped at
        /// three the chain could not move what it produced, so every building that added a
        /// mine, a smelter or a train was pushing on a stage that was not the bottleneck.
        /// </summary>
        public const double UnlockFactor = 102626d / 88037d;

        /// <summary>What a maxed, fully-built coal island earns — the ladder keys off this.</summary>
        public const double MaxedCoalPerMin = 102626d;

        // ─────────────────────────────────────────────────────────────────────────────
        //  PACING TARGETS
        //
        //  Everything below is in INCOME-HOURS: hours of earning at the island's full
        //  rate. That is the unit the simulation produces, and it is NOT wall-clock —
        //  an idle game earns while it is closed, so a player collects far more than
        //  the time they spend looking at it.
        //
        //  The conversion is the whole reason to be careful here. A player who opens
        //  the app twice a day collects, per real day:
        //
        //      overnight gap   min(10 h, cap) x efficiency
        //      daytime gap     min(6 h, cap) x efficiency
        //      active play     ~40 min at full rate
        //
        //  At OfflineConfig's 8 h cap and 35% efficiency that is 2.8 + 2.1 + 0.7 = 5.6.
        //  Offline generosity is therefore a pacing dial as strong as any cost curve,
        //  which is why it is quoted here rather than buried in OfflineConfig.
        //
        //  THIS NUMBER IS THE NO-ADS BASELINE, and the gap matters. A player who takes
        //  every rewarded ad collects 11.5 instead: the welcome-back doubler pays the
        //  whole offline grant a second time on each return (+4.9), the cash slot adds
        //  15 min x 3 charges (+0.75) and the x2 boost slot 5 min x 3 (+0.25). So the
        //  ladder finishes in 40 real days for one player and 19 for the other, off the
        //  same 222 income-hours. Efficiency was cut from 50% to 35% on 2026-08-07 to
        //  narrow that spread — at 50% it was 7.7 against 15.7, a clean 2x.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Income-hours a twice-a-day player collects, at the 8 h / 35% offline cap.</summary>
        public const double IncomeHoursPerDay = 5.6d;

        /// <summary>
        /// Real days each island should take: a fast onboarding ramp settling to a week,
        /// so a daily player stays level with the weekly island drop forever.
        /// </summary>
        private static readonly double[] TargetDays = { 0.4, 0.9, 1.7, 2.7, 4.0, 5.5, 6.5, 7.0 };

        /// <summary>Real days island <paramref name="n"/> should take; a week from the eighth on.</summary>
        public static double TargetElapsedDays(int n)
            => n < TargetDays.Length ? TargetDays[n] : TargetDays[TargetDays.Length - 1];

        /// <summary>The same target as the simulation measures it.</summary>
        public static double TargetIncomeHours(int n) => TargetElapsedDays(n) * IncomeHoursPerDay;

        /// <summary>Coal's $/min at a uniform build level, interpolated between samples.</summary>
        public static double CoalPerMinute(double level, bool unlocksBought = false)
        {
            double v;
            if (level <= SampleLevel[0]) v = SamplePerMin[0];
            else if (level >= SampleLevel[SampleLevel.Length - 1]) v = SamplePerMin[SamplePerMin.Length - 1];
            else
            {
                int i = 0;
                while (i < SampleLevel.Length - 2 && SampleLevel[i + 1] < level) i++;
                double t = (level - SampleLevel[i]) / (double)(SampleLevel[i + 1] - SampleLevel[i]);
                v = SamplePerMin[i] + (SamplePerMin[i + 1] - SamplePerMin[i]) * t;
            }
            return unlocksBought ? v * UnlockFactor : v;
        }

        /// <summary>
        /// One island's run: how long to take it from bought to fully upgraded, and what it
        /// earns along the way. <paramref name="backgroundPerMin"/> is what the islands
        /// already owned keep paying while this one is being built.
        /// </summary>
        public struct Run
        {
            public double HoursToMax;          // from level 0 to every axis capped
            public double HoursToNextUnlock;   // until the NEXT island is affordable
            public double SpentToMax;
            public double FinalPerMin;         // fully upgraded
            /// <summary>
            /// What it earns at the moment the player moves on — and therefore what it
            /// keeps paying forever after. Only the ACTIVE island simulates; every other
            /// one is frozen at its last measured rate (see WorldIslands.SavedRate), so an
            /// island left half-built stays half-built until the player comes back to it.
            /// Using the MAXED rate for background income instead would quietly assume a
            /// player who finishes everything before moving on, which the pacing this
            /// ladder is tuned for specifically does not ask them to do.
            /// </summary>
            public double PerMinAtGate;
            public bool ReachedNextUnlock;
        }

        /// <summary>
        /// Walks a balanced build up the track one level at a time, spending what it earns.
        ///
        /// A level costs what <see cref="IslandEconomy"/> says it costs, and pays what the
        /// measured curve says it pays, scaled by the island's tier. That makes the answer
        /// only as good as the recording — which is the honest state of affairs, and much
        /// better than the alternative of a formula nobody has checked against the game.
        /// </summary>
        public static Run Simulate(IslandEconomy econ, double valueMultiplier,
                                   double nextUnlockCost, double backgroundPerMin,
                                   double unlockBuildingsCost)
        {
            var run = new Run();
            double cash = 0d, minutes = 0d, spent = 0d;
            bool unlocked = false;
            int cap = econ.Config.AxisLevelCap;
            int level = 0;

            void Buy(double price)
            {
                double rate = PerMin(level, unlocked, valueMultiplier) + backgroundPerMin;
                double wait = cash >= price || rate <= 0d ? 0d : (price - cash) / rate;
                minutes += wait;
                cash += rate * wait - price;
                if (cash < 0d) cash = 0d;
                spent += price;
            }

            for (level = 0; level < cap; level++)
            {
                // Buy one level on every axis that has not capped yet.
                for (int s = 0; s < IslandEconomy.Axes.Length; s++)
                    for (int a = 0; a < IslandEconomy.Axes[s].Length; a++)
                    {
                        if (econ.AxisMaxed(s, a)) continue;
                        Buy(econ.AxisCost(s, a));
                        econ.Levels[s][a]++;
                    }

                // The ghost buildings are bought as a block a third of the way up, which is
                // roughly when a player can afford them.
                if (!unlocked && level >= cap / 3)
                {
                    Buy(unlockBuildingsCost);
                    unlocked = true;
                }

                // The guard on zero matters: the newest island on the ladder has no next
                // island to pay for, and without it the gate would trip on the first
                // purchase and report the island as "left earning" its level-1 rate.
                if (!run.ReachedNextUnlock && nextUnlockCost > 0d && spent >= nextUnlockCost)
                {
                    run.ReachedNextUnlock = true;
                    run.HoursToNextUnlock = minutes / 60d;
                    run.PerMinAtGate = PerMin(level + 1, unlocked, valueMultiplier);
                }
            }

            run.HoursToMax = minutes / 60d;
            run.SpentToMax = spent;
            run.FinalPerMin = PerMin(cap, unlocked, valueMultiplier);
            if (!run.ReachedNextUnlock)
            {
                // Maxed out and still short of the next island: keep earning at the ceiling.
                double rate = run.FinalPerMin + backgroundPerMin;
                run.HoursToNextUnlock = (minutes + (nextUnlockCost - spent) / rate) / 60d;
                run.ReachedNextUnlock = nextUnlockCost > 0d;
                run.PerMinAtGate = run.FinalPerMin;
            }
            return run;
        }

        private static double PerMin(double level, bool unlocked, double valueMultiplier)
            => CoalPerMinute(level, unlocked) * valueMultiplier;

        /// <summary>Minutes of earning needed to afford <paramref name="price"/>.</summary>
        private static double Wait(double cash, double price, double perMin)
            => cash >= price || perMin <= 0d ? 0d : (price - cash) / perMin;
    }
}
