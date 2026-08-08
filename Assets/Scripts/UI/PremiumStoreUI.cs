using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
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
            [Tooltip("Cash granted — GoldPackIAP. Taban görevi görür: incomeMinutes daha azını " +
                     "hesaplarsa bu ödenir, yani hiçbir kart bu rakamın altına inmez.")]
            public double cashAmount = 0d;
            [Tooltip("Kaç dakikalık imparatorluk geliri versin. 0'dan büyükse kart oyuncunun kendi " +
                     "hızıyla ölçeklenir ve üstündeki rakam her açılışta yeniden yazılır. Sabit bir " +
                     "tutar her adada başka bir ürün demek: 1M, maksimum kömürde on dakika, elmas " +
                     "adasında altıda bir saniye.")]
            public float incomeMinutes = 0f;
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
            [Tooltip("Kalıcı çevrimdışı tavan artışı, saat. Taban OfflineConfig'te ve 8 saat — " +
                     "yani 6 yazarsan 8 saatten 14 saate çıkar. Bu alanlar toplanır: iki ayrı " +
                     "teklif alan oyuncunun tavanı ikisinin toplamı kadar yükselir.")]
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

            // ---- elmasla satılanlar (gemOffers listesi) ----
            // Bunlar hiyerarşide kart olarak durmaz; ızgaraya cellTemplate'ten klonlanır ve gerçek para
            // yerine elmasla ödenir. sku yine kimliktir: oneTime takibi purchasedOffers üzerinden gider.
            [Tooltip("0'dan büyükse kart IAP yerine ELMAS ile ödenir ve elmas ızgarasına eklenir.")]
            public long gemPrice = 0;
            [Tooltip("Kart zemini — elmas ızgarasındaki hücrenin arka planı olur (kahramansız).")]
            public Sprite icon;
            [Tooltip("Zeminin üstüne konan kahraman ikon. Boşsa hücrenin Ikon çocuğu kapalı kalır.")]
            public Sprite hero;
            [Tooltip("Kahramanın boştaki hareketi — ürüne uygun olanı seç.")]
            public StoreHeroFx.Motion heroMotion = StoreHeroFx.Motion.Bob;
            [Tooltip("Hücrede yazan başlık, ör. \"×2 · 8 SAAT\".")]
            public string title = "";
            [Tooltip("Kartın altındaki tek satırlık açıklama. Boşsa satır hiç görünmez — bir rakamın " +
                     "kendini anlattığı kartlarda (süre, tutar) boş bırak.")]
            [TextArea(2, 3)] public string description = "";
            [Tooltip("Bu kadar dakikalık imparatorluk geliri kadar anında nakit verir (0 = nakit yok).")]
            public float incomeMinutes = 0f;
            [Tooltip("Prestij yapsa kazanacağı yatırımcının bu oranını verir (0.5 = yarısı). " +
                     "Ömür boyu kazançtan karşılığını yakar, bkz. PrestigeService.TakeInvestorShare.")]
            [Range(0f, 1f)] public float investorShare = 0f;
        }

        [Header("Panel (UI_Magaza prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Paket ızgaraları")]
        [Tooltip("GoldPackIAP hücrelerinin eklendiği ızgara.")]
        [SerializeField] private RectTransform goldGrid;
        [Tooltip("GemPackIAP hücrelerinin eklendiği ızgara.")]
        [SerializeField] private RectTransform diamondGrid;
        [Tooltip("ELMAS İLE AL bölümü: elmasla ödenen tekliflerin eklendiği ızgara.")]
        [SerializeField] private RectTransform gemSpendGrid;
        [Tooltip("Pasif şablon hücre; katalogdaki her paket için bir kopya açılır.")]
        [SerializeField] private GameObject cellTemplate;

        [Header("Katalog (serbestçe düzenle)")]
        [SerializeField] private List<StoreItem> items = new List<StoreItem>();
        [SerializeField] private List<OfferBinding> offers = new List<OfferBinding>();
        [Tooltip("Elmasla satın alınanlar. gemPrice > 0 olmalı; hücreleri gemSpendGrid'e klonlanır.")]
        [SerializeField] private List<OfferBinding> gemOffers = new List<OfferBinding>();

        [Tooltip("ELMAS İLE AL kartlarında fiyatın soluna konan elmas. Boş bırakılırsa fiyat çıplak bir " +
                 "sayı kalır ve neyle ödendiği yazmaz.")]
        [SerializeField] private Sprite gemIcon;

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
        private PrestigeService _prestige;
        private Game.Gameplay.WorldIslands _world;
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
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void ResolveServices()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_iap == null) _iap = ServiceLocator.Get<IIAPService>();
            if (_boost == null) _boost = ServiceLocator.Get<BoostService>();
            if (_free == null) _free = ServiceLocator.Get<FreeRewardService>();
            if (_data == null) _data = ServiceLocator.Get<SaveData>();
            if (_save == null) _save = ServiceLocator.Get<SaveService>();
            if (_prestige == null) _prestige = ServiceLocator.Get<PrestigeService>();
            if (_world == null) _world = FindAnyObjectByType<Game.Gameplay.WorldIslands>();
        }

        /// <summary>
        /// What the empire earns a minute — prices the "instant cash" gem cards, exactly as the rewarded-ad
        /// screen prices its own. Sold as minutes rather than a fixed sum so a card is worth the same
        /// share of progress on coal as it is on diamond.
        /// </summary>
        private double IncomePerMinute()
        {
            if (_world != null)
            {
                double sum = 0d;
                for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
                if (sum > 0d) return sum;
            }
            return 0d;
        }

        /// <summary>
        /// True when the offer would charge for an empty grant. A card priced in minutes of income pays
        /// nothing while the empire has not reported a rate yet, and a card that converts progress into
        /// investors pays nothing before there is any progress to convert. Gems leave the wallet before
        /// <see cref="Grant"/> runs, so without this the sale takes the price and hands back nothing.
        /// </summary>
        private bool NothingToGrant(OfferBinding offer)
        {
            if (offer.incomeMinutes > 0f && IncomePerMinute() <= 0d) return true;
            if (offer.investorShare > 0f && _prestige != null
                && _prestige.PendingInvestors().Mantissa <= 0d) return true;
            return false;
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
            RefreshGoldAmounts();
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
        private void OnEnable()
        {
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += Rebuild;
        }

        private void OnDisable()
        {
            if (_loc != null) _loc.Changed -= Rebuild;
        }

        /// <summary>
        /// Cell captions are written once as the grid is built, so a language change has to build it
        /// again. Cheap and rare — the alternative is re-writing every caption on every refresh tick.
        /// </summary>
        private void Rebuild()
        {
            if (!_built) return;
            Clear(goldGrid);
            Clear(diamondGrid);
            Clear(gemSpendGrid);
            _built = false;
            BuildCells();
            RefreshOffers();
        }

        private void Clear(RectTransform grid)
        {
            if (grid == null) return;
            // Destroy sona ertelendiği için önce ayır ve kapat, yoksa eski hücreler bir kare daha ızgarada durur.
            for (int i = grid.childCount - 1; i >= 0; i--)
            {
                GameObject child = grid.GetChild(i).gameObject;
                if (child == cellTemplate) continue;
                child.SetActive(false);
                child.transform.SetParent(null, false);
                Destroy(child);
            }
        }

        private LocalizationService _loc;

        private void BuildCells()
        {
            if (_built || cellTemplate == null) return;
            _built = true;
            cellTemplate.SetActive(false);
            CollectOfferLabels();

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
                    // Gelire bağlı altın kartlarının rakamı sabit değil; her açılışta yeniden yazılsın
                    // diye etiketi burada tutuyoruz. Elmas kartları sert para, onlar yazıldığı gibi kalır.
                    if (amountTxt != null && item.kind == StoreItemKind.GoldPackIAP && item.incomeMinutes > 0f)
                        _cashLabels.Add(new CashLabel
                        {
                            label = amountTxt, floor = item.cashAmount, minutes = item.incomeMinutes
                        });
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

            // The ELMAS İLE AL section. Same template, same grant path as the hierarchy-authored offer
            // cards — only the till differs. Wiring each offer's `button` here is what lets RefreshOffers
            // grey them out without knowing where the card came from.
            if (gemSpendGrid == null) return;
            for (int i = 0; i < gemOffers.Count; i++)
            {
                OfferBinding offer = gemOffers[i];
                if (offer == null || offer.gemPrice <= 0) continue;

                GameObject go = Instantiate(cellTemplate, gemSpendGrid);
                go.name = "Elmas_" + offer.sku;
                go.SetActive(true);

                Image bg = go.GetComponent<Image>();
                if (bg != null && offer.icon != null) bg.sprite = offer.icon;

                // Kart yazıları sku'ya göre çevrilir ("magaza.gem_boost_2h"). Satırı olmayan bir sku
                // Inspector'daki metniyle kalır, yani yeni bir ürün eklenince kart boş çıkmaz.
                Transform amountT = go.transform.Find("Adet");
                if (amountT != null)
                {
                    TMP_Text amountTxt = amountT.GetComponent<TMP_Text>();
                    if (amountTxt != null) amountTxt.text = Line("magaza." + offer.sku, offer.title);
                }

                Transform priceT = go.transform.Find("Fiyat");
                if (priceT != null)
                {
                    TMP_Text priceTxt = priceT.GetComponent<TMP_Text>();
                    if (priceTxt != null)
                    {
                        priceTxt.text = offer.gemPrice.ToString();
                        GemMark(priceTxt);
                    }
                }

                // A price and a duration explain themselves; "×2" and "%50" do not. Only the cards that
                // sell a rule rather than an amount carry a line, and the rest leave it switched off
                // rather than showing an empty strip.
                Transform noteT = go.transform.Find("Aciklama");
                if (noteT != null)
                {
                    TMP_Text noteTxt = noteT.GetComponent<TMP_Text>();
                    bool hasNote = !string.IsNullOrEmpty(offer.description) && noteTxt != null;
                    if (hasNote) noteTxt.text = Line("magaza." + offer.sku + ".aciklama", offer.description);
                    noteT.gameObject.SetActive(hasNote);
                }

                // The pack cells bake their hero into the card art, so the template's Ikon child ships
                // switched off and only a gem cell turns it on. Phase is the cell's own index, which is
                // what keeps six cards in a grid from bobbing and rattling in lockstep.
                Transform heroT = go.transform.Find("Ikon");
                if (heroT != null)
                {
                    Image heroImg = heroT.GetComponent<Image>();
                    if (offer.hero != null && heroImg != null)
                    {
                        heroImg.sprite = offer.hero;
                        heroT.gameObject.SetActive(true);
                        StoreHeroFx fx = heroT.GetComponent<StoreHeroFx>();
                        if (fx != null) fx.Configure(offer.heroMotion, i * 0.37f);
                    }
                    else heroT.gameObject.SetActive(false);
                }

                offer.button = go.GetComponent<Button>();
                OfferBinding captured = offer;
                if (offer.button != null) offer.button.onClick.AddListener(() => BuyOffer(captured));
            }
        }

        /// <summary>
        /// Puts the gem beside a gem card's price. The packs above are priced in real money and carry the
        /// currency symbol with them; these cards showed a bare "35", which says how many but never of
        /// what. The badge goes to the left of the digits and the digits slide right by half of it, so the
        /// pair still reads as centred on the card whatever the price is.
        /// </summary>
        private void GemMark(TMP_Text price)
        {
            if (gemIcon == null || price == null) return;
            const float size = 42f, gap = 6f;
            float digits = price.GetPreferredValues(price.text).x;

            var go = new GameObject("Elmas", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(price.rectTransform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-(digits * 0.5f + gap + size * 0.5f), 0f);

            Image img = go.GetComponent<Image>();
            img.sprite = gemIcon;
            img.preserveAspect = true;
            img.raycastTarget = false;   // the whole card is the button

            price.rectTransform.anchoredPosition += new Vector2((size + gap) * 0.5f, 0f);
        }

        /// <summary>Translated line for <paramref name="key"/>, or the authored text when the table has
        /// no row for it — a card added in the Inspector shows something either way.</summary>
        private static string Line(string key, string authored)
        {
            string v = Loc.T(key);
            return v == key ? authored : v;
        }

        // ---------- purchase handling ----------
        /// <summary>Real IAP normally; the dev toggle short-circuits to success so grants are testable in-editor.</summary>
        private void PurchaseFlow(string sku, Action<bool> onDone)
        {
            if (devFreeIAP && (Application.isEditor || Debug.isDebugBuild)) { onDone(true); return; }
            if (_iap != null) _iap.Purchase(sku, onDone);
        }

        /// <summary>
        /// What a cash card actually pays. Priced in minutes of the empire's own income, so one card is
        /// worth the same share of progress on every island — a fixed sum is a different product on each
        /// one and has to be rebalanced every time the curve moves. Each island earns 3.2× the one before
        /// it, so by the diamond island a fixed million is under a second of income.
        ///
        /// The authored sum is kept as a floor rather than replaced, which covers the case the income
        /// path cannot: a player whose measured rate is far below what they have actually unlocked.
        /// That is not a corner case — an island bought but not yet built reports nothing, so without
        /// a floor the whole store collapses to pocket change the moment someone sails somewhere new.
        ///
        /// Every floor is written against ONE reference — about 19.3K/min, which is a fifth of what
        /// building the coal island costs spread over the smallest pack's fifteen minutes. Before this
        /// the six gold packs sat at 166.7/min and the two offers at 102,626/min, six hundred times
        /// apart, and the ₺999,99 pack paid less than the cheapest offer. One reference is the fix;
        /// the exact number is a balance choice, not arithmetic.
        ///
        /// That fifth is what has to be held, not the reference itself: re-solving the ladder moves
        /// every island's cost multiplier, so the floors move with it. They were last recomputed on
        /// 2026-08-07, when coal's build cost went from 1,995,902 to 1,451,603 and all eight floors
        /// were scaled by the same 0.7273.
        ///
        /// The floor is then scaled by the island, because a fifth of coal is nothing on diamond.
        /// </summary>
        private double CashGrant(double floor, float minutes)
        {
            double scaledFloor = floor * IslandScale();
            if (minutes <= 0f) return scaledFloor;
            double scaled = IncomePerMinute() * minutes;
            return scaled > scaledFloor ? scaled : scaledFloor;
        }

        /// <summary>
        /// Where the player is standing, as a multiple of coal: 1 on coal, 3.2 on copper, 10.24 on iron.
        ///
        /// Read off the island caps rather than written down again, so the day the ladder's step changes
        /// the store follows it instead of quietly paying the old curve.
        /// </summary>
        private double IslandScale()
        {
            if (_world == null) return 1d;
            double coal = _world.CapPerMin(0);
            if (coal <= 0d) return 1d;
            return _world.CapPerMin(_world.ActiveIndex) / coal;
        }

        /// <summary>
        /// Rewrites what the gold cards say they give. Called on every open because the answer changes
        /// as the empire grows: the cells themselves are built once, but the number on them is not a
        /// property of the cell, it is a property of the player.
        /// </summary>
        private void RefreshGoldAmounts()
        {
            for (int i = 0; i < _cashLabels.Count; i++)
            {
                CashLabel c = _cashLabels[i];
                if (c.label == null) continue;
                c.label.text = NumberFormatter.Format(new BigDouble(CashGrant(c.floor, c.minutes)));
            }
        }

        /// <summary>
        /// One number on a card that has to be recomputed rather than read. Gold cells and the two
        /// cash offers land in the same list because the question they answer is the same one, and
        /// the offers' labels are authored in the hierarchy where nothing else would find them.
        /// </summary>
        private struct CashLabel
        {
            public TMP_Text label;
            public double floor;
            public float minutes;
        }

        private readonly List<CashLabel> _cashLabels = new List<CashLabel>();

        /// <summary>Finds the amount label authored on each hierarchy offer card. Runs once, at build.</summary>
        private void CollectOfferLabels()
        {
            for (int i = 0; i < offers.Count; i++)
            {
                OfferBinding offer = offers[i];
                if (offer == null || offer.button == null || offer.incomeMinutes <= 0f) continue;
                var found = offer.button.GetComponentsInChildren<TMP_Text>(true);
                for (int t = 0; t < found.Length; t++)
                    if (found[t].name == "DegerAltin")
                    {
                        _cashLabels.Add(new CashLabel
                        {
                            label = found[t], floor = offer.cashAmount, minutes = offer.incomeMinutes
                        });
                        break;
                    }
            }
        }

        private void Buy(StoreItem item, RectTransform card)
        {
            if (item.kind == StoreItemKind.GoldPackIAP)
                PurchaseFlow(item.sku, ok =>
                {
                    if (!ok) return;
                    if (_wallet != null) _wallet.AddCash(new BigDouble(CashGrant(item.cashAmount, item.incomeMinutes)));
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

        /// <summary>
        /// The one till in the game. <see cref="OfferPopupUI"/> sells through here as well rather than
        /// keeping a second copy of the grant rules — a pop-up sale and a store sale then cannot drift
        /// apart, and the pop-up's cards get the same sound, haptic and effects for free. It calls this
        /// while the store is shut, hence the resolve: nothing else has run on this component yet.
        /// <paramref name="onDone"/> reports whether anything was actually granted.
        /// </summary>
        public void BuyOffer(OfferBinding offer, Action<bool> onDone = null)
        {
            ResolveServices();
            if (offer == null || Owned(offer)) { onDone?.Invoke(false); return; }
            // Two tills, one till-slip. A gem card is paid for out of the wallet and either succeeds or
            // does not, so there is nothing to wait for; a money card goes out to the store and comes
            // back later. Both hand the same offer to the same grant.
            if (offer.gemPrice > 0)
            {
                if (NothingToGrant(offer)) { onDone?.Invoke(false); return; }
                if (_wallet == null || !_wallet.TrySpendGems(offer.gemPrice)) { onDone?.Invoke(false); return; }
                Grant(offer);
                onDone?.Invoke(true);
            }
            else
            {
                PurchaseFlow(offer.sku, ok => { if (ok) Grant(offer); onDone?.Invoke(ok); });
            }
        }

        private void Grant(OfferBinding offer)
        {
            if (_wallet != null)
            {
                double cash = CashGrant(offer.cashAmount, offer.incomeMinutes);
                if (cash > 0d) _wallet.AddCash(new BigDouble(cash));
            }
            if (_wallet != null && offer.gemAmount > 0) _wallet.AddGems(offer.gemAmount);
            if (_boost != null && offer.boostMultiplier > 1d && offer.boostSeconds > 0d)
                _boost.AddBoost(offer.boostMultiplier, offer.boostSeconds);
            if (_prestige != null && offer.investorShare > 0f) _prestige.TakeInvestorShare(offer.investorShare);
            if (offer.removeAds) AdsRemoved = true;
            GrantPermanent(offer);
            RefreshOffers();

            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Purchase);
            var haptic = ServiceLocator.Get<HapticService>();
            if (haptic != null) haptic.Medium();

            if (purchaseFx != null && offer.button != null)
            {
                RectTransform card = (RectTransform)offer.button.transform;
                if (offer.cashAmount > 0d || offer.incomeMinutes > 0f) purchaseFx.PlayCash(card);
                if (offer.gemAmount > 0) purchaseFx.PlayGems(card);
            }
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

        /// <summary>
        /// A card greys out once what it sells is already owned; everything else stays buyable. Gem cards
        /// grey out for two more reasons — not enough gems, and nothing yet to grant — which is the honest
        /// way to say "not yet" on a currency the player earns rather than buys.
        /// </summary>
        private void RefreshOffers()
        {
            Grey(offers);
            Grey(gemOffers);
        }

        private void Grey(List<OfferBinding> list)
        {
            long gems = _wallet != null ? _wallet.Gems : 0L;
            for (int i = 0; i < list.Count; i++)
            {
                OfferBinding o = list[i];
                if (o == null || o.button == null) continue;
                bool spent = Owned(o) || (o.removeAds && AdsRemoved)
                             || (o.gemPrice > 0 && (gems < o.gemPrice || NothingToGrant(o)));
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
