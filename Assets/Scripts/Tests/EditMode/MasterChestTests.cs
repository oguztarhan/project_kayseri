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

        // ---- the slot roll -----------------------------------------------------------------------

        [Test]
        public void EverySlotIsAlwaysReachable()
        {
            for (int i = 0; i <= 1000; i++)
                Assert.That(MasterChest.RollSlot(i / 1000d), Is.InRange(0, Foremen.Count - 1));
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => MasterChest.RollSlot(double.NaN));
            Assert.That(MasterChest.RollSlot(-5d), Is.Zero);
            Assert.That(MasterChest.RollSlot(1d), Is.EqualTo(Foremen.Count - 1));
            Assert.That(MasterChest.RollSlot(double.NaN), Is.InRange(0, Foremen.Count - 1));
        }

        [Test]
        public void TheSlotDistributionIsFlat()
        {
            // Sweep the unit interval rather than sampling: the share of the interval landing on a slot
            // IS its probability. Every master must be equally reachable — rarity in this system is how
            // far you have taken a master, never which card dropped.
            const int n = 80000;
            var count = new int[Foremen.Count];
            for (int i = 0; i < n; i++) count[MasterChest.RollSlot((i + 0.5d) / n)]++;

            double expected = n / (double)Foremen.Count;
            for (int s = 0; s < Foremen.Count; s++)
                Assert.That(count[s], Is.EqualTo(expected).Within(expected * 0.02d), "slot " + s);
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
