using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Tips reaching the wallet through the market, and what the tip analytics report.</summary>
    public sealed class ShopTipPayoutTests
    {
        private const string BusinessId = "mining-shop.island-01-04";
        private MiningShopCampaignConfig _campaignConfig;
        private MiningShopCampaign _campaign;
        private SaveData _data;
        private WalletService _wallet;

        private sealed class Recorder : IAnalytics
        {
            public readonly List<string> Events = new List<string>();
            public readonly List<object> Values = new List<object>();
            public void Log(string eventName) { Events.Add(eventName); Values.Add(null); }
            public void Log(string eventName, string paramName, object value) { Events.Add(eventName); Values.Add(value); }
            public int Count(string eventName)
            {
                int n = 0;
                for (int i = 0; i < Events.Count; i++) if (Events[i] == eventName) n++;
                return n;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _campaign = _campaignConfig.CreateCampaign();
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_campaignConfig);

        /// <summary>The shipped tuning, with a chosen tip chance so a short run is sure to see tips (or none).</summary>
        private static MiningShopBusinessSimulation.Tuning TipChance(double chance)
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            tuning.Tips.BaseChance = chance;
            tuning.Tips.MaxChance = chance;
            return tuning;
        }

        private MiningShopBusinessService Open(MarketService market, MiningShopBusinessSimulation.Tuning tuning)
            => market.OpenMiningShopBusiness(_campaign, BusinessId, tuning);

        [Test]
        public void ATipIsPaidAtTheSaleMultipliersOnTopOfThePrice()
        {
            var time = new TimeService();
            _data.stationSpeedMultiplier = 1.5d;
            _data.boostMultiplier = 2d;
            _data.boostEndUnix = time.NowUnix() + 3600L;
            _data.miningGearGrade = new[] { 3, 4, 5, 2 };
            var gear = new MiningGearService(_data, null, time);
            var goals = new GoalService(_data, _wallet, null, time);
            var market = new MarketService(_data, _wallet, new BoostService(_data, time), goals: goals, miningGear: gear);
            double multiplier = 1.5d * gear.IncomeMultiplier * 2d;
            Open(market, TipChance(1d));

            double heardTips = 0d, heardCash = 0d;
            long units = 0L;
            int tipped = 0;
            market.MiningShopBusinessSold += sale =>
            {
                heardCash += sale.Cash;
                heardTips += sale.Tip;
                units += sale.Units;
                if (sale.TipTier != ShopTips.None) tipped++;
            };
            for (int i = 0; i < 600; i++) market.Tick(1f);

            MiningShopBusinessState business = _data.miningShopBusinesses[0].Business;
            Assert.That(tipped, Is.GreaterThan(5));
            Assert.That(business.TipsEarned, Is.GreaterThan(0d));
            Assert.That(heardTips, Is.EqualTo(business.TipsEarned * multiplier).Within(1e-6), "the tip gets the sale's multipliers");
            Assert.That(heardCash, Is.EqualTo(business.Lines[0].Earned * multiplier).Within(1e-6));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(heardCash + heardTips).Within(1e-6), "the wallet took both");
            Assert.That(goals.Lifetime(Goals.BarsSold), Is.EqualTo(units), "a tip is not a bar sold");
        }

        [Test]
        public void TipsLeaveThePublishedRateAndSoOfflineEarningsAlone()
        {
            var withTips = new MarketService(_data, _wallet, null);
            MiningShopBusinessService a = Open(withTips, TipChance(1d));
            double rate = _data.incomeRatePerSec;

            var otherData = new SaveData();
            var noTips = new MarketService(otherData, new WalletService(otherData.wallet), null);
            MiningShopBusinessService b = noTips.OpenMiningShopBusiness(_campaign, BusinessId, TipChance(0d));
            Assert.That(rate, Is.EqualTo(otherData.incomeRatePerSec));
            Assert.That(a.SteadyStateRate(), Is.EqualTo(b.SteadyStateRate()));

            for (int i = 0; i < 600; i++) withTips.Tick(1f);
            Assert.That(_data.miningShopBusinesses[0].Business.TipCount, Is.GreaterThan(0L));
            Assert.That(_data.incomeRatePerSec, Is.EqualTo(a.SteadyStateRate()), "tips paid never enter the rate");
        }

        [Test]
        public void AnalyticsLogTheFirstAndHugeTipsAndOneSummaryPerSession()
        {
            var market = new MarketService(_data, _wallet, null);
            MiningShopBusinessSimulation.Tuning tuning = TipChance(1d);
            tuning.Tips.Weights = new[] { 1, 1, 1 };   // enough huge tips in a short run
            Open(market, tuning);
            var recorder = new Recorder();
            var analytics = new ShopTipAnalytics(market, recorder);

            int huge = 0, tips = 0;
            double cash = 0d, tipCash = 0d;
            market.MiningShopBusinessSold += sale =>
            {
                cash += sale.Cash;
                if (sale.TipTier == ShopTips.None) return;
                tips++;
                tipCash += sale.Tip;
                if (sale.TipTier == ShopTips.TierCount - 1) huge++;
            };
            for (int i = 0; i < 1200; i++) market.Tick(1f);

            Assert.That(tips, Is.GreaterThan(10));
            Assert.That(huge, Is.GreaterThan(0));
            Assert.That(recorder.Count(ShopTipAnalytics.FirstEvent), Is.EqualTo(1));
            Assert.That(recorder.Count(ShopTipAnalytics.HugeEvent), Is.EqualTo(huge));
            Assert.That(recorder.Count(ShopTipAnalytics.SessionCountEvent), Is.Zero, "nothing summed is sent early");

            analytics.Flush();
            int at = recorder.Events.IndexOf(ShopTipAnalytics.SessionCountEvent);
            Assert.That(recorder.Values[at], Is.EqualTo(tips));
            int shareAt = recorder.Events.IndexOf(ShopTipAnalytics.SessionShareEvent);
            Assert.That((double)recorder.Values[shareAt], Is.EqualTo(System.Math.Round(tipCash / cash * 100d, 1)));

            analytics.Flush();
            Assert.That(recorder.Count(ShopTipAnalytics.SessionCountEvent), Is.EqualTo(1), "a pause then a quit reports once");
        }

        /// <summary>One run of the shop at the shipped odds, with the goal tally, as the game wires it.</summary>
        private sealed class Run
        {
            public SaveData Data;
            public WalletService Wallet;
            public GoalService Goals;
            public MarketService Market;
            public readonly List<MiningShopBusinessSimulation.Sale> Tips = new List<MiningShopBusinessSimulation.Sale>();

            public Run(SaveData data, MiningShopCampaign campaign)
            {
                Data = data;
                Wallet = new WalletService(data.wallet);
                Goals = new GoalService(data, Wallet, null, new TimeService());
                Market = new MarketService(data, Wallet, null, goals: Goals);
                Market.OpenMiningShopBusiness(campaign, BusinessId, MiningShopBusinessSimulation.Tuning.Default);
                Market.MiningShopBusinessSold += sale => { if (sale.TipTier != ShopTips.None) Tips.Add(sale); };
            }

            public void Tick(int seconds)
            {
                for (int i = 0; i < seconds; i++) Market.Tick(1f);
            }
        }

        [Test]
        public void AReloadThroughTheEncryptedSaveNeitherRerollsNorRepaysATip()
        {
            var straight = new Run(new SaveData(), _campaign);
            straight.Tick(6000);

            var first = new Run(new SaveData(), _campaign);
            first.Tick(3000);
            var save = new SaveService("shop-tip-memory-only-test.dat");
            SaveData loaded = save.Decrypt(save.Encrypt(first.Data), out bool tampered);
            Assert.That(tampered, Is.False);
            var resumed = new Run(loaded, _campaign);
            resumed.Tick(3000);

            var reloaded = new List<MiningShopBusinessSimulation.Sale>(first.Tips);
            reloaded.AddRange(resumed.Tips);
            Assert.That(straight.Tips.Count, Is.GreaterThan(20));
            Assert.That(reloaded.Count, Is.EqualTo(straight.Tips.Count));
            for (int i = 0; i < straight.Tips.Count; i++)
            {
                Assert.That(reloaded[i].Sequence, Is.EqualTo(straight.Tips[i].Sequence), "tip " + i);
                Assert.That(reloaded[i].TipTier, Is.EqualTo(straight.Tips[i].TipTier), "tip " + i);
                Assert.That(reloaded[i].Tip, Is.EqualTo(straight.Tips[i].Tip).Within(1e-9).Percent, "tip " + i);
            }
            MiningShopBusinessState a = straight.Data.miningShopBusinesses[0].Business;
            MiningShopBusinessState b = loaded.miningShopBusinesses[0].Business;
            Assert.That(b.TipCount, Is.EqualTo(a.TipCount));
            Assert.That(b.TipsEarned, Is.EqualTo(a.TipsEarned).Within(1e-9).Percent);
            Assert.That(resumed.Goals.Lifetime(Goals.Tips), Is.EqualTo(straight.Goals.Lifetime(Goals.Tips)));
            Assert.That(resumed.Goals.Lifetime(Goals.Tips), Is.EqualTo(a.TipCount), "every tip counted once for the achievement");
            Assert.That(resumed.Wallet.Cash.ToDouble(), Is.EqualTo(straight.Wallet.Cash.ToDouble()).Within(1e-9).Percent,
                "the wallet took each tip exactly once");
        }

        [Test]
        public void ASessionWithNoSalesSendsNoSummary()
        {
            var market = new MarketService(_data, _wallet, null);
            Open(market, TipChance(1d));
            var recorder = new Recorder();
            new ShopTipAnalytics(market, recorder).Flush();
            Assert.That(recorder.Events, Is.Empty);
        }
    }
}
