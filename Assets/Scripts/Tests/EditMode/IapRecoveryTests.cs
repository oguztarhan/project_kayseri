using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>
    /// Orders the platform hands back with no tap waiting (app killed after paying, a pending or
    /// delayed payment approved later, a tap that timed out). MobileIAPService acknowledges such an
    /// order only on <see cref="UnfinishedPurchases.Outcome.Handled"/>; anything else keeps it queued
    /// and unacknowledged. These tests drive the two real listeners in the game's subscription order:
    /// the season pass from boot, the store once Main has started.
    /// </summary>
    public sealed class IapRecoveryTests
    {
        private const string PassSku = "industry_pass_2026_09";

        private sealed class Iap : IIAPService
        {
            public bool Ready => true;
            public IReadOnlyList<string> Entitlements => new string[0];
            public event Action ProductsUpdated { add { } remove { } }
            public event Action<IReadOnlyList<string>> EntitlementsUpdated { add { } remove { } }
            public event UnfinishedPurchaseHandler UnfinishedPurchase;
            public string LocalizedPrice(string sku, string fallback) => fallback;
            public void Purchase(string sku, Action<bool, string> onDone) => onDone(false, null);
            public void RestorePurchases(Action<bool, string> onDone) => onDone(false, null);
            public void RetryUnfinishedPurchases() { }

            public int Listeners => UnfinishedPurchase == null ? 0 : UnfinishedPurchase.GetInvocationList().Length;

            public UnfinishedPurchases.Outcome Deliver(string sku, string transactionId)
                => UnfinishedPurchases.Dispatch(UnfinishedPurchase, sku, transactionId, out _);
        }

        private SaveData _data;
        private WalletService _wallet;
        private Iap _iap;
        private SeasonalIndustryPassService _pass;
        private GameObject _root;
        private PremiumStoreUI _store;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            _data = new SaveData();
            _wallet = new WalletService(_data.wallet);
            _iap = new Iap();

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var events = new LiveEventService(_data, new List<LiveEvents.Definition>
            {
                new LiveEvents.Definition
                {
                    Id = "industry-pass-test", Kind = SeasonalIndustryPass.Kind,
                    StartUnix = now - 86400L, EndUnix = now + 86400L, ConfigVersion = 1,
                    Slots = SeasonalIndustryPass.Slots, MinIslands = 0,
                },
            });
            // Built at boot in the game, long before Main and its store exist.
            _pass = new SeasonalIndustryPassService(events, new GoalService(_data, _wallet), _wallet,
                SeasonalIndustryPass.Tuning.Default, data: _data, iap: _iap);
            Assert.That(_pass.PremiumSku, Is.EqualTo(PassSku));
        }

        [TearDown]
        public void TearDown()
        {
            _pass?.Dispose();
            if (_root != null) Object.DestroyImmediate(_root);
            ServiceLocator.Clear();
        }

        /// <summary>The store as Main's Start leaves it: services resolved and subscribed to the till.</summary>
        private void OpenStore(bool withSave = true)
        {
            _root = new GameObject("IapRecoveryStore");
            _store = _root.AddComponent<PremiumStoreUI>();
            var save = new SaveService("iap_recovery_test.dat") { Suspended = true };

            ServiceLocator.Register(_wallet);
            ServiceLocator.Register(_data);
            ServiceLocator.Register(new FreeRewardService(_data, null));
            if (withSave) ServiceLocator.Register(save);
            ServiceLocator.Register<IIAPService>(_iap);

            Set("items", new List<PremiumStoreUI.StoreItem>
            {
                new PremiumStoreUI.StoreItem { sku = "gems_80", kind = PremiumStoreUI.StoreItemKind.GemPackIAP, gemAmount = 80 },
            });
            Set("offers", new List<PremiumStoreUI.OfferBinding>
            {
                new PremiumStoreUI.OfferBinding { sku = "offer_gecevardiyasi", oneTime = true, removeAds = true, gemAmount = 300 },
                new PremiumStoreUI.OfferBinding { sku = "offer_hazine", oneTime = true, dailyGemStipend = 20, gemAmount = 500 },
            });
            Set("_offerPopup", _root.AddComponent<OfferPopupUI>());
            Invoke("ResolveServices");
        }

        private void Set(string field, object value)
            => typeof(PremiumStoreUI).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_store, value);

        private void Invoke(string method)
            => typeof(PremiumStoreUI).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_store, null);

        // ---------- the dispatcher itself ----------

        [Test]
        public void NoListenerAtAll_LeavesTheOrderUnclaimed()
        {
            Assert.That(UnfinishedPurchases.Dispatch(null, "gems_80", "tx", out _),
                Is.EqualTo(UnfinishedPurchases.Outcome.Unclaimed));
        }

        [Test]
        public void AFailingListener_KeepsTheOrderEvenWhenAnotherClaimedIt()
        {
            UnfinishedPurchaseHandler listeners = (sku, id) => true;
            listeners += (sku, id) => throw new InvalidOperationException("not yet");

            Assert.That(UnfinishedPurchases.Dispatch(listeners, "gems_80", "tx", out string error),
                Is.EqualTo(UnfinishedPurchases.Outcome.Failed));
            Assert.That(error, Is.EqualTo("not yet"));
        }

        [Test]
        public void AListenerAfterAFailure_IsStillAsked()
        {
            bool asked = false;
            UnfinishedPurchaseHandler listeners = (sku, id) => throw new InvalidOperationException("x");
            listeners += (sku, id) => { asked = true; return false; };

            UnfinishedPurchases.Dispatch(listeners, "gems_80", "tx", out _);
            Assert.That(asked, Is.True);
        }

        // ---------- the boot window: only the pass is listening ----------

        [Test]
        public void GemOrderAtBoot_BeforeTheStoreSubscribes_IsNotAcknowledgedAndGrantsNothing()
        {
            Assert.That(_iap.Listeners, Is.EqualTo(1), "only the pass service listens at boot");

            Assert.That(_iap.Deliver("gems_80", "tx-boot"), Is.EqualTo(UnfinishedPurchases.Outcome.Unclaimed));
            Assert.That(_data.wallet.gems, Is.EqualTo(0));
            Assert.That(IapTransactionJournal.Contains(_data, "tx-boot"), Is.False);
        }

        [Test]
        public void GemOrderKeptAtBoot_IsGrantedOnceTheStoreSubscribes()
        {
            _iap.Deliver("gems_80", "tx-boot");
            OpenStore();

            Assert.That(_iap.Deliver("gems_80", "tx-boot"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.wallet.gems, Is.EqualTo(80));
        }

        [Test]
        public void PassOrderAtBoot_IsGrantedAndHandledByThePassAlone()
        {
            Assert.That(_iap.Deliver(PassSku, "tx-pass"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_pass.HasPremium, Is.True);
        }

        // ---------- both listeners, as in Main ----------

        [Test]
        public void PassOrder_IsIgnoredByTheStore_AndHandledOnce()
        {
            OpenStore();
            Assert.That(_iap.Listeners, Is.EqualTo(2));

            Assert.That(_iap.Deliver(PassSku, "tx-pass"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_iap.Deliver(PassSku, "tx-pass"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));

            Assert.That(_pass.HasPremium, Is.True);
            Assert.That(_data.purchasedOffers.FindAll(s => s == PassSku).Count, Is.EqualTo(1));
            Assert.That(_data.processedIapTransactions.FindAll(s => s == "tx-pass").Count, Is.EqualTo(1));
            Assert.That(_data.wallet.gems, Is.EqualTo(0), "the store must not pay anything for the pass");
        }

        [Test]
        public void PassOrderWithoutTransactionId_IsStillGrantedOnce()
        {
            OpenStore();
            Assert.That(_iap.Deliver(PassSku, null), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_iap.Deliver(PassSku, null), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.purchasedOffers.FindAll(s => s == PassSku).Count, Is.EqualTo(1));
        }

        [Test]
        public void GemOrder_RedeliveredWithTheSameId_PaysOnce()
        {
            OpenStore();
            Assert.That(_iap.Deliver("gems_80", "tx-gems"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_iap.Deliver("gems_80", "tx-gems"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.wallet.gems, Is.EqualTo(80));
        }

        [Test]
        public void RemoveAdsOffer_IsGrantedOnce_AndItsRedeliveryPaysNothingMore()
        {
            OpenStore();
            Assert.That(_iap.Deliver("offer_gecevardiyasi", "tx-ads"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.adsRemoved, Is.True);
            Assert.That(_data.wallet.gems, Is.EqualTo(300));

            Assert.That(_iap.Deliver("offer_gecevardiyasi", "tx-ads"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.wallet.gems, Is.EqualTo(300));
            Assert.That(_data.purchasedOffers.FindAll(s => s == "offer_gecevardiyasi").Count, Is.EqualTo(1));
        }

        [Test]
        public void OwnedOneTimeOffer_UnderANewId_IsAcknowledgedWithoutStackingThePerk()
        {
            OpenStore();
            _iap.Deliver("offer_hazine", "tx-1");
            Assert.That(_data.dailyGemStipend, Is.EqualTo(20));

            Assert.That(_iap.Deliver("offer_hazine", "tx-2"), Is.EqualTo(UnfinishedPurchases.Outcome.Handled));
            Assert.That(_data.dailyGemStipend, Is.EqualTo(20));
            Assert.That(_data.wallet.gems, Is.EqualTo(500));
            Assert.That(IapTransactionJournal.Contains(_data, "tx-2"), Is.True);
        }

        [Test]
        public void UnknownSku_IsLeftUnclaimedInsteadOfThrowing()
        {
            OpenStore();
            Assert.That(_iap.Deliver("not_in_the_catalogue", "tx-x"), Is.EqualTo(UnfinishedPurchases.Outcome.Unclaimed));
        }

        [Test]
        public void IslandOfferWithNoIncomeYet_IsKeptForARetry()
        {
            OpenStore();
            Assert.That(_iap.Deliver("teklif_kucuk", "tx-island"), Is.EqualTo(UnfinishedPurchases.Outcome.Failed));
            Assert.That(IapTransactionJournal.Contains(_data, "tx-island"), Is.False);
        }

        [Test]
        public void StoreWithoutASave_RefusesToClaimRatherThanPayingIntoNothing()
        {
            OpenStore(withSave: false);
            Assert.That(_iap.Deliver("gems_80", "tx-nosave"), Is.EqualTo(UnfinishedPurchases.Outcome.Failed));
            Assert.That(_data.wallet.gems, Is.EqualTo(0));
        }

        [Test]
        public void ClosingTheStore_StopsItClaiming_SoLaterOrdersWaitForItsReturn()
        {
            OpenStore();
            // A scene unload runs OnDestroy; edit mode does not, so run the store's own one.
            Invoke("OnDestroy");
            Assert.That(_iap.Listeners, Is.EqualTo(1));

            Assert.That(_iap.Deliver("gems_80", "tx-sea"), Is.EqualTo(UnfinishedPurchases.Outcome.Unclaimed));
            Assert.That(_data.wallet.gems, Is.EqualTo(0));
        }
    }
}
