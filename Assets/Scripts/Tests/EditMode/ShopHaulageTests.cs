using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ShopHaulageTests
    {
        [Test]
        public void SavedDeliveriesAndUnchangedProductionDoNotDispatch()
        {
            var route = new ShopHaulage(10, 20, 1);
            route.Observe(500);
            route.Observe(500);
            route.Tick(1000);
            Assert.AreEqual(0, route.Delivered);
            Assert.AreEqual(ShopHaulage.Phase.Idle, route.First);
        }

        [Test]
        public void ParcelMustUnloadAtDepotBeforeSecondCarrierStarts()
        {
            var route = new ShopHaulage(10, 20, 1);
            route.Baseline(0);
            route.Observe(1);
            route.Tick(11);
            Assert.AreEqual(ShopHaulage.Phase.Unloading, route.First);
            Assert.AreEqual(ShopHaulage.Phase.Idle, route.Second);
            route.Tick(1);
            Assert.AreEqual(ShopHaulage.Phase.Returning, route.First);
            Assert.AreEqual(ShopHaulage.Phase.Loading, route.Second);
            Assert.AreEqual(0, route.Delivered);
            route.Tick(21);
            Assert.AreEqual(ShopHaulage.Phase.Unloading, route.Second);
            Assert.AreEqual(0, route.Delivered);
            route.Tick(1);
            Assert.AreEqual(1, route.Delivered);
            Assert.AreEqual(ShopHaulage.Phase.Returning, route.Second);
            route.Tick(1000);
            Assert.AreEqual(1, route.Delivered);
            Assert.AreEqual(ShopHaulage.Phase.Idle, route.Second);
        }

        [Test]
        public void BurstsAndSlowHarbourCannotOverflowDepot()
        {
            var route = new ShopHaulage(1, 100, 1);
            route.Baseline(0);
            for (int i = 1; i <= 1000; i++)
            {
                route.Observe(i * 1000);
                route.Tick(1);
                Assert.That(route.Waiting, Is.InRange(0, ShopHaulage.Capacity));
                Assert.That(route.AtDepot, Is.InRange(0, ShopHaulage.Capacity));
            }
        }

        [Test]
        public void LargeAndSplitStepsPreserveHandoffTiming()
        {
            var whole = new ShopHaulage(10, 20, 1);
            var split = new ShopHaulage(10, 20, 1);
            whole.Baseline(0); split.Baseline(0);
            whole.Observe(3); split.Observe(3);
            whole.Tick(80.5);
            for (int i = 0; i < 161; i++) split.Tick(0.5);
            Assert.AreEqual(whole.Delivered, split.Delivered);
            Assert.AreEqual(whole.AtDepot, split.AtDepot);
            Assert.AreEqual(whole.First, split.First);
            Assert.AreEqual(whole.Second, split.Second);
            Assert.AreEqual(whole.Progress(false), split.Progress(false), 1e-9);
        }

        [Test]
        public void OfflineBaselineDoesNotReplayOldProduction()
        {
            var route = new ShopHaulage(10, 20, 1);
            route.Baseline(1);
            route.Baseline(1000);
            route.Observe(1000);
            route.Tick(1000);
            Assert.AreEqual(0, route.Delivered);
            route.Observe(1001);
            route.Tick(1000);
            Assert.AreEqual(1, route.Delivered);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidElapsedTimeCannotMoveCargo(double seconds)
        {
            var route = new ShopHaulage(10, 20, 1);
            route.Baseline(0);
            route.Observe(1);
            route.Tick(seconds);
            Assert.AreEqual(1, route.Waiting);
            Assert.AreEqual(ShopHaulage.Phase.Idle, route.First);
        }
    }
}
