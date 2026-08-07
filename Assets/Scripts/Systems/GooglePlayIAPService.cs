using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Game.Systems
{
    /// <summary>
    /// The real till, behind the same one-method facade the stub used (<see cref="IIAPService"/>) — so
    /// the store, the offer popup and the gem grid did not have to change a line.
    ///
    /// Unity IAP v5 is event-driven and store-wide: there is ONE OnPurchasePending for every purchase,
    /// whoever started it and whenever it lands. The callback the store hands us is per-tap. The gap
    /// between those two shapes is what this class is: the in-flight sku is remembered, and the order is
    /// matched against it when it arrives.
    /// </summary>
    public sealed class GooglePlayIAPService : IIAPService
    {
        /// <summary>
        /// Play Console ürün kimlikleri. Mağaza kartlarındaki sku'larla BİREBİR aynı olmalı — eşleşmeyen
        /// bir kimlik satın alma anında "ürün yok" diye geri döner, kart da sessizce çalışmaz görünür.
        /// </summary>
        private static readonly string[] Consumables =
        {
            "gold_2500", "gold_8000", "gold_25000", "gold_75000", "gold_250000", "gold_1000000",
            "gems_80", "gems_250", "gems_700", "gems_1800", "gems_4500", "gems_12000",
            "teklif_kucuk", "teklif_orta", "teklif_buyuk",
            "offer_madenpatronu",
        };

        /// <summary>
        /// Kalıcı hak satan üç ürün. Tüketilir olarak açılırlarsa oyuncu telefon değiştirdiğinde
        /// reklamsızlığını kaybeder ve bu geri alınamaz — Play Console'da da tüketilmez olmalılar.
        /// </summary>
        private static readonly string[] NonConsumables =
        {
            "offer_baslangic", "offer_hazine", "offer_gecevardiyasi",
        };

        private StoreController _store;
        private Action<bool> _waiting;   // the tap still owed an answer
        private string _waitingSku;
        private bool _ready;

        /// <summary>True once the store is connected and the catalogue has arrived.</summary>
        public bool Ready => _ready;

        public GooglePlayIAPService() => Boot();

        private async void Boot()
        {
            try
            {
                await UnityServices.InitializeAsync();

                _store = UnityIAPServices.StoreController(UnityIAPServices.GetDefaultStore());
                _store.OnStoreConnected += OnConnected;
                _store.OnProductsFetched += OnFetched;
                _store.OnProductsFetchFailed += OnFetchFailed;
                _store.OnPurchasePending += OnPending;
                _store.OnPurchaseFailed += OnFailed;

                await _store.Connect();
            }
            catch (Exception e)
            {
                // Swallowed on purpose: a store that will not open must not take the game down with it.
                // Every card stays tappable and simply reports failure, which is what Purchase does below.
                Debug.LogError("[IAP] başlatılamadı: " + e.Message);
            }
        }

        private void OnConnected()
        {
            var defs = new List<ProductDefinition>(Consumables.Length + NonConsumables.Length);
            for (int i = 0; i < Consumables.Length; i++)
                defs.Add(new ProductDefinition(Consumables[i], ProductType.Consumable));
            for (int i = 0; i < NonConsumables.Length; i++)
                defs.Add(new ProductDefinition(NonConsumables[i], ProductType.NonConsumable));

            _store.FetchProductsWithNoRetries(defs);
        }

        private void OnFetched(List<Product> products)
        {
            _ready = true;
            Debug.Log("[IAP] mağaza hazır — " + products.Count + " ürün.");
        }

        private void OnFetchFailed(ProductFetchFailed fail)
        {
            Debug.LogError("[IAP] ürünler alınamadı: " + fail);
        }

        public void Purchase(string sku, Action<bool> onDone)
        {
            if (!_ready || _store == null) { Refuse(onDone, "mağaza hazır değil"); return; }
            // Play tek seferde tek siparişi kabul eder; ikinciyi göndermek ilkini de düşürür.
            if (_waiting != null) { Refuse(onDone, "zaten süren bir satın alma var"); return; }

            Product product = Find(sku);
            if (product == null || !product.availableToPurchase) { Refuse(onDone, "ürün yok: " + sku); return; }

            _waitingSku = sku;
            _waiting = onDone;
            _store.PurchaseProduct(product);
        }

        private Product Find(string sku)
        {
            var all = _store.GetProducts();
            for (int i = 0; i < all.Count; i++)
            {
                Product p = all[i];
                if (p != null && p.definition != null && p.definition.id == sku) return p;
            }
            return null;
        }

        private void OnPending(PendingOrder order)
        {
            string sku = SkuOf(order);

            // Bizim başlattığımız alım değil: uygulama önceki oturumda ödemeyle ödül arasında kapanmış.
            // Onaylarsak sipariş tüketilir ve oyuncu parayı ödeyip hiçbir şey almamış olur. Bekletirsek
            // Google üç gün sonra kendisi iade eder — ikisi arasında doğru olan ikincisi.
            if (_waiting == null || sku != _waitingSku)
            {
                Debug.LogWarning("[IAP] sahipsiz sipariş bekletiliyor (iadeye bırakıldı): " + sku);
                return;
            }

            Action<bool> done = _waiting;
            _waiting = null;
            _waitingSku = null;

            // Ödül önce, onay sonra. Onay ağ üzerinden gider ve düşebilir; o sırada ödül çoktan
            // verilmiş olur ve sipariş bekleyip iade edilir. Ters sırada oyuncu parayı kaybederdi.
            done(true);
            _store.ConfirmPurchase(order);
        }

        private void OnFailed(FailedOrder order)
        {
            string sku = SkuOf(order);
            Debug.LogWarning("[IAP] alım başarısız (" + sku + "): " + order.FailureReason + " " + order.Details);

            if (_waiting == null || sku != _waitingSku) return;

            Action<bool> done = _waiting;
            _waiting = null;
            _waitingSku = null;
            done(false);
        }

        private static string SkuOf(Order order)
        {
            if (order == null || order.CartOrdered == null) return null;
            var items = order.CartOrdered.Items();
            if (items == null || items.Count == 0) return null;
            Product p = items[0].Product;
            return p != null && p.definition != null ? p.definition.id : null;
        }

        private static void Refuse(Action<bool> onDone, string why)
        {
            Debug.LogWarning("[IAP] " + why);
            if (onDone != null) onDone(false);
        }
    }
}
