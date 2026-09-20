using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public class InventoryResourceCatalogueTests
    {
        private GameObject _host;
        private bool _hadLanguage;
        private string _previousLanguage;
        private bool _hadChoiceMarker;
        private int _previousChoiceMarker;

        [SetUp]
        public void SetUp()
        {
            _hadLanguage = PlayerPrefs.HasKey(LocalizationService.PrefKey);
            _previousLanguage = PlayerPrefs.GetString(LocalizationService.PrefKey, "");
            _hadChoiceMarker = PlayerPrefs.HasKey(LocalizationService.UserChoicePrefKey);
            _previousChoiceMarker = PlayerPrefs.GetInt(LocalizationService.UserChoicePrefKey, 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
            if (_hadLanguage) PlayerPrefs.SetString(LocalizationService.PrefKey, _previousLanguage);
            else PlayerPrefs.DeleteKey(LocalizationService.PrefKey);
            if (_hadChoiceMarker) PlayerPrefs.SetInt(LocalizationService.UserChoicePrefKey, _previousChoiceMarker);
            else PlayerPrefs.DeleteKey(LocalizationService.UserChoicePrefKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void GoldAndDiamondOreNamesStayDistinctFromTheirRefinedProductsInBothLanguages()
        {
            var localization = new LocalizationService();
            localization.SetLanguage("tr");
            Assert.That(localization.Get("cevher.gold"), Is.EqualTo("ALTIN"));
            Assert.That(localization.Get("urun.gold_bar"), Is.EqualTo("ALTIN KÜLÇE"));
            Assert.That(localization.Get("cevher.diamond"), Is.EqualTo("ELMAS"));
            Assert.That(localization.Get("urun.polished_diamond"), Is.EqualTo("PARLATILMIŞ ELMAS"));

            localization.SetLanguage("en");
            Assert.That(localization.Get("cevher.gold"), Is.EqualTo("GOLD"));
            Assert.That(localization.Get("urun.gold_bar"), Is.EqualTo("GOLD BAR"));
            Assert.That(localization.Get("cevher.diamond"), Is.EqualTo("DIAMOND"));
            Assert.That(localization.Get("urun.polished_diamond"), Is.EqualTo("POLISHED DIAMOND"));
        }

        [Test]
        public void CatalogueShowsLiveMarketStockAndDoesNotPretendCompositeGoodsHaveStock()
        {
            var data = new SaveData();
            for (int i = 1; i < Catalogue.OreCount; i++) data.unlockedIslands.Add(Catalogue.OreKeys[i]);
            var market = new MarketService(data, new WalletService(data.wallet), null);
            market.Deliver("gold", MarketService.IslandProduct, 7d);
            ServiceLocator.Register(market);
            var localization = new LocalizationService();
            localization.SetLanguage("en");
            ServiceLocator.Register(localization);

            _host = new GameObject("InventoryResourceCatalogueSmoke");
            var ui = _host.AddComponent<InventoryUI>();
            EnsureAwake(ui);
            ui.Show();
            typeof(InventoryUI).GetMethod("SetTab", BindingFlags.Instance | BindingFlags.NonPublic)
                               .Invoke(ui, new object[] { true });

            var names = (Text[])typeof(InventoryUI).GetField("_entryName", BindingFlags.Instance | BindingFlags.NonPublic)
                                                     .GetValue(ui);
            var quantities = (Text[])typeof(InventoryUI).GetField("_entryQuantity", BindingFlags.Instance | BindingFlags.NonPublic)
                                                          .GetValue(ui);

            Assert.That(names[4].text, Is.EqualTo("GOLD"));
            Assert.That(names[12].text, Is.EqualTo("GOLD BAR"));
            Assert.That(names[7].text, Is.EqualTo("DIAMOND"));
            Assert.That(names[16].text, Is.EqualTo("POLISHED DIAMOND"));
            Assert.That(quantities[12].text, Does.Contain("Market stock: 7"));
            Assert.That(quantities[14].text, Does.Contain("no live stock"), "ruby ring has no market row");
            Assert.That(market.Stock("gold"), Is.EqualTo(7d).Within(1e-9), "catalogue refresh is read-only");
        }

        /// <summary>
        /// The catalogue still names all eight goods — they are what the ore table describes — but only
        /// one of them is ever traded, and it is the first one. The per-island mapping this used to pin
        /// is gone: a chapter changes the island's look, not its output.
        /// </summary>
        [Test]
        public void TheOneTradedProductIsTheCataloguesFirstGood()
        {
            Assert.That(MarketService.IslandProduct, Is.EqualTo("Coke"));
            Assert.That(Catalogue.OreKeys[0], Is.EqualTo("coal"),
                        "the traded product is the first ore's good; if the table is re-cut, re-read this");
        }

        private static void EnsureAwake(InventoryUI ui)
        {
            var field = typeof(InventoryUI).GetField("_market", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field.GetValue(ui) == null)
                typeof(InventoryUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                   .Invoke(ui, null);
        }
    }
}
