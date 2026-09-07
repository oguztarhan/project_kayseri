using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The master chest. Like <see cref="CaptainCrate"/> it takes its roll as an argument, so the slot
    /// distribution can be walked exactly rather than sampled — a chest that quietly favoured one
    /// station would otherwise take a very long run of real play to notice.
    /// </summary>
    public class MasterChestTests
    {
        private static MasterChest.Tuning T => MasterChest.Tuning.Default;

        // ---- price -------------------------------------------------------------------------------

        [Test]
        public void NoChests_CostNothing()
        {
            Assert.That(MasterChest.Cost(0, T), Is.Zero);
            Assert.That(MasterChest.Cost(-3, T), Is.Zero);
            Assert.That(MasterChest.CardsFor(0, T), Is.Zero);
        }

        [Test]
        public void SinglesArePricedOneAtATime()
        {
            Assert.That(MasterChest.Cost(1, T), Is.EqualTo(T.GemCost));
            Assert.That(MasterChest.Cost(3, T), Is.EqualTo(T.GemCost * 3));
        }

        [Test]
        public void TheBulkOpenIsCheaperPerChest()
        {
            long bulk = MasterChest.Cost(T.BulkCount, T);
            Assert.That(bulk, Is.EqualTo(T.BulkGemCost));
            Assert.That(bulk, Is.LessThan(T.GemCost * T.BulkCount),
                        "the bulk button must actually be a discount, or nobody presses it");
        }

        [Test]
        public void CardCountScalesWithChests()
        {
            Assert.That(MasterChest.CardsFor(1, T), Is.EqualTo(T.CardsPerChest));
            Assert.That(MasterChest.CardsFor(T.BulkCount, T), Is.EqualTo(T.CardsPerChest * T.BulkCount));
        }

        [Test]
        public void DirectedCardsNeverExceedTheChest()
        {
            Assert.That(MasterChest.DirectedIn(T), Is.InRange(0, T.CardsPerChest));

            // A config with more aimed cards than the chest holds must not manufacture cards.
            var greedy = T;
            greedy.DirectedPerChest = T.CardsPerChest + 5;
            Assert.That(MasterChest.DirectedIn(greedy), Is.EqualTo(greedy.CardsPerChest));

            var negative = T;
            negative.DirectedPerChest = -2;
            Assert.That(MasterChest.DirectedIn(negative), Is.Zero);
        }

        // ---- the card roll -----------------------------------------------------------------------

        [Test]
        public void EveryMasterIsAlwaysReachable()
        {
            for (int i = 0; i <= 200; i++)
                for (int j = 0; j <= 200; j++)
                    Assert.That(MasterChest.RollMaster(i / 200d, j / 200d, T),
                                Is.InRange(0, Foremen.Count - 1));
        }

        [Test]
        public void EveryMasterIsActuallyDrawn()
        {
            // A rarity nobody can roll and a station nobody can roll are the same bug, and either one
            // makes a card in the collection unreachable.
            var seen = new bool[Foremen.Count];
            const int n = 400;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    seen[MasterChest.RollMaster((i + 0.5d) / n, (j + 0.5d) / n, T)] = true;

            for (int m = 0; m < Foremen.Count; m++) Assert.That(seen[m], Is.True, "master " + m);
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => MasterChest.RollMaster(double.NaN, double.NaN, T));
            Assert.That(MasterChest.RollMaster(-5d, -5d, T), Is.Zero);
            Assert.That(MasterChest.RollMaster(1d, 1d, T), Is.EqualTo(Foremen.Count - 1));
            Assert.That(MasterChest.RollMaster(double.NaN, double.NaN, T),
                        Is.InRange(0, Foremen.Count - 1));
        }

        [Test]
        public void TheRarityDistributionMatchesTheWeights()
        {
            // Sweep the unit interval rather than sampling: the share of the interval landing on a
            // rarity IS its probability. Rarity is DRAWN now — it used to be earned, and the roll used
            // to be flat over eight slots.
            const int n = 80000;
            var count = new int[Foremen.RarityCount];
            for (int i = 0; i < n; i++) count[(int)MasterChest.RollRarity((i + 0.5d) / n, T)]++;

            double total = T.WeightCommon + T.WeightRare + T.WeightLegendary;
            var weight = new[] { T.WeightCommon, T.WeightRare, T.WeightLegendary };
            for (int r = 0; r < Foremen.RarityCount; r++)
            {
                double expected = n * weight[r] / total;
                Assert.That(count[r], Is.EqualTo(expected).Within(expected * 0.02d), "rarity " + r);
            }
        }

        [Test]
        public void ACommonIsCommonerThanALegendary()
        {
            // The one property the whole rarity table exists for. A tuning change that inverts it
            // makes the Legendary the card you cannot avoid.
            Assert.That(MasterChest.RollRarity(0.0d, T), Is.EqualTo(Foremen.Rarity.Common));
            Assert.That(MasterChest.RollRarity(0.99d, T), Is.EqualTo(Foremen.Rarity.Legendary));
            Assert.That(T.WeightCommon, Is.GreaterThan(T.WeightRare));
            Assert.That(T.WeightRare, Is.GreaterThan(T.WeightLegendary));
        }

        [Test]
        public void TheStationDistributionIsFlatWithinARarity()
        {
            // Rarity decides how good the card is; which of the five stations it belongs to is a
            // straight roll, or one station's ladder is quietly longer than the rest.
            const int n = 60000;
            var count = new int[Foremen.StationCount];
            for (int i = 0; i < n; i++)
            {
                int master = MasterChest.RollMaster(0d, (i + 0.5d) / n, T);   // 0 = always Common
                count[Foremen.StationOf(master)]++;
            }

            double expected = n / (double)Foremen.StationCount;
            for (int s = 0; s < Foremen.StationCount; s++)
                Assert.That(count[s], Is.EqualTo(expected).Within(expected * 0.02d), "station " + s);
        }

        [Test]
        public void AllZeroWeightsFallBackToCommonRatherThanDividingByNothing()
        {
            var broken = T;
            broken.WeightCommon = broken.WeightRare = broken.WeightLegendary = 0d;
            Assert.That(MasterChest.RollRarity(0.5d, broken), Is.EqualTo(Foremen.Rarity.Common));
            Assert.That(MasterChest.RollMaster(0.5d, 0.5d, broken), Is.InRange(0, Foremen.Count - 1));
        }

        // ---- the free chest ----------------------------------------------------------------------

        [Test]
        public void AFreshSaveHasAChestWaiting()
        {
            // Never claimed reads as due now: the first thing a collection screen should do is hand you
            // something.
            Assert.That(MasterChest.FreeReady(0L, 0L, T), Is.True);
            Assert.That(MasterChest.FreeReadyAtUnix(0L, T), Is.Zero);
            Assert.That(MasterChest.FreeSecondsLeft(0L, 0L, T), Is.Zero);
        }

        [Test]
        public void ClaimingStartsTheWaitAgain()
        {
            const long now = 1_700_000_000L;
            Assert.That(MasterChest.FreeReady(now, now, T), Is.False);
            Assert.That(MasterChest.FreeReady(now + T.FreeIntervalSeconds - 1, now, T), Is.False);
            Assert.That(MasterChest.FreeReady(now + T.FreeIntervalSeconds, now, T), Is.True);
            Assert.That(MasterChest.FreeSecondsLeft(now, now, T), Is.EqualTo(T.FreeIntervalSeconds));
        }

        [Test]
        public void AClockRolledBackwardsOnlyEverDelays()
        {
            const long claimed = 1_700_000_000L;
            Assert.That(MasterChest.FreeReady(claimed - 90000L, claimed, T), Is.False);
            Assert.That(MasterChest.FreeSecondsLeft(claimed - 90000L, claimed, T),
                        Is.GreaterThan(T.FreeIntervalSeconds));
        }

        [Test]
        public void AWeekAwayStillOnlyBanksOneChest()
        {
            // FreeReady is a boolean, not a count — the service claims one and re-stamps. This pins the
            // shape: being away longer can never be worth more than being away exactly long enough.
            const long claimed = 1_700_000_000L;
            Assert.That(MasterChest.FreeReady(claimed + T.FreeIntervalSeconds, claimed, T), Is.True);
            Assert.That(MasterChest.FreeReady(claimed + T.FreeIntervalSeconds * 21, claimed, T), Is.True);
        }
    }
}
