using System;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopBusinessServiceTests
    {
        private const string BusinessId = "mining-shop.island-01-04";
        private MiningShopCampaignConfig _campaignConfig;
        private MiningShopCampaign _campaign;
        private SaveData _data;
        private WalletService _wallet;
        private MarketService _market;

        [SetUp]
        public void SetUp()
        {
            _campaignConfig = ScriptableObject.CreateInstance<MiningShopCampaignConfig>();
            _campaign = _campaignConfig.CreateCampaign();
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
            _market = new MarketService(_data, _wallet, null);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_campaignConfig);

        private MiningShopBusinessService Open() => _market.OpenMiningShopBusiness(_campaign, BusinessId,
            MiningShopBusinessSimulation.Tuning.Default);

        [Test]
        public void TablesSpendOnceInOrderAndPersistInTheBusinessRecord()
        {
            _wallet.AddCash(new BigDouble(10000d));
            MiningShopBusinessService shop = Open();
            Assert.That(shop.TryBuildTable(2), Is.False);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(10000d));
            Assert.That(shop.TryBuildTable(1), Is.True);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(9700d).Within(1e-6));
            Assert.That(shop.TryBuildTable(1), Is.False);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(9700d).Within(1e-6));
            Assert.That(shop.TryBuildTable(2), Is.True);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(8300d).Within(1e-6));
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[1].TableBuilt, Is.True);
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[2].TableBuilt, Is.True);
        }

        [Test]
        public void MarketCreditsEachProductReceiptOnceThroughTheSharedBusinessPayer()
        {
            _wallet.AddCash(new BigDouble(300d));
            MiningShopBusinessService shop = Open();
            Assert.That(shop.TryBuildTable(1), Is.True);
            int pickaxes = 0, helmets = 0;
            _market.MiningShopBusinessSold += sale =>
            {
                if (sale.ProductId == "mining-shop.pickaxe") pickaxes++;
                if (sale.ProductId == "mining-shop.helmet") helmets++;
                Assert.That(sale.Sequence, Is.EqualTo(_data.miningShopBusinesses[0].Business.ReceiptSequence));
            };
            for (int i = 0; i < 240; i++) _market.Tick(1f);
            Assert.That(pickaxes, Is.GreaterThan(0));
            Assert.That(helmets, Is.GreaterThan(0));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(
                _data.miningShopBusinesses[0].Business.Lines[0].Earned +
                _data.miningShopBusinesses[0].Business.Lines[1].Earned).Within(1e-6));
            Assert.That(_data.incomeRatePerSec, Is.EqualTo(shop.SteadyStateRate()));
        }

        [Test]
        public void SalesPayGearPermanentAndTimedBoostWhileThePublishedRateLeavesTheTimedBoostOut()
        {
            var time = new TimeService();
            _data.stationSpeedMultiplier = 1.5d;
            _data.boostMultiplier = 2d;
            _data.boostEndUnix = time.NowUnix() + 3600L;
            _data.miningGearGrade = new[] { 3, 4, 5, 2 };
            var gear = new MiningGearService(_data, null, time);
            Assert.That(gear.IncomeMultiplier, Is.GreaterThan(1d));
            _market = new MarketService(_data, _wallet, new BoostService(_data, time), miningGear: gear);
            double standing = 1.5d * gear.IncomeMultiplier;

            MiningShopBusinessService shop = Open();
            Assert.That(_data.incomeRatePerSec, Is.EqualTo(shop.SteadyStateRate() * standing).Within(1e-9),
                "offline earnings add the timed boost themselves");

            double heard = 0d;
            _market.MiningShopBusinessSold += sale => heard += sale.Cash;
            for (int i = 0; i < 120; i++) _market.Tick(1f);
            MiningShopProductLineState pickaxe = _data.miningShopBusinesses[0].Business.Lines[0];
            Assert.That(pickaxe.Earned, Is.GreaterThan(0d));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(pickaxe.Earned * standing * 2d).Within(1e-6));
            Assert.That(heard, Is.EqualTo(_wallet.Cash.ToDouble()).Within(1e-6), "listeners hear what the wallet took");

            _data.boostEndUnix = time.NowUnix() - 1L;
            double cashBefore = _wallet.Cash.ToDouble(), earnedBefore = pickaxe.Earned;
            for (int i = 0; i < 120; i++) _market.Tick(1f);
            double earned = pickaxe.Earned - earnedBefore;
            Assert.That(earned, Is.GreaterThan(0d));
            Assert.That(_wallet.Cash.ToDouble() - cashBefore, Is.EqualTo(earned * standing).Within(1e-6),
                "an expired boost stops paying mid-session");
        }

        [Test]
        public void ShopPurchasesAndSalesCountForGoalsAndTheLeagueAndRefusalsDoNot()
        {
            var goals = new GoalService(_data, _wallet, null, new TimeService());
            var board = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, Leaderboards.ThreeDayCadenceSeconds);
            var ladder = new LadderService(_data, null, goals, board, _wallet);
            _market = new MarketService(_data, _wallet, null, goals: goals);
            _wallet.AddCash(new BigDouble(400d));
            MiningShopBusinessService shop = Open();
            long score = ladder.Score;

            Assert.That(shop.TryBuildTable(1), Is.True);
            Assert.That(shop.TryBuyUpgrade(0, true), Is.True);
            Assert.That(goals.TodayProgress(Goals.Upgrades), Is.EqualTo(2L), "a build and an upgrade");
            Assert.That(shop.TryBuildTable(1), Is.False);
            Assert.That(shop.TryBuyUpgrade(3, true), Is.False, "a table that is not built");
            _wallet.TrySpendCash(_wallet.Cash);
            Assert.That(shop.TryBuyUpgrade(0, false), Is.False, "nothing left to pay with");
            Assert.That(goals.Lifetime(Goals.Upgrades), Is.EqualTo(2L), "refusals count nothing");

            for (int i = 0; i < 240; i++) _market.Tick(1f);
            MiningShopBusinessState business = _data.miningShopBusinesses[0].Business;
            long sold = business.Lines[0].Sold + business.Lines[1].Sold;
            Assert.That(sold, Is.GreaterThan(0L));
            Assert.That(goals.Lifetime(Goals.BarsSold), Is.EqualTo(sold), "one per item sold");
            Assert.That(goals.TodayProgress(Goals.BarsSold), Is.EqualTo(sold));

            Assert.That(ladder.Score, Is.GreaterThan(score), "the league reads the same counts");
            if (ladder.ScoresPoints)
                foreach (Ladder.ScoringRule rule in Ladder.Scoring)
                    if (rule.Metric == Goals.Upgrades) Assert.That(ladder.CountedActions(rule), Is.EqualTo(2L));
        }

        [Test]
        public void OpeningPublishesTheShopRateInPlaceOfTheSavedOreRateAndUpgradesRaiseIt()
        {
            _data.incomeRatePerSec = 500d;
            _wallet.AddCash(new BigDouble(1000d));
            MiningShopBusinessService shop = Open();
            double opened = shop.SteadyStateRate();
            Assert.That(opened, Is.GreaterThan(0d));
            Assert.That(_data.incomeRatePerSec, Is.EqualTo(opened), "published before the first tick");
            Assert.That(_market.MiningShopIncomePerSec, Is.EqualTo(opened));

            Assert.That(shop.TryBuyUpgrade(0, false), Is.True);
            _market.Tick(1f);
            Assert.That(_data.incomeRatePerSec, Is.GreaterThan(opened));
            Assert.That(_data.incomeRatePerSec, Is.EqualTo(shop.SteadyStateRate()));
        }

        [Test]
        public void BusinessAndPickaxeOnlyServicesCannotRunTogether()
        {
            Open();
            Assert.Throws<InvalidOperationException>(() => _market.OpenMiningShop(_campaign, BusinessId,
                MiningShopSimulation.Tuning.Default));

            var separate = new MarketService(new SaveData(), new WalletService(new WalletData()), null);
            separate.OpenMiningShop(_campaign, BusinessId, MiningShopSimulation.Tuning.Default);
            Assert.Throws<InvalidOperationException>(() => separate.OpenMiningShopBusiness(_campaign, BusinessId,
                MiningShopBusinessSimulation.Tuning.Default));
        }

        [Test]
        public void EncryptedSaveRestoresBuiltLinesWithoutReplayingReceipts()
        {
            _wallet.AddCash(new BigDouble(300d));
            MiningShopBusinessService shop = Open();
            Assert.That(shop.TryBuildTable(1), Is.True);
            for (int i = 0; i < 43; i++) _market.Tick(1f);
            var save = new SaveService("mining-shop-business-memory-only-test.dat");
            byte[] blob = save.Encrypt(_data);
            SaveData loaded = save.Decrypt(blob, out bool tampered);
            Assert.That(tampered, Is.False);
            var wallet = new WalletService(loaded.wallet);
            var market = new MarketService(loaded, wallet, null);
            double before = wallet.Cash.ToDouble();
            MiningShopBusinessService reopened = market.OpenMiningShopBusiness(_campaign, BusinessId,
                MiningShopBusinessSimulation.Tuning.Default);
            Assert.That(reopened.View.ProductAt(1).TableBuilt, Is.True);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(before));
            market.Tick(1f);
            Assert.That(wallet.Cash.ToDouble(), Is.GreaterThanOrEqualTo(before));
        }

        [Test]
        public void PreBusinessFlatPickaxeRecordMigratesAfterSaveServiceRoundTrip()
        {
            const string legacyJson = "{\"BusinessId\":\"mining-shop.island-01-01\",\"SpeedLevel\":2,\"ValueLevel\":1,\"ElapsedSeconds\":27,\"Crafting\":true,\"CraftRemaining\":3,\"CraftDuration\":9.090909090909091,\"OutputStock\":0,\"PickupReserved\":0,\"Cargo\":1,\"DestinationReserved\":1,\"Carrier\":2,\"CarrierRemaining\":2,\"CarrierDuration\":4,\"ShelfStock\":0,\"WaitingCustomers\":0,\"ArrivalRemaining\":3,\"Serving\":false,\"ServiceRemaining\":0,\"ServiceDuration\":0,\"ServicePrice\":0,\"Produced\":3,\"Sold\":2,\"Earned\":40}";
            MiningShopState preBusiness = JsonUtility.FromJson<MiningShopState>(legacyJson);
            Assert.That(preBusiness.Business == null || preBusiness.Business.Lines == null || preBusiness.Business.Lines.Count == 0,
                Is.True, "A JSON payload written before the Business field must not contain product lines.");

            var beforeMigration = new SaveData();
            beforeMigration.miningShopBusinesses.Add(preBusiness);
            var save = new SaveService("mining-shop-pre-business-memory-only-test.dat");
            SaveData loaded = save.Decrypt(save.Encrypt(beforeMigration), out bool tampered);
            Assert.That(tampered, Is.False);

            var wallet = new WalletService(loaded.wallet);
            var market = new MarketService(loaded, wallet, null);
            MiningShopBusinessService shop = market.OpenMiningShopBusiness(_campaign, "mining-shop.island-01-01",
                MiningShopBusinessSimulation.Tuning.Default);
            MiningShopBusinessSimulation.ProductSnapshot pickaxe = shop.View.ProductAt(0);
            Assert.That(pickaxe.SpeedLevel, Is.EqualTo(2));
            Assert.That(pickaxe.CraftRemaining, Is.EqualTo(3d));
            Assert.That(shop.View.Carrier, Is.EqualTo(MiningShopSimulation.CarrierPhase.ToMarket));
            Assert.That(shop.View.CarrierProductIndex, Is.EqualTo(0));
            Assert.That(shop.View.CarrierCount, Is.EqualTo(1));
            Assert.That(shop.View.ReceiptSequence, Is.EqualTo(2));
            Assert.That(wallet.Cash.ToDouble(), Is.Zero, "Opening a migrated record must not replay its old receipts.");
            Assert.That(loaded.miningShopBusinesses[0].Sold, Is.EqualTo(2), "The additive flat record stays intact.");
            Assert.That(loaded.miningShopBusinesses[0].Business.Lines.Count, Is.EqualTo(MiningShopCampaign.ProductCount));

            SaveData reloaded = save.Decrypt(save.Encrypt(loaded), out tampered);
            Assert.That(tampered, Is.False);
            var resumedWallet = new WalletService(reloaded.wallet);
            var resumedMarket = new MarketService(reloaded, resumedWallet, null);
            MiningShopBusinessService resumed = resumedMarket.OpenMiningShopBusiness(_campaign, "mining-shop.island-01-01",
                MiningShopBusinessSimulation.Tuning.Default);
            Assert.That(resumed.View.ReceiptSequence, Is.EqualTo(2));
            Assert.That(resumedWallet.Cash.ToDouble(), Is.Zero, "Reloading the migrated record must not pay it again.");
        }
    }
}
