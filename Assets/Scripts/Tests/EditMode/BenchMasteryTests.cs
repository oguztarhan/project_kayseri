using System;
using System.Globalization;
using System.Text;
using Game.Core;
using Game.Data;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BenchMasteryTests
    {
        private static readonly BenchMastery.Tuning T = BenchMastery.Tuning.Default;

        // The bench table the approved plan balanced against. P2 moves these onto the shop's product tuning; until
        // then they live here so the pacing below is measured on the numbers that were signed off.
        private static readonly double[] Craft = { 10d, 20d, 40d, 60d };
        private static readonly double[] Price = { 20d, 240d, 3200d, 30000d };
        private static readonly double[] FirstLevel = { 40d, 400d, 4000d, 40000d };
        private static readonly double[] Build = { 0d, 3000d, 150000d, 6000000d };
        private const int BuildRequiresLevel = 25;
        private static readonly string[] Names = { "pickaxe", "helmet", "lantern", "bag" };

        [Test]
        public void StarsLandOnTheirLevelsAndAlternateValueAndSpeed()
        {
            Assert.That(BenchMastery.StarsAt(1), Is.EqualTo(0));
            Assert.That(BenchMastery.StarsAt(9), Is.EqualTo(0));
            Assert.That(BenchMastery.StarsAt(10), Is.EqualTo(1));
            Assert.That(BenchMastery.StarsAt(25), Is.EqualTo(2));
            Assert.That(BenchMastery.StarsAt(99), Is.EqualTo(4));
            Assert.That(BenchMastery.StarsAt(100), Is.EqualTo(5));
            Assert.That(BenchMastery.NextStarLevel(1), Is.EqualTo(10));
            Assert.That(BenchMastery.NextStarLevel(10), Is.EqualTo(25));
            Assert.That(BenchMastery.NextStarLevel(100), Is.EqualTo(0));
            Assert.That(BenchMastery.LevelsToNextStar(1), Is.EqualTo(9));
            Assert.That(BenchMastery.LevelsToNextStar(76), Is.EqualTo(24));
            Assert.That(BenchMastery.LevelsToNextStar(100), Is.EqualTo(0));

            Assert.That(BenchMastery.ValueMultiplier(9, T), Is.EqualTo(1.8d).Within(1e-12));
            Assert.That(BenchMastery.ValueMultiplier(10, T), Is.EqualTo(3.8d).Within(1e-12), "value star");
            Assert.That(BenchMastery.SpeedMultiplier(10, T), Is.EqualTo(1.27d).Within(1e-12), "not a speed star");
            Assert.That(BenchMastery.SpeedMultiplier(25, T), Is.EqualTo(3.44d).Within(1e-12), "speed star");
            Assert.That(BenchMastery.ValueMultiplier(100, T), Is.EqualTo(87.2d).Within(1e-9));
            Assert.That(BenchMastery.SpeedMultiplier(100, T), Is.EqualTo(15.88d).Within(1e-9));
            Assert.That(BenchMastery.IncomePerSecond(20d, 10d, 100, T) / BenchMastery.IncomePerSecond(20d, 10d, 1, T),
                Is.EqualTo(1384.736d).Within(1e-6));
        }

        [Test]
        public void EveryLevelEarnsMoreThanTheOneBeforeOnEveryBench()
        {
            for (int bench = 0; bench < Craft.Length; bench++)
                for (int level = BenchMastery.MinLevel; level < BenchMastery.MaxLevel; level++)
                    Assert.That(BenchMastery.IncomePerSecond(Price[bench], Craft[bench], level + 1, T),
                        Is.GreaterThan(BenchMastery.IncomePerSecond(Price[bench], Craft[bench], level, T)),
                        Names[bench] + " level " + level);
        }

        [Test]
        public void TheCycleFloorTurnsExtraSpeedIntoValueWithoutChangingIncome()
        {
            Assert.That(BenchMastery.CycleSeconds(10d, 1, T), Is.EqualTo(10d));
            Assert.That(BenchMastery.CycleSeconds(10d, 100, T), Is.EqualTo(T.MinCycleSeconds), "pickaxe is floored");
            Assert.That(BenchMastery.CycleSeconds(60d, 100, T), Is.EqualTo(60d / 15.88d).Within(1e-9), "bag never reaches it");
            Assert.That(BenchMastery.CycleSeconds(1d, 1, T), Is.EqualTo(1d), "a fast base is never slowed to the floor");

            for (int bench = 0; bench < Craft.Length; bench++)
                for (int level = BenchMastery.MinLevel; level <= BenchMastery.MaxLevel; level++)
                {
                    double perItem = BenchMastery.ItemValue(Price[bench], Craft[bench], level, T);
                    double cycle = BenchMastery.CycleSeconds(Craft[bench], level, T);
                    Assert.That(perItem / cycle, Is.EqualTo(BenchMastery.IncomePerSecond(Price[bench], Craft[bench], level, T))
                        .Within(1e-9).Percent, Names[bench] + " level " + level);
                }
        }

        [Test]
        public void BulkPurchasesCostExactlyWhatTheSameLevelsCostOneAtATime()
        {
            Assert.That(BenchMastery.LevelCost(40d, 1, T), Is.EqualTo(40d));
            Assert.That(BenchMastery.LevelCost(40d, 2, T), Is.EqualTo(46d), "rounded up, as today's upgrades are");
            Assert.That(BenchMastery.LevelCost(40d, 99, T), Is.GreaterThan(0d));
            Assert.That(BenchMastery.LevelCost(40d, 100, T), Is.Zero, "nothing past the top");

            double singles = 0d;
            for (int level = 7; level < 17; level++) singles += BenchMastery.LevelCost(40d, level, T);
            Assert.That(BenchMastery.CostOfLevels(40d, 7, 10, T), Is.EqualTo(singles));
            Assert.That(BenchMastery.CostOfLevels(40d, 98, 10, T),
                Is.EqualTo(BenchMastery.LevelCost(40d, 98, T) + BenchMastery.LevelCost(40d, 99, T)), "stops at the top");

            double three = BenchMastery.CostOfLevels(40d, 1, 3, T);
            Assert.That(BenchMastery.AffordableLevels(40d, 1, three, 99, T), Is.EqualTo(3));
            Assert.That(BenchMastery.AffordableLevels(40d, 1, three - 1d, 99, T), Is.EqualTo(2));
            Assert.That(BenchMastery.AffordableLevels(40d, 1, three, 2, T), Is.EqualTo(2), "the limit holds");
            Assert.That(BenchMastery.AffordableLevels(40d, 100, 1e300, 99, T), Is.Zero);
        }

        [Test]
        public void PerfectSalesWorkersAndStarGemsReadTheirTables()
        {
            Assert.That(BenchMastery.PerfectChance(0, T), Is.EqualTo(0.04d).Within(1e-12));
            Assert.That(BenchMastery.PerfectChance(5, T), Is.EqualTo(0.09d).Within(1e-12));
            Assert.That(BenchMastery.PerfectChance(9, T), Is.EqualTo(0.09d).Within(1e-12), "clamped to five stars");
            Assert.That(BenchMastery.PerfectAverage(0, T), Is.EqualTo(1.08d).Within(1e-12));
            Assert.That(BenchMastery.PerfectAverage(5, T), Is.EqualTo(1.18d).Within(1e-12));

            Foremen.Tuning masters = Foremen.Tuning.Default;
            int common = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            int legendary = Foremen.IndexOf(Foremen.Market, Foremen.Rarity.Legendary);
            // Half the station value: a maxed Legendary is +400% at his station and +200% at a bench.
            Assert.That(BenchMastery.WorkerMultiplier(-1, 0, masters, T), Is.EqualTo(1d), "the apprentice");
            Assert.That(BenchMastery.WorkerMultiplier(common, 0, masters, T), Is.EqualTo(1d), "not hired");
            Assert.That(BenchMastery.WorkerMultiplier(common, 1, masters, T), Is.EqualTo(1.05d).Within(1e-12));
            Assert.That(BenchMastery.WorkerMultiplier(common, 5, masters, T), Is.EqualTo(1.25d).Within(1e-12));
            Assert.That(BenchMastery.WorkerMultiplier(legendary, 5, masters, T), Is.EqualTo(3d).Within(1e-12));
            BenchMastery.Tuning full = T;
            full.WorkerShare = 1d;
            Assert.That(BenchMastery.WorkerMultiplier(legendary, 5, masters, full), Is.EqualTo(5d).Within(1e-12));

            long gems = 0L;
            for (int star = 0; star < BenchMastery.StarCount; star++) gems += BenchMastery.StarGems(star, T);
            Assert.That(gems, Is.EqualTo(40L), "the 160-per-island line the gem budget was signed off with");
            Assert.That(BenchMastery.StarGems(BenchMastery.StarCount, T), Is.Zero);
        }

        [Test]
        public void UnpaidStarsAreThoseReachedAndNotYetInTheMask()
        {
            Assert.That(BenchMastery.UnpaidStars(9, 0), Is.Zero);
            Assert.That(BenchMastery.UnpaidStars(10, 0), Is.EqualTo(0b00001));
            Assert.That(BenchMastery.UnpaidStars(10, 0b00001), Is.Zero, "paid once");
            Assert.That(BenchMastery.UnpaidStars(29, 0), Is.EqualTo(0b00011), "a migrated save owes both stars it passed");
            Assert.That(BenchMastery.UnpaidStars(60, 0b00001), Is.EqualTo(0b00110));
            Assert.That(BenchMastery.UnpaidStars(100, 0), Is.EqualTo(0b11111));
            Assert.That(BenchMastery.UnpaidStars(100, ~0), Is.Zero, "stray high bits pay nothing and cost nothing");
        }

        [Test]
        public void BadTuningIsRefused()
        {
            BenchMastery.Tuning flat = T;
            flat.CostGrowth = 1d;
            Assert.Throws<ArgumentException>(() => flat.Validate());
            BenchMastery.Tuning certain = T;
            certain.PerfectBaseChance = 0.99d;
            Assert.Throws<ArgumentException>(() => certain.Validate(), "five stars would push it past one");
            BenchMastery.Tuning shortGems = T;
            shortGems.StarGems = new[] { 1L, 2L };
            Assert.Throws<ArgumentException>(() => shortGems.Validate());
            BenchMastery.Tuning nan = T;
            nan.ValuePerLevel = double.NaN;
            Assert.Throws<ArgumentException>(() => nan.Validate());
        }

        [Test]
        public void InspectorDefaultsMatchTheCodeDefaults()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                BenchMastery.Tuning fromConfig = config.ToMasteryTuning();
                Assert.That(fromConfig.ValuePerLevel, Is.EqualTo(T.ValuePerLevel));
                Assert.That(fromConfig.SpeedPerLevel, Is.EqualTo(T.SpeedPerLevel));
                Assert.That(fromConfig.StarMultiplier, Is.EqualTo(T.StarMultiplier));
                Assert.That(fromConfig.CostGrowth, Is.EqualTo(T.CostGrowth));
                Assert.That(fromConfig.MinCycleSeconds, Is.EqualTo(T.MinCycleSeconds));
                Assert.That(fromConfig.PerfectBaseChance, Is.EqualTo(T.PerfectBaseChance));
                Assert.That(fromConfig.PerfectChancePerStar, Is.EqualTo(T.PerfectChancePerStar));
                Assert.That(fromConfig.PerfectMultiplier, Is.EqualTo(T.PerfectMultiplier));
                Assert.That(fromConfig.StarGems, Is.EqualTo(T.StarGems));
                Assert.That(fromConfig.WorkerShare, Is.EqualTo(T.WorkerShare));
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        /// <summary>
        /// The pacing the plan was approved on: a player who always buys whatever pays itself back soonest, with no
        /// workers, perfect sales or offline time. Times are income-hours. The windows are wide on purpose — this
        /// guards against a retune that moves a milestone by a factor, not against the second decimal.
        /// </summary>
        [Test]
        public void AGreedyPlayerReachesEachMilestoneInsideItsWindow()
        {
            var level = new int[Craft.Length];
            level[0] = 1;
            double cash = 0d, t = 0d, firstStar = -1d, allStars = -1d;
            var built = new double[Craft.Length];
            var report = new StringBuilder();
            const double Step = 2d;

            while (t < 200d * 3600d && allStars < 0d)
            {
                double rate = 0d;
                for (int b = 0; b < Craft.Length; b++)
                    if (level[b] > 0) rate += BenchMastery.IncomePerSecond(Price[b], Craft[b], level[b], T);
                cash += rate * Step;
                t += Step;

                while (true)
                {
                    int best = -1;
                    double bestRatio = double.MaxValue, bestCost = 0d;
                    for (int b = 0; b < Craft.Length; b++)
                    {
                        double cost, gain;
                        if (level[b] == 0)
                        {
                            if (level[b - 1] < BuildRequiresLevel) continue;
                            cost = Build[b];
                            gain = BenchMastery.IncomePerSecond(Price[b], Craft[b], 1, T);
                        }
                        else if (level[b] < BenchMastery.MaxLevel)
                        {
                            cost = BenchMastery.LevelCost(FirstLevel[b], level[b], T);
                            gain = BenchMastery.IncomePerSecond(Price[b], Craft[b], level[b] + 1, T) -
                                   BenchMastery.IncomePerSecond(Price[b], Craft[b], level[b], T);
                        }
                        else continue;
                        if (cost / gain < bestRatio) { bestRatio = cost / gain; bestCost = cost; best = b; }
                    }
                    if (best < 0 || cash < bestCost) break;
                    cash -= bestCost;
                    if (level[best] == 0) { built[best] = t; report.AppendLine(Hours(t) + " build " + Names[best]); }
                    int stars = BenchMastery.StarsAt(level[best]);
                    level[best]++;
                    if (BenchMastery.StarsAt(level[best]) > stars && level[best] > 1)
                    {
                        report.AppendLine(Hours(t) + " " + Names[best] + " L" + level[best]);
                        if (firstStar < 0d) firstStar = t;
                    }
                }

                bool done = true;
                for (int b = 0; b < Craft.Length; b++) done &= level[b] == BenchMastery.MaxLevel;
                if (done) allStars = t;
            }
            TestContext.WriteLine(report.ToString());

            Assert.That(FirstLevel[0] / BenchMastery.IncomePerSecond(Price[0], Craft[0], 1, T),
                Is.LessThanOrEqualTo(20d), "the first upgrade is affordable within 20 s");
            Assert.That(firstStar / 60d, Is.InRange(2d, 6d), "pickaxe star 1, minutes");
            Assert.That(built[1] / 60d, Is.InRange(5d, 20d), "helmet built, minutes");
            Assert.That(built[2] / 3600d, Is.InRange(0.25d, 1.5d), "lantern built, hours");
            Assert.That(built[3] / 3600d, Is.InRange(1d, 4d), "bag built, hours");
            Assert.That(allStars / 3600d, Is.InRange(80d, 150d), "all twenty stars, income-hours");
        }

        private static string Hours(double seconds) =>
            (seconds / 3600d).ToString("F2", CultureInfo.InvariantCulture) + "h";
    }
}
