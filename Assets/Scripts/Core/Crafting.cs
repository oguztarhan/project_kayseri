using System;

namespace Game.Core
{
    /// <summary>
    /// The workshop bench: what one craft point buys, and how the bench itself grows.
    ///
    /// WHY IT IS ITS OWN FILE. Docs/FIVE_LAYERS.md stage 4 named "craft, rarity grades" a feature
    /// with its own balance surface, and this is that surface: a per-level odds table, an XP curve,
    /// and the retooling gates. Every one of them is a function of its inputs and nothing else, so
    /// the whole ladder can be asserted by a test instead of hoped about.
    ///
    /// THE ROLL IS AN ARGUMENT, NOT A CALL. Nothing here touches a random number generator —
    /// <see cref="RollGrade"/> takes a value in [0,1) and is a pure lookup, the same contract
    /// <see cref="CaptainCrate.RollGrade"/> and <see cref="SeaCombat"/> keep. The service supplies
    /// the dice.
    ///
    /// LEVEL-GATED, NOT PITY-GATED. The crate's question is "how long can bad luck last"; the
    /// bench's question is "how early can good luck land". So the odds are a function of the
    /// workshop level alone: the first bracket rolls Common and Rare only, Epic opens at 6,
    /// Legendary not before 16, Mythic not before 26 — a fresh account cannot craft its way to a
    /// Legendary no matter how many points it holds.
    ///
    /// EVERY 10TH LEVEL IS A RETOOLING STOP. Reaching 10, 20 and 30 parks the level behind a
    /// wall-clock cooldown; crafting and salvaging carry on and the XP banks, but level-ups wait.
    /// Clearing a stop also raises the stat BUDGET of everything crafted after it — the gates hand
    /// out <see cref="SeaCombat.SlotHull"/>'s next tier column, so they pace raw power as well as
    /// rarity. Both brakes on "Legendary at breakfast" live here, on purpose, together.
    /// </summary>
    public static class Crafting
    {
        public const int MaxLevel = 30;
        public const int BracketSize = 5;
        public const int GateEvery = 10;

        /// <summary>How many retooling stops the ladder has — one per 10th level, 30 included:
        /// the last stop is what buys the tier-3 budget at the top of the ladder.</summary>
        public const int GateCount = MaxLevel / GateEvery;

        /// <summary>
        /// Grade weights per level bracket, columns in <see cref="Captains.Grade"/> order. Rows are
        /// five levels each and each row sums to 1 for the panel's benefit, but <see cref="RollGrade"/>
        /// normalises anyway so a retouched cell cannot skew its neighbours. A zero is a grade that
        /// bracket CANNOT roll — that is the "no Legendary at early levels" rule, as data.
        /// Append-only by convention, like every table a save leans on.
        /// </summary>
        public static readonly double[][] LevelOdds =
        {
            new[] { 0.82d, 0.18d, 0.00d, 0.00d, 0.00d },   // 1–5   the bench learns its shapes
            new[] { 0.68d, 0.28d, 0.04d, 0.00d, 0.00d },   // 6–10  Epic opens, barely
            new[] { 0.56d, 0.33d, 0.11d, 0.00d, 0.00d },   // 11–15
            new[] { 0.46d, 0.36d, 0.16d, 0.02d, 0.00d },   // 16–20 Legendary opens, after gate 1
            new[] { 0.38d, 0.37d, 0.20d, 0.05d, 0.00d },   // 21–25
            new[] { 0.30d, 0.38d, 0.23d, 0.08d, 0.01d },   // 26–30 Mythic stays an event
        };

        /// <summary>
        /// XP a scrapped item teaches, by grade. Deliberately steeper than
        /// <see cref="SeaCombat.ScrapSalvage"/>'s hurda ladder: hurda is the consolation, the
        /// LESSON is why you would ever feed the bench a Legendary.
        /// </summary>
        public static readonly long[] SalvageXp = { 6L, 18L, 45L, 110L, 250L };

        /// <summary>XP one level-up costs, flat within each bracket of five. ~4 salvaged crafts a
        /// level at the bottom, ~15 at the top — roughly 280 crafts bench-to-max if everything is
        /// fed back, before sea scraps shorten it.</summary>
        public static readonly long[] XpPerLevel = { 30L, 70L, 130L, 210L, 320L, 460L };

        // ------------------------------------------------------------------ tuning
        public struct Tuning
        {
            /// <summary>Points one craft costs.</summary>
            public long CraftCost;

            /// <summary>The retooling stops at levels 10, 20 and 30, in hours of wall clock.</summary>
            public double Gate1Hours, Gate2Hours, Gate3Hours;

            /// <summary>A won sea encounter's chance of dropping points, and how many it drops.</summary>
            public double PointDropChance;
            public int PointsPerWin;

            /// <summary>Points a claimed voyage pays, flat — a drop, never a rate.</summary>
            public int PointsPerVoyage;

            public static Tuning Default => new Tuning
            {
                CraftCost = 1L,

                // Short enough to be "later today", long enough that 16 — the first Legendary
                // odds — is never the same sitting the account was made in.
                Gate1Hours = 6d,
                Gate2Hours = 12d,
                Gate3Hours = 24d,

                // ~6 points per full energy pool, +2 per voyage claimed: a heavy day is 10–15
                // points, which prices the 280-craft ladder in weeks rather than sittings.
                PointDropChance = 0.20d,
                PointsPerWin = 1,
                PointsPerVoyage = 2,
            };
        }

        // ------------------------------------------------------------------- odds
        /// <summary>Which odds row a level rolls from. Levels clamp into 1..MaxLevel.</summary>
        public static int BracketOf(int level)
        {
            if (level < 1) level = 1;
            if (level > MaxLevel) level = MaxLevel;
            return (level - 1) / BracketSize;
        }

        /// <summary>
        /// A grade's normalised share at this level — the number the panel prints, and exactly what
        /// <see cref="RollGrade"/> rolls against. 0 for a grade the bracket cannot make.
        /// </summary>
        public static double OddsOf(int level, int grade)
        {
            if (grade < 0 || grade >= Captains.GradeCount) return 0d;
            double[] row = LevelOdds[BracketOf(level)];
            double total = 0d;
            for (int g = 0; g < row.Length; g++) total += row[g] > 0d ? row[g] : 0d;
            if (total <= 0d) return grade == 0 ? 1d : 0d;
            double w = row[grade] > 0d ? row[grade] : 0d;
            return w / total;
        }

        /// <summary>The level a grade first becomes craftable at, or 0 when no bracket carries it —
        /// what the panel's locked rows print.</summary>
        public static int UnlockLevelOf(int grade)
        {
            if (grade < 0 || grade >= Captains.GradeCount) return 0;
            for (int b = 0; b < LevelOdds.Length; b++)
                if (grade < LevelOdds[b].Length && LevelOdds[b][grade] > 0d)
                    return b * BracketSize + 1;
            return 0;
        }

        /// <summary>
        /// What grade one craft comes out as. <paramref name="roll"/> is in [0,1) and is the only
        /// chance in the whole feature.
        /// </summary>
        public static int RollGrade(double roll, int level)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;

            double[] row = LevelOdds[BracketOf(level)];
            double total = 0d;
            for (int g = 0; g < row.Length; g++) total += row[g] > 0d ? row[g] : 0d;
            if (total <= 0d) return 0;

            double target = roll * total;
            double acc = 0d;
            for (int g = 0; g < row.Length; g++)
            {
                if (row[g] <= 0d) continue;
                acc += row[g];
                if (target < acc) return g;
            }
            return 0;
        }

        // --------------------------------------------------------------------- xp
        /// <summary>XP the NEXT level-up costs from this level. 0 at the top of the ladder.</summary>
        public static long XpToNext(int level)
        {
            if (level >= MaxLevel) return 0L;
            return XpPerLevel[BracketOf(level)];
        }

        /// <summary>Total XP spent reaching this level from level 1.</summary>
        public static long XpForLevel(int level)
        {
            if (level < 1) level = 1;
            if (level > MaxLevel) level = MaxLevel;
            long total = 0L;
            for (int l = 1; l < level; l++) total += XpPerLevel[BracketOf(l)];
            return total;
        }

        /// <summary>The level this much lifetime XP has earned, gates ignored. Never above MaxLevel.</summary>
        public static int LevelForXp(long xp)
        {
            int level = 1;
            while (level < MaxLevel)
            {
                long cost = XpPerLevel[BracketOf(level)];
                if (xp < cost) break;
                xp -= cost;
                level++;
            }
            return level;
        }

        /// <summary>XP sitting above <paramref name="level"/>'s floor — the bar's fill, and while a
        /// gate holds the level down, the banked surplus too. The caller clamps for display.</summary>
        public static long XpIntoLevel(long xp, int level)
        {
            long into = xp - XpForLevel(level);
            return into < 0L ? 0L : into;
        }

        // ------------------------------------------------------------------ gates
        /// <summary>The highest level this many cleared stops allows.</summary>
        public static int CapForGates(int gatesCleared)
        {
            if (gatesCleared < 0) gatesCleared = 0;
            long cap = (long)(gatesCleared + 1) * GateEvery;
            return cap >= MaxLevel ? MaxLevel : (int)cap;
        }

        /// <summary>The bench's level right now: what the XP has earned, held down by the stops.</summary>
        public static int LevelAt(long xp, int gatesCleared)
        {
            int earned = LevelForXp(xp);
            int cap = CapForGates(gatesCleared);
            return earned < cap ? earned : cap;
        }

        /// <summary>Whether the XP has hit the next stop — the moment the retooling clock starts.</summary>
        public static bool AtGate(long xp, int gatesCleared)
        {
            if (gatesCleared >= GateCount) return false;
            return LevelForXp(xp) >= CapForGates(gatesCleared);
        }

        /// <summary>One stop's length in seconds. Index 0 is the level-10 stop.</summary>
        public static double GateSeconds(int gateIndex, in Tuning t)
        {
            double hours;
            switch (gateIndex)
            {
                case 0:  hours = t.Gate1Hours; break;
                case 1:  hours = t.Gate2Hours; break;
                default: hours = t.Gate3Hours; break;
            }
            return hours <= 0d ? 0d : hours * 3600d;
        }

        /// <summary>
        /// The stat-budget column crafted items are built from — the cleared stops, straight into
        /// <see cref="SeaCombat.ItemFor"/>'s tier. Levels 1–9 craft tier-0 budgets, each cleared
        /// stop buys the next column, the level-30 stop buys the last.
        /// </summary>
        public static int TierFor(int gatesCleared)
        {
            if (gatesCleared < 0) return 0;
            int top = Voyages.TierCount - 1;
            return gatesCleared > top ? top : gatesCleared;
        }

        // ---------------------------------------------------------------- salvage
        /// <summary>XP one scrapped item of this grade teaches the bench.</summary>
        public static long SalvageXpFor(int grade)
        {
            if (grade < 0) return 0L;
            if (grade >= SalvageXp.Length) grade = SalvageXp.Length - 1;
            return SalvageXp[grade];
        }
    }
}
