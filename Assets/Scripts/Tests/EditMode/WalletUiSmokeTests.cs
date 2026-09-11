using NUnit.Framework;
using Game.Core;
using Game.Systems;
using Game.UI;
using UnityEngine;

namespace Game.Tests
{
    public class WalletUiSmokeTests
    {
        private CurrencyRegistry _registry;
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_registry != null)
            {
                _registry.Dispose();
                _registry = null;
            }
            if (_host != null) Object.DestroyImmediate(_host);
            ServiceLocator.Clear();
        }

        [Test]
        public void WalletBuildsFiveGroupsAndAllTenRowsAndCanClose()
        {
            Register(new SaveData());
            _host = new GameObject("WalletSmoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);

            ui.Show();

            Assert.That(ui.IsOpen, Is.True);
            Assert.That(ui.BuiltRowCount, Is.EqualTo(10));
            Assert.That(ui.BuiltGroupCount, Is.EqualTo(5));
            ui.Hide();
            Assert.That(ui.IsOpen, Is.False);
        }

        [Test]
        public void WalletRowsRefreshWhenAnExistingSourceChanges()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            ServiceLocator.Register(wallet);
            _registry = new CurrencyRegistry(null, wallet, null, null, null, null, null);
            ServiceLocator.Register(_registry);
            ServiceLocator.Register(new LocalizationService());

            _host = new GameObject("WalletLiveSmoke");
            WalletUI ui = _host.AddComponent<WalletUI>();
            ui.Initialize(null);
            ui.Show();
            Assert.That(ui.DisplayedValue(CurrencyId.Cash), Is.EqualTo("0"));

            wallet.AddCash(new BigDouble(42d));

            Assert.That(ui.DisplayedValue(CurrencyId.Cash), Is.EqualTo("42"));
        }

        [Test]
        public void WalletLabelsHaveTurkishAndEnglishCoverage()
        {
            var localization = new LocalizationService();
            localization.SetLanguage("tr");
            Assert.That(localization.Get("wallet.title"), Is.EqualTo("CÜZDAN"));
            Assert.That(localization.Get("currency.cash"), Is.EqualTo("NAKİT"));
            Assert.That(localization.Get("currency.pet_essence"), Is.EqualTo("PET ÖZÜ"));

            localization.SetLanguage("en");
            Assert.That(localization.Get("wallet.title"), Is.EqualTo("WALLET"));
            Assert.That(localization.Get("currency.cash"), Is.EqualTo("CASH"));
            Assert.That(localization.Get("currency.pet_essence"), Is.EqualTo("PET ESSENCE"));
        }

        private void Register(SaveData data)
        {
            var wallet = new WalletService(data.wallet);
            ServiceLocator.Register(wallet);
            _registry = new CurrencyRegistry(null, wallet, null, null, null, null, null);
            ServiceLocator.Register(_registry);
            ServiceLocator.Register(new LocalizationService());
        }
    }
}
