using System;

namespace Game.Core
{
    /// <summary>
    /// The goal system as pure maths: what the game asks you to do today, what it asks you to do
    /// eventually, and how far along you are.
    ///
    /// WHY IT EXISTS. Contracts were the only goal in the game, and they are one ship at a time. A
    /// player opening the app had nothing telling them what to do next and nothing to finish — which
    /// is the retention hole underneath "there is nothing to chase". This adds the two layers the
    /// genre runs on: a short daily checklist, and a long permanent ladder.
    ///
    /// TWO DELIBERATE CHOICES:
    ///
    /// The daily three are picked from the DAY NUMBER, not from a random roll. Nothing about the
    /// selection is saved, two devices on the same date get the same tasks, and a test can ask what
    /// day 12,000 looks like. A saved RNG seed would buy nothing and would be one more thing to
    /// migrate.
    ///
    /// Daily metrics are all COUNT-based — upgrades bought, contracts finished, repairs made, foreman
    /// levels gained — and never money. Cash and bars inflate by 3.2x per ore tier, so a daily target
    /// in bars is either impossible on coal or free on diamond, and one that scaled with the player
    /// would be a second economy to balance. Counts mean the same thing on every island forever.
    /// Bars and cash still drive the ACHIEVEMENT ladder, where tiers absorb the inflation by design.
    /// </summary>
    public static class Goals
    {
        // Metric indices. Saves address these by number, so they must never be reordered.
        public const int BarsSold = 0, Upgrades = 1, Contracts = 2, Repairs = 3,
                         Islands = 4, ForemanLevels = 5;
        public const int MetricCount = 6;

        /// <summary>How many daily tasks are up at once.</summary>
        public const int DailySlots = 3;

        /// <summary>How many longer tasks feed the weekly milestone track.</summary>
        public const int WeeklySlots = 4;

        // ------------------------------------------------------------------ dailies
        public struct Task
        {
            public int Metric;
            public long Target;
            public long Gems;
            public int Cards;
        }

        /// <summary>
        /// The pool the day's three are drawn from. Count-based only — see the class summary. Targets
        /// are small on purpose: a daily list is a reason to open the app, not a second job.
        /// </summary>
        public static readonly Task[] DailyPool =
        {
            new Task { Metric = Upgrades,      Target = 5,  Gems = 25, Cards = 0 },
            new Task { Metric = Upgrades,      Target = 15, Gems = 45, Cards = 1 },
            new Task { Metric = Contracts,     Target = 1,  Gems = 40, Cards = 1 },
            new Task { Metric = Repairs,       Target = 2,  Gems = 30, Cards = 0 },
            new Task { Metric = ForemanLevels, Target = 1,  Gems = 35, Cards = 0 },
        };

        public struct WeeklyTask
        {
            public string Id;
            public int Metric;
            public long Target;
            public int Points;
        }

        /// <summary>
        /// Weekly tasks deliberately reuse the same count-based metrics as dailies. Their immutable
        /// IDs are persistence/analytics keys; array positions are only presentation order.
        /// </summary>
        public static readonly WeeklyTask[] WeeklyTasks =
        {
            new WeeklyTask { Id = "weekly_upgrades",  Metric = Upgrades,      Target = 50, Points = 25 },
            new WeeklyTask { Id = "weekly_contracts", Metric = Contracts,     Target = 7,  Points = 25 },
            new WeeklyTask { Id = "weekly_repairs",   Metric = Repairs,       Target = 12, Points = 25 },
            new WeeklyTask { Id = "weekly_foremen",   Metric = ForemanLevels, Target = 5,  Points = 25 },
        };

        public struct WeeklyMilestone
        {
            public string Id;
            public int Points;
            public long Gems;
            public int Cards;
        }

        /// <summary>Stable IDs make reordering the visible track safe for existing saves.</summary>
        public static readonly WeeklyMilestone[] WeeklyMilestones =
        {
            new WeeklyMilestone { Id = "weekly_25",  Points = 25,  Gems = 35,  Cards = 0 },
            new WeeklyMilestone { Id = "weekly_50",  Points = 50,  Gems = 60,  Cards = 1 },
            new WeeklyMilestone { Id = "weekly_75",  Points = 75,  Gems = 90,  Cards = 1 },
            new WeeklyMilestone { Id = "weekly_100", Points = 100, Gems = 150, Cards = 3 },
        };

        /// <summary>
        /// The task in <paramref name="slot"/> on <paramref name="dayNumber"/>. Deterministic: nothing
        /// about the selection is saved, and the same date gives the same three tasks everywhere.
        /// </summary>
        public static Task DailyTask(int dayNumber, int slot)
            => DailyPool[DailyIndex(dayNumber, slot)];

        /// <summary>
        /// Walks the pool from a scrambled start in a fixed stride.
        ///
        /// THE INVARIANT: the three slots must never land on the same task, and that holds here only
        /// because <see cref="DailyPool"/>'s length is PRIME — every stride in 1..n-1 is then coprime
        /// with n, so slots 0..n-1 are distinct. Growing the pool to a composite length silently
        /// breaks it (at length 6, stride 2 gives 0,2,4,0). DailyTasksAreAlwaysDistinct in the tests
        /// is what catches that; keep the pool prime, or replace this with a real shuffle.
        /// </summary>
        public static int DailyIndex(int dayNumber, int slot)
        {
            int n = DailyPool.Length;
            if (n <= 0) return 0;
            unchecked
            {
                int h = dayNumber * 73856093;
                h ^= h >> 13;
                h *= 19349663;
                h ^= h >> 16;
                int start = Mod(h, n);
                int stride = n > 1 ? 1 + Mod(dayNumber * 7 + 3, n - 1) : 0;   // 1..n-1, never 0
                return Mod(start + stride * slot, n);
            }
        }

        private static int Mod(int a, int m)
        {
            if (m <= 0) return 0;
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        // ------------------------------------------------------------- achievements
        public struct Achievement
        {
            public int Metric;
            public long[] Tiers;      // ascending thresholds
            public long GemsPerTier;  // paid per tier passed, multiplied by the tier number
            public int CardsPerTier;
        }

        /// <summary>
        /// The long ladder. Tiers absorb the ore-tier inflation the dailies deliberately avoid: a bar
        /// count that is a wall on coal is a formality on diamond, and that is the intended shape of a
        /// lifetime total.
        /// </summary>
        public static readonly Achievement[] Ladder =
        {
            new Achievement { Metric = BarsSold,      GemsPerTier = 20, CardsPerTier = 1,
                              Tiers = new[] { 100L, 1000L, 10000L, 100000L, 1000000L, 25000000L } },
            new Achievement { Metric = Upgrades,      GemsPerTier = 15, CardsPerTier = 1,
                              Tiers = new[] { 10L, 50L, 200L, 750L, 2500L, 8000L } },
            new Achievement { Metric = Contracts,     GemsPerTier = 25, CardsPerTier = 2,
                              Tiers = new[] { 1L, 10L, 50L, 150L, 400L, 1000L } },
            new Achievement { Metric = Repairs,       GemsPerTier = 15, CardsPerTier = 1,
                              Tiers = new[] { 5L, 25L, 100L, 300L, 800L, 2000L } },
            new Achievement { Metric = Islands,       GemsPerTier = 60, CardsPerTier = 3,
                              Tiers = new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L } },
            new Achievement { Metric = ForemanLevels, GemsPerTier = 30, CardsPerTier = 0,
                              Tiers = new[] { 1L, 8L, 25L, 50L, 80L } },
        };

        /// <summary>How many tiers of an achievement a given lifetime total has passed.</summary>
        public static int TiersReached(in Achievement a, long total)
        {
            if (a.Tiers == null) return 0;
            int n = 0;
            for (int i = 0; i < a.Tiers.Length; i++) if (total >= a.Tiers[i]) n++;
            return n;
        }

        /// <summary>The threshold the player is working toward, or 0 once the ladder is finished.</summary>
        public static long NextTier(in Achievement a, long total)
        {
            if (a.Tiers == null) return 0L;
            for (int i = 0; i < a.Tiers.Length; i++) if (total < a.Tiers[i]) return a.Tiers[i];
            return 0L;
        }

        /// <summary>What claiming tier <paramref name="tier"/> (1-based) pays.</summary>
        public static long TierGems(in Achievement a, int tier)
            => tier < 1 ? 0L : a.GemsPerTier * tier;

        public static int TierCards(in Achievement a, int tier)
            => tier < 1 ? 0 : a.CardsPerTier;

        // ------------------------------------------------------------------ progress
        /// <summary>
        /// Fraction of a target reached, clamped to 0..1. Guards a zero target so a misconfigured
        /// task reads as finished rather than dividing by nothing.
        /// </summary>
        public static float Progress(long have, long target)
        {
            if (target <= 0L) return 1f;
            if (have <= 0L) return 0f;
            if (have >= target) return 1f;
            return (float)(have / (double)target);
        }

        /// <summary>The UTC day number a timestamp falls on — what a daily reset is measured in.</summary>
        public static int DayNumber(long unixSeconds) => (int)Math.Floor(unixSeconds / 86400d);

        /// <summary>
        /// UTC week number with Monday as the boundary. This is timezone-independent and does not
        /// depend on locale or ISO calendar APIs.
        /// </summary>
        public static int WeekNumber(long unixSeconds)
            => (int)Math.Floor((unixSeconds / 86400d + 3d) / 7d);
    }
}
