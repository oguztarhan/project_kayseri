using System;
using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Premium Store / Shop (GDD §10). A right-edge button opens a skinned panel with two sections —
    /// GOLD PACKS and DIAMOND PACKS — each a grid of cells (icon + amount + price button). Gold packs
    /// grant cash and diamond packs grant gems through <see cref="IIAPService"/>; the panel also supports
    /// rewarded-ad and gem-priced boost rows if added to the catalog. Editor-authored: the whole hierarchy
    /// lives in the UI_Store prefab and every reference below is wired in the Inspector, so layout, skin
    /// and catalog are all tunable without touching code.
    /// </summary>
    public sealed class PremiumStoreUI : MonoBehaviour
    {
        /// <summary>What a store cell does when its action button is pressed.</summary>
        public enum StoreItemKind
        {
            GemPackIAP,     // 0 - IAP grants gemAmount gems (diamond pack)
            GoldPackIAP,    // 1 - IAP grants cashAmount cash (gold pack)
            RemoveAdsIAP,   // 2 - IAP flags ads removed on success
            RewardedGems,   // 3 - watch a rewarded ad, receive gemAmount
            RewardedBoost,  // 4 - watch a rewarded ad, receive an income boost
            GemBoost        // 5 - spend gemCost gems for an income boost
        }

        /// <summary>One catalog entry. Edit the list on the component to add/remove/retune offers.</summary>
        [Serializable]
        public sealed class StoreItem
        {
            public string title = "New Offer";
            public StoreItemKind kind = StoreItemKind.GemPackIAP;
            public Sprite icon;
            [Tooltip("Extra scale applied to the cell icon (bigger pile for bigger packs).")]
            public float iconScale = 1f;
            [Tooltip("Real-money price shown on the buy button, e.g. \"TRY 109,99\" (IAP kinds).")]
            public string priceLabel = "";
            [Tooltip("IAP product id — GemPackIAP / GoldPackIAP / RemoveAdsIAP")]
            public string sku = "";
            [Tooltip("Cash granted — GoldPackIAP")]
            public double cashAmount = 0d;
            [Tooltip("Gems granted — GemPackIAP / RewardedGems")]
            public long gemAmount = 100;
            [Tooltip("Gems spent — GemBoost")]
            public long gemCost = 50;
            [Tooltip("Income multiplier — RewardedBoost / GemBoost")]
            public double boostMultiplier = 2d;
            [Tooltip("Boost duration in seconds — RewardedBoost / GemBoost")]
            public double boostSeconds = 300d;
        }

        [Header("Wiring (assigned on the UI_Store prefab)")]
        [SerializeField] private Button openButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text gemsBalanceText;
        [SerializeField] private Text cashBalanceText;
        [Tooltip("Grid that receives GoldPackIAP cells.")]
        [SerializeField] private RectTransform goldGrid;
        [Tooltip("Grid that receives all other (gem/diamond) cells.")]
        [SerializeField] private RectTransform diamondGrid;
        [Tooltip("Fallback container used when the grids above are not set.")]
        [SerializeField] private RectTransform content;
        [Tooltip("Inactive template cell; one clone is spawned per catalog item.")]
        [SerializeField] private GameObject cellTemplate;

        [Header("Catalog (edit freely)")]
        [SerializeField] private List<StoreItem> items = new List<StoreItem>();

        private WalletService _wallet;
        private IAdService _ad;
        private IIAPService _iap;
        private BoostService _boost;
        private bool _adsRemoved;
        private bool _built;

        private sealed class Row
        {
            public StoreItem item;
            public Button button;
            public Text actionLabel;
        }
        private readonly List<Row> _rows = new List<Row>();

        private void Start()
        {
            ResolveServices();

            if (openButton != null) openButton.onClick.AddListener(Show);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);

            BuildRows();

            if (_wallet != null) { _wallet.GemsChanged += OnBalanceChanged; _wallet.CashChanged += OnBalanceChanged; }
            if (panelRoot != null) panelRoot.SetActive(false);
            RefreshBalance();
            RefreshRows();
        }

        private void OnDestroy()
        {
            if (_wallet != null) { _wallet.GemsChanged -= OnBalanceChanged; _wallet.CashChanged -= OnBalanceChanged; }
        }

        private void ResolveServices()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_ad == null) _ad = ServiceLocator.Get<IAdService>();
            if (_iap == null) _iap = ServiceLocator.Get<IIAPService>();
            if (_boost == null) _boost = ServiceLocator.Get<BoostService>();
        }

        // ---------- open / close ----------
        public void Show()
        {
            ResolveServices();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshBalance();
            RefreshRows();
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ---------- construction ----------
        private RectTransform ParentFor(StoreItem item)
        {
            if (item.kind == StoreItemKind.GoldPackIAP) return goldGrid != null ? goldGrid : content;
            return diamondGrid != null ? diamondGrid : content;
        }

        private void BuildRows()
        {
            if (_built || cellTemplate == null) return;
            _built = true;
            cellTemplate.SetActive(false);

            for (int i = 0; i < items.Count; i++)
            {
                StoreItem item = items[i];
                if (item == null) continue;
                RectTransform parent = ParentFor(item);
                if (parent == null) continue;

                GameObject go = Instantiate(cellTemplate, parent);
                go.name = "Cell_" + item.kind + "_" + i;
                go.SetActive(true);

                Transform iconT = go.transform.Find("Icon");
                if (iconT != null)
                {
                    Image iconImg = iconT.GetComponent<Image>();
                    if (iconImg != null)
                    {
                        if (item.icon != null) iconImg.sprite = item.icon;
                        iconImg.enabled = iconImg.sprite != null;
                    }
                    iconT.localScale = Vector3.one * (item.iconScale <= 0f ? 1f : item.iconScale);
                }

                Transform titleT = go.transform.Find("Title");
                if (titleT != null)
                {
                    Text titleTxt = titleT.GetComponent<Text>();
                    if (titleTxt != null) titleTxt.text = item.title;
                }

                Button actionBtn = null;
                Transform actionT = go.transform.Find("Action");
                if (actionT != null) actionBtn = actionT.GetComponent<Button>();
                if (actionBtn == null) continue;

                Text actionLabel = actionBtn.GetComponentInChildren<Text>();
                StoreItem captured = item;
                actionBtn.onClick.AddListener(() => Buy(captured));
                if (actionLabel != null) actionLabel.text = ActionText(item);

                _rows.Add(new Row { item = item, button = actionBtn, actionLabel = actionLabel });
            }
        }

        private static string ActionText(StoreItem item)
        {
            switch (item.kind)
            {
                case StoreItemKind.GemPackIAP:
                case StoreItemKind.GoldPackIAP:
                case StoreItemKind.RemoveAdsIAP:
                    return string.IsNullOrEmpty(item.priceLabel) ? "Buy" : item.priceLabel;
                case StoreItemKind.RewardedGems:
                case StoreItemKind.RewardedBoost:
                    return "Watch Ad";
                case StoreItemKind.GemBoost:
                    return item.gemCost + " Gems";
                default:
                    return "Buy";
            }
        }

        // ---------- purchase handling ----------
        private void Buy(StoreItem item)
        {
            switch (item.kind)
            {
                case StoreItemKind.GoldPackIAP:
                    if (_iap != null)
                        _iap.Purchase(item.sku, ok => { if (ok && _wallet != null) _wallet.AddCash(new BigDouble(item.cashAmount)); });
                    break;

                case StoreItemKind.GemPackIAP:
                    if (_iap != null)
                        _iap.Purchase(item.sku, ok => { if (ok && _wallet != null) _wallet.AddGems(item.gemAmount); });
                    break;

                case StoreItemKind.RemoveAdsIAP:
                    if (_iap != null)
                        _iap.Purchase(item.sku, ok => { if (ok) { _adsRemoved = true; RefreshRows(); } });
                    break;

                case StoreItemKind.RewardedGems:
                    if (_ad != null && _ad.Available)
                        _ad.ShowRewarded(() => { if (_wallet != null) _wallet.AddGems(item.gemAmount); });
                    break;

                case StoreItemKind.RewardedBoost:
                    if (_ad != null && _ad.Available)
                        _ad.ShowRewarded(() => { if (_boost != null) _boost.SetBoost(item.boostMultiplier, item.boostSeconds); });
                    break;

                case StoreItemKind.GemBoost:
                    if (_wallet != null && _wallet.TrySpendGems(item.gemCost) && _boost != null)
                        _boost.SetBoost(item.boostMultiplier, item.boostSeconds);
                    break;
            }
        }

        // ---------- refresh ----------
        private void OnBalanceChanged()
        {
            RefreshBalance();
            RefreshRows();
        }

        private void RefreshBalance()
        {
            if (gemsBalanceText != null)
                gemsBalanceText.text = "Gems: " + (_wallet != null ? _wallet.Gems.ToString() : "0");
            if (cashBalanceText != null)
                cashBalanceText.text = "Gold: " + (_wallet != null ? NumberFormatter.Format(_wallet.Cash) : "0");
        }

        private void RefreshRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.item.kind == StoreItemKind.GemBoost)
                {
                    r.button.interactable = _wallet != null && _wallet.Gems >= r.item.gemCost;
                }
                else if (r.item.kind == StoreItemKind.RemoveAdsIAP && _adsRemoved)
                {
                    r.button.interactable = false;
                    if (r.actionLabel != null) r.actionLabel.text = "Owned";
                }
            }
        }
    }
}
