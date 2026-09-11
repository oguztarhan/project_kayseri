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

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
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
            market.Deliver("gold", MarketService.ProductFor("gold"), 7d);
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

        [Test]
        public void MarketProductMappingStillMatchesEachOreIsland()
        {
            string[] expected = { "Coke", "CopperBar", "SteelBeam", "SilverBar",
                                  "GoldBar", "CutRuby", "CutEmerald", "PolishedDiamond" };
            for (int i = 0; i < expected.Length; i++)
                Assert.That(MarketService.ProductFor(Catalogue.OreKeys[i]), Is.EqualTo(expected[i]));
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
