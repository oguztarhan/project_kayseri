using System;

namespace Game.Core
{
    /// <summary>
    /// A shop bench's mastery track as pure maths: one level from 1 to <see cref="MaxLevel"/> that raises both what
    /// an item is worth and how fast one is made, five stars that each double one of the two, and what the next
    /// levels cost. It replaces the separate speed and value tracks, which topped out at 21 behind a carrier that
    /// made every speed level past the first worth nothing.
    ///
    /// STARS ALTERNATE. Value at 10, speed at 25, value at 50, speed at 75 and value again at 100 — the "income ×2"
    /// moment. Doubling one axis at a time keeps each star legible as a single change on the bench card, and ending
    /// on value means the last star pays even when the cycle is already at its floor.
    ///
    /// SPEED HAS A FLOOR, INCOME DOES NOT. Below <see cref="Tuning.MinCycleSeconds"/> a bench would need more bodies
    /// than the island can draw, so the cycle stops shortening there and every further bit of speed becomes value
    /// per item instead (<see cref="ItemValue"/>). Income per second is identical either way; only the picture
    /// changes. A bench whose base cycle is already under the floor is never slowed down to it.
    ///
    /// NOTHING HERE OWNS A BENCH. No save, no wallet, no dice: callers pass a bench's base craft seconds, base price
    /// and first-level cost, the same split <see cref="MiningGear"/> and <see cref="Crafting"/> keep.
    /// </summary>
    public static class BenchMastery
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 100;
        public const int StarCount = 5;

        public enum StarEffect { Value = 0, Speed = 1 }

        /// <summary>The level each star lands on, ascending. Saves index star claims by position, so this may not
        /// be reordered or shortened.</summary>
        public static readonly int[] StarLevels = { 10, 25, 50, 75, 100 };

        /// <summary>What each star doubles, in step with <see cref="StarLevels"/>.</summary>
        public static readonly StarEffect[] StarEffects =
            { StarEffect.Value, StarEffect.Speed, StarEffect.Value, StarEffect.Speed, StarEffect.Value };

        public struct Tuning
        {
            /// <summary>Linear value gained per level past the first, before stars.</summary>
            public double ValuePerLevel;
            /// <summary>Linear speed gained per level past the first, before stars.</summary>
            public double SpeedPerLevel;
            /// <summary>What one star multiplies its axis by.</summary>
            public double StarMultiplier;
            /// <summary>Each level costs this much more than the one before it.</summary>
            public double CostGrowth;
            /// <summary>The shortest cycle a bench is drawn at; faster than this becomes value per item.</summary>
            public double MinCycleSeconds;
            /// <summary>Chance a sale is perfect with no stars on its bench.</summary>
            public double PerfectBaseChance;
            /// <summary>Chance a perfect sale gains per star on its bench.</summary>
            public double PerfectChancePerStar;
            /// <summary>What a perfect sale multiplies its price by.</summary>
            public double PerfectMultiplier;
            /// <summary>Gems each star pays once, in step with <see cref="StarLevels"/>.</summary>
            public long[] StarGems;
            /// <summary>How much of a master's station throughput he brings to a bench. Below one so workers do
            /// not shorten the shop's pacing by the whole roster; stations keep the full value.</summary>
            public double WorkerShare;

            public static Tuning Default => new Tuning
            {
                ValuePerLevel = 0.10d,
                SpeedPerLevel = 0.03d,
                StarMultiplier = 2d,
                CostGrowth = 1.14d,
                MinCycleSeconds = 2d,
                PerfectBaseChance = 0.04d,
                PerfectChancePerStar = 0.01d,
                PerfectMultiplier = 3d,
                StarGems = new[] { 3L, 5L, 7L, 10L, 15L },
                WorkerShare = 0.5d
            };

            public void Validate()
            {
                if (!Finite(ValuePerLevel) || ValuePerLevel <= 0d || !Finite(SpeedPerLevel) || SpeedPerLevel <= 0d ||
                    !Finite(StarMultiplier) || StarMultiplier < 1d || !Finite(CostGrowth) || CostGrowth <= 1d ||
                    !Finite(MinCycleSeconds) || MinCycleSeconds <= 0d ||
                    !Finite(PerfectBaseChance) || PerfectBaseChance < 0d ||
                    !Finite(PerfectChancePerStar) || PerfectChancePerStar < 0d ||
                    PerfectBaseChance + PerfectChancePerStar * StarCount > 1d ||
                    !Finite(PerfectMultiplier) || PerfectMultiplier < 1d ||
                    StarGems == null || StarGems.Length != StarCount || !Finite(WorkerShare) || WorkerShare < 0d)
                    throw new ArgumentException("Bench mastery tuning requires finite positive rates, growth above one, " +
                                                "perfect odds that stay a probability and one gem award per star.");
                for (int i = 0; i < StarGems.Length; i++)
                    if (StarGems[i] < 0L) throw new ArgumentException("A star cannot take gems away.");
            }
        }

        // ------------------------------------------------------------------ levels and stars
        public static int Clamp(int level) => level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level;

        /// <summary>Stars earned at this level.</summary>
        public static int StarsAt(int level)
        {
            int stars = 0;
            for (int i = 0; i < StarCount; i++) if (level >= StarLevels[i]) stars++;
            return stars;
        }

        /// <summary>The level of the next star, or 0 once all five are earned.</summary>
        public static int NextStarLevel(int level)
        {
            for (int i = 0; i < StarCount; i++) if (level < StarLevels[i]) return StarLevels[i];
            return 0;
        }

        /// <summary>
        /// Stars reached at this level that <paramref name="paidMask"/> has not paid yet, one bit per star. A mask
        /// rather than a count, so a star can never be paid twice however the level got there — a purchase, a hold
        /// crossing two stars, or an old save migrated straight past them.
        /// </summary>
        public static int UnpaidStars(int level, int paidMask)
        {
            int unpaid = 0;
            for (int i = 0; i < StarCount; i++)
                if (level >= StarLevels[i] && (paidMask & (1 << i)) == 0) unpaid |= 1 << i;
            return unpaid;
        }

        /// <summary>Levels still to buy before the next star, or 0 once all five are earned.</summary>
        public static int LevelsToNextStar(int level)
        {
            int next = NextStarLevel(level);
            return next == 0 ? 0 : next - Clamp(level);
        }

        /// <summary>Everything that multiplies an item's worth at this level, stars included.</summary>
        public static double ValueMultiplier(int level, in Tuning t) => Axis(level, StarEffect.Value, t.ValuePerLevel, t);

        /// <summary>Everything that multiplies how fast a bench works at this level, stars included.</summary>
        public static double SpeedMultiplier(int level, in Tuning t) => Axis(level, StarEffect.Speed, t.SpeedPerLevel, t);

        private static double Axis(int level, StarEffect effect, double perLevel, in Tuning t)
        {
            level = Clamp(level);
            double m = 1d + perLevel * (level - 1);
            for (int i = 0; i < StarCount; i++)
                if (level >= StarLevels[i] && StarEffects[i] == effect) m *= t.StarMultiplier;
            return m;
        }

        // ------------------------------------------------------------------ one bench's output
        /// <summary>Seconds one item takes on screen: the sped-up cycle, held at the floor.</summary>
        public static double CycleSeconds(double baseCraftSeconds, int level, in Tuning t)
        {
            double sped = baseCraftSeconds / SpeedMultiplier(level, t);
            double floor = Math.Min(baseCraftSeconds, t.MinCycleSeconds);
            return sped > floor ? sped : floor;
        }

        /// <summary>
        /// What one item sells for before any wallet multiplier: the base price at this level's value, times however
        /// many sped-up items the floored cycle stands for.
        /// </summary>
        public static double ItemValue(double basePrice, double baseCraftSeconds, int level, in Tuning t)
        {
            double sped = baseCraftSeconds / SpeedMultiplier(level, t);
            return basePrice * ValueMultiplier(level, t) * (CycleSeconds(baseCraftSeconds, level, t) / sped);
        }

        /// <summary>A bench's cash per second at this level, before the worker, perfect sales and the wallet.</summary>
        public static double IncomePerSecond(double basePrice, double baseCraftSeconds, int level, in Tuning t)
            => basePrice * ValueMultiplier(level, t) * SpeedMultiplier(level, t) / baseCraftSeconds;

        // ------------------------------------------------------------------ costs
        /// <summary>What the next level costs from <paramref name="level"/>, or 0 at the top.</summary>
        public static double LevelCost(double firstLevelCost, int level, in Tuning t)
        {
            level = Clamp(level);
            if (level >= MaxLevel) return 0d;
            return Math.Ceiling(firstLevelCost * Math.Pow(t.CostGrowth, level - 1));
        }

        /// <summary>
        /// The price of the next <paramref name="count"/> levels, stopping at the top. Summed level by level, so a
        /// bulk buy costs exactly what the same levels bought one at a time would.
        /// </summary>
        public static double CostOfLevels(double firstLevelCost, int level, int count, in Tuning t)
        {
            level = Clamp(level);
            double total = 0d;
            for (int i = 0; i < count && level + i < MaxLevel; i++) total += LevelCost(firstLevelCost, level + i, t);
            return total;
        }

        /// <summary>How many of the next levels, up to <paramref name="limit"/>, this much cash pays for.</summary>
        public static int AffordableLevels(double firstLevelCost, int level, double cash, int limit, in Tuning t)
        {
            level = Clamp(level);
            int bought = 0;
            while (bought < limit && level + bought < MaxLevel)
            {
                double cost = LevelCost(firstLevelCost, level + bought, t);
                if (cost > cash) break;
                cash -= cost;
                bought++;
            }
            return bought;
        }

        // ------------------------------------------------------------------ riders
        /// <summary>Chance one sale off a bench with this many stars is perfect.</summary>
        public static double PerfectChance(int stars, in Tuning t)
        {
            if (stars < 0) stars = 0;
            if (stars > StarCount) stars = StarCount;
            return t.PerfectBaseChance + t.PerfectChancePerStar * stars;
        }

        /// <summary>What perfect sales add to a bench's income on average — the figure offline earnings pay.</summary>
        public static double PerfectAverage(int stars, in Tuning t) => 1d + PerfectChance(stars, t) * (t.PerfectMultiplier - 1d);

        /// <summary>
        /// What a master posted at a bench multiplies its income by: his share of the masters' own throughput table,
        /// so a bench worker and a station master can never disagree about what a star is worth; no master is ×1.
        /// </summary>
        public static double WorkerMultiplier(int master, int masterStars, in Foremen.Tuning masters, in Tuning t)
            => 1d + t.WorkerShare * Foremen.SkillValue(master, masterStars, Foremen.Skill.Throughput, masters);

        /// <summary>Gems the star at this position pays once.</summary>
        public static long StarGems(int star, in Tuning t) => star >= 0 && star < StarCount ? t.StarGems[star] : 0L;

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
