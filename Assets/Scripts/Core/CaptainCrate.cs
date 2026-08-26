using System;

namespace Game.Core
{
    /// <summary>
    /// The crate: what a chart buys, and what stops a run of bad luck from lasting forever.
    ///
    /// WHY IT IS ITS OWN FILE. Docs/VOYAGES.md §16 deferred this deliberately — "Gacha rarity and
    /// pity… a separate feature with its own balance surface" — and the surface is the reason. A
    /// weight table is one number per grade and reads as harmless; the pity rules on top of it are
    /// what actually decide how the system feels, and they are the part that is easy to get wrong in
    /// a way nobody notices for a month. Keeping them here means every one of them is a function of
    /// its inputs and nothing else.
    ///
    /// THE ROLL IS AN ARGUMENT, NOT A CALL. Nothing in this file touches a random number generator.
    /// <see cref="RollGrade"/> takes a value in [0,1) and is a pure lookup, which is what lets the
    /// tests assert the whole distribution over ten thousand pulls rather than hoping. The service
    /// supplies the randomness — the same split <see cref="Voyages"/> uses for its risk roll.
    ///
    /// TWO PITIES, NOT ONE. A single counter is the obvious design and it gives a bad shape: set it
    /// short and legendaries stop being rare, set it long and a new player can pull thirty commons in
    /// a row and conclude the crate is broken. The short one guarantees an Epic often enough that a
    /// session always shows progress; the long one guarantees a Legendary rarely enough that it stays
    /// an event, with a soft ramp before it so the guarantee is usually beaten by a real roll.
    /// </summary>
    public static class CaptainCrate
    {
        public struct Tuning
        {
            /// <summary>Base weights. They need not sum to one — everything is normalised against the
            /// grades actually reachable, which is what keeps the table honest when
            /// <see cref="Captains.Roster"/> has nobody at a grade yet.</summary>
            public double CommonWeight, RareWeight, EpicWeight, LegendaryWeight, MythicWeight;

            /// <summary>Pulls without an Epic-or-better before the next one is guaranteed to be one.
            /// 0 turns the guarantee off.</summary>
            public int EpicPity;

            /// <summary>Pulls without a Legendary-or-better before the next one is guaranteed.</summary>
            public int LegendaryPity;

            /// <summary>Where the Legendary weight starts climbing, counted the same way.</summary>
            public int SoftPityStart;

            /// <summary>Weight added to Legendary for each pull past <see cref="SoftPityStart"/>.</summary>
            public double SoftPityStep;

            /// <summary>Charts for one crate.</summary>
            public long ChartCost;

            /// <summary>How many a bulk open buys, and what it costs — cheaper per crate on purpose.</summary>
            public int BulkCount;
            public long BulkChartCost;

            public static Tuning Default => new Tuning
            {
                // 60 / 26 / 10.5 / 3 / 0.5. A Mythic every two hundred pulls, before pity.
                CommonWeight    = 0.600d,
                RareWeight      = 0.260d,
                EpicWeight      = 0.105d,
                LegendaryWeight = 0.030d,
                MythicWeight    = 0.005d,

                // Ten is short enough that a bulk open always contains an Epic — which is the whole
                // reason the bulk open exists, and why it is the one most players will press.
                EpicPity = 10,

                // 3.5% a pull compounds to a Legendary around pull 30 on average, so 70 is a long way
                // past unlucky rather than a schedule the player can farm.
                LegendaryPity = 70,
                SoftPityStart = 45,
                SoftPityStep  = 0.010d,

                ChartCost     = 100L,
                BulkCount     = 10,
                BulkChartCost = 900L,
            };
        }

        // ------------------------------------------------------------------ weights
        private static double BaseWeight(int grade, in Tuning t)
        {
            switch (grade)
            {
                case (int)Captains.Grade.Common:    return t.CommonWeight;
                case (int)Captains.Grade.Rare:      return t.RareWeight;
                case (int)Captains.Grade.Epic:      return t.EpicWeight;
                case (int)Captains.Grade.Legendary: return t.LegendaryWeight;
                case (int)Captains.Grade.Mythic:    return t.MythicWeight;
                default:                            return 0d;
            }
        }

        /// <summary>
        /// A grade's weight for this pull. Zero for a grade nobody in the roster carries, which is what
        /// keeps the crate from rolling a rank it cannot then hand over — the alternative is a silent
        /// re-roll nobody can see in the odds.
        /// </summary>
        public static double WeightOf(int grade, int sinceLegendary, in Tuning t)
        {
            if (grade < 0 || grade >= Captains.GradeCount) return 0d;
            if (Captains.CountOfGrade((Captains.Grade)grade) <= 0) return 0d;

            double w = BaseWeight(grade, t);
            if (w < 0d) w = 0d;
            if (grade == (int)Captains.Grade.Legendary) w += SoftPityBonus(sinceLegendary, t);
            return w;
        }

        /// <summary>Extra Legendary weight earned by a dry run. Zero until the ramp starts.</summary>
        public static double SoftPityBonus(int sinceLegendary, in Tuning t)
        {
            if (t.SoftPityStart <= 0 || t.SoftPityStep <= 0d) return 0d;
            int past = sinceLegendary + 1 - t.SoftPityStart;
            return past <= 0 ? 0d : t.SoftPityStep * past;
        }

        /// <summary>
        /// The lowest grade this pull may come out as. <see cref="Captains.Grade.Common"/> normally;
        /// higher when a pity has come due.
        /// </summary>
        public static Captains.Grade Floor(int sinceEpic, int sinceLegendary, in Tuning t)
        {
            if (t.LegendaryPity > 0 && sinceLegendary + 1 >= t.LegendaryPity) return Captains.Grade.Legendary;
            if (t.EpicPity > 0 && sinceEpic + 1 >= t.EpicPity) return Captains.Grade.Epic;
            return Captains.Grade.Common;
        }

        // -------------------------------------------------------------------- roll
        /// <summary>
        /// What grade a pull comes out as. <paramref name="roll"/> is in [0,1) and is the only source
        /// of chance in the whole system.
        /// </summary>
        public static Captains.Grade RollGrade(double roll, int sinceEpic, int sinceLegendary, in Tuning t)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;

            int floor = (int)Floor(sinceEpic, sinceLegendary, t);

            double total = 0d;
            for (int g = floor; g < Captains.GradeCount; g++) total += WeightOf(g, sinceLegendary, t);

            // Every grade at or above the floor is empty — the roster has nobody that rare yet. Fall
            // back down the ladder rather than returning a grade that cannot be handed over.
            if (total <= 0d)
            {
                for (int g = Captains.GradeCount - 1; g >= 0; g--)
                    if (Captains.CountOfGrade((Captains.Grade)g) > 0) return (Captains.Grade)g;
                return Captains.Grade.Common;
            }

            double target = roll * total;
            double acc = 0d;
            for (int g = floor; g < Captains.GradeCount; g++)
            {
                acc += WeightOf(g, sinceLegendary, t);
                if (target < acc) return (Captains.Grade)g;
            }
            return (Captains.Grade)(Captains.GradeCount - 1);
        }

        /// <summary>
        /// Which captain a pull hands over. Grade first, then flat among everyone who carries it —
        /// two rolls rather than one weight per captain, so adding a captain changes who you might get
        /// without changing how often a Legendary appears.
        /// </summary>
        public static int RollCaptain(double gradeRoll, double memberRoll,
                                      int sinceEpic, int sinceLegendary, in Tuning t)
        {
            Captains.Grade grade = RollGrade(gradeRoll, sinceEpic, sinceLegendary, t);
            int n = Captains.CountOfGrade(grade);
            if (n <= 0) return -1;

            if (memberRoll < 0d) memberRoll = 0d;
            if (memberRoll >= 1d) memberRoll = 0.9999999999d;
            int nth = (int)(memberRoll * n);
            if (nth >= n) nth = n - 1;
            return Captains.OfGrade(grade, nth);
        }

        /// <summary>
        /// Moves the two dry counters on by one pull. A pull at or above a grade clears that grade's
        /// counter; anything else lengthens it. Legendary clears both, because a Legendary is also an
        /// Epic-or-better and leaving the short counter running would owe the player a second
        /// guarantee they have just been paid.
        /// </summary>
        public static void Advance(Captains.Grade got, ref int sinceEpic, ref int sinceLegendary)
        {
            if (got >= Captains.Grade.Legendary) { sinceEpic = 0; sinceLegendary = 0; return; }
            if (got >= Captains.Grade.Epic) { sinceEpic = 0; sinceLegendary++; return; }
            sinceEpic++;
            sinceLegendary++;
        }

        // -------------------------------------------------------------------- cost
        /// <summary>Charts for <paramref name="crates"/> opened at once. The bulk count is the only
        /// discounted size; anything else is priced one at a time.</summary>
        public static long Cost(int crates, in Tuning t)
        {
            if (crates <= 0) return 0L;
            long single = Math.Max(0L, t.ChartCost);
            if (t.BulkCount > 0 && crates == t.BulkCount) return Math.Max(0L, t.BulkChartCost);
            return single * crates;
        }
    }
}
