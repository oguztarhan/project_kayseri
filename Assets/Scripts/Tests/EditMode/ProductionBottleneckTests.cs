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
                             barFraction: 0.95d),
                Is.EqualTo(ProductionBottleneck.CargoFleet));
        }

        [Test]
        public void Find_MarketOverflowIsTheFinalBottleneck()
        {
            Assert.That(Find(storageFraction: 1d, queue: 100d, barFraction: 1d, overflow: 4d),
                Is.EqualTo(ProductionBottleneck.Market));
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
            double storageFraction = 0d,
            double queue = 0d,
            double sixSeconds = 18d,
            double barFraction = 0d,
            double overflow = 0d)
            => ProductionBottleneck.Find(ready, mined, hauled, refined, delivered,
                                         storageFraction, queue, sixSeconds, barFraction, overflow);
    }
}
