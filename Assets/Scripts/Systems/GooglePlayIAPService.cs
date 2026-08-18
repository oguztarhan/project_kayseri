using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Game.Systems
{
    /// <summary>
    /// Android ve iOS'un ortak gerçek kasası. Unity IAP platforma göre Google Play Billing veya
    /// StoreKit'i seçer; oyun tarafında ürün kimlikleri ve hak geri yükleme akışı tek yerde kalır.
    ///
    /// Unity IAP v5 is event-driven and store-wide: there is ONE OnPurchasePending for every purchase,
    /// whoever started it and whenever it lands. The callback the store hands us is per-tap. The gap
    /// between those two shapes is what this class is: the in-flight sku is remembered, and the order is
    /// matched against it when it arrives.
    /// </summary>
    public sealed class MobileIAPService : IIAPService
    {
        /// <summary>
        /// Play Console / App Store Connect ürün kimlikleri. Mağaza kartlarındaki sku'larla BİREBİR aynı olmalı — eşleşmeyen
        /// bir kimlik satın alma anında "ürün yok" diye geri döner, kart da sessizce çalışmaz görünür.
        /// </summary>
        private static readonly string[] Consumables =
        {
            "gems_80", "gems_250", "gems_700", "gems_1800", "gems_4500", "gems_12000",
            "teklif_kucuk", "teklif_orta", "teklif_buyuk",
        };

        /// <summary>
        /// Kalıcı hak satan ürünler. Store panellerinde de tüketilmez olmalılar; böylece cihaz değişimi
        /// veya yeniden kurulumdan sonra FetchPurchases ile hakları tekrar uygulanabilir.
        /// </summary>
        private static readonly string[] NonConsumables =
        {
            "offer_baslangic", "offer_hazine", "offer_gecevardiyasi", "offer_madenpatronu",
        };

        private StoreController _store;
        private Action<bool> _waiting;   // the tap still owed an answer
        private string _waitingSku;
        private bool _ready;
        private readonly List<string> _entitlements = new List<string>();
        private readonly List<PendingOrder> _unfinishedOrders = new List<PendingOrder>();
        private Action<string> _unfinishedPurchase;

        /// <summary>True once the store is connected and the catalogue has arrived.</summary>
        public bool Ready => _ready;
        public IReadOnlyList<string> Entitlements => _entitlements;
        public event Action ProductsUpdated;
        public event Action<IReadOnlyList<string>> EntitlementsUpdated;
        public event Action<string> UnfinishedPurchase
        {
            add
            {
                _unfinishedPurchase += value;
                FlushUnfinishedPurchases();
            }
            remove { _unfinishedPurchase -= value; }
        }

        public MobileIAPService() => Boot();

        private async void Boot()
        {
            try
            {
                await UnityServices.InitializeAsync();

                _store = UnityIAPServices.StoreController(UnityIAPServices.GetDefaultStore());
                _store.OnStoreConnected += OnConnected;
                _store.OnProductsFetched += OnFetched;
                _store.OnProductsFetchFailed += OnFetchFailed;
                _store.OnPurchasesFetched += OnPurchasesFetched;
                _store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
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
            ProductsUpdated?.Invoke();
            _store.FetchPurchases();
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

        public string LocalizedPrice(string sku, string fallback)
        {
            Product product = Find(sku);
            string localized = product != null && product.metadata != null
                ? product.metadata.localizedPriceString
                : null;
            return string.IsNullOrEmpty(localized) ? fallback : localized;
        }

        public void RestorePurchases(Action<bool, string> onDone)
        {
            if (!_ready || _store == null)
            {
                onDone?.Invoke(false, "Mağaza hazır değil.");
                return;
            }

            _store.RestoreTransactions((success, error) =>
            {
                if (!success) Debug.LogWarning("[IAP] geri yükleme başarısız: " + error);
                onDone?.Invoke(success, error);
            });
        }

        private Product Find(string sku)
        {
            if (_store == null || string.IsNullOrEmpty(sku)) return null;
            var all = _store.GetProducts();
            for (int i = 0; i < all.Count; i++)
            {
                Product p = all[i];
                if (p != null && p.definition != null && p.definition.id == sku) return p;
            }
            return null;
        }

        private void OnPurchasesFetched(Orders orders)
        {
            _entitlements.Clear();
            if (orders != null && orders.ConfirmedOrders != null)
                for (int i = 0; i < orders.ConfirmedOrders.Count; i++)
                {
                    string sku = SkuOf(orders.ConfirmedOrders[i]);
                    if (IsNonConsumable(sku) && !_entitlements.Contains(sku))
                        _entitlements.Add(sku);
                }

            Debug.Log("[IAP] " + _entitlements.Count + " kalıcı hak eşitlendi.");
            EntitlementsUpdated?.Invoke(_entitlements);
        }

        private static void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning("[IAP] satın alma geçmişi alınamadı: " + failure);
        }

        private static bool IsNonConsumable(string sku)
        {
            if (string.IsNullOrEmpty(sku)) return false;
            for (int i = 0; i < NonConsumables.Length; i++)
                if (NonConsumables[i] == sku) return true;
            return false;
        }

        private void OnPending(PendingOrder order)
        {
            string sku = SkuOf(order);

            // Bizim başlattığımız alım değil: uygulama önceki oturumda ödemeyle ödül arasında kapanmış.
            // Onaylarsak sipariş tüketilir ve oyuncu parayı ödeyip hiçbir şey almamış olur. Bekletirsek
            // Google üç gün sonra kendisi iade eder — ikisi arasında doğru olan ikincisi.
            if (_waiting == null || sku != _waitingSku)
            {
                // Kalıcı ürün yarıda kaldıysa iade kuyruğuna bırakmak hakkı sonsuza kadar kilitler.
                // UI hazır olana kadar siparişi tut; kayda ödül yazıldıktan sonra onayla. UI satın alma
                // öncesi çökerse ödülü ilk kez, ödül sonrası çökerse owned kontrolüyle sıfır kez daha verir.
                if (IsNonConsumable(sku))
                {
                    _unfinishedOrders.Add(order);
                    FlushUnfinishedPurchases();
                    return;
                }
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

        private void FlushUnfinishedPurchases()
        {
            if (_unfinishedPurchase == null || _store == null) return;
            for (int i = _unfinishedOrders.Count - 1; i >= 0; i--)
            {
                PendingOrder order = _unfinishedOrders[i];
                string sku = SkuOf(order);
                try
                {
                    _unfinishedPurchase(sku);
                    _store.ConfirmPurchase(order);
                    _unfinishedOrders.RemoveAt(i);
                }
                catch (Exception e)
                {
                    // Hak kayda yazılamadıysa siparişi tüketme; sonraki açılışta tekrar teslim edilir.
                    Debug.LogError("[IAP] yarım satın alma tamamlanamadı (" + sku + "): " + e.Message);
                }
            }
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
