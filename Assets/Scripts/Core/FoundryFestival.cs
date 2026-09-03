using System;

namespace Game.Core
{
    /// <summary>
    /// The Foundry Festival: seven days of tasks feeding five milestone chests, inside one live-event
    /// window. The first module to sit on <see cref="LiveEvents"/>, and the shape every later one is
    /// meant to copy.
    ///
    /// WHAT IT ADDS TO THE SAVE: nothing. Not one field. The lifecycle layer already gives an event a
    /// row of <c>progress[]</c> and <c>claimed[]</c> longs and flags, and everything below is a way of
    /// spending those two arrays — which is why this whole feature lands without touching one line of
    /// <c>SaveMigration</c>. The slot map is the entire persistence design.
    ///
    /// THE TASKS ARE NOT NEW COUNTERS. Every target is a metric <see cref="Goals"/> already tallies
    /// from six call sites in the simulation, so the festival reads work the game was counting anyway
    /// and adds no hook to any gameplay file. That is also why the cursor slots exist: the festival has
    /// to turn a LIFETIME total into a delta earned inside its own window, and the cursor is where the
    /// "how far had they got when we last looked" lives. Seeded as <c>lifetime + 1</c> so a genuine
    /// zero total is distinguishable from a cursor nobody has written yet — without that one bit, a
    /// brand-new player's first five upgrades would be counted twice and a veteran's four thousand
    /// would clear day one on sight.
    ///
    /// COUNT METRICS ONLY, and for the reason <see cref="Goals"/> already spells out: output inflates
    /// x3.2 per ore tier, so a bars-sold target is either impossible on coal or free on diamond. A
    /// seven-day event has exactly the same problem as a daily, so it takes the same answer.
    ///
    /// POINTS ARE DERIVED, NEVER STORED. A chest is earned on the points of every FINISHED task, not
    /// every claimed one — the chest measures the work, not the tapping, so a player who never opens
    /// the screen until the last day still walks away with what they earned. Deriving it also means
    /// there is no counter that can drift, be paid twice, or need its own migration.
    ///
    /// The clock is an argument here, as everywhere in Core. The service supplies it.
    /// </summary>
    public static class FoundryFestival
    {
        /// <summary>The <c>Kind</c> a <see cref="LiveEvents.Definition"/> carries to say "this window
        /// is a festival". 0 stays the schedule-only kind that owns no content.</summary>
        public const int Kind = 1;

        public const int Days = 7;
        public const int TasksPerDay = 3;
        public const int TaskCount = Days * TasksPerDay;      // 21
        public const int MilestoneCount = 5;

        /// <summary>
        /// What the config row must give the event. Tasks, then chests, then one cursor per goal
        /// metric: 21 + 5 + 6 = 32, inside <see cref="LiveEvents.MaxSlots"/>. A row authored with
        /// fewer is refused by the service rather than silently indexing past the end.
        /// </summary>
        public const int Slots = TaskCount + MilestoneCount + Goals.MetricCount;

        public static int TaskSlot(int day, int index) => day * TasksPerDay + index;
        public static int MilestoneSlot(int index) => TaskCount + index;
        public static int CursorSlot(int metric) => TaskCount + MilestoneCount + metric;

        /// <summary>Which day a task slot belongs to — what decides whether it has unlocked.</summary>
        public static int DayOf(int taskSlot) => taskSlot / TasksPerDay;

        // ------------------------------------------------------------------- days
        /// <summary>
        /// Which festival day <paramref name="nowUnix"/> falls on, 0-based and clamped to the last.
        ///
        /// Measured from the event's own start rather than from midnight UTC, because the window is
        /// authored as a start plus a length and need not begin at midnight. Clamped rather than
        /// wrapped: a window longer than seven days keeps day seven open to its end, which is a
        /// scheduling mistake that costs the player nothing.
        /// </summary>
        public static int DayIndex(long startUnix, long nowUnix)
        {
            if (nowUnix <= startUnix) return 0;
            long day = (nowUnix - startUnix) / 86400L;
            if (day < 0L) return 0;
            return day >= Days ? Days - 1 : (int)day;
        }

        /// <summary>Seconds until the next day unlocks; 0 on the last day and 0 before the start —
        /// the caller knows which it is from <see cref="LiveEvents.PhaseAt"/>.</summary>
        public static long SecondsToNextDay(long startUnix, long nowUnix)
        {
            if (nowUnix < startUnix) return 0L;
            int day = DayIndex(startUnix, nowUnix);
            if (day >= Days - 1) return 0L;
            long next = startUnix + (day + 1L) * 86400L;
            long left = next - nowUnix;
            return left > 0L ? left : 0L;
        }

        // ------------------------------------------------------------------ shape
        /// <summary>One task row. Rewards ride along with the target because they are the same
        /// decision — what the player is asked for and what it is worth.</summary>
        public struct Task
        {
            /// <summary>A <see cref="Goals"/> metric index. Count metrics only.</summary>
            public int Metric;

            /// <summary>How much of that metric, counted from the moment the task's day unlocked.</summary>
            public long Target;

            /// <summary>What finishing it contributes toward the chests.</summary>
            public int Points;

            public long Gems;
            public int Cards;
        }

        /// <summary>
        /// One milestone chest. Pays wider than a task does — charts and a boost as well as gems and
        /// cards — because it is the thing the week is actually chased for.
        /// </summary>
        public struct Milestone
        {
            /// <summary>Cumulative points that open it.</summary>
            public int Points;

            public long Gems;
            public int Cards;
            public long Charts;

            /// <summary>A temporary income boost, handed to <c>BoostService.AddBoost</c> so it stacks
            /// with whatever is already running instead of replacing it. 0 mult = no boost.</summary>
            public double BoostMult;
            public double BoostSeconds;
        }

        /// <summary>
        /// The whole festival's balance. <c>FoundryFestivalConfig</c> is an authoring surface over
        /// this; the numbers below are what ships when no asset is wired.
        /// </summary>
        public struct Tuning
        {
            public Task[] Tasks;              // TaskCount long, in day order
            public Milestone[] Milestones;    // MilestoneCount long, ascending by Points

            public static Tuning Default => new Tuning
            {
                // Targets climb across the week and the metrics rotate, so no single day is the same
                // errand three times. Upgrades appear every day because they are the one thing a
                // player can always do; contracts and repairs pace themselves; a foreman star-up shows
                // up three times in seven days, which is roughly what the free chests pay for.
                //
                // 735 gems over 21 tasks is deliberately one week of dailies (3 x ~35 x 7) — the
                // festival's tasks are a second checklist of the same weight, and the chests below are
                // what makes the week worth more than an ordinary one.
                Tasks = new[]
                {
                    // day 1
                    new Task { Metric = Goals.Upgrades,      Target = 5,  Points = 10, Gems = 20 },
                    new Task { Metric = Goals.Contracts,     Target = 1,  Points = 10, Gems = 20 },
                    new Task { Metric = Goals.Repairs,       Target = 2,  Points = 10, Gems = 20 },
                    // day 2
                    new Task { Metric = Goals.Upgrades,      Target = 8,  Points = 10, Gems = 25 },
                    new Task { Metric = Goals.Contracts,     Target = 1,  Points = 10, Gems = 25 },
                    new Task { Metric = Goals.ForemanLevels, Target = 1,  Points = 15, Gems = 25, Cards = 1 },
                    // day 3
                    new Task { Metric = Goals.Upgrades,      Target = 10, Points = 10, Gems = 30 },
                    new Task { Metric = Goals.Repairs,       Target = 3,  Points = 10, Gems = 30 },
                    new Task { Metric = Goals.Contracts,     Target = 2,  Points = 15, Gems = 30, Cards = 1 },
                    // day 4
                    new Task { Metric = Goals.Upgrades,      Target = 12, Points = 15, Gems = 35 },
                    new Task { Metric = Goals.ForemanLevels, Target = 1,  Points = 15, Gems = 35, Cards = 1 },
                    new Task { Metric = Goals.Contracts,     Target = 2,  Points = 15, Gems = 35 },
                    // day 5
                    new Task { Metric = Goals.Upgrades,      Target = 15, Points = 15, Gems = 40 },
                    new Task { Metric = Goals.Repairs,       Target = 4,  Points = 15, Gems = 40 },
                    new Task { Metric = Goals.Contracts,     Target = 2,  Points = 15, Gems = 40, Cards = 1 },
                    // day 6
                    new Task { Metric = Goals.Upgrades,      Target = 18, Points = 15, Gems = 45 },
                    new Task { Metric = Goals.Contracts,     Target = 3,  Points = 20, Gems = 45 },
                    new Task { Metric = Goals.ForemanLevels, Target = 2,  Points = 20, Gems = 45, Cards = 1 },
                    // day 7
                    new Task { Metric = Goals.Upgrades,      Target = 20, Points = 20, Gems = 50 },
                    new Task { Metric = Goals.Repairs,       Target = 5,  Points = 20, Gems = 50 },
                    new Task { Metric = Goals.Contracts,     Target = 3,  Points = 20, Gems = 50, Cards = 2 },
                },

                // 305 points are on the table and the last chest opens at 260, so a day and a half can
                // be missed and the week still finishes. A final chest priced at everything would mean
                // one skipped repair costs the headline prize, which is how a live event teaches
                // players that live events are not worth starting.
                //
                // Charts rather than gems in the third and fifth: 100 charts is one captain crate, so
                // the festival pays a crate and most of another into the collection that only ever
                // affects sailing — a closed loop, exactly as Voyages.ChartRate intends.
                Milestones = new[]
                {
                    new Milestone { Points = 40,  Gems = 60,  Cards = 2 },
                    new Milestone { Points = 95,  Gems = 90,  Cards = 3 },
                    new Milestone { Points = 155, Gems = 120, Charts = 60 },
                    new Milestone { Points = 210, Gems = 150, Cards = 4, BoostMult = 2d, BoostSeconds = 1800d },
                    new Milestone { Points = 260, Gems = 250, Cards = 6, Charts = 120 },
                },
            };
        }

        /// <summary>
        /// Whether a tuning can be run at all. Checked on load rather than trusted, the same contract
        /// <see cref="LiveEvents.IsWellFormed"/> keeps with a schedule row: every mistake below is
        /// silent at runtime, and the loudest of them — a chest priced above every point in the week —
        /// is a prize the player chases all seven days and never gets.
        /// </summary>
        public static bool IsWellFormed(in Tuning t)
        {
            if (t.Tasks == null || t.Tasks.Length != TaskCount) return false;
            if (t.Milestones == null || t.Milestones.Length != MilestoneCount) return false;

            for (int i = 0; i < t.Tasks.Length; i++)
            {
                if (t.Tasks[i].Metric < 0 || t.Tasks[i].Metric >= Goals.MetricCount) return false;
                if (t.Tasks[i].Target <= 0L) return false;
                if (t.Tasks[i].Points < 0) return false;
            }

            int previous = 0;
            for (int i = 0; i < t.Milestones.Length; i++)
            {
                if (t.Milestones[i].Points <= previous) return false;   // ascending, and none free
                previous = t.Milestones[i].Points;
            }

            return previous <= TotalPoints(t);
        }

        /// <summary>Every point the week can pay — what the last chest must sit under.</summary>
        public static int TotalPoints(in Tuning t)
        {
            if (t.Tasks == null) return 0;
            int sum = 0;
            for (int i = 0; i < t.Tasks.Length; i++) sum += t.Tasks[i].Points;
            return sum;
        }

        /// <summary>How many chests <paramref name="points"/> has opened.</summary>
        public static int MilestonesEarned(in Tuning t, int points)
        {
            if (t.Milestones == null) return 0;
            int n = 0;
            for (int i = 0; i < t.Milestones.Length; i++) if (points >= t.Milestones[i].Points) n++;
            return n;
        }

        /// <summary>The next chest's price, or 0 once they are all open — the number the bar counts
        /// toward.</summary>
        public static int NextMilestonePoints(in Tuning t, int points)
        {
            if (t.Milestones == null) return 0;
            for (int i = 0; i < t.Milestones.Length; i++)
                if (points < t.Milestones[i].Points) return t.Milestones[i].Points;
            return 0;
        }
    }
}
