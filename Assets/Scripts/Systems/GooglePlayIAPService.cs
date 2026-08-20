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
            "teklif_kucuk", "teklif_orta", "teklif_buyuk", "offer_baslangic",
        };

        /// <summary>
        /// Kalıcı hak satan ürünler. Store panellerinde de tüketilmez olmalılar; böylece cihaz değişimi
        /// veya yeniden kurulumdan sonra FetchPurchases ile hakları tekrar uygulanabilir.
        /// </summary>
        private static readonly string[] NonConsumables =
        {
            "offer_hazine", "offer_gecevardiyasi", "offer_madenpatronu",
        };

        private StoreController _store;
        private Action<bool, string> _waiting;   // success + transaction id still owed to the tap
        private string _waitingSku;
        private bool _ready;
        private readonly List<string> _entitlements = new List<string>();
        private readonly List<PendingOrder> _unfinishedOrders = new List<PendingOrder>();
        private Action<string, string> _unfinishedPurchase;

        /// <summary>True once the store is connected and the catalogue has arrived.</summary>
        public bool Ready => _ready;
        public IReadOnlyList<string> Entitlements => _entitlements;
        public event Action ProductsUpdated;
        public event Action<IReadOnlyList<string>> EntitlementsUpdated;
        public event Action<string, string> UnfinishedPurchase
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
            Debug.Log("[IAP] boot: platform=" + Application.platform + " id=" + Application.identifier +
                      " mağaza=" + UnityIAPServices.GetDefaultStore());

            // UGS'yi yalnız Unity Analytics ve Unity Authentication ister; bu proje ikisini de kullanmıyor
            // ve bağlı bir cloud proje kimliği yok. Eskiden bu çağrı kasanın önünde aynı try içinde
            // duruyordu, yani patladığında mağaza hiç açılmıyordu — artık kendi başına uyarı veriyor.
            try
            {
                await UnityServices.InitializeAsync();
                Debug.Log("[IAP] UGS hazır.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IAP] UGS başlatılamadı, mağaza yine de açılıyor: " +
                                 e.GetType().Name + " — " + e.Message);
            }

            try
            {
                _store = UnityIAPServices.StoreController(UnityIAPServices.GetDefaultStore());
                _store.OnStoreConnected += OnConnected;
                _store.OnStoreDisconnected += OnDisconnected;
                _store.OnProductsFetched += OnFetched;
                _store.OnProductsFetchFailed += OnFetchFailed;
                _store.OnPurchasesFetched += OnPurchasesFetched;
                _store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
                _store.OnPurchasePending += OnPending;
                _store.OnPurchaseDeferred += OnDeferred;
                _store.OnPurchaseFailed += OnFailed;

                Debug.Log("[IAP] Connect() çağrıldı.");
                await _store.Connect();
                Debug.Log("[IAP] Connect() döndü — durum: " + _store.GetConnectionState());
            }
            catch (Exception e)
            {
                // Swallowed on purpose: a store that will not open must not take the game down with it.
                // Every card stays tappable and simply reports failure, which is what Purchase does below.
                Debug.LogError("[IAP] başlatılamadı: " + e.GetType().Name + " — " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// Başarısız bağlantı OnStoreConnected'a değil BURAYA düşer — dead bir mağazanın tek bir log
        /// satırı bile bırakmamasının sebebi buydu. Bekleyen dokunuşu da serbest bırakır: bırakmazsa
        /// tek bir başarısız bağlantı _waiting'i dolu tutar ve oturumun geri kalanında her alım
        /// "zaten süren bir satın alma var" diye reddedilir.
        /// </summary>
        private void OnDisconnected(StoreConnectionFailureDescription failure)
        {
            _ready = false;
            Debug.LogError("[IAP] mağaza bağlantısı yok: " +
                           (failure != null ? failure.Message : "sebep bildirilmedi") +
                           " (tekrar denenebilir: " + (failure != null && failure.IsRetryable) + ")");

            if (_waiting == null) return;
            Action<bool, string> done = _waiting;
            _waiting = null;
            _waitingSku = null;
            done(false, null);
        }

        /// <summary>
        /// Ask to Buy ve 3-D Secure alımı bitirmek yerine bekletir. Para hareket etmediği için hiçbir
        /// şey verilmez, ama dokunuşun serbest bırakılması şart: yoksa süren-alım kilidi oturumun
        /// kalanını bloklar. Sipariş, onay çıkınca OnPurchasePending üzerinden geri gelir.
        /// </summary>
        private void OnDeferred(DeferredOrder order)
        {
            string sku = SkuOf(order);
            Debug.Log("[IAP] satın alma onay bekliyor (deferred): " + sku);
            if (_waiting == null || sku != _waitingSku) return;

            Action<bool, string> done = _waiting;
            _waiting = null;
            _waitingSku = null;
            done(false, null);
        }

        private void OnConnected()
        {
            Debug.Log("[IAP] mağazaya bağlanıldı; " +
                      (Consumables.Length + NonConsumables.Length) + " ürün isteniyor.");

            // StoreKit bitirilmemiş işlemleri her açılışta yeniden teslim eder, Play de onaylanmamışları.
            // Bunları OnPurchasePending'e yönlendiren anahtar bu: orada ödül verilip sipariş onaylanıyor,
            // yoksa sessizce ölüyorlar.
            _store.ProcessPendingOrdersOnPurchasesFetched(true);

            var defs = new List<ProductDefinition>(Consumables.Length + NonConsumables.Length);
            for (int i = 0; i < Consumables.Length; i++)
                defs.Add(new ProductDefinition(Consumables[i], ProductType.Consumable));
            for (int i = 0; i < NonConsumables.Length; i++)
                defs.Add(new ProductDefinition(NonConsumables[i], ProductType.NonConsumable));

            // FetchProductsWithNoRetries değil FetchProducts: soğuk şebekede tek bir takılma
            // mağazayı kalıcı olarak hazır-değil bırakıyordu ve tekrar deneyecek hiçbir şey yoktu.
            _store.FetchProducts(defs);
        }

        private void OnFetched(List<Product> products)
        {
            _ready = true;
            Debug.Log("[IAP] mağaza hazır — " + products.Count + " ürün.");
            for (int i = 0; i < products.Count; i++)
            {
                Product p = products[i];
                Debug.Log("[IAP]   " + (p.definition != null ? p.definition.id : "?") +
                          " alınabilir=" + p.availableToPurchase +
                          " fiyat=" + (p.metadata != null ? p.metadata.localizedPriceString : "-"));
            }
            ProductsUpdated?.Invoke();
            _store.FetchPurchases();
        }

        private void OnFetchFailed(ProductFetchFailed fail)
        {
            var ids = new System.Text.StringBuilder();
            if (fail != null && fail.FailedFetchProducts != null)
                for (int i = 0; i < fail.FailedFetchProducts.Count; i++)
                {
                    if (i > 0) ids.Append(", ");
                    ids.Append(fail.FailedFetchProducts[i].id);
                }
            Debug.LogError("[IAP] ürünler alınamadı (" +
                           (fail != null ? fail.FailureReason.ToString() : "?") + "): " + ids);

            // Yanlış tanımlanmış tek bir sku bütün kasayı kapatmasın: elimizde ürün varsa mağaza açılır.
            if (_ready || _store == null || _store.GetProducts().Count == 0) return;
            _ready = true;
            ProductsUpdated?.Invoke();
            _store.FetchPurchases();
        }

        public void Purchase(string sku, Action<bool, string> onDone)
        {
            if (!_ready || _store == null)
            {
                Refuse(onDone, "mağaza hazır değil (bağlantı=" +
                               (_store != null ? _store.GetConnectionState().ToString() : "yok") +
                               ", ürün=" + (_store != null ? _store.GetProducts().Count : 0) + ")");
                return;
            }
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

            // Bizim başlattığımız alım değil: uygulama ödemeyle ödül arasında kapanmış ya da mağaza
            // bitirilmemiş bir işlemi yeniden teslim ediyor. Tüketilebilir de kalıcı da aynı kuyruğa
            // girer: önce ödül kayda yazılır, sonra sipariş onaylanır. Tüketilebiliri bekletmek
            // Android'de üç gün sonra iadeye, iOS'ta ise hiçbir şeye dönüşmez — StoreKit'te otomatik
            // iade yoktur, sipariş her açılışta geri gelir ve oyuncu ödediğini hiç alamaz.
            if (_waiting == null || sku != _waitingSku)
            {
                if (AlreadyQueued(order)) return;
                Debug.LogWarning("[IAP] sahipsiz sipariş kuyruğa alındı: " + sku);
                _unfinishedOrders.Add(order);
                FlushUnfinishedPurchases();
                return;
            }

            Action<bool, string> done = _waiting;
            _waiting = null;
            _waitingSku = null;

            // Ödül önce, onay sonra. Onay ağ üzerinden gider ve düşebilir; o sırada ödül çoktan
            // verilmiş olur ve sipariş bekleyip iade edilir. Ters sırada oyuncu parayı kaybederdi.
            done(true, TransactionKey(order));
            _store.ConfirmPurchase(order);
        }

        /// <summary>Aynı sipariş hem OnPurchasePending hem FetchPurchases yolundan gelebilir.</summary>
        private bool AlreadyQueued(PendingOrder order)
        {
            string id = TransactionKey(order);
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < _unfinishedOrders.Count; i++)
            {
                if (TransactionKey(_unfinishedOrders[i]) == id) return true;
            }
            return false;
        }

        /// <summary>
        /// Kuyrukta bekleyen bir siparişi yeniden dener. Ada teklifleri günlük gelire göre ölçülür ve
        /// açılış anında gelir henüz sıfır olabilir; mağaza açıldığında dünyanın bir geliri olduğu
        /// kesindir, o yüzden PremiumStoreUI.Show buradan tekrar tetikler.
        /// </summary>
        public void RetryUnfinishedPurchases() => FlushUnfinishedPurchases();

        private void FlushUnfinishedPurchases()
        {
            if (_unfinishedPurchase == null || _store == null) return;
            for (int i = _unfinishedOrders.Count - 1; i >= 0; i--)
            {
                PendingOrder order = _unfinishedOrders[i];
                string sku = SkuOf(order);
                try
                {
                    _unfinishedPurchase(sku, TransactionKey(order));
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

            Action<bool, string> done = _waiting;
            _waiting = null;
            _waitingSku = null;
            done(false, null);
        }

        /// <summary>
        /// StoreKit and Play normally provide TransactionID. The receipt fallback keeps fake stores and
        /// unusual platform responses idempotent too without persisting the (potentially large) receipt.
        /// FNV-1a is not a security primitive; it is only a compact, stable equality key.
        /// </summary>
        private static string TransactionKey(Order order)
        {
            if (order == null || order.Info == null) return null;
            if (!string.IsNullOrEmpty(order.Info.TransactionID)) return order.Info.TransactionID;
            string receipt = order.Info.Receipt;
            if (string.IsNullOrEmpty(receipt)) return null;

            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < receipt.Length; i++)
                {
                    hash ^= receipt[i];
                    hash *= 1099511628211UL;
                }
                return "receipt:" + hash.ToString("x16");
            }
        }

        private static string SkuOf(Order order)
        {
            if (order == null || order.CartOrdered == null) return null;
            var items = order.CartOrdered.Items();
            if (items == null || items.Count == 0) return null;
            Product p = items[0].Product;
            return p != null && p.definition != null ? p.definition.id : null;
        }

        private static void Refuse(Action<bool, string> onDone, string why)
        {
            Debug.LogWarning("[IAP] " + why);
            if (onDone != null) onDone(false, null);
        }
    }
}
