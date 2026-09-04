using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class IslandDevelopmentTests
    {
        [Test]
        public void Measure_DerivesLevelFromExistingEconomyState()
        {
            int[][] levels = IslandEconomy.NewLevels();
            levels[IslandEconomy.Mine][0] = 20;
            bool[] unlocks = new bool[10];
            unlocks[0] = true;

            var economy = new IslandEconomy(IslandEconomy.Tuning.Default, levels, unlocks);
            IslandDevelopment.Progress progress = IslandDevelopment.Measure(economy);

            Assert.That(progress.Points, Is.EqualTo(25));
            Assert.That(progress.Level, Is.EqualTo(2));
            Assert.That(progress.PointsIntoLevel, Is.EqualTo(0));
        }

        [Test]
        public void Measure_ClampsLegacyLevelsToCurrentCaps()
        {
            int[][] levels = IslandEconomy.NewLevels();
            levels[IslandEconomy.OreTrucks][0] = 99;
            var economy = new IslandEconomy(IslandEconomy.Tuning.Default, levels, new bool[10]);

            Assert.That(IslandDevelopment.Measure(economy).Points,
                        Is.EqualTo(economy.AxisCap(IslandEconomy.OreTrucks, 0)));
        }

        [Test]
        public void Measure_ReportsCompletedAtTheTrueEconomyCeiling()
        {
            int[][] levels = IslandEconomy.NewLevels();
            bool[] unlocks = new bool[10];
            var economy = new IslandEconomy(IslandEconomy.Tuning.Default, levels, unlocks);
            for (int station = 0; station < levels.Length; station++)
                for (int axis = 0; axis < levels[station].Length; axis++)
                    levels[station][axis] = economy.AxisCap(station, axis);
            for (int i = 0; i < unlocks.Length; i++) unlocks[i] = true;

            IslandDevelopment.Progress progress = IslandDevelopment.Measure(economy);
            Assert.That(progress.IsMaxed, Is.True);
            Assert.That(progress.Level, Is.EqualTo(progress.MaxLevel));
            Assert.That(progress.Normalized, Is.EqualTo(1f));
        }

        [Test]
        public void Compare_PutsAffordableUpgradeFirst()
        {
            var affordable = R(0, 0, 8, 50, 1000d, true);
            var cheaperLockedByCash = R(1, 0, 0, 50, 10d, false);

            Assert.That(IslandDevelopment.Compare(affordable, cheaperLockedByCash), Is.LessThan(0));
        }

        [Test]
        public void Compare_SpreadsLevelsBeforeChoosingTheCheapestAxis()
        {
            var developedCheap = R(0, 0, 40, 50, 10d, true);
            var freshDear = R(1, 0, 2, 50, 1000d, true);

            Assert.That(IslandDevelopment.Compare(freshDear, developedCheap), Is.LessThan(0));
        }

        [Test]
        public void Compare_UsesCostAndThenIndicesForStableTies()
        {
            var dear = R(1, 1, 5, 50, 100d, true);
            var cheap = R(1, 0, 5, 50, 50d, true);
            Assert.That(IslandDevelopment.Compare(cheap, dear), Is.LessThan(0));

            dear.Cost = cheap.Cost;
            Assert.That(IslandDevelopment.Compare(cheap, dear), Is.LessThan(0));
        }

        [TestCase(1, true, true, true)]
        [TestCase(1, true, false, false)]
        [TestCase(1, false, true, false)]
        [TestCase(0, true, true, false)]
        public void CanUnlockNext_RequiresPreviousIslandAndObjectives(
            int destination, bool previousOwned, bool complete, bool expected)
        {
            Assert.That(IslandDevelopment.CanUnlockNext(destination, previousOwned, complete), Is.EqualTo(expected));
        }

        private static IslandDevelopment.Recommendation R(
            int station, int axis, int level, int cap, double cost, bool affordable)
            => new IslandDevelopment.Recommendation
            {
                Station = station,
                Axis = axis,
                Level = level,
                Cap = cap,
                Cost = new BigDouble(cost),
                Affordable = affordable,
            };
    }
}
