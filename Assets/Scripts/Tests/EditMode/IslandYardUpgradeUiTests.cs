using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class IslandYardUpgradeUiTests
    {
        private GameObject _root;
        private SaveData _data;
        private WalletService _wallet;
        private MarketService _market;
        private IslandYardUpgradeUI _ui;
        private string _saved;
        private int _saves;
        private sealed class Terms : IIslandSaleTerms
        {
            public double BarPriceRaw => 10d;
            public double IncomeCapPerMinuteRaw => 1000d;
            public double UpgradeTreeCostRaw => 10000d;
        }

        [SetUp]
        public void Setup()
        {
            ServiceLocator.Clear();
            _saves = 0;
            _saved = null;
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
            _wallet.AddCash(new BigDouble(1e8));
            _market = new MarketService(_data, _wallet, null);
            _market.Register("coal", new Terms());
            _market.Register("copper", new Terms());
            _market.SetActiveIsland("coal");
            _root = new GameObject("C0Test");
            _ui = _root.AddComponent<IslandYardUpgradeUI>();
            _ui.Configure(_market, _wallet, () => { _saved = JsonUtility.ToJson(_data); _saves++; });
            _ui.Show("coal");
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(_root);
            ServiceLocator.Clear();
        }

        private Button Button(string name)
        {
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            Assert.Fail("Button missing: " + name);
            return null;
        }

        [TestCase(YardUpgrade.DepositSlot)]
        [TestCase(YardUpgrade.QueueSlot)]
        [TestCase(YardUpgrade.HireCarry)]
        [TestCase(YardUpgrade.HireServe)]
        [TestCase(YardUpgrade.HireCollect)]
        [TestCase(YardUpgrade.CarryCapacity)]
        public void EachButtonBuysOneLevelAndPersistsExactlyTheAuthoritativeCost(YardUpgrade kind)
        {
            double cost = _market.Cost("coal", kind);
            var before = _wallet.Cash;
            int old = _market.Level("coal", kind);
            Button("Buy_" + kind).onClick.Invoke();
            Assert.That(_market.Level("coal", kind), Is.EqualTo(old + 1));
            Assert.That(_wallet.Cash, Is.EqualTo(before - new BigDouble(cost)));
            Assert.That(_saves, Is.EqualTo(1));
            var restored = JsonUtility.FromJson<SaveData>(_saved);
            var service = new MarketService(restored, new WalletService(restored.wallet), null);
            service.Register("coal", new Terms());
            Assert.That(service.Level("coal", kind), Is.EqualTo(old + 1));
        }

        [Test]
        public void InsufficientCashCannotBuyAndIncomeEnablesButton()
        {
            _wallet.TrySpendCash(_wallet.Cash);
            _ui.Refresh();
            Assert.That(Button("Buy_HireCarry").interactable, Is.False);
            Assert.That(_ui.Purchase(YardUpgrade.HireCarry), Is.False);
            Assert.That(_saves, Is.Zero);
            _wallet.AddCash(new BigDouble(_market.Cost("coal", YardUpgrade.HireCarry)));
            _ui.Refresh();
            Assert.That(Button("Buy_HireCarry").interactable, Is.True);
            Assert.That(_ui.Purchase(YardUpgrade.HireCarry), Is.True);
        }

        [Test]
        public void CapAndClosedPanelPreventPurchases()
        {
            for (int i = 0; i < MarketPrices.MaxCarryLevel; i++) Assert.That(_ui.Purchase(YardUpgrade.CarryCapacity), Is.True);
            Assert.That(Button("Buy_CarryCapacity").interactable, Is.False);
            var before = _wallet.Cash;
            Assert.That(_ui.Purchase(YardUpgrade.CarryCapacity), Is.False);
            Assert.That(_wallet.Cash, Is.EqualTo(before));
            _ui.Hide();
            Assert.That(_ui.Purchase(YardUpgrade.HireServe), Is.False);
        }

        [Test]
        public void ReopeningDoesNotAccumulatePurchaseListeners()
        {
            for (int i = 0; i < 4; i++) { _ui.Hide(); _ui.Show("coal"); }
            Button("Buy_HireServe").onClick.Invoke();
            Assert.That(_market.Level("coal", YardUpgrade.HireServe), Is.EqualTo(1));
            Assert.That(_saves, Is.EqualTo(1));
        }

        [Test]
        public void IslandChangeRedirectsQueuedClickWhileLoadUpgradeIsGlobal()
        {
            _ui.Purchase(YardUpgrade.CarryCapacity);
            _market.SetActiveIsland("copper");
            Button("Buy_HireCollect").onClick.Invoke();
            Assert.That(_ui.IslandKey, Is.EqualTo("copper"));
            Assert.That(_market.Row("copper").dispatchLevel, Is.EqualTo(1));
            Assert.That(_market.Row("coal").dispatchLevel, Is.Zero);
            Assert.That(_market.Level("copper", YardUpgrade.CarryCapacity), Is.EqualTo(1));
        }

        [Test]
        public void MissingTermsNeverLooksLikeFreeUpgrade()
        {
            _market.SetActiveIsland("iron");
            _ui.Show("iron");
            Assert.That(Button("Buy_HireCarry").interactable, Is.False);
            Assert.That(_ui.Purchase(YardUpgrade.HireCarry), Is.False);
            Assert.That(_saves, Is.Zero);
        }

        [Test]
        public void LastDispatchPurchaseCompletesExistingYardBeatAndSalesContinue()
        {
            var row = _market.Row("coal");
            row.hireCarry = row.hireServe = 5;
            row.dispatchLevel = 4;
            var chapters = new ChapterService(_data, _wallet, null, Chapters.Tuning.Default);
            Assert.That(chapters.Satisfied(0, Chapters.TheYard), Is.False);
            Assert.That(_ui.Purchase(YardUpgrade.HireCollect), Is.True);
            Assert.That(chapters.Satisfied(0, Chapters.TheYard), Is.True);
            _market.Product("coal").stock = 10;
            _market.Product("coal").deliveredPerMin = 60;
            var before = _wallet.Cash;
            _market.Tick(1f);
            Assert.That(_wallet.Cash > before, Is.True);
            Assert.That(_ui.IsOpen, Is.True);
        }

        [Test]
        public void AllSixRowsStayInsideTheSheetWithoutOverlap()
        {
            float previousBottom = 1f;
            for (int i = 0; i < 6; i++)
            {
                var button = Button("Buy_" + (YardUpgrade)i);
                var row = (RectTransform)button.transform.parent;
                Assert.That(row.anchorMin.y, Is.GreaterThan(0.075f));
                Assert.That(row.anchorMax.y, Is.LessThanOrEqualTo(previousBottom));
                previousBottom = row.anchorMin.y;
                var bounds = (RectTransform)button.transform;
                Assert.That(bounds.anchorMin.x, Is.GreaterThanOrEqualTo(0f));
                Assert.That(bounds.anchorMax.x, Is.LessThanOrEqualTo(1f));
            }
        }
    }
}
