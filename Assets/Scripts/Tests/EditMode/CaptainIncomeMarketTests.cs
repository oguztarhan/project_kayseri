using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    public class CaptainIncomeMarketTests
    {
        private const string Coal = "coal";
        private const double Price = 10d;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw { get; set; }
            public double IncomeCapPerMinuteRaw { get; set; }
            public double UpgradeTreeCostRaw { get; set; }
        }

        private static MarketService Build(int level, double cap, out SaveData data,
                                           out WalletService wallet, out CaptainService captains)
        {
            data = new SaveData();
            wallet = new WalletService(data.wallet);
            data.captainLevels[0] = level;
            captains = new CaptainService(data, Captains.Tuning.Default,
                                          CaptainCrate.Tuning.Default);
            var market = new MarketService(data, wallet, null, null, null, null, null, null, captains);
            market.Register(Coal, new Terms { BarPriceRaw = Price, IncomeCapPerMinuteRaw = cap });
            market.Product(Coal).deliveredPerMin = 120d;
            return market;
        }

        [Test]
        public void PayoutAndBarPriceUseTheSameCaptainMultiplier()
        {
            SaveData data; WalletService wallet; CaptainService captains;
            MarketService market = Build(Captains.MaxLevel, 1e12d, out data, out wallet, out captains);
            market.SetActiveIsland(Coal);
            market.Deliver(Coal, MarketService.ProductFor(Coal), 50d);
            market.Tick(1f);

            Assert.That(captains.IncomeMultiplier, Is.EqualTo(5d).Within(1e-9));
            Assert.That(market.BarPrice(Coal), Is.EqualTo(Price * 5d).Within(1e-9));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(3d * 5d).Within(1e-9));
        }

        [Test]
        public void IncomeCapIncludesCaptainMultiplier()
        {
            SaveData data; WalletService wallet; CaptainService captains;
            MarketService market = Build(Captains.MaxLevel, 10d, out data, out wallet, out captains);
            market.SetActiveIsland(Coal);
            market.Deliver(Coal, MarketService.ProductFor(Coal), 50d);
            for (int second = 0; second < 15; second++)
            {
                market.Tick(1f);
                market.Deliver(Coal, MarketService.ProductFor(Coal), 50d);
            }

            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(50d).Within(1e-9));
            Assert.That(data.incomeRatePerSec, Is.EqualTo(50d / 60d).Within(1e-9));
        }

        [Test]
        public void OfflineRateContainsCaptainBonusExactlyOnce()
        {
            SaveData data; WalletService wallet; CaptainService captains;
            MarketService market = Build(Captains.MaxLevel, 1e12d, out data, out wallet, out captains);
            market.SetActiveIsland(Coal);
            for (int second = 0; second < 15; second++)
            {
                market.Deliver(Coal, MarketService.ProductFor(Coal), 50d);
                market.Tick(1f);
            }

            double expectedRate = 3d * 5d;
            Assert.That(data.incomeRatePerSec, Is.EqualTo(expectedRate).Within(1e-9));
            BigDouble offline = OfflineEarnings.Compute(new BigDouble(data.incomeRatePerSec), 3600L, 1d, 7200L);
            Assert.That(offline.ToDouble(), Is.EqualTo(expectedRate * 3600d).Within(1e-6));
        }

        [Test]
        public void ContractRewardsUseTheCaptainAdjustedSavedIncomeOnce()
        {
            double baseCash = ContractOfferFromSavedRate(100d);
            double captainCash = ContractOfferFromSavedRate(500d);

            Assert.That(captainCash, Is.EqualTo(baseCash * 5d).Within(1e-6));
        }

        private static double ContractOfferFromSavedRate(double incomeRatePerSec)
        {
            var data = new SaveData { incomeRatePerSec = incomeRatePerSec };
            var service = new ContractService(new WalletService(data.wallet), null, data,
                                               new TimeService());
            service.Seed(data.incomeRatePerSec * 60d);
            service.Tick(61f, 0d);
            service.Tick(15f, 0d);

            Assert.That(service.HasOffers, Is.True);
            return service.GetOffer(ContractService.NormalTier).Cash;
        }
    }
}
