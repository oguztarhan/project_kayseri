using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class MarketFlowTests
    {
        private static int[] Hires(int carry, int serve, int collect) => new[] { carry, serve, collect };

        private const int Max = MarketFlow.MaxHireLevel;

        // ---- the state table the design is specified against ------------------------------------

        [Test]
        public void BareYard_FallsToTheTrickle()
        {
            Assert.That(MarketFlow.ServiceRate(Hires(0, 0, 0)),
                        Is.EqualTo(MarketFlow.IdleTrickle).Within(1e-9));
        }

        [Test]
        public void OneUnhiredJob_ThrottlesEverything()
        {
            // carry and collect at full speed still only move as fast as whoever is not serving
            Assert.That(MarketFlow.ServiceRate(Hires(Max, 0, Max)),
                        Is.EqualTo(MarketFlow.IdleTrickle).Within(1e-9));
        }

        [Test]
        public void MaxedYard_RunsAtFullSpeedWithNobodyThere()
        {
            Assert.That(MarketFlow.ServiceRate(Hires(Max, Max, Max)), Is.EqualTo(1d).Within(1e-9));
            Assert.IsTrue(MarketFlow.IsMaxed(Hires(Max, Max, Max)));
        }

        [Test]
        public void HiringHelpsButOnlyOnTheSlowestJob()
        {
            // levelling a job that is not the bottleneck must not move the yard at all
            double before = MarketFlow.ServiceRate(Hires(1, 0, 1));
            double after = MarketFlow.ServiceRate(Hires(Max, 0, Max));
            Assert.That(after, Is.EqualTo(before).Within(1e-12));
        }

        // ---- hires ------------------------------------------------------------------------------

        [Test]
        public void FreshHire_BeatsTheTrickle_ButIsNotFullSpeed()
        {
            double fresh = MarketFlow.JobRate(1);
            Assert.That(fresh, Is.EqualTo(MarketFlow.HireBase).Within(1e-9));
            Assert.Greater(fresh, MarketFlow.IdleTrickle);
            Assert.Less(fresh, 1d);
        }

        [Test]
        public void HireAtMaxLevel_IsExactlyOne()
        {
            // the whole "a finished yard never needs another visit" promise lands on this being 1.0
            Assert.That(MarketFlow.JobRate(Max), Is.EqualTo(1d).Within(1e-12));
        }

        [Test]
        public void HireLevelsClimbMonotonically()
        {
            double previous = MarketFlow.IdleTrickle;
            for (int level = 1; level <= Max; level++)
            {
                double rate = MarketFlow.JobRate(level);
                Assert.Greater(rate, previous, "level " + level + " should beat level " + (level - 1));
                previous = rate;
            }
        }

        [Test]
        public void HireLevelAboveMax_ClampsInsteadOfOverflowing()
        {
            Assert.That(MarketFlow.JobRate(Max + 50), Is.EqualTo(1d).Within(1e-12));
        }

        [Test]
        public void PartlyMaxed_IsNotMaxed()
        {
            Assert.IsFalse(MarketFlow.IsMaxed(Hires(Max, Max, Max - 1)));
            Assert.IsFalse(MarketFlow.IsMaxed(Hires(Max, 0, Max)));
            Assert.IsFalse(MarketFlow.IsMaxed(null));
        }

        [Test]
        public void EveryStateIsBetweenTheTrickleAndFullSpeed()
        {
            // nothing may ever stop an island earning altogether, or earn more than its yard can hold
            for (int c = 0; c <= Max; c++)
                for (int s = 0; s <= Max; s++)
                    for (int k = 0; k <= Max; k++)
                    {
                        double rate = MarketFlow.ServiceRate(Hires(c, s, k));
                        Assert.GreaterOrEqual(rate, MarketFlow.IdleTrickle);
                        Assert.LessOrEqual(rate, 1d);
                    }
        }

        // ---- capacity ---------------------------------------------------------------------------

        [Test]
        public void OneQueueSlot_KeepsUpWithTheIslandExactly()
        {
            // parity at the first slot, so staffing is the only throttle a new yard has
            Assert.That(MarketFlow.SellCapacityPerSecond(2d, 1), Is.EqualTo(2d).Within(1e-9));
        }

        [Test]
        public void ExtraQueueSlots_AreHeadroomAboveParity()
        {
            Assert.Greater(MarketFlow.SellCapacityPerSecond(2d, 2), 2d);
            Assert.That(MarketFlow.SellCapacityPerSecond(2d, MarketFlow.MaxQueueSlots),
                        Is.EqualTo(2d * (1d + 0.25d * (MarketFlow.MaxQueueSlots - 1))).Within(1e-9));
        }

        [Test]
        public void AFullyStaffedOneSlotYard_LosesNothingTheIslandSends()
        {
            // the regression that matters: queue length and staffing must not multiply into a nerf
            double capacity = MarketFlow.SellCapacityPerSecond(2d, 1);
            double rate = MarketFlow.ServiceRate(Hires(Max, Max, Max));
            Assert.That(capacity * rate, Is.EqualTo(2d).Within(1e-9));
        }

        [Test]
        public void AnEmptyOneSlotYard_RunsAtExactlyTheTrickle()
        {
            double capacity = MarketFlow.SellCapacityPerSecond(2d, 1);
            double rate = MarketFlow.ServiceRate(Hires(0, 0, 0));
            Assert.That(capacity * rate, Is.EqualTo(2d * MarketFlow.IdleTrickle).Within(1e-9));
        }

        [Test]
        public void CapacityScalesWithTheIsland_NotWithAbsoluteNumbers()
        {
            // a diamond island delivers orders of magnitude more than coal; the yard has to follow it
            double coal = MarketFlow.SellCapacityPerSecond(2d, 2);
            double diamond = MarketFlow.SellCapacityPerSecond(2d * 3.2d * 3.2d, 2);
            Assert.That(diamond / coal, Is.EqualTo(3.2d * 3.2d).Within(1e-9));
        }

        [Test]
        public void StockCapacity_IsMinutesOfDelivery()
        {
            // one pad = three minutes of what the island sends
            Assert.That(MarketFlow.StockCapacity(2d, 1), Is.EqualTo(2d * 60d * 3d).Within(1e-9));
            Assert.That(MarketFlow.StockCapacity(2d, MarketFlow.MaxDepositSlots),
                        Is.EqualTo(2d * 60d * 3d * MarketFlow.MaxDepositSlots).Within(1e-9));
        }

        [Test]
        public void SlotCountsClampToTheirCeilings()
        {
            Assert.That(MarketFlow.SellCapacityPerSecond(2d, 999),
                        Is.EqualTo(MarketFlow.SellCapacityPerSecond(2d, MarketFlow.MaxQueueSlots)).Within(1e-9));
            Assert.That(MarketFlow.StockCapacity(2d, 0),
                        Is.EqualTo(MarketFlow.StockCapacity(2d, 1)).Within(1e-9));
        }

        // ---- flow -------------------------------------------------------------------------------

        [Test]
        public void SellsWhatTheCounterCanMove_WhenThePadIsFull()
        {
            // capacity 2/s at rate 1.0 for half a second
            Assert.That(MarketFlow.SoldInTick(1000d, 2d, 1d, 0.5d), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void NeverSellsMoreThanIsOnThePad()
        {
            Assert.That(MarketFlow.SoldInTick(0.25d, 2d, 1d, 10d), Is.EqualTo(0.25d).Within(1e-9));
        }

        [Test]
        public void EmptyPadSellsNothing()
        {
            Assert.That(MarketFlow.SoldInTick(0d, 2d, 1d, 1d), Is.EqualTo(0d));
            Assert.That(MarketFlow.SoldInTick(-5d, 2d, 1d, 1d), Is.EqualTo(0d));
        }

        [Test]
        public void ServiceRateScalesTheSale()
        {
            double full = MarketFlow.SoldInTick(1000d, 2d, 1d, 1d);
            double trickle = MarketFlow.SoldInTick(1000d, 2d, MarketFlow.IdleTrickle, 1d);
            Assert.That(trickle, Is.EqualTo(full * MarketFlow.IdleTrickle).Within(1e-9));
        }

        [Test]
        public void AddStock_FillsToCapacityAndReportsTheSpill()
        {
            double overflow;
            double stock = MarketFlow.AddStock(90d, 30d, 100d, out overflow);
            Assert.That(stock, Is.EqualTo(100d).Within(1e-9));
            Assert.That(overflow, Is.EqualTo(20d).Within(1e-9));
        }

        [Test]
        public void AddStock_UnderCapacity_SpillsNothing()
        {
            double overflow;
            double stock = MarketFlow.AddStock(10d, 30d, 100d, out overflow);
            Assert.That(stock, Is.EqualTo(40d).Within(1e-9));
            Assert.That(overflow, Is.EqualTo(0d));
        }

        // ---- the loop the offline grant runs ------------------------------------------------------

        [Test]
        public void UnattendedYard_KeepsSellingButFallsBehind()
        {
            // one minute of a bare yard: deliveries arrive at 2/s, nobody is working
            const double supply = 2d;
            double capacity = MarketFlow.StockCapacity(supply, 1);
            double sellCap = MarketFlow.SellCapacityPerSecond(supply, 1);
            double rate = MarketFlow.ServiceRate(Hires(0, 0, 0));

            double stock = 0d, sold = 0d, spilled = 0d;
            for (int second = 0; second < 60; second++)
            {
                double overflow;
                stock = MarketFlow.AddStock(stock, supply, capacity, out overflow);
                spilled += overflow;
                double s = MarketFlow.SoldInTick(stock, sellCap, rate, 1d);
                stock -= s;
                sold += s;
            }

            Assert.Greater(sold, 0d, "an unattended yard must still earn something");
            Assert.Less(sold, supply * 60d, "and must fall behind what the island delivered");
            Assert.That(spilled, Is.EqualTo(0d), "three minutes of buffer should not overflow in one");
            Assert.Greater(stock, 0d, "the difference has to be sitting on the pad");
        }

        [Test]
        public void MaxedYard_KeepsUpWithItsIsland()
        {
            const double supply = 2d;
            double capacity = MarketFlow.StockCapacity(supply, MarketFlow.MaxDepositSlots);
            double sellCap = MarketFlow.SellCapacityPerSecond(supply, MarketFlow.MaxQueueSlots);
            double rate = MarketFlow.ServiceRate(Hires(Max, Max, Max));

            double stock = 0d, sold = 0d;
            for (int second = 0; second < 600; second++)
            {
                double overflow;
                stock = MarketFlow.AddStock(stock, supply, capacity, out overflow);
                double s = MarketFlow.SoldInTick(stock, sellCap, rate, 1d);
                stock -= s;
                sold += s;
            }

            // everything delivered gets sold, give or take the single tick in flight
            Assert.That(sold, Is.EqualTo(supply * 600d).Within(supply * 1.01d));
        }
    }
}
