using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Putting out from the island's port and coming ashore. The ship is the player's own now — no
    /// berth, no boarding rules — so the guarantee the old boarding tests held one assert at a time
    /// is held by distance instead: a trip never touches the dock's voyages at all, and the last
    /// test still proves it field by field.
    /// </summary>
    public class ExpeditionServiceTests
    {
        private const string Coal = "coal";
        private const double NoCeiling = 1e12d;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static VoyageService Dock(out SaveData data, out MarketService market)
        {
            data = new SaveData();
            var wallet = new WalletService(data.wallet);
            market = new MarketService(data, wallet, null);
            market.Register(Coal, new Terms { BarPriceRaw = 10d, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);
            market.Row(Coal).deliveredPerMin = 600d;
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            return new VoyageService(data, market, foremen, wallet, new TimeService(),
                                     Voyages.Tuning.Default);
        }

        private static void Sail(VoyageService dock, MarketService market)
        {
            if (dock.At(0) == null) dock.TryStart(Coal, 0);
            market.Deliver(Coal, dock.At(0).holdSize * 2d);
            dock.Tick((float)Voyages.SecondsToFill(0, Voyages.Tuning.Default) + 1f);
        }

        // ---- the session ---------------------------------------------------------------------

        [Test]
        public void AshoreUntilSheSails()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.Progress, Is.Zero);
            Assert.That(sea.SecondsLeft, Is.Zero);
            Assert.That(sea.SailedUnix, Is.Zero);
            Assert.That(sea.IslandKey, Is.Empty);
        }

        [Test]
        public void SettingSailOpensTheTripFromThatPort()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.That(sea.SetSail(Coal), Is.True);
            Assert.That(sea.Active, Is.True);
            Assert.That(sea.IslandKey, Is.EqualTo(Coal));
            Assert.That(sea.SailedUnix, Is.GreaterThan(0L));
            Assert.That(sea.Finds, Is.Zero);

            sea.Ashore();
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.IslandKey, Is.Empty);
        }

        [Test]
        public void AskingAgainMidTripChangesNothing()
        {
            // A double tap, or a second entry point racing the first: the answer is yes, and the
            // trip already underway — its seed, its port, its finds — is the one that continues.
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);
            long stamp = sea.SailedUnix;
            sea.CountFind();

            Assert.That(sea.SetSail("iron"), Is.True);
            Assert.That(sea.IslandKey, Is.EqualTo(Coal));
            Assert.That(sea.SailedUnix, Is.EqualTo(stamp));
            Assert.That(sea.Finds, Is.EqualTo(1));
        }

        [Test]
        public void ComingAshoreTwiceIsHarmless()
        {
            var sea = new ExpeditionService(null, new TimeService());
            Assert.DoesNotThrow(() => { sea.Ashore(); sea.Ashore(); });
        }

        [Test]
        public void EveryTripDealsItsOwnDeck()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.CountFind();
            Assert.That(sea.Finds, Is.Zero, "no finds ashore — there is no trip to count them into");

            sea.SetSail(Coal);
            sea.CountFind();
            sea.CountFind();
            Assert.That(sea.Finds, Is.EqualTo(2));

            sea.Ashore();
            sea.SetSail(Coal);
            Assert.That(sea.Finds, Is.Zero, "a new trip starts its seed index over");
        }

        [Test]
        public void ANullDockIsSurvivable()
        {
            // Combat is a port activity now: with no dock wired at all she still sails, and the
            // fights are simply priced for the first route.
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.Tier, Is.Zero);
            Assert.That(sea.SetSail(Coal), Is.True);
            Assert.That(sea.Active, Is.True);
            Assert.That(sea.Progress, Is.GreaterThanOrEqualTo(0d).And.LessThan(1d));
            Assert.DoesNotThrow(() => sea.Ashore());
        }

        // ---- the patrol ----------------------------------------------------------------------

        [Test]
        public void ANewlySailedShipIsAtTheHomePortAndOutbound()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);

            Assert.That(sea.Progress, Is.LessThan(0.05d));
            Assert.That(sea.LanePosition, Is.LessThan(0.1d));
            Assert.That(sea.Outbound, Is.True);
            Assert.That(sea.SecondsLeft, Is.GreaterThan(0d));
        }

        [Test]
        public void ThePatrolStaysOnTheLane()
        {
            var sea = new ExpeditionService(null, new TimeService());
            sea.SetSail(Coal);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(sea.Progress, Is.InRange(0d, 1d));
                Assert.That(sea.LanePosition, Is.InRange(0d, 1d));
            }
        }

        // ---- the dock, at arm's length -------------------------------------------------------

        [Test]
        public void TheFightsArePricedForTheFurthestOpenRoute()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());

            Assert.That(sea.Tier, Is.Zero, "a fresh account fights in the first waters");

            data.voyagesCompleted = 999;   // every route long since opened
            Assert.That(dock.MaxTier(), Is.GreaterThan(0), "the premise: the ladder actually opened");
            Assert.That(sea.Tier, Is.EqualTo(dock.MaxTier()),
                        "combat climbs the same ladder the voyages climb");
        }

        [Test]
        public void SailingNeverTouchesTheDock()
        {
            // The point of moving the entry to the port, asserted rather than trusted: a whole
            // trip — out, read everything, ashore — leaves a running voyage exactly as it was.
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);

            VoyageState v = dock.At(0);
            long sailed = v.sailedUnix, returns = v.returnsUnix;
            double held = v.held, hold = v.holdSize;
            int tier = v.tier, foreman = v.foreman, captain = v.captain;
            bool settled = v.settled;

            sea.SetSail(Coal);
            for (int i = 0; i < 200; i++)
            {
                double _ = sea.Progress + sea.LanePosition + sea.SecondsLeft + sea.Tier;
                bool __ = sea.Outbound;
            }
            sea.Ashore();

            Assert.That(v.sailedUnix, Is.EqualTo(sailed));
            Assert.That(v.returnsUnix, Is.EqualTo(returns), "sailing must not shorten the crossing");
            Assert.That(v.held, Is.EqualTo(held));
            Assert.That(v.holdSize, Is.EqualTo(hold));
            Assert.That(v.tier, Is.EqualTo(tier));
            Assert.That(v.foreman, Is.EqualTo(foreman));
            Assert.That(v.captain, Is.EqualTo(captain));
            Assert.That(v.settled, Is.EqualTo(settled));
        }
    }
}
