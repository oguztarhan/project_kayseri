using System;

namespace Game.Core
{
    /// <summary>
    /// The captain's mining loadout, as pure maths: what a craft rolls, what a grade is worth, and
    /// what the four worn items add to the island's income. No RNG lives here — <see cref="RollSlot"/>
    /// and <see cref="RollGrade"/> take a value in [0,1), the same contract <see cref="Crafting.RollGrade"/>
    /// and <see cref="SeaCombat.RollSlot"/> keep.
    ///
    /// FOUR FIXED SLOTS, NOT A RANDOM-FILL LOADOUT. Unlike <see cref="SeaCombat"/>'s five combat
    /// slots, a mining loadout is always exactly one pickaxe, one helmet, one bag and one lantern —
    /// there is no slot you are "not ready for" yet, so a craft never needs a shelf to sit on while
    /// you decide. It is compared to what is worn and the better one is kept; see
    /// <see cref="IsUpgrade"/>. That is also why this is its own small file rather than a rider on
    /// <see cref="GearStash"/>, which is typed to <see cref="SeaCombat.Item"/> and exists to solve a
    /// problem this loadout does not have.
    ///
    /// ONE NUMBER OUT. <see cref="IncomeMultiplier"/> is the only thing the island economy ever asks
    /// this file for — it never needs to know a pickaxe from a lantern, only what the set is worth.
    /// </summary>
    public static class MiningGear
    {
        public const int SlotPickaxe = 0, SlotHelmet = 1, SlotBag = 2, SlotLantern = 3;
        public const int SlotCount = 4;

        /// <summary>No item in a slot. Grade -1 is empty, the same convention <see cref="SeaCombat.Item"/> uses.</summary>
        public const int NoGrade = -1;

        /// <summary>One item: which slot it fills and how it rolled. Grades reuse <see cref="Captains.Grade"/> —
        /// the same five-rung ladder and tint already shared by <see cref="Captains"/>, <see cref="Crafting"/>
        /// and <see cref="SeaCombat"/>.</summary>
        public struct Item
        {
            public int Slot;
            public int Grade;
        }

        // ------------------------------------------------------------------ tuning
        public struct Tuning
        {
            /// <summary>Mining Points one craft costs.</summary>
            public long CraftCost;

            /// <summary>Craft weights per grade. Need not sum to 1 — a grade nobody can reach falls
            /// out on its own. Defaulted off <see cref="CaptainCrate"/>'s proven shape.</summary>
            public double CommonWeight, RareWeight, EpicWeight, LegendaryWeight, MythicWeight;

            /// <summary>What one worn item adds to the island's income, as a fraction on top of 1.0.
            /// A full Mythic set is the four of these summed.</summary>
            public double CommonBonus, RareBonus, EpicBonus, LegendaryBonus, MythicBonus;

            /// <summary>How Mining Points accrue: <see cref="PointsPerTick"/> every <see cref="TickSeconds"/>
            /// of wall clock, capped at <see cref="PointCap"/> — the captain earns duty pay just by
            /// being assigned, the same shape <c>seaEnergy</c> regenerates in.</summary>
            public int PointsPerTick;
            public double TickSeconds;
            public long PointCap;

            public static Tuning Default => new Tuning
            {
                CraftCost = 5L,

                CommonWeight    = 0.600d,
                RareWeight      = 0.260d,
                EpicWeight      = 0.105d,
                LegendaryWeight = 0.030d,
                MythicWeight    = 0.005d,

                // A full Mythic set (all four slots) is +80% income; a full Common set is +8%.
                CommonBonus    = 0.020d,
                RareBonus      = 0.040d,
                EpicBonus      = 0.070d,
                LegendaryBonus = 0.120d,
                MythicBonus    = 0.200d,

                PointsPerTick = 1,
                TickSeconds   = 600d,   // one point every 10 minutes
                PointCap      = 50L,
            };
        }

        // ------------------------------------------------------------------- rolls
        public static int RollSlot(double roll)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;
            return (int)(roll * SlotCount);
        }

        /// <summary>What grade one craft comes out as. Zero-allocation: the five weights are walked
        /// in place rather than built into an array, the same way <see cref="Crafting.RollGrade"/>
        /// walks its bracket row.</summary>
        public static int RollGrade(double roll, in Tuning t)
        {
            if (roll < 0d) roll = 0d;
            if (roll >= 1d) roll = 0.9999999999d;

            double cw = Math.Max(0d, t.CommonWeight);
            double rw = Math.Max(0d, t.RareWeight);
            double ew = Math.Max(0d, t.EpicWeight);
            double lw = Math.Max(0d, t.LegendaryWeight);
            double mw = Math.Max(0d, t.MythicWeight);
            double total = cw + rw + ew + lw + mw;
            if (total <= 0d) return 0;

            double target = roll * total;
            double acc = cw;
            if (target < acc) return 0;
            acc += rw;
            if (target < acc) return 1;
            acc += ew;
            if (target < acc) return 2;
            acc += lw;
            if (target < acc) return 3;
            return 4;
        }

        // ------------------------------------------------------------------ grades
        /// <summary>What one worn item of this grade adds. 0 for an empty slot (grade &lt; 0).</summary>
        public static double BonusFor(int grade, in Tuning t)
        {
            switch (grade)
            {
                case (int)Captains.Grade.Mythic:    return Math.Max(0d, t.MythicBonus);
                case (int)Captains.Grade.Legendary: return Math.Max(0d, t.LegendaryBonus);
                case (int)Captains.Grade.Epic:      return Math.Max(0d, t.EpicBonus);
                case (int)Captains.Grade.Rare:      return Math.Max(0d, t.RareBonus);
                case (int)Captains.Grade.Common:    return Math.Max(0d, t.CommonBonus);
                default:                            return 0d;
            }
        }

        /// <summary>Small salvage a scrapped item pays — the consolation for the craft that lost the
        /// compare, the same shape as <see cref="SeaCombat.ScrapFor"/>.</summary>
        private static readonly long[] ScrapByGrade = { 2L, 4L, 8L, 16L, 30L };

        public static long ScrapFor(int grade)
        {
            if (grade < 0) return 0L;
            int g = grade >= ScrapByGrade.Length ? ScrapByGrade.Length - 1 : grade;
            return ScrapByGrade[g];
        }

        /// <summary>Whether a freshly crafted item beats what is worn in its slot. An empty slot is
        /// always an upgrade; a tie is not.</summary>
        public static bool IsUpgrade(int candidateGrade, int wornGrade)
        {
            if (candidateGrade < 0) return false;
            if (wornGrade < 0) return true;
            return candidateGrade > wornGrade;
        }

        /// <summary>The island's income multiplier from the loadout: 1 plus every worn slot's bonus.
        /// <paramref name="grades"/> is read up to <see cref="SlotCount"/> entries; an empty or short
        /// array reads as nothing worn.</summary>
        public static double IncomeMultiplier(int[] grades, in Tuning t)
        {
            double bonus = 0d;
            if (grades != null)
            {
                int n = grades.Length < SlotCount ? grades.Length : SlotCount;
                for (int i = 0; i < n; i++) bonus += BonusFor(grades[i], t);
            }
            return 1d + bonus;
        }

        // ----------------------------------------------------------------- points
        /// <summary>How many Mining Points this many elapsed seconds accrue. The caller caps the
        /// running total at <see cref="Tuning.PointCap"/>; this only answers the ladder's shape.</summary>
        public static long PointsForElapsed(long elapsedSeconds, in Tuning t)
        {
            if (elapsedSeconds <= 0L) return 0L;
            double tick = t.TickSeconds <= 0d ? 1d : t.TickSeconds;
            long ticks = (long)(elapsedSeconds / tick);
            long perTick = t.PointsPerTick < 0 ? 0 : t.PointsPerTick;
            return ticks * perTick;
        }
    }
}
