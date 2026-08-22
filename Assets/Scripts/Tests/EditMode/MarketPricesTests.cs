using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class MarketPricesTests
    {
        private const double Cap = 1000d;   // a stand-in island ceiling, $/min

        [Test]
        public void SlotsStartAtOne_EverythingElseAtZero()
        {
            Assert.That(MarketPrices.MinLevel(YardUpgrade.DepositSlot), Is.EqualTo(1));
            Assert.That(MarketPrices.MinLevel(YardUpgrade.QueueSlot), Is.EqualTo(1));
            Assert.That(MarketPrices.MinLevel(YardUpgrade.HireCarry), Is.EqualTo(0));
            Assert.That(MarketPrices.MinLevel(YardUpgrade.CarryCapacity), Is.EqualTo(0));
        }

        [Test]
        public void TrackCeilingsMatchTheFlowTheyDrive()
        {
            // the prices and the maths must agree about how far a track goes, or a pad sells a level
            // the yard will not read
            Assert.That(MarketPrices.MaxLevel(YardUpgrade.DepositSlot), Is.EqualTo(MarketFlow.MaxDepositSlots));
            Assert.That(MarketPrices.MaxLevel(YardUpgrade.QueueSlot), Is.EqualTo(MarketFlow.MaxQueueSlots));
            Assert.That(MarketPrices.MaxLevel(YardUpgrade.HireCarry), Is.EqualTo(MarketFlow.MaxHireLevel));
            Assert.That(MarketPrices.MaxLevel(YardUpgrade.HireServe), Is.EqualTo(MarketFlow.MaxHireLevel));
            Assert.That(MarketPrices.MaxLevel(YardUpgrade.HireCollect), Is.EqualTo(MarketFlow.MaxHireLevel));
        }

        [Test]
        public void FirstStepIsPricedFromTheFirstLevel_NotFromZero()
        {
            // a deposit slot's first purchase is its level-1 price, even though the track starts at 1
            double first = MarketPrices.Cost(YardUpgrade.DepositSlot, 1, Cap);
            double second = MarketPrices.Cost(YardUpgrade.DepositSlot, 2, Cap);
            Assert.Greater(first, 0d);
            Assert.Greater(second, first);
        }

        [Test]
        public void EveryTrackCompounds()
        {
            foreach (YardUpgrade kind in System.Enum.GetValues(typeof(YardUpgrade)))
            {
                int min = MarketPrices.MinLevel(kind);
                double previous = MarketPrices.Cost(kind, min, Cap);
                for (int level = min + 1; level < MarketPrices.MaxLevel(kind); level++)
                {
                    double cost = MarketPrices.Cost(kind, level, Cap);
                    Assert.Greater(cost, previous, kind + " level " + level + " should cost more than the last");
                    previous = cost;
                }
            }
        }

        [Test]
        public void FinishedTrackCostsNothing()
        {
            foreach (YardUpgrade kind in System.Enum.GetValues(typeof(YardUpgrade)))
            {
                Assert.IsTrue(MarketPrices.IsMaxed(kind, MarketPrices.MaxLevel(kind)));
                Assert.That(MarketPrices.Cost(kind, MarketPrices.MaxLevel(kind), Cap), Is.EqualTo(0d));
                Assert.That(MarketPrices.Cost(kind, MarketPrices.MaxLevel(kind) + 5, Cap), Is.EqualTo(0d));
            }
        }

        [Test]
        public void PricesScaleWithTheIsland_NotWithAbsoluteNumbers()
        {
            // the whole reason prices are quoted in minutes: a diamond yard costs a diamond island's
            // minutes, with no second table
            double coal = MarketPrices.Cost(YardUpgrade.HireCarry, 0, Cap);
            double diamond = MarketPrices.Cost(YardUpgrade.HireCarry, 0, Cap * 3.2d * 3.2d);
            Assert.That(diamond / coal, Is.EqualTo(3.2d * 3.2d).Within(1e-9));
        }

        [Test]
        public void AnIslandWithNoCeilingSellsNothing()
        {
            // guards the first seconds of a fresh save, before the yard has measured anything
            Assert.That(MarketPrices.Cost(YardUpgrade.QueueSlot, 1, 0d), Is.EqualTo(0d));
            Assert.That(MarketPrices.Cost(YardUpgrade.QueueSlot, 1, -5d), Is.EqualTo(0d));
        }

        [Test]
        public void AYardIsPricedAgainstItsOwnIslandsTree_UntilTheCeilingBites()
        {
            // the bug this replaced: a flat number of capped minutes made the coal market five times
            // the price of finishing the whole coal island, and four per cent of the diamond one.
            // A cheap tree has to produce a cheap yard.
            double cheapTree = Cap * 30d;              // an island finished in half an hour of its cap
            double vastTree = Cap * 4000d;             // a late island, a deliberate grind

            double cheap = MarketPrices.YardBudget(Cap, cheapTree);
            double vast = MarketPrices.YardBudget(Cap, vastTree);

            Assert.Less(cheap, vast, "a cheaper island must give a cheaper yard");
            Assert.Less(cheap, cheapTree, "a yard must never cost more than the island it stands in");
            // and the ceiling is what stops the vast one running away with it
            Assert.That(vast, Is.EqualTo(MarketPrices.YardBudget(Cap, vastTree * 10d)),
                        "past the ceiling the tree stops mattering");
        }

        [Test]
        public void NoTreeReportedFallsBackToTheCeiling_NotToFree()
        {
            // an old save, or the first frames before an island's economy exists
            Assert.Greater(MarketPrices.YardBudget(Cap, 0d), 0d);
            Assert.That(MarketPrices.YardBudget(Cap, 0d), Is.EqualTo(MarketPrices.YardBudget(Cap, 1e30d)));
        }

        [Test]
        public void EveryStepOfEveryTrackAddsBackUpToTheYardsBudget()
        {
            // the shares are shares: re-shaping a growth curve moves what the steps cost relative to
            // each other and must never move what the yard costs in total
            double tree = Cap * 200d;
            double budget = MarketPrices.YardBudget(Cap, tree);

            double total = 0d;
            foreach (YardUpgrade kind in System.Enum.GetValues(typeof(YardUpgrade)))
                total += MarketPrices.CostToMax(kind, MarketPrices.MinLevel(kind), Cap, tree);

            Assert.That(total, Is.EqualTo(budget).Within(budget * 1e-9),
                        "the six tracks together are the whole budget");
        }

        [Test]
        public void HiresCostMoreThanSlots()
        {
            // a slot makes a yard better; a hire makes visiting it optional. The second is the prize.
            double slot = MarketPrices.Cost(YardUpgrade.QueueSlot, 1, Cap);
            Assert.Greater(MarketPrices.Cost(YardUpgrade.HireCarry, 0, Cap), slot);
            Assert.Greater(MarketPrices.Cost(YardUpgrade.HireServe, 0, Cap), slot);
            Assert.Greater(MarketPrices.Cost(YardUpgrade.HireCollect, 0, Cap), slot);
        }

        [Test]
        public void CostToMax_IsTheSumOfEveryStepLeft()
        {
            double manual = 0d;
            for (int level = 0; level < MarketPrices.MaxLevel(YardUpgrade.HireServe); level++)
                manual += MarketPrices.Cost(YardUpgrade.HireServe, level, Cap);
            Assert.That(MarketPrices.CostToMax(YardUpgrade.HireServe, 0, Cap), Is.EqualTo(manual).Within(1e-6));
        }

        [Test]
        public void CostToMax_OnAFinishedTrackIsZero()
        {
            Assert.That(MarketPrices.CostToMax(YardUpgrade.HireServe, MarketFlow.MaxHireLevel, Cap),
                        Is.EqualTo(0d));
        }

        [Test]
        public void MakingAYardAutomaticCostsMoreThanFillingItWithSlots()
        {
            // the shape the progression depends on: you can afford a busy yard long before a free one
            double slots = MarketPrices.CostToMax(YardUpgrade.DepositSlot, 1, Cap)
                         + MarketPrices.CostToMax(YardUpgrade.QueueSlot, 1, Cap);
            double staff = MarketPrices.CostToMax(YardUpgrade.HireCarry, 0, Cap)
                         + MarketPrices.CostToMax(YardUpgrade.HireServe, 0, Cap)
                         + MarketPrices.CostToMax(YardUpgrade.HireCollect, 0, Cap);
            Assert.Greater(staff, slots);
        }
    }
}
