using System;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopServiceTests
    {
        private const string BusinessId = "mining-shop.island-01-01";
        private MiningShopCampaignConfig _campaignConfig;
        private MiningShopCampaign _campaign;
        private SaveData _data;
        private WalletService _wallet;
        private MarketService _market;

        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw => 10d;
            public double IncomeCapPerMinuteRaw => 1e12d;
            public double UpgradeTreeCostRaw => 100d;
        }

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

        private MiningShopService Open() => _market.OpenMiningShop(_campaign, BusinessId, MiningShopSimulation.Tuning.Default);

        [Test]
        public void MarketIsTheOnlyClockAndCreditsOneCorrectlyIdentifiedSale()
        {
            var shop = Open();
            int receipts = 0;
            _market.MiningShopSold += sale =>
            {
                receipts++;
                Assert.That(sale.BusinessId, Is.EqualTo(BusinessId));
                Assert.That(sale.ProductId, Is.EqualTo("mining-shop.pickaxe"));
                Assert.That(sale.Sequence, Is.EqualTo(1));
                Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(20d));
                Assert.That(shop.View.Sold, Is.EqualTo(1));
            };
            _market.Tick(16f);
            Assert.That(_wallet.Cash.ToDouble(), Is.Zero);
            _market.Tick(1f);
            Assert.That(receipts, Is.EqualTo(1));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(20d));
            _market.Tick(0f);
            Assert.That(receipts, Is.EqualTo(1));
        }

        [Test]
        public void RebindingTheSameBusinessCannotCreateASecondProducer()
        {
            var shop = Open();
            _market.Tick(5f);
            Assert.That(Open(), Is.SameAs(shop));
            Assert.That(_data.miningShopBusinesses.Count, Is.EqualTo(1));
            Assert.That(shop.View.CraftRemaining, Is.EqualTo(5d));
            _market.Tick(12f);
            Assert.That(shop.View.Sold, Is.EqualTo(1));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(20d));
        }

        [Test]
        public void WalletNotificationCannotReenterTheClockOrBuyDuringSaleSettlement()
        {
            var shop = Open();
            _wallet.CashChanged += () =>
            {
                _market.Tick(100f);
                Assert.That(shop.TryBuyUpgrade(true), Is.False);
            };
            _market.Tick(17f);
            Assert.That(shop.View.Sold, Is.EqualTo(1));
            Assert.That(shop.View.ElapsedSeconds, Is.EqualTo(17d));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(20d));
        }

        [Test]
        public void UpgradeSpendsOncePreservesProgressAndNotifiesAConsistentPurchase()
        {
            _wallet.AddCash(new BigDouble(100d));
            var shop = Open();
            _market.Tick(5f);
            int events = 0;
            _wallet.CashChanged += () =>
            {
                events++;
                Assert.That(shop.View.SpeedLevel, Is.EqualTo(2));
                Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(60d).Within(1e-9));
                Assert.That(shop.TryBuyUpgrade(true), Is.False, "A notification cannot recursively purchase.");
            };
            Assert.That(shop.TryBuyUpgrade(true), Is.True);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(shop.View.CraftRemaining / shop.View.CraftDuration, Is.EqualTo(0.5d).Within(1e-9));
        }

        [Test]
        public void UnaffordableOrCappedUpgradeDoesNotSpendOrChangeState()
        {
            var tuning = MiningShopSimulation.Tuning.Default;
            tuning.MaxUpgradeLevel = 2;
            var shop = _market.OpenMiningShop(_campaign, BusinessId, tuning);
            Assert.That(shop.TryBuyUpgrade(false), Is.False);
            Assert.That(shop.View.ValueLevel, Is.EqualTo(1));
            _wallet.AddCash(new BigDouble(100d));
            Assert.That(shop.TryBuyUpgrade(false), Is.True);
            Assert.That(shop.TryBuyUpgrade(false), Is.False);
            Assert.That(shop.UnitPrice, Is.EqualTo(23d).Within(1e-9));
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(60d).Within(1e-9));
        }

        [Test]
        public void NewShopPreservesLegacyStockClaimsAndEquipmentAndSuppressesLegacyPayouts()
        {
            _market.Register("coal", new Terms());
            _market.Product("coal").stock = 50d;
            _market.Product("coal").deliveredPerMin = 120d;
            _data.miningGearGrade = new[] { 3, 4, 5, 2 };
            _data.chapters.Add(new ChapterState { id = "coal", claimed = new[] { true, true, false, false, false } });
            _data.incomeRatePerSec = 500d;
            string legacy = JsonUtility.ToJson(_market.Row("coal"));
            int version = _data.version;
            Open();
            Assert.That(_data.incomeRatePerSec, Is.Zero);
            Assert.That(_market.Deliver("coal", "Coke", 10d), Is.Zero);
            _market.Tick(17f);
            _market.SettleOffline(10000);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(20d));
            Assert.That(JsonUtility.ToJson(_market.Row("coal")), Is.EqualTo(legacy));
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 2 }, _data.miningGearGrade);
            Assert.That(_data.chapters[0].claimed[1], Is.True);
            Assert.That(_data.version, Is.EqualTo(version));
        }

        [Test]
        public void LegacyMarketKeepsWorkingUntilTheShopIsExplicitlyOpened()
        {
            _market.Register("coal", new Terms());
            _market.SetActiveIsland("coal");
            _market.Product("coal").deliveredPerMin = 120d;
            _market.Deliver("coal", "Coke", 50d);
            _market.Tick(1f);
            Assert.That(_wallet.Cash.ToDouble(), Is.EqualTo(3d).Within(1e-6));
            Assert.That(_data.miningShopBusinesses, Is.Empty);
        }

        [TestCase(12f)]
        [TestCase(16f)]
        [TestCase(17f)]
        public void EncryptedSaveRestoresCargoOrServiceWithoutPayingTwice(float checkpoint)
        {
            Open();
            _market.Tick(checkpoint);
            var save = new SaveService("mining-shop-memory-only-test.dat");
            byte[] blob = save.Encrypt(_data);
            var loaded = save.Decrypt(blob, out bool tampered);
            Assert.That(tampered, Is.False);
            var wallet = new WalletService(loaded.wallet);
            var market = new MarketService(loaded, wallet, null);
            double before = wallet.Cash.ToDouble();
            market.Tick(2f);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(before), "Unbound mining save must not resume ore sales.");
            var reopened = market.OpenMiningShop(_campaign, BusinessId, MiningShopSimulation.Tuning.Default);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(before));
            market.Tick(27f - checkpoint);
            Assert.That(reopened.View.Sold, Is.EqualTo(2));
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(40d));
            Assert.That(loaded.miningShopBusinesses.Count, Is.EqualTo(1));
        }

        [Test]
        public void AWalletObserverCanSaveACompleteSaleTransaction()
        {
            Open();
            string snapshot = null;
            _wallet.CashChanged += () => snapshot = JsonUtility.ToJson(_data);
            _market.Tick(17f);
            var loaded = JsonUtility.FromJson<SaveData>(snapshot);
            Assert.That(loaded.wallet.cash.ToDouble(), Is.EqualTo(20d));
            Assert.That(loaded.miningShopBusinesses[0].Sold, Is.EqualTo(1));
            Assert.That(loaded.miningShopBusinesses[0].Serving, Is.False);
        }

        [Test]
        public void UnknownBusinessOrDuplicateSaveRowsCannotCreateFallbackStock()
        {
            Assert.Throws<ArgumentException>(() => _market.OpenMiningShop(_campaign, "coal", MiningShopSimulation.Tuning.Default));
            Assert.That(_data.miningShopBusinesses, Is.Empty);
            _data.miningShopBusinesses.Add(new MiningShopState { BusinessId = BusinessId });
            _data.miningShopBusinesses.Add(new MiningShopState { BusinessId = BusinessId });
            Assert.Throws<InvalidOperationException>(() => Open());
            Assert.That(_data.activeMiningShopBusinessId, Is.Empty);
        }

        [Test]
        public void InspectorDefaultsMatchTheApprovedPickaxeTimingAndPrice()
        {
            var config = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                var tuning = config.ToTuning();
                Assert.That(tuning.CraftSeconds, Is.EqualTo(10d));
                Assert.That(tuning.UnitPrice, Is.EqualTo(20d));
                Assert.DoesNotThrow(() => tuning.Validate());
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }
    }
}
