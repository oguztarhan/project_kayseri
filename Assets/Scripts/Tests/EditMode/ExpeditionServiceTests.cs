using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Boarding and coming ashore, and the one guarantee the whole layer rests on: going to sea with
    /// a ship must not change anything about her.
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

        // ---- boarding ----------------------------------------------------------------------------

        [Test]
        public void AnEmptyBerthCannotBeBoarded()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());

            Assert.That(sea.CanBoard(0), Is.False);
            Assert.That(sea.Board(0), Is.False);
            Assert.That(sea.Active, Is.False);
        }

        [Test]
        public void AHoldStillFillingHasNowhereToTakeAnybody()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            dock.TryStart(Coal, 0);
            var sea = new ExpeditionService(dock, new TimeService());

            Assert.That(dock.At(0), Is.Not.Null);
            Assert.That(dock.At(0).sailedUnix, Is.Zero, "the premise: she has not sailed");
            Assert.That(sea.CanBoard(0), Is.False);
            Assert.That(sea.Board(0), Is.False);
        }

        [Test]
        public void AShipAtSeaCanBeBoardedAndLeft()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);

            Assert.That(sea.CanBoard(0), Is.True);
            Assert.That(sea.Board(0), Is.True);
            Assert.That(sea.Active, Is.True);
            Assert.That(sea.Berth, Is.Zero);
            Assert.That(sea.IslandKey, Is.EqualTo(Coal));

            sea.Ashore();
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.Berth, Is.EqualTo(-1));
        }

        [Test]
        public void ComingAshoreTwiceIsHarmless()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Assert.DoesNotThrow(() => { sea.Ashore(); sea.Ashore(); });
        }

        [Test]
        public void TheShipGoingHomeUnderneathEndsTheView()
        {
            // The berth can be claimed and re-let while the scene is open. Whatever is there now is
            // not the ship the player boarded, so the view has to notice rather than follow it.
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);
            sea.Board(0);
            Assert.That(sea.Active, Is.True);

            data.voyages[0].returnsUnix = 1L;
            dock.Tick(1f);
            dock.TryClaim(0);

            Assert.That(sea.Active, Is.False, "the boat she was standing on is gone");
            Assert.That(sea.Progress, Is.Zero);
            Assert.That(sea.SecondsLeft, Is.Zero);
        }

        [Test]
        public void ANullDockIsSurvivable()
        {
            var sea = new ExpeditionService(null, null);
            Assert.That(sea.CanBoard(0), Is.False);
            Assert.That(sea.Board(0), Is.False);
            Assert.That(sea.Active, Is.False);
            Assert.That(sea.Progress, Is.Zero);
            Assert.That(sea.LanePosition, Is.Zero);
            Assert.That(sea.Tier, Is.Zero);
            Assert.That(sea.IslandKey, Is.Empty);
            Assert.DoesNotThrow(() => sea.Ashore());
        }

        // ---- the guarantee -----------------------------------------------------------------------

        [Test]
        public void GoingToSeaWithHerChangesNothingAboutTheVoyage()
        {
            // Docs/FIVE_LAYERS.md §4, asserted rather than trusted. Every field of the voyage is
            // compared across a board / read / come-ashore cycle.
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);

            VoyageState v = dock.At(0);
            long sailed = v.sailedUnix, returns = v.returnsUnix;
            double held = v.held, hold = v.holdSize;
            int tier = v.tier, foreman = v.foreman, captain = v.captain;
            bool settled = v.settled;

            sea.Board(0);
            for (int i = 0; i < 200; i++)
            {
                double _ = sea.Progress + sea.LanePosition + sea.SecondsLeft;
                bool __ = sea.Outbound;
            }
            sea.Ashore();

            Assert.That(v.sailedUnix, Is.EqualTo(sailed));
            Assert.That(v.returnsUnix, Is.EqualTo(returns), "watching must not shorten the crossing");
            Assert.That(v.held, Is.EqualTo(held));
            Assert.That(v.holdSize, Is.EqualTo(hold));
            Assert.That(v.tier, Is.EqualTo(tier));
            Assert.That(v.foreman, Is.EqualTo(foreman));
            Assert.That(v.captain, Is.EqualTo(captain));
            Assert.That(v.settled, Is.EqualTo(settled));
        }

        [Test]
        public void ProgressAgreesWithTheVoyagesOwnClock()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var time = new TimeService();
            var sea = new ExpeditionService(dock, time);
            Sail(dock, market);
            sea.Board(0);

            VoyageState v = dock.At(0);
            double expected = Expedition.Progress(v.sailedUnix, v.returnsUnix, time.NowUnix());
            Assert.That(sea.Progress, Is.EqualTo(expected).Within(1e-6));
            Assert.That(sea.LanePosition, Is.EqualTo(Expedition.LanePosition(expected)).Within(1e-6));
            Assert.That(sea.Outbound, Is.EqualTo(Expedition.Outbound(expected)));
        }

        [Test]
        public void ANewlySailedShipIsAtTheHomePortAndOutbound()
        {
            SaveData data; MarketService market;
            VoyageService dock = Dock(out data, out market);
            var sea = new ExpeditionService(dock, new TimeService());
            Sail(dock, market);
            sea.Board(0);

            Assert.That(sea.Progress, Is.LessThan(0.05d));
            Assert.That(sea.LanePosition, Is.LessThan(0.1d));
            Assert.That(sea.Outbound, Is.True);
        }
    }
}
