using System;
using System.Collections.Generic;
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
            _wallet.AddCash(new BigDouble(1e7));
            MiningShopBusinessService shop = Open();
            Assert.That(shop.TryBuildTable(1), Is.False, "the pickaxe bench is not level 25 yet");
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(1e7));
            double levels = shop.CostOfLevels(0, 24);
            Assert.That(shop.TryBuyLevels(0, 24), Is.EqualTo(24));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(1e7 - levels).Within(1e-6));
            Assert.That(shop.TryBuildTable(2), Is.False, "out of order");
            Assert.That(shop.TryBuildTable(1), Is.True);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(1e7 - levels - 3000d).Within(1e-6));
            Assert.That(shop.TryBuildTable(1), Is.False);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(1e7 - levels - 3000d).Within(1e-6));
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[1].TableBuilt, Is.True);
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[0].Level, Is.EqualTo(25));
        }

        [Test]
        public void LevelsBuyWhatTheWalletCoversAndNoMore()
        {
            MiningShopBusinessService shop = Open();
            double three = shop.CostOfLevels(0, 3);
            _wallet.AddCash(new BigDouble(three + 1d));
            Assert.That(shop.TryBuyLevels(0, 10), Is.EqualTo(3), "a x10 press buys the three it can pay for");
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(1d).Within(1e-6));
            Assert.That(shop.TryBuyLevels(0, 1), Is.Zero);
            Assert.That(shop.TryBuyLevels(1, 1), Is.Zero, "not built");
            Assert.That(shop.TryBuyLevels(0, 0), Is.Zero);
            Assert.That(shop.View.ProductAt(0).Level, Is.EqualTo(4));
        }

        [Test]
        public void HeldPurchasesReachTheDiskOnlyWhenTheHoldEnds()
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "shop-hold-" + Guid.NewGuid().ToString("N") + ".dat");
            var save = new SaveService(path);
            try
            {
                MiningShopBusinessService shop = _market.OpenMiningShopBusiness(_campaign, BusinessId,
                    MiningShopBusinessSimulation.Tuning.Default, save);
                _wallet.AddCash(new BigDouble(shop.CostOfLevels(0, 5) + 1d));
                for (int i = 0; i < 5; i++) Assert.That(shop.TryBuyLevels(0, 1, false), Is.EqualTo(1));
                Assert.That(save.TryLoad(out SaveData held), Is.True);
                Assert.That(held.miningShopBusinesses[0].Business.Lines[0].Level, Is.EqualTo(1), "nothing written mid-hold");

                shop.FlushSave();
                Assert.That(save.TryLoad(out SaveData released), Is.True);
                Assert.That(released.miningShopBusinesses[0].Business.Lines[0].Level, Is.EqualTo(6));
                Assert.That(released.wallet.cash.ToDouble(), Is.EqualTo(1d).Within(1e-6), "the cash left with the levels");
            }
            finally
            {
                foreach (string suffix in new[] { "", SaveService.TempSuffix, SaveService.BackupSuffix, SaveService.UnreadableSuffix })
                    if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
            }
        }

        [Test]
        public void AnOldSavesRefundIsPaidOnceAndSavedWithItsLevels()
        {
            var old = new MiningShopState { BusinessId = BusinessId, Business = new MiningShopBusinessState() };
            for (int i = 0; i < MiningShopCampaign.ProductCount; i++)
                old.Business.Lines.Add(new MiningShopProductLineState
                {
                    ProductId = MiningShopCampaign.ProductIdAt(i), TableBuilt = i == 0,
                    SpeedLevel = i == 0 ? 21 : 1, ValueLevel = i == 0 ? 21 : 1
                });
            _data.miningShopBusinesses.Add(old);

            MiningShopBusinessService shop = Open();
            Assert.That(shop.View.ProductAt(0).Level, Is.EqualTo(29));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(815d).Within(1e-6));
            Assert.That(_wallet.Gems, Is.EqualTo(3L + 5L), "the two stars level 29 stands past are paid on open");
            Assert.That(shop.View.ProductAt(0).StarsPaid, Is.EqualTo(0b11));

            var save = new SaveService("mining-shop-refund-memory-only-test.dat");
            SaveData loaded = save.Decrypt(save.Encrypt(_data), out bool tampered);
            Assert.That(tampered, Is.False);
            var wallet = new WalletService(loaded.wallet);
            var market = new MarketService(loaded, wallet, null);
            MiningShopBusinessService reopened = market.OpenMiningShopBusiness(_campaign, BusinessId,
                MiningShopBusinessSimulation.Tuning.Default);
            Assert.That(reopened.View.ProductAt(0).Level, Is.EqualTo(29));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(815d).Within(1e-6), "a reload does not pay the refund again");
            Assert.That(wallet.Gems, Is.EqualTo(8L), "nor the stars");
        }

        [Test]
        public void EachStarPaysItsGemsOnceAndNamesItself()
        {
            _wallet.AddCash(new BigDouble(1e7));
            MiningShopBusinessService shop = Open();
            var heard = new List<string>();
            shop.StarPaid += (product, star, gems) => heard.Add(product + ":" + star + ":" + gems);

            Assert.That(shop.TryBuyLevels(0, 8), Is.EqualTo(8));
            Assert.That(_wallet.Gems, Is.Zero, "level 9 has no star");
            Assert.That(shop.TryBuyLevels(0, 1), Is.EqualTo(1));
            Assert.That(_wallet.Gems, Is.EqualTo(3L));
            Assert.That(heard, Is.EqualTo(new[] { "0:0:3" }));

            Assert.That(shop.TryBuyLevels(0, 5), Is.EqualTo(5));
            Assert.That(shop.TryBuyLevels(0, 30), Is.EqualTo(30), "one purchase from 15 to 45 crosses the level-25 star");
            Assert.That(_wallet.Gems, Is.EqualTo(3L + 5L));
            Assert.That(heard, Is.EqualTo(new[] { "0:0:3", "0:1:5" }));
            Assert.That(_data.miningShopBusinesses[0].Business.Lines[0].StarsPaid, Is.EqualTo(0b11));

            var save = new SaveService("mining-shop-stars-memory-only-test.dat");
            SaveData loaded = save.Decrypt(save.Encrypt(_data), out bool tampered);
            Assert.That(tampered, Is.False);
            var wallet = new WalletService(loaded.wallet);
            MiningShopBusinessService reopened = new MarketService(loaded, wallet, null).OpenMiningShopBusiness(_campaign,
                BusinessId, MiningShopBusinessSimulation.Tuning.Default);
            Assert.That(wallet.Gems, Is.EqualTo(8L), "a reload pays nothing twice");
            Assert.That(reopened.View.ProductAt(0).StarsPaid, Is.EqualTo(0b11));
        }

        [Test]
        public void AStarReachedMidHoldIsSavedAtOnceWithItsGems()
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "shop-star-" + Guid.NewGuid().ToString("N") + ".dat");
            var save = new SaveService(path);
            try
            {
                MiningShopBusinessService shop = _market.OpenMiningShopBusiness(_campaign, BusinessId,
                    MiningShopBusinessSimulation.Tuning.Default, save);
                _wallet.AddCash(new BigDouble(shop.CostOfLevels(0, 9) + 1d));
                Assert.That(shop.TryBuyLevels(0, 8, false), Is.EqualTo(8));
                Assert.That(save.TryLoad(out SaveData held), Is.True);
                Assert.That(held.miningShopBusinesses[0].Business.Lines[0].Level, Is.EqualTo(1), "no star yet, still held");

                Assert.That(shop.TryBuyLevels(0, 1, false), Is.EqualTo(1));
                Assert.That(save.TryLoad(out SaveData starred), Is.True);
                Assert.That(starred.miningShopBusinesses[0].Business.Lines[0].Level, Is.EqualTo(10));
                Assert.That(starred.miningShopBusinesses[0].Business.Lines[0].StarsPaid, Is.EqualTo(1));
                Assert.That(starred.wallet.gems, Is.EqualTo(3L), "the gems and the star they pay for land together");
            }
            finally
            {
                foreach (string suffix in new[] { "", SaveService.TempSuffix, SaveService.BackupSuffix, SaveService.UnreadableSuffix })
                    if (System.IO.File.Exists(path + suffix)) System.IO.File.Delete(path + suffix);
            }
        }

        [Test]
        public void MarketCreditsEachProductReceiptOnceThroughTheSharedBusinessPayer()
        {
            MiningShopBusinessService shop = Open();
            // A coin over exact change: the wallet's big-number subtraction may leave the build a hair short otherwise.
            _wallet.AddCash(new BigDouble(shop.CostOfLevels(0, 24) + shop.TableCost(1) + 1d));
            Assert.That(shop.TryBuyLevels(0, 24), Is.EqualTo(24));
            Assert.That(shop.TryBuildTable(1), Is.True);
            _wallet.TrySpendCash(_wallet.Cash);
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
            MiningShopBusinessService shop = Open();
            _wallet.AddCash(new BigDouble(shop.CostOfLevels(0, 24) + shop.TableCost(1) + 1d));
            long score = ladder.Score;

            Assert.That(shop.TryBuyLevels(0, 24), Is.EqualTo(24));
            Assert.That(shop.TryBuildTable(1), Is.True);
            Assert.That(goals.TodayProgress(Goals.Upgrades), Is.EqualTo(25L), "24 levels and a build");
            Assert.That(shop.TryBuildTable(1), Is.False);
            Assert.That(shop.TryBuyLevels(3, 1), Is.Zero, "a table that is not built");
            _wallet.TrySpendCash(_wallet.Cash);
            Assert.That(shop.TryBuyLevels(0, 1), Is.Zero, "nothing left to pay with");
            Assert.That(goals.Lifetime(Goals.Upgrades), Is.EqualTo(25L), "refusals count nothing");

            for (int i = 0; i < 240; i++) _market.Tick(1f);
            MiningShopBusinessState business = _data.miningShopBusinesses[0].Business;
            long sold = business.Lines[0].Sold + business.Lines[1].Sold;
            Assert.That(sold, Is.GreaterThan(0L));
            Assert.That(goals.Lifetime(Goals.BarsSold), Is.EqualTo(sold), "one per item sold, bundles included");
            Assert.That(goals.TodayProgress(Goals.BarsSold), Is.EqualTo(sold));

            Assert.That(ladder.Score, Is.GreaterThan(score), "the league reads the same counts");
            if (ladder.ScoresPoints)
                foreach (Ladder.ScoringRule rule in Ladder.Scoring)
                    if (rule.Metric == Goals.Upgrades) Assert.That(ladder.CountedActions(rule), Is.EqualTo(25L));
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

            Assert.That(shop.TryBuyLevels(0, 1), Is.EqualTo(1));
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
            MiningShopBusinessService shop = Open();
            _wallet.AddCash(new BigDouble(shop.CostOfLevels(0, 24) + shop.TableCost(1) + 1d));
            Assert.That(shop.TryBuyLevels(0, 24), Is.EqualTo(24));
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
            Assert.That(loaded.miningShopBusinesses[0].Business.Lines[0].SpeedLevel, Is.EqualTo(2),
                "kept for the level migration to read");
            Assert.That(pickaxe.Level, Is.EqualTo(2), "one speed upgrade (40) buys level 2 (40)");
            Assert.That(pickaxe.CraftRemaining / pickaxe.CraftDuration, Is.EqualTo(3d / 9.090909090909091).Within(1e-12));
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
