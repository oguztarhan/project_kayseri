using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>The shop coins' numbers: the 48-hour UTC cycle, the caps, the seeded waits and the reward roll.</summary>
    public sealed class ShopCoinsTests
    {
        private static readonly ShopCoins.Tuning T = ShopCoins.Tuning.Default;
        private const long Start = 11000L * ShopCoins.CycleSeconds;

        [Test]
        public void TheCycleTurnsOverAtExactlyFortyEightHoursOfUtc()
        {
            Assert.AreEqual(11000L, ShopCoins.CycleAt(Start));
            Assert.AreEqual(11000L, ShopCoins.CycleAt(Start + ShopCoins.CycleSeconds - 1L));
            Assert.AreEqual(11001L, ShopCoins.CycleAt(Start + ShopCoins.CycleSeconds));
            Assert.AreEqual(1L, ShopCoins.SecondsToReset(Start + ShopCoins.CycleSeconds - 1L));
            Assert.AreEqual(ShopCoins.CycleSeconds, ShopCoins.SecondsToReset(Start));
            // Every second UTC midnight: the cycle starts on a whole UTC day.
            Assert.AreEqual(0L, ShopCoins.CycleStartUnix(11001L) % ShopCoins.DaySeconds);
        }

        [Test]
        public void TheFirstDayHoldsEightAndTheWholeCycleTwelve()
        {
            Assert.AreEqual(8, ShopCoins.CapAt(Start, T));
            Assert.AreEqual(8, ShopCoins.CapAt(Start + ShopCoins.DaySeconds - 1L, T));
            Assert.AreEqual(12, ShopCoins.CapAt(Start + ShopCoins.DaySeconds, T));
            Assert.AreEqual(12, ShopCoins.CapAt(Start + ShopCoins.CycleSeconds - 1L, T));
        }

        [Test]
        public void AFirstDayCapAboveTheCycleCapIsTheCycleCap()
        {
            ShopCoins.Tuning t = T;
            t.FirstDayCap = 20;
            Assert.AreEqual(12, ShopCoins.CapAt(Start, t));
        }

        [Test]
        public void TheFirstWaitIsShortAndEveryLaterOneIsAGap()
        {
            for (int cycle = 0; cycle < 200; cycle++)
            {
                double first = ShopCoins.DelayBefore("p", cycle, 0, T);
                Assert.That(first, Is.InRange(T.FirstSpawnMinSeconds, T.FirstSpawnMaxSeconds));
                for (int i = 1; i < 20; i++)
                    Assert.That(ShopCoins.DelayBefore("p", cycle, i, T), Is.InRange(T.GapMinSeconds, T.GapMaxSeconds));
            }
        }

        [Test]
        public void RollsAreFixedByPlayerCycleAndIndex()
        {
            Assert.AreEqual(ShopCoins.DelayBefore("abc", 7L, 3, T), ShopCoins.DelayBefore("abc", 7L, 3, T));
            Assert.AreEqual(ShopCoins.RewardFor("abc", 7L, 3, T).Kind, ShopCoins.RewardFor("abc", 7L, 3, T).Kind);

            bool differs = false;
            for (int i = 0; i < 50 && !differs; i++)
                differs = ShopCoins.RewardFor("abc", 7L, i, T).Kind != ShopCoins.RewardFor("xyz", 7L, i, T).Kind;
            Assert.IsTrue(differs, "two players drew the same fifty rewards");
        }

        [Test]
        public void TheRollEndsSitOnTheFirstAndLastReward()
        {
            Assert.AreEqual(ShopCoins.RewardKind.SmallCash, ShopCoins.RewardAt(0d, T).Kind);
            Assert.AreEqual(ShopCoins.RewardKind.Jackpot, ShopCoins.RewardAt(0.99999d, T).Kind);
            Assert.AreEqual(T.SmallCashMinutes, ShopCoins.RewardAt(0d, T).CashMinutes);
            Assert.AreEqual(T.JackpotMinutes, ShopCoins.RewardAt(0.99999d, T).CashMinutes);
        }

        [Test]
        public void SeededRewardsLandOnTheirWeights()
        {
            const int n = 100000;
            var counts = new int[ShopCoins.RewardKindCount];
            for (int i = 0; i < n; i++) counts[(int)ShopCoins.RewardFor("player", 11000L + i / 1000, i % 1000, T).Kind]++;

            double total = T.SmallCashWeight + T.MediumCashWeight + T.BoostWeight + T.GemWeight + T.JackpotWeight;
            AssertShare(counts, n, ShopCoins.RewardKind.SmallCash, T.SmallCashWeight / total);
            AssertShare(counts, n, ShopCoins.RewardKind.MediumCash, T.MediumCashWeight / total);
            AssertShare(counts, n, ShopCoins.RewardKind.Boost, T.BoostWeight / total);
            AssertShare(counts, n, ShopCoins.RewardKind.Gems, T.GemWeight / total);
            AssertShare(counts, n, ShopCoins.RewardKind.Jackpot, T.JackpotWeight / total);
        }

        [Test]
        public void ZeroGemsRemovesGemCoinsAndTheOthersShareTheRoll()
        {
            ShopCoins.Tuning noWeight = T;
            noWeight.GemWeight = 0d;
            ShopCoins.Tuning noAmount = T;
            noAmount.Gems = 0L;

            for (int i = 0; i < 5000; i++)
            {
                Assert.AreNotEqual(ShopCoins.RewardKind.Gems, ShopCoins.RewardFor("p", 1L, i, noWeight).Kind);
                Assert.AreNotEqual(ShopCoins.RewardKind.Gems, ShopCoins.RewardFor("p", 1L, i, noAmount).Kind);
            }
            Assert.AreEqual(0d, ShopCoins.ExpectedGems(noWeight));
            Assert.AreEqual(0d, ShopCoins.ExpectedGems(noAmount));
            Assert.AreEqual(ShopCoins.RewardKind.Jackpot, ShopCoins.RewardAt(0.99999d, noAmount).Kind);
        }

        [Test]
        public void TheDefaultCoinPaysAQuarterGemOnAverage()
        {
            Assert.AreEqual(0.25d, ShopCoins.ExpectedGems(T), 1e-9);
        }

        [Test]
        public void CashIsMinutesOfIncomeNeverBelowTheFloor()
        {
            Assert.AreEqual(1000d, ShopCoins.Cash(2d, 500d, 100d));
            Assert.AreEqual(100d, ShopCoins.Cash(2d, 10d, 100d));
            Assert.AreEqual(100d, ShopCoins.Cash(2d, double.NaN, 100d));
            Assert.AreEqual(0d, ShopCoins.Cash(2d, 0d, -5d));
        }

        [Test]
        public void TheDefaultsValidateAndBrokenTuningIsRefused()
        {
            Assert.DoesNotThrow(() => T.Validate());

            ShopCoins.Tuning nothing = T;
            nothing.SmallCashWeight = nothing.MediumCashWeight = nothing.BoostWeight = nothing.GemWeight = nothing.JackpotWeight = 0d;
            Assert.Throws<ArgumentException>(() => nothing.Validate());

            ShopCoins.Tuning backwards = T;
            backwards.GapMaxSeconds = backwards.GapMinSeconds - 1d;
            Assert.Throws<ArgumentException>(() => backwards.Validate());

            ShopCoins.Tuning noLife = T;
            noLife.LifetimeSeconds = 0d;
            Assert.Throws<ArgumentException>(() => noLife.Validate());

            ShopCoins.Tuning negative = T;
            negative.GemWeight = -1d;
            Assert.Throws<ArgumentException>(() => negative.Validate());
        }

        private static void AssertShare(int[] counts, int n, ShopCoins.RewardKind kind, double expected)
            => Assert.AreEqual(expected, counts[(int)kind] / (double)n, 0.01d, kind.ToString());
    }
}
