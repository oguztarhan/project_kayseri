using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ProductionBottleneckTests
    {
        [Test]
        public void Find_ReturnsUnknownUntilMetersAreReady()
        {
            Assert.That(Find(ready: false), Is.EqualTo(ProductionBottleneck.Unknown));
        }

        [Test]
        public void Find_DoesNotDefaultToMineWhenOreFleetIsMeasuredSlower()
        {
            Assert.That(Find(mined: 100d, hauled: 60d, refined: 60d, delivered: 60d),
                Is.EqualTo(ProductionBottleneck.OreFleet));
        }

        [Test]
        public void Find_UsesMostDownstreamMeasuredRestriction()
        {
            Assert.That(Find(mined: 100d, hauled: 80d, refined: 60d, delivered: 30d),
                Is.EqualTo(ProductionBottleneck.CargoFleet));
        }

        [Test]
        public void Find_BackPressureOutranksRateNoise()
        {
            Assert.That(Find(mined: 100d, hauled: 50d, refined: 40d, delivered: 35d,
                             barStoreFull: 30d),
                Is.EqualTo(ProductionBottleneck.CargoFleet));
        }

        [Test]
        public void Find_MarketOverflowIsTheFinalBottleneck()
        {
            Assert.That(Find(yardFull: 60d, furnaceQueue: 60d, barStoreFull: 60d, overflow: 4d),
                Is.EqualTo(ProductionBottleneck.Market));
        }

        /// <summary>
        /// The bug this report was reworked for: a chain that carries the same rate end to end
        /// because the yard is jammed, sampled at a moment the level happens to be low. Only the
        /// clock says the trucks cannot clear it — the rates never will.
        /// </summary>
        [Test]
        public void Find_BlamesOreFleetWhenTheYardSpendsTheMinuteFull()
        {
            Assert.That(Find(mined: 60d, hauled: 60d, refined: 60d, delivered: 60d, yardFull: 45d),
                Is.EqualTo(ProductionBottleneck.OreFleet));
        }

        /// <summary>A pile that touches its ceiling between two truckloads is not a bottleneck.</summary>
        [Test]
        public void Find_IgnoresBuffersThatAreOnlyBrieflyFull()
        {
            Assert.That(Find(mined: 100d, hauled: 95d, refined: 92d, delivered: 90d,
                             yardFull: 2d, furnaceQueue: 2d, barStoreFull: 2d),
                Is.EqualTo(ProductionBottleneck.Source));
        }

        [Test]
        public void Find_ReturnsSourceOnlyWhenChainIsBalancedAndClear()
        {
            Assert.That(Find(mined: 100d, hauled: 95d, refined: 92d, delivered: 90d),
                Is.EqualTo(ProductionBottleneck.Source));
        }

        private static int Find(
            bool ready = true,
            double mined = 100d,
            double hauled = 100d,
            double refined = 100d,
            double delivered = 100d,
            double yardFull = 0d,
            double furnaceQueue = 0d,
            double barStoreFull = 0d,
            double overflow = 0d)
            => ProductionBottleneck.Find(ready, mined, hauled, refined, delivered,
                                         yardFull, furnaceQueue, barStoreFull, overflow);
    }
}
