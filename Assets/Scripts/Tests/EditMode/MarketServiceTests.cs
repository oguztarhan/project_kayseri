using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The ledger end to end, with no scene: deliveries in, sales out, and what an absence does to a
    /// yard. <see cref="MarketService"/> takes plain C# collaborators and every one of them tolerates
    /// null, so the whole of the AFK and offline behaviour can be driven from here rather than by
    /// leaving the game running and watching.
    /// </summary>
    public class MarketServiceTests
    {
        private const string Coal = "coal";
        private const double Price = 10d;
        private const double NoCeiling = 1e12d;     // high enough that the income cap never bites

        /// <summary>A price list, standing in for the island that would normally supply one.</summary>
        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
        }

        private static MarketService Build(out SaveData data, out WalletService wallet,
                                           double capPerMinute = NoCeiling)
        {
            data = new SaveData();
            wallet = new WalletService(data.wallet);
            // Prestige and boost are left null on purpose: the service treats their absence as x1, and
            // a test that had to construct them would be testing them too.
            var market = new MarketService(data, wallet, null, null);
            market.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = capPerMinute });
            return market;
        }

        /// <summary>Two bars a second arriving, which is what every capacity below is measured against.</summary>
        private static void SetSupply(MarketService market, double barsPerMinute)
            => market.Row(Coal).deliveredPerMin = barsPerMinute;

        private static void Staff(MarketService market, int level)
        {
            MarketYard row = market.Row(Coal);
            row.hireCarry = row.hireServe = row.hireCollect = level;
        }

        // ---- the money path ---------------------------------------------------------------------

        [Test]
        public void Delivery_ThenTick_PaysTheWallet()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);                 // 2 bars a second
            market.SetActiveIsland(Coal);            // its lorries are running, so Deliver is the only supply

            market.Deliver(Coal, 50d);
            market.Tick(1f);

            // bare yard: capacity 2/s at the 0.15 trickle = 0.3 bars, at $10 a bar
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(3d).Within(1e-6));
            Assert.That(market.Stock(Coal), Is.EqualTo(49.7d).Within(1e-6));
        }

        [Test]
        public void MaxedYard_SellsEverythingItsIslandSends()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);
            Staff(market, MarketFlow.MaxHireLevel);

            // no active island, so the yard is fed by its own measured rate — the "you are elsewhere" case
            for (int second = 0; second < 60; second++) market.Tick(1f);

            // 120 bars arrived over the minute and a staffed yard keeps up, give or take one tick
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(120d * Price).Within(2d * Price));
            Assert.Less(market.Stock(Coal), 2.5d);
        }

        [Test]
        public void UnstaffedYard_KeepsEarningButFallsBehind()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);

            for (int second = 0; second < 60; second++) market.Tick(1f);

            Assert.Greater(wallet.Cash.ToDouble(), 0d, "an unattended yard must still earn something");
            Assert.Less(wallet.Cash.ToDouble(), 120d * Price, "and must fall behind what arrived");
            Assert.Greater(market.Stock(Coal), 0d, "the difference has to be sitting on the pads");
        }

        [Test]
        public void IncomeCap_ClampsWhatAYardCanEarn()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet, capPerMinute: 25d);
            SetSupply(market, 6000d);                // far more than the ceiling allows
            Staff(market, MarketFlow.MaxHireLevel);

            for (int second = 0; second < 60; second++) market.Tick(1f);

            // the ceiling is measured against a trailing minute, so a minute of ticks may not exceed it
            Assert.LessOrEqual(wallet.Cash.ToDouble(), 25d + 1e-6);
        }

        // ---- selling by hand --------------------------------------------------------------------

        [Test]
        public void HandSale_MakesMoneyButDoesNotBankIt()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            market.Deliver(Coal, 10d);

            double paid = market.SellByHand(Coal, 1d);

            Assert.That(paid, Is.EqualTo(Price).Within(1e-6));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(0d), "it is lying on the yard floor, not in the wallet");

            market.Collect(Coal, paid);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(Price).Within(1e-6));
        }

        [Test]
        public void TakeFromStock_NeverHandsOutMoreThanIsThere()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            market.Deliver(Coal, 2.5d);

            Assert.That(market.TakeFromStock(Coal, 1d), Is.EqualTo(1d).Within(1e-9));
            Assert.That(market.TakeFromStock(Coal, 5d), Is.EqualTo(1.5d).Within(1e-9), "only what was left");
            Assert.That(market.TakeFromStock(Coal, 1d), Is.EqualTo(0d));
            Assert.That(market.Stock(Coal), Is.EqualTo(0d).Within(1e-9));
        }

        // ---- the launch guard -------------------------------------------------------------------

        [Test]
        public void FreshLaunch_KeepsThePersistedRate_UntilTheMeterIsWorthBelieving()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            // what the previous session measured and saved
            data.islandRates.Add(new IslandRate { id = Coal, perMin = 600d });

            market.Tick(1f);

            // The regression this exists for: the live meter honestly reads zero for the first seconds
            // of every launch, and that zero used to be written straight into the figure the NEXT
            // launch's offline grant is computed from.
            Assert.That(market.RatePerMin(Coal), Is.EqualTo(600d).Within(1e-9));
            Assert.That(data.incomeRatePerSec, Is.EqualTo(10d).Within(1e-9));
        }

        [Test]
        public void OnceTheWindowFills_TheLiveMeterTakesOver()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            data.islandRates.Add(new IslandRate { id = Coal, perMin = 600d });
            SetSupply(market, 120d);
            Staff(market, MarketFlow.MaxHireLevel);

            for (int second = 0; second < 60; second++) market.Tick(1f);

            // 120 bars a minute at $10 is 1200/min, and the meter should now be saying so rather than
            // repeating the stale 600 it launched with
            Assert.That(market.RatePerMin(Coal), Is.EqualTo(1200d).Within(60d));
        }

        // ---- the boost, spent on the island's clock ----------------------------------------------
        //
        //  A rewarded x2 on the island being simulated buys TIME rather than price: the whole chain
        //  runs at x2 and delivers twice the bars, so the player can watch the ad working instead of
        //  reading about it on the top bar. MarketService.IslandTimeScale is the contract, and these
        //  two tests are the two ways it can go wrong — paying the boost twice, and banking it.

        /// <summary>
        /// Runs a live island's yard for a minute and reports what it earned and what it persisted.
        /// <paramref name="simSpeed"/> is what the island's lorries do about the boost: at x2 the
        /// island's clock is running twice as fast, so twice as many bars arrive per real second.
        /// </summary>
        private static void RunLiveIsland(double boostMult, double simSpeed,
                                          out double cash, out double savedRate,
                                          double permanentMult = 1d)
        {
            var data = new SaveData();
            data.stationSpeedMultiplier = permanentMult;
            var wallet = new WalletService(data.wallet);
            BoostService boost = null;
            if (boostMult > 1d || permanentMult > 1d)
            {
                boost = new BoostService(data, new TimeService());
                boost.AddBoost(boostMult, 3600d);     // far longer than the minute below takes to run
            }
            var market = new MarketService(data, wallet, null, boost);
            market.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = NoCeiling });
            market.SetActiveIsland(Coal);             // its lorries are running, so Deliver is the supply
            Staff(market, MarketFlow.MaxHireLevel);   // a yard that keeps up, so the counter is not the wall

            // One tick before the lorries start, because the speed is latched once a second: the island
            // reads it and this ledger divides it back out, and they have to be looking at the same value.
            market.Tick(1f);
            for (int second = 0; second < 120; second++)
            {
                market.Deliver(Coal, 2d * simSpeed);  // 2 bars a second, doubled when the clock is
                market.Tick(1f);
            }

            cash = wallet.Cash.ToDouble();
            savedRate = market.Row(Coal).deliveredPerMin;
        }

        [Test]
        public void BoostedIsland_IsWorthExactlyTwice_NotFourTimes()
        {
            double plainCash, plainRate;
            RunLiveIsland(1d, 1d, out plainCash, out plainRate);

            double boostCash, boostRate;
            RunLiveIsland(2d, 2d, out boostCash, out boostRate);

            // The failure this guards is the obvious one: leaving the price multiplier on while the
            // island's clock also runs at x2 pays for the same ad twice and quadruples the reward.
            Assert.That(boostCash, Is.EqualTo(plainCash * 2d).Within(plainCash * 0.02d),
                        "a x2 must be worth x2 whether it is spent on price or on time");
            Assert.Greater(plainCash, 0d, "the control has to have earned something to be a control");
        }

        [Test]
        public void BoostedIsland_NeverBanksItsBoostedRate()
        {
            double plainCash, plainRate;
            RunLiveIsland(1d, 1d, out plainCash, out plainRate);

            double boostCash, boostRate;
            RunLiveIsland(2d, 2d, out boostCash, out boostRate);

            // deliveredPerMin is what feeds this island's yard once the player sails away, and the rate
            // beside it is what the NEXT launch's offline grant is paid from. A five-minute ad that left
            // either of them reading double would go on paying double for as long as the player stayed
            // off the island — which is a rewarded ad that rewards forever.
            // Pinned to the arithmetic rather than to each other: two rates that were both silently zero
            // would agree perfectly and prove nothing. 2 bars a second is 120 a minute, boost or no boost.
            Assert.That(plainRate, Is.EqualTo(120d).Within(1d), "the control measured the wrong thing");
            Assert.That(boostRate, Is.EqualTo(120d).Within(1d),
                        "the persisted delivery rate must describe an unboosted island");
        }

        [Test]
        public void PermanentStationSpeed_DoublesIncomeButKeepsTheDeliveryRateClean()
        {
            double plainCash, plainRate;
            RunLiveIsland(1d, 1d, out plainCash, out plainRate);

            double patronCash, patronRate;
            RunLiveIsland(1d, 2d, out patronCash, out patronRate, 2d);

            Assert.That(patronCash, Is.EqualTo(plainCash * 2d).Within(plainCash * 0.02d));
            Assert.That(patronRate, Is.EqualTo(plainRate).Within(1d),
                        "permanent speed belongs in income, not in the stored physical delivery baseline");
        }

        // ---- what an absence does ---------------------------------------------------------------

        [Test]
        public void SettleOffline_BuriesAnUnstaffedYard()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);                 // 2/s in, 0.3/s out

            market.SettleOffline(3600L);             // an hour away

            // three minutes of buffer on one pad, so an hour of neglect fills it and stops
            double capacity = MarketFlow.StockCapacity(2d, 1);
            Assert.That(market.Stock(Coal), Is.EqualTo(capacity).Within(1e-6));
            Assert.That(market.StockFraction(Coal), Is.EqualTo(1d).Within(1e-6));
        }

        [Test]
        public void SettleOffline_LeavesAStaffedYardClear()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);
            Staff(market, MarketFlow.MaxHireLevel);

            market.SettleOffline(3600L);

            // this is the promise: come back to a finished yard and it has been coping without you
            Assert.That(market.Stock(Coal), Is.EqualTo(0d).Within(1e-6));
        }

        [Test]
        public void SettleOffline_PaysNothing()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);

            market.SettleOffline(3600L);

            // the welcome-back grant already paid for the absence off the persisted rate; paying again
            // here would be the same hour of work bought twice
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(0d));
        }

        [Test]
        public void SettleOffline_IgnoresAClockRolledBackwards()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            SetSupply(market, 120d);
            market.Deliver(Coal, 20d);

            market.SettleOffline(-9999L);

            Assert.That(market.Stock(Coal), Is.EqualTo(20d).Within(1e-9));
        }

        [Test]
        public void AnIslandYouDoNotOwn_NeverSettles()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet);
            market.Register("copper", new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = NoCeiling });
            market.Row("copper").deliveredPerMin = 120d;

            for (int second = 0; second < 30; second++) market.Tick(1f);

            Assert.That(market.Stock("copper"), Is.EqualTo(0d), "an unbought island has no yard to fill");
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(0d));

            data.unlockedIslands.Add("copper");
            for (int second = 0; second < 30; second++) market.Tick(1f);
            Assert.Greater(wallet.Cash.ToDouble(), 0d, "buying the island opens its yard");
        }

        // ---- buying -----------------------------------------------------------------------------

        [Test]
        public void TryBuy_SpendsTheWalletAndRaisesTheTrack()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet, capPerMinute: 1000d);
            wallet.AddCash(new BigDouble(1e9));

            double cost = market.Cost(Coal, YardUpgrade.HireCarry);
            double before = wallet.Cash.ToDouble();

            Assert.IsTrue(market.TryBuy(Coal, YardUpgrade.HireCarry));
            Assert.That(market.Level(Coal, YardUpgrade.HireCarry), Is.EqualTo(1));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(before - cost).Within(1e-3));
        }

        [Test]
        public void TryBuy_RefusesWhenTheWalletIsShort()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet, capPerMinute: 1000d);

            Assert.IsFalse(market.TryBuy(Coal, YardUpgrade.HireCarry));
            Assert.That(market.Level(Coal, YardUpgrade.HireCarry), Is.EqualTo(0));
        }

        [Test]
        public void TryBuy_RefusesAFinishedTrack()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet, capPerMinute: 1000d);
            wallet.AddCash(new BigDouble(1e12));

            for (int i = 0; i < MarketFlow.MaxHireLevel; i++)
                Assert.IsTrue(market.TryBuy(Coal, YardUpgrade.HireServe), "step " + i + " should sell");

            Assert.IsFalse(market.TryBuy(Coal, YardUpgrade.HireServe));
            Assert.IsTrue(market.IsTrackMaxed(Coal, YardUpgrade.HireServe));
        }

        [Test]
        public void StaffingEveryJob_MakesTheYardAutomatic()
        {
            SaveData data; WalletService wallet;
            MarketService market = Build(out data, out wallet, capPerMinute: 1000d);
            wallet.AddCash(new BigDouble(1e12));

            Assert.IsFalse(market.IsMaxed(Coal));
            foreach (YardUpgrade job in new[] { YardUpgrade.HireCarry, YardUpgrade.HireServe, YardUpgrade.HireCollect })
                for (int i = 0; i < MarketFlow.MaxHireLevel; i++)
                    market.TryBuy(Coal, job);

            Assert.IsTrue(market.IsMaxed(Coal));
            Assert.That(market.ServiceRate(Coal), Is.EqualTo(1d).Within(1e-9));
        }
    }
}
