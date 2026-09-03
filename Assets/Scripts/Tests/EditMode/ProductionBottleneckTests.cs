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

        // ---- the banner's chip: back-pressure only -----------------------------------------------

        /// <summary>
        /// The whole reason Blocked exists. Find always names a stage, so a chip wired to it would
        /// read BOTTLENECK: MINE on every healthy island forever, which is how a warning stops being
        /// read. A balanced chain must light nothing.
        /// </summary>
        [Test]
        public void Blocked_IsUnknownOnAChainThatIsMerelySupplyLimited()
        {
            Assert.That(Find(mined: 100d, hauled: 95d, refined: 92d, delivered: 90d),
                        Is.EqualTo(ProductionBottleneck.Source), "Find still answers");
            Assert.That(ProductionBottleneck.Blocked(0d, 0d, 0d, 0d),
                        Is.EqualTo(ProductionBottleneck.Unknown), "the chip stays dark");
        }

        [Test]
        public void Blocked_IgnoresBuffersThatAreOnlyBrieflyFull()
        {
            Assert.That(ProductionBottleneck.Blocked(2d, 2d, 2d, 0d),
                        Is.EqualTo(ProductionBottleneck.Unknown));
        }

        [Test]
        public void Blocked_NamesEachStageItsOwnFullPilePointsAt()
        {
            Assert.That(ProductionBottleneck.Blocked(45d, 0d, 0d, 0d),
                        Is.EqualTo(ProductionBottleneck.OreFleet));
            Assert.That(ProductionBottleneck.Blocked(0d, 45d, 0d, 0d),
                        Is.EqualTo(ProductionBottleneck.Smelter));
            Assert.That(ProductionBottleneck.Blocked(0d, 0d, 45d, 0d),
                        Is.EqualTo(ProductionBottleneck.CargoFleet));
            Assert.That(ProductionBottleneck.Blocked(0d, 0d, 0d, 4d),
                        Is.EqualTo(ProductionBottleneck.Market));
        }

        /// <summary>
        /// Read from the market backwards: a yard is only ever full because the leg after it cannot
        /// clear it, so the downstream cause has to win over every pile it backs up behind itself.
        /// </summary>
        [Test]
        public void Blocked_LetsTheDownstreamCauseWinOverThePilesItCreates()
        {
            Assert.That(ProductionBottleneck.Blocked(60d, 60d, 60d, 4d),
                        Is.EqualTo(ProductionBottleneck.Market));
            Assert.That(ProductionBottleneck.Blocked(60d, 60d, 60d, 0d),
                        Is.EqualTo(ProductionBottleneck.CargoFleet));
            Assert.That(ProductionBottleneck.Blocked(60d, 60d, 0d, 0d),
                        Is.EqualTo(ProductionBottleneck.Smelter));
        }

        /// <summary>
        /// The two must not drift: whenever Blocked answers, that answer IS Find's, because Find's
        /// back-pressure half is now this function and nothing else.
        /// </summary>
        [Test]
        public void Blocked_AgreesWithFindWheneverItAnswers()
        {
            double[] clocks = { 0d, 2d, 6d, 45d };
            for (int y = 0; y < clocks.Length; y++)
                for (int f = 0; f < clocks.Length; f++)
                    for (int b = 0; b < clocks.Length; b++)
                        for (int o = 0; o < clocks.Length; o++)
                        {
                            int wall = ProductionBottleneck.Blocked(clocks[y], clocks[f], clocks[b], clocks[o]);
                            if (wall == ProductionBottleneck.Unknown) continue;
                            Assert.That(Find(yardFull: clocks[y], furnaceQueue: clocks[f],
                                             barStoreFull: clocks[b], overflow: clocks[o]),
                                        Is.EqualTo(wall),
                                        "y" + clocks[y] + " f" + clocks[f] + " b" + clocks[b] + " o" + clocks[o]);
                        }
        }

        /// <summary>
        /// The report's rows and IslandEconomy's stations are two different numberings — the report
        /// groups the mine with its railway and gives the storage shed a line of its own, so row 2 is
        /// ORE TRUCKS while station 2 is STORAGE. Pinned by NAME so a re-cut station list fails here
        /// rather than mislabelling the chip.
        /// </summary>
        [Test]
        public void StationOf_MapsEveryRowOntoTheRightStation()
        {
            Assert.That(Name(ProductionBottleneck.Source), Is.EqualTo("MINE"));
            Assert.That(Name(ProductionBottleneck.OreFleet), Is.EqualTo("ORE TRUCKS"));
            Assert.That(Name(ProductionBottleneck.Smelter), Is.EqualTo("SMELTER"));
            Assert.That(Name(ProductionBottleneck.CargoFleet), Is.EqualTo("CARGO TRUCKS"));
            Assert.That(Name(ProductionBottleneck.Market), Is.EqualTo("MARKET"));
        }

        [Test]
        public void StationOf_HasNoStationForAnUnknownRow()
        {
            Assert.That(ProductionBottleneck.StationOf(ProductionBottleneck.Unknown), Is.EqualTo(-1));
            Assert.That(ProductionBottleneck.StationOf(1), Is.EqualTo(-1));   // STORAGE is not a verdict
            Assert.That(ProductionBottleneck.StationOf(99), Is.EqualTo(-1));
        }

        private static string Name(int row)
        {
            int station = ProductionBottleneck.StationOf(row);
            Assert.That(station, Is.InRange(0, IslandEconomy.Stations.Length - 1), "row " + row);
            return IslandEconomy.Stations[station];
        }
    }
}
