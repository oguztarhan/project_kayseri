using System;
using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The store (GDD §10), reskinned with the Figma set: serit_baslik ribbon, four offer cards
    /// (offer_1..4), then ALTIN and ELMAS pack sections — a purple title strip (gold_title /
    /// gems_title) over a 3×2 grid of full-card pack sprites (gold_* / gems_*, 280×360). One
    /// vertical ScrollRect holds everything. Editor-authored: the hierarchy lives in the UI_Magaza
    /// prefab; offer cards and their texts are plain scene objects, pack cells are cloned from an
    /// inactive template so the catalog list below stays the single source of truth for packs.
    ///
    /// Each pack sprite already contains its art, amount pill and price strip, so a cell is just
    /// the card image (also the button) plus two TMP labels the catalog fills in.
    /// </summary>
    public sealed class PremiumStoreUI : MonoBehaviour
    {
        /// <summary>What a pack cell grants. Offers are separate — see <see cref="OfferBinding"/>.</summary>
        public enum StoreItemKind
        {
            GemPackIAP,     // 0 - IAP grants gemAmount gems (green card)
            GoldPackIAP     // 1 - IAP grants cashAmount cash (blue card)
        }

        /// <summary>One pack cell. Edit the list on the component to retune amounts and prices.</summary>
        [Serializable]
        public sealed class StoreItem
        {
            public string title = "2.5K";
            public StoreItemKind kind = StoreItemKind.GoldPackIAP;
            [Tooltip("Full card sprite (gold_2500, gems_80, ...) — becomes the cell background.")]
            public Sprite icon;
            [Tooltip("Real-money price shown on the card, e.g. \"₺14,99\".")]
            public string priceLabel = "";
            [Tooltip("IAP product id.")]
            public string sku = "";
            [Tooltip("Cash granted — GoldPackIAP.")]
            public double cashAmount = 0d;
            [Tooltip("Gems granted — GemPackIAP.")]
            public long gemAmount = 0;
        }

        /// <summary>
        /// One offer card, authored in the hierarchy (its texts are plain TMP objects there); this
        /// binding only says which button sells what. All grants are optional — fill what the
        /// offer promises and leave the rest at zero.
        /// </summary>
        [Serializable]
        public sealed class OfferBinding
        {
            public string name = "Teklif";
            public Button button;
            [Tooltip("IAP product id.")]
            public string sku = "";
            public double cashAmount = 0d;
            public long gemAmount = 0;
            [Tooltip("Income boost applied on purchase; skipped while multiplier ≤ 1 or seconds ≤ 0.")]
            public double boostMultiplier = 1d;
            public double boostSeconds = 0d;
            [Tooltip("Purchase also removes forced ads (offer_2 / offer_3).")]
            public bool removeAds = false;
            [Tooltip("Kalıcı çevrimdışı verim artışı, puan olarak (0.25 = %50'den %75'e). Temel değer OfflineConfig'te.")]
            public double offlineEfficiencyBonus = 0d;
            [Tooltip("Kalıcı çevrimdışı tavan artışı, saat (6 = 2 saatten 8 saate).")]
            public float offlineCapBonusHours = 0f;
            [Tooltip("Günlük ödüle kalıcı çarpan; temel 1× üstüne eklenir, yani 1 = ödül ikiye katlanır.")]
            public double dailyRewardBonusMult = 0d;
            [Tooltip("Ödüllü reklam slotlarının her birine kalıcı olarak eklenen günlük hak sayısı.")]
            public int freeRewardBonusCharges = 0;
            [Tooltip("Her günlük ödül alışına eklenen sabit elmas. Çarpandan bağımsızdır — kart ikisini " +
                     "ayrı satırlarda sattığı için bunu da katlamak gizli bir fazladan olurdu.")]
            public long dailyGemStipend = 0;
            [Tooltip("Yalnızca bir kez satın alınır. Kalıcı bir şey veren her teklif bunu işaretlemeli — " +
                     "yoksa oyuncu aynı kalıcı yükseltmeyi üst üste alıp istifler.")]
            public bool oneTime = false;
        }

        [Header("Panel (UI_Magaza prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Paket ızgaraları")]
        [Tooltip("GoldPackIAP hücrelerinin eklendiği ızgara.")]
        [SerializeField] private RectTransform goldGrid;
        [Tooltip("GemPackIAP hücrelerinin eklendiği ızgara.")]
        [SerializeField] private RectTransform diamondGrid;
        [Tooltip("Pasif şablon hücre; katalogdaki her paket için bir kopya açılır.")]
        [SerializeField] private GameObject cellTemplate;

        [Header("Katalog (serbestçe düzenle)")]
        [SerializeField] private List<StoreItem> items = new List<StoreItem>();
        [SerializeField] private List<OfferBinding> offers = new List<OfferBinding>();

        [Tooltip("Editör testi: IAP stub'ı her satın almayı reddettiği için, bu açıkken ödüller IAP'siz anında verilir. Cihaz sürümünde yok sayılır.")]
        [SerializeField] private bool devFreeIAP;

        [Header("Efektler")]
        [Tooltip("Satın alma başarılı olunca karttan yukarı coin/elmas uçuran efekt; boşsa sessizce atlanır.")]
        [SerializeField] private StorePurchaseFx purchaseFx;

        private WalletService _wallet;
        private IIAPService _iap;
        private BoostService _boost;
        private FreeRewardService _free;
        private SaveData _data;
        private SaveService _save;
        private bool _built;

        /// <summary>
        /// The remove-ads entitlement lives in the save (via <see cref="FreeRewardService"/>) rather than
        /// in a field here: it is a purchase, and a purchase that a restart forgets is a refund request.
        /// </summary>
        private bool AdsRemoved
        {
            get { return _free != null && _free.AdsRemoved; }
            set { if (_free != null) _free.AdsRemoved = value; }
        }

        private void Start()
        {
            ResolveServices();

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            for (int i = 0; i < offers.Count; i++)
            {
                OfferBinding captured = offers[i];
                if (captured != null && captured.button != null)
                    captured.button.onClick.AddListener(() => BuyOffer(captured));
            }

            BuildCells();
            RefreshOffers();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void ResolveServices()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_iap == null) _iap = ServiceLocator.Get<IIAPService>();
            if (_boost == null) _boost = ServiceLocator.Get<BoostService>();
            if (_free == null) _free = ServiceLocator.Get<FreeRewardService>();
            if (_data == null) _data = ServiceLocator.Get<SaveData>();
            if (_save == null) _save = ServiceLocator.Get<SaveService>();
        }

        /// <summary>Has this one-time offer already been bought? Untracked skus are always buyable.</summary>
        private bool Owned(OfferBinding offer)
        {
            if (!offer.oneTime || _data == null || string.IsNullOrEmpty(offer.sku)) return false;
            for (int i = 0; i < _data.purchasedOffers.Count; i++)
                if (_data.purchasedOffers[i] == offer.sku) return true;
            return false;
        }

        // ---------- open / close ----------
        public void Show()
        {
            ResolveServices();
            StampStarterWindow();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshOffers();
        }

        /// <summary>
        /// Starts the starter offer's countdown the first time the store is actually opened rather than
        /// at install: someone who comes back on day three should still get the full window instead of
        /// an offer that expired while they were away. <see cref="OfferCountdown"/> only reads it.
        /// </summary>
        private void StampStarterWindow()
        {
            if (_data == null || _data.starterOfferSeenUnix > 0L) return;
            TimeService time = ServiceLocator.Get<TimeService>();
            if (time == null) return;
            _data.starterOfferSeenUnix = time.NowUnix();
            if (_save != null) _save.Save(_data);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ---------- construction ----------
        private void BuildCells()
        {
            if (_built || cellTemplate == null) return;
            _built = true;
            cellTemplate.SetActive(false);

            for (int i = 0; i < items.Count; i++)
            {
                StoreItem item = items[i];
                if (item == null) continue;
                RectTransform parent = item.kind == StoreItemKind.GoldPackIAP ? goldGrid : diamondGrid;
                if (parent == null) continue;

                GameObject go = Instantiate(cellTemplate, parent);
                go.name = "Paket_" + item.sku;
                go.SetActive(true);

                Image bg = go.GetComponent<Image>();
                if (bg != null && item.icon != null) bg.sprite = item.icon;

                Transform amountT = go.transform.Find("Adet");
                if (amountT != null)
                {
                    TMP_Text amountTxt = amountT.GetComponent<TMP_Text>();
                    if (amountTxt != null) amountTxt.text = item.title;
                }

                Transform priceT = go.transform.Find("Fiyat");
                if (priceT != null)
                {
                    TMP_Text priceTxt = priceT.GetComponent<TMP_Text>();
                    if (priceTxt != null) priceTxt.text = item.priceLabel;
                }

                Button btn = go.GetComponent<Button>();
                StoreItem captured = item;
                RectTransform cardRt = (RectTransform)go.transform;   // the fx launches off the cell
                if (btn != null) btn.onClick.AddListener(() => Buy(captured, cardRt));
            }
        }

        // ---------- purchase handling ----------
        /// <summary>Real IAP normally; the dev toggle short-circuits to success so grants are testable in-editor.</summary>
        private void PurchaseFlow(string sku, Action<bool> onDone)
        {
            if (devFreeIAP && (Application.isEditor || Debug.isDebugBuild)) { onDone(true); return; }
            if (_iap != null) _iap.Purchase(sku, onDone);
        }

        private void Buy(StoreItem item, RectTransform card)
        {
            if (item.kind == StoreItemKind.GoldPackIAP)
                PurchaseFlow(item.sku, ok =>
                {
                    if (!ok) return;
                    if (_wallet != null) _wallet.AddCash(new BigDouble(item.cashAmount));
                    if (purchaseFx != null) purchaseFx.PlayCash(card);
                });
            else
                PurchaseFlow(item.sku, ok =>
                {
                    if (!ok) return;
                    if (_wallet != null) _wallet.AddGems(item.gemAmount);
                    if (purchaseFx != null) purchaseFx.PlayGems(card);
                });
        }

        private void BuyOffer(OfferBinding offer)
        {
            if (Owned(offer)) return;
            PurchaseFlow(offer.sku, ok =>
            {
                if (!ok) return;
                if (_wallet != null && offer.cashAmount > 0d) _wallet.AddCash(new BigDouble(offer.cashAmount));
                if (_wallet != null && offer.gemAmount > 0) _wallet.AddGems(offer.gemAmount);
                if (_boost != null && offer.boostMultiplier > 1d && offer.boostSeconds > 0d)
                    _boost.SetBoost(offer.boostMultiplier, offer.boostSeconds);
                if (offer.removeAds) AdsRemoved = true;
                GrantPermanent(offer);
                RefreshOffers();

                if (purchaseFx != null && offer.button != null)
                {
                    RectTransform card = (RectTransform)offer.button.transform;
                    if (offer.cashAmount > 0d) purchaseFx.PlayCash(card);
                    if (offer.gemAmount > 0) purchaseFx.PlayGems(card);
                }
            });
        }

        /// <summary>
        /// Banks the perks that outlive the session and writes the save immediately. Waiting for the
        /// next pause/quit would mean a crash right after a purchase loses it, and a purchase a restart
        /// forgets is a refund request.
        /// </summary>
        private void GrantPermanent(OfferBinding offer)
        {
            if (_data == null) return;
            if (offer.offlineEfficiencyBonus > 0d) _data.offlineEfficiencyBonus += offer.offlineEfficiencyBonus;
            if (offer.offlineCapBonusHours > 0f) _data.offlineCapBonusSeconds += (long)(offer.offlineCapBonusHours * 3600f);
            if (offer.dailyRewardBonusMult > 0d) _data.dailyRewardBonusMult += offer.dailyRewardBonusMult;
            if (offer.freeRewardBonusCharges > 0) _data.freeRewardBonusCharges += offer.freeRewardBonusCharges;
            if (offer.dailyGemStipend > 0) _data.dailyGemStipend += offer.dailyGemStipend;
            if (offer.oneTime && !string.IsNullOrEmpty(offer.sku)) _data.purchasedOffers.Add(offer.sku);
            if (_save != null) _save.Save(_data);
        }

        /// <summary>A card greys out once what it sells is already owned; everything else stays buyable.</summary>
        private void RefreshOffers()
        {
            for (int i = 0; i < offers.Count; i++)
            {
                OfferBinding o = offers[i];
                if (o == null || o.button == null) continue;
                bool spent = Owned(o) || (o.removeAds && AdsRemoved);
                o.button.interactable = !spent;
                // The disabled tint latches when the panel opens and the state changes in the same
                // frame; push the right colour straight away, as the other screens do.
                if (o.button.targetGraphic != null)
                    o.button.targetGraphic.CrossFadeColor(
                        spent ? o.button.colors.disabledColor : Color.white, 0f, true, true);
            }
        }
    }
}
