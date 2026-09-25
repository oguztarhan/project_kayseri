using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The always-on HUD — first screen of the editor-authored UI set (Figma "hud_montaj"): gold and
    /// gem pills with live TMP values, the income-rate pill under the cash pill, the right rail of
    /// openers (store, offer, daily reward, map) and the big UPGRADE button bottom-right. Thin
    /// controller: every reference below is wired in the Inspector on the UI_HUD prefab, so layout,
    /// sprites and spacing are all tunable from the hierarchy without touching code.
    ///
    /// Replaces the code-built top bar and all of MetaHud. <see cref="ContractService"/>
    /// ticking lives here now (MetaHud used to do it): the HUD is the one screen that is always
    /// loaded, and it is also the only thing that knows the whole empire's income per minute, which
    /// is what sizes each contract. <see cref="ContractUI"/> only reads and claims.
    /// </summary>
    public sealed class HudUI : MonoBehaviour
    {
        public const string SailButtonName = "BtnDenizSavasi";
        public const string MasterButtonName = "BtnUstabasi";
        public const string CaptainButtonName = "BtnKaptan";
        public const string BalloonButtonName = "BtnBalon";
        public const string MoreButtonName = "BtnDahaFazla";

        // The main-menu art is kept in Resources so the existing inspector-authored HUD and the
        // code-built More sheet can share one sprite set without prefab edits.  The names below are
        // the stable opener names used by the existing UI screens.
        private const string MainHudIconRoot = "UI/MainHudButtons/";

        /// <summary>The openers compact mode keeps on the rail itself. Everything else it moves into
        /// the More sheet — see <see cref="AttachBottomButton"/>.</summary>
        private static bool IsCompactOpener(string name)
        {
            // The 3:4 portrait rail has room for every compact opener except the sailboat.
            // Sending that destination to More prevents a second column over the world; wider portrait
            // phones and landscape keep the primary sailboat shortcut on the rail.
            bool crampedPortrait = Screen.width > 0 && Screen.height > 0
                                   && Screen.height >= Screen.width
                                   && (float)Screen.height / Screen.width < 1.5f;
            if (name == SailButtonName && crampedPortrait) return false;
            return name == SailButtonName || name == MasterButtonName || name == CaptainButtonName
                   || name == BalloonButtonName || name == MoreButtonName || name == "BtnPazarGelistir";
        }

        [Header("Dikey tersane sade HUD")]
        [Tooltip("Yalnızca Inspector'daki dört ana eylemi kenar rayında tutar; eski bölüm bildirimi ve yinelenen kısayolları gizler.")]
        [SerializeField] private bool compactShipyardHud = true;
        [Header("Üst bar")]
        [Tooltip("Nakit ve elmas göstergelerinin özgün oranını bozmadan uygulanan ortak ölçek.")]
        [SerializeField, Range(0.6f, 1f)] private float currencyIndicatorScale = 0.76f;
        [Tooltip("Küçültülmüş nakit ve elmas göstergeleri arasındaki tasarım boşluğu.")]
        [SerializeField, Min(0f)] private float currencyIndicatorGap = 18f;
        [SerializeField] private TMP_Text goldValue;
        [SerializeField] private TMP_Text gemsValue;
        [SerializeField] private TMP_Text rateValue;
        [Header("Gelir / hızlandırıcı / kalkan hapları")]
        [Tooltip("Hapların sol kenardan uzaklığı. Nakit plakasının kendi boşluğuna yakın tutulur.")]
        [SerializeField, Min(0f)] private float infoPillLeft = 30f;
        [SerializeField, Min(24f)] private float infoPillHeight = 48f;
        [SerializeField, Min(10f)] private float infoPillFont = 25f;
        [Tooltip("Yazının iki yanındaki toplam boşluk. Genişlik yazıya göre hesaplanır.")]
        [SerializeField, Min(0f)] private float infoPillPadding = 36f;
        [Tooltip("En dar ve en geniş hap. Yazı en geniş olana sığmazsa küçülür.")]
        [SerializeField] private Vector2 infoPillWidth = new Vector2(96f, 340f);
        [Tooltip("Alt alta dizilen hapların arasındaki boşluk.")]
        [SerializeField, Min(0f)] private float infoPillGap = 12f;

        [Header("Bölüm hapı (sağ üst)")]
        [Tooltip("Hapın sağ kenardan uzaklığı.")]
        [SerializeField, Min(0f)] private float stagePillRight = 10f;
        [SerializeField, Min(24f)] private float stagePillHeight = 40f;
        [SerializeField, Min(10f)] private float stagePillFont = 30f;
        [SerializeField, Min(0f)] private float stagePillPadding = 40f;
        [SerializeField] private Vector2 stagePillWidth = new Vector2(104f, 220f);
        [SerializeField] private Button settingsButton;
        [Tooltip("Altın hapının kendisi. Üstündeki + rozeti mağazayı vaat ediyor, o yüzden hap da mağazayı açar.")]
        [SerializeField] private Button goldButton;
        [Tooltip("Elmas hapının kendisi — altın hapı gibi mağazayı açar.")]
        [SerializeField] private Button gemsButton;
        [Tooltip("$/dk hapı. Bir rakam söyleyip susmak yerine, o rakamın nereden geldiğini gösteren " +
                 "ZİNCİR sayfasını açar — hangi aşamanın adayı beklettiği orada yazıyor.")]
        [SerializeField] private Button rateButton;

        [Header("Sağ ray")]
        [SerializeField] private Button storeButton;
        [Tooltip("Reklam butonunun altındaki fırsat kısayolu. HUD'un kalıcı parçası: açık teklif yokken "
                 + "de yerinde durur, o hâlde mağazayı açar.")]
        [SerializeField] private Button offerButton;
        [Tooltip("Fırsat butonunun altındaki geri sayım.")]
        [SerializeField] private TMP_Text offerTimerValue;
        [Tooltip("Geri sayımın kapsülü. Satacak paket kalmayınca kapanır — buton kalır, sayaç gider.")]
        [SerializeField] private GameObject offerTimerChip;
        [SerializeField] private Button dailyButton;
        [SerializeField] private Button contractButton;
        [Tooltip("Kontrat butonunun altındaki canlı sayaç.")]
        [SerializeField] private TMP_Text contractTimerValue;
        [SerializeField] private Button adButton;

        [Header("Balon reklamı")]
        [Tooltip("Ayrı HUD balon simgesi. Boşsa mevcut reklam simgesi kullanılır.")]
        [SerializeField] private Sprite balloonIcon;
        [Tooltip("Nakit ödülünün oyuncunun mevcut gelirinden hesaplanan dakika karşılığı.")]
        [SerializeField, Min(0f)] private float balloonCashMinutes = 5f;
        [Tooltip("Yeni hesaplarda balon ödülünün boş kalmaması için en düşük nakit ödülü.")]
        [SerializeField, Min(0f)] private double balloonCashFloor = 100d;
        [SerializeField, Range(0f, 1f)] private float balloonDiamondChance = 0.10f;
        [SerializeField, Min(1)] private long balloonDiamondAmount = 10L;

        [Header("Hızlandırıcı göstergesi")]
        [Tooltip("Sadece bir hızlandırıcı çalışırken açılır.")]
        [SerializeField] private GameObject boostIndicator;
        [SerializeField] private TMP_Text boostValue;

        [Header("Bakım kalkanı göstergesi")]
        [Tooltip("Sadece mağazadan alınan bakım kalkanı çalışırken açılır. Hızlandırıcı hapının " +
                 "eşi — ikisi aynı anda görünebilir.")]
        [SerializeField] private GameObject shieldIndicator;
        [SerializeField] private TMP_Text shieldValue;

        [Header("Alt sira")]
        [Tooltip("Alt siradaki dugmeler, soldan saga. Yerleri koddan veriliyor: esit aralikla " +
                 "ekranin ortasina diziliyorlar, yani buradaki SIRA disinda ayarlanacak bir sey yok. " +
                 "Gorev ve ustabasi acicilari calisirken bu dizinin soluna ekleniyor.")]
        [SerializeField] private RectTransform[] bottomRow;
        [Tooltip("Iki dugme merkezi arasi mesafe. 220'den 195'e dusuruldu: sira ona ciktiginda " +
                 "9x220+150 = 2130 birim tutuyor ve 16:9 bir telefonun ~2120 birimlik tuvaline " +
                 "sigmiyordu. 195'te 1905 birim tutuyor, yani kenar boslugu dokuz dugmelik eski " +
                 "siranin biraktigiyla ayni kaliyor.")]
        [SerializeField] private float bottomPitch = 195f;

        [Header("Yan ray")]
        // The row used to run across the bottom of the screen. In landscape that is the one dimension
        // the game has none of - the canvas is about 2340x1080, so a 150px row plus its margin took a
        // sixth of the HEIGHT while both side margins sat empty over open sea. As a rail it costs
        // width, which is the thing there is plenty of, and the camera gets that height back (see
        // OperationCameraBoot.hudSideFraction).
        [Tooltip("Alt sira yerine kenar rayi. Kapatilirsa eski yatay alt sira geri gelir.")]
        [SerializeField] private bool sideRail = true;
        [Tooltip("Ray solda mi? OperationCameraBoot.hudRailOnLeft ile ayni tarafta olmali.")]
        [SerializeField] private bool railOnLeft = true;
        [Tooltip("Ekran kenari ile ilk sutunun merkezi arasindaki mesafe.")]
        [SerializeField] private float railInset = 105f;
        [Tooltip("Ust bardaki haplara birakilan yukseklik. Ray bunun altinda basliyor.")]
        [SerializeField] private float railTopReserve = 400f;
        [Tooltip("Ray dugmelerinin dikey araligi ve tasan sutunlarin yatay araligi. 150'lik " +
                 "dugmelerde 162, sekiz aciciyi iki sutuna sigdiriyor; 172'de uc sutun gerekiyor " +
                 "ve ray adanin uzerine tasiyor.")]
        [SerializeField] private float railPitch = 162f;
        [Tooltip("Sağ raydaki bütün ana menü butonlarının kare kenar ölçüsü. Koddaki ve prefabdaki " +
                 "butonlar bu ölçüye eşitlenir.")]
        [SerializeField] private float railButtonSize = 150f;
        [Tooltip("Çentik ve hareket çubuğundan sonra bırakılan minimum boşluk.")]
        [SerializeField] private float safeAreaMargin = 24f;

        [Header("Alt")]
        [SerializeField] private Button upgradeButton;
        [Tooltip("Yükseltmenin solundaki kısayol: reklam izle, gelir 2× olsun. Hak ve bekleme süresi UI_Reklam'daki yuvanın.")]
        [SerializeField] private Button boostButton;
        [SerializeField] private Image boostButtonImage;
        [Tooltip("Butonun içindeki tek satır: \"×2 GELİR\". Kalan süre üstteki hızlandırıcı göstergesinde.")]
        [SerializeField] private TMP_Text boostButtonTitle;

        [Header("Ekran bağlantıları (sahne nesneleri)")]
        [SerializeField] private PremiumStoreUI store;
        [Tooltip("Yükseltme ekranı. Tek seferlik genişletmeler oradaki şeridin son yuvasından açılır, " +
                 "yani HUD'un eski uzun listeye bağlanacak bir işi kalmadı.")]
        [SerializeField] private StationScreenUI stationScreen;
        [SerializeField] private SettingsUI settings;
        [SerializeField] private DailyRewardUI dailyScreen;
        [SerializeField] private ContractUI contractScreen;
        [SerializeField] private AdRewardUI adScreen;
        [Tooltip("Açılır fırsat penceresi. Kendi zamanlamasını kendi yönetir; HUD sadece butonu ona açar.")]
        [SerializeField] private OfferPopupUI offerScreen;

        [SerializeField] private float refreshInterval = 0.25f;

        [Header("Maden dükkânı")]
        [Tooltip("Cash, gem and rate numbers. The pill art is dark, so the numbers must be light to be read.")]
        [SerializeField] private Color counterTextColor = Color.white;
        [Tooltip("Seconds of shop sale receipts the rate pill adds up while the mining shop owns Main.")]
        [SerializeField, Min(1f)] private float shopRateWindow = 60f;

        [Header("Sayaç vuruşu")]
        [Tooltip("Para geldiğinde sayının ne kadar büyüdüğü. Hapın kendisi değil, içindeki sayı zıplar — "
                 + "hapa dokunma yaylanması yazıyor, ikisi aynı ölçeği paylaşamaz.")]
        [SerializeField] private float counterPunch = 0.16f;
        [SerializeField] private float counterPunchSeconds = 0.28f;
        [Tooltip("Elmas sayacının dolma hızı. Nakitinki 9 — para saniyede bir damlıyor, elmas ise "
                 + "yılda birkaç kez; yavaş sayması izlenecek bir şey oluyor. Küçük değer = yavaş.")]
        [SerializeField] private float gemRollSpeed = 5.5f;

        private WalletService _wallet;
        private StageService _stages;
        private ContractService _contract;
        // In the shop the Contract button belongs to the shop's contracts; the port ship's stay with ore mode.
        private ShopContractService _shopContract;
        private ShopContractUI _shopContractScreen;
        private FoundryFestivalService _festival;
        private HarborFestivalService _harborFestival;
        private ProductionSprintService _productionSprint;
        private BoostService _boost;
        private MaintenanceService _maintenance;
        private FreeRewardService _freeRewards;
        private BalloonRewardService _balloon;
        private IAdService _ad;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;
        private MarketService _market;
        // ponytail: fixed ring of recent shop receipts. One sale per service (2 s at least) is ~30 a minute, so 64
        // covers the window; widen it if service gets faster.
        private readonly float[] _saleTimes = new float[64];
        private readonly double[] _saleCash = new double[64];
        private int _saleHead, _saleCount;
        private double _shownCash;        // eased display value behind the real balance
        private bool _haveShownCash;
        private double _shownGems;        // same easing for gems — they used to snap
        private long _writtenGems = -1;   // last integer actually written, so a settled counter allocates nothing
        private bool _haveShownGems;
        private float _goldPunch;         // seconds left on the pop
        private float _gemPunch;
        private Vector2 _shieldSlot;      // where the shield chip was authored — beside the boost chip
        private Vector2 _boostSlot;       // and the boost chip's own slot, which it borrows when empty
        // The bottom row, kept sorted by the key each entry came in with. Two parallel lists
        // rather than a sorted dictionary: it is six items, written twice at startup and never again.
        private readonly List<int> _bottomOrder = new List<int>();
        private readonly List<RectTransform> _bottomRects = new List<RectTransform>();
        private float _railWidth;         // what the side rail takes off the sheet, for the top strip
        private Button _balloonButton;
        private GameObject _balloonTimerChip;
        private TMP_Text _balloonTimerValue;
        private RectTransform _rewardBadge;
        private RectTransform _authoredRewardBadge;
        private float _rewardBadgeTime;

        // The stage chip is a live clone of the authored rate pill: cloning preserves the HUD's
        // existing material, border, font and spacing without adding a scene-only prefab dependency.
        // It remains a label rather than a second chapter opener; ChapterUI owns NEXT CHAPTER.
        private RectTransform _stageIndicator;
        private TMP_Text _stageIndicatorLabel;
        private string _shownStageLabel;
        private string _fitRate, _fitBoost, _fitShield, _fitStage;   // the text each pill was last sized to

        // The objective strip under the currency bar. Its position is solved from the authored rects
        // above it rather than authored itself, so it is re-solved whenever the sheet changes size.
        private RectTransform _topStrip;
        private float _topStripWidth, _topStripHeight, _topStripGap;
        private Vector2 _sheetSize = new Vector2(-1f, -1f);
        private static readonly Vector3[] Corners = new Vector3[4];
        private Rect _appliedSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private Vector2Int _appliedScreenSize;
        private Vector2 _appliedSheetSize = new Vector2(-1f, -1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _stages = ServiceLocator.Get<StageService>();
            _contract = ServiceLocator.Get<ContractService>();
            _festival = ServiceLocator.Get<FoundryFestivalService>();
            _harborFestival = ServiceLocator.Get<HarborFestivalService>();
            _productionSprint = ServiceLocator.Get<ProductionSprintService>();
            _boost = ServiceLocator.Get<BoostService>();
            _maintenance = ServiceLocator.Get<MaintenanceService>();
            _freeRewards = ServiceLocator.Get<FreeRewardService>();
            _balloon = ServiceLocator.Get<BalloonRewardService>();
            _ad = ServiceLocator.Get<IAdService>();
            _market = ServiceLocator.Get<MarketService>();
            if (_market != null) _market.MiningShopBusinessSold += OnShopSold;
            // The contract customer's progress bar lives with the HUD, so no scene has to carry it.
            if (_market != null && _market.MiningShopBusiness != null && GetComponent<ShopContractMarker>() == null)
                gameObject.AddComponent<ShopContractMarker>();
            _shopContract = ServiceLocator.Get<ShopContractService>();
            if (_shopContract != null) BuildShopContractScreens();
            if (goldValue != null) goldValue.color = counterTextColor;
            if (gemsValue != null) gemsValue.color = counterTextColor;
            if (rateValue != null) rateValue.color = counterTextColor;
            if (rateValue != null && _market != null && _market.MiningShopBusiness != null)
            {
                // "12 SOLD · $240/min" is several times longer than the "$0/min" the pill was sized for.
                rateValue.fontSizeMax = rateValue.fontSize;
                rateValue.fontSizeMin = rateValue.fontSize * 0.5f;
                rateValue.enableAutoSizing = true;
            }
            _world = FindAnyObjectByType<WorldIslands>();
            BindEnabledOp();
            ApplyMainHudIcons();
            LayoutCurrencyIndicators();
            // Before the slots are read: they are where the chips actually sit, not where they were authored.
            LayoutTopInfoIndicators();
            if (shieldIndicator != null)
                _shieldSlot = ((RectTransform)shieldIndicator.transform).anchoredPosition;
            if (boostIndicator != null)
                _boostSlot = ((RectTransform)boostIndicator.transform).anchoredPosition;

            if (storeButton != null) storeButton.onClick.AddListener(OnStore);
            if (goldButton != null) goldButton.onClick.AddListener(OnStore);
            if (gemsButton != null) gemsButton.onClick.AddListener(OnStore);
            if (dailyButton != null) dailyButton.onClick.AddListener(OnDaily);
            if (contractButton != null) contractButton.onClick.AddListener(OnContract);
            if (contractButton != null) contractButton.gameObject.SetActive(true);
            if (adButton != null) adButton.onClick.AddListener(OnAds);
            if (offerButton != null) offerButton.onClick.AddListener(OnOffer);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(OnUpgrades);
            if (rateButton != null)
            {
                rateButton.onClick.AddListener(OnRate);
                // 271 by 61 units: wide enough, too short for a thumb. The cash pill is right above it, so
                // the extra height goes below.
                rateButton.gameObject.AddComponent<TouchPad>().Configure(0.85f);
            }
            if (boostButton != null) boostButton.onClick.AddListener(OnBoost);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);

            // Awake'ten eklenen acicilar zaten sirada; kurgulanmis dugmeler onlarin sagina
            // giriyor ve sira bir kez ortalaniyor.
            if (bottomRow != null)
                for (int i = 0; i < bottomRow.Length; i++) InsertBottom(AuthoredOrder + i, bottomRow[i]);
            BuildBalloonButton();
            _rewardBadge = BuildRewardBadge(_balloonButton);
            _authoredRewardBadge = BuildRewardBadge(adButton);
            // Ad and offer were pinned down the top-left edge, which is where the rail now runs. Left
            // out of it they would sit on top of it; folded in they are just the two lowest-priority
            // openers, which is what they are. Their counter chips are their own children, so both
            // travel with the button.
            if (compactShipyardHud)
            {
                if (adButton != null) adButton.gameObject.SetActive(false);
                if (offerButton != null) offerButton.gameObject.SetActive(false);
            }
            else if (sideRail)
            {
                InsertBottom(PromoOrder + 0, adButton != null ? (RectTransform)adButton.transform : null);
                InsertBottom(PromoOrder + 1, offerButton != null ? (RectTransform)offerButton.transform : null);
            }
            ApplySafeArea();
            BuildStageIndicator();
            PlaceStageIndicator();
            LayoutBottomRow();
            if (!compactShipyardHud)
                BuildObjectiveStrip();
            // The league screen is built in either mode. It used to sit inside the branch above, which
            // meant compact mode never created it at all — LadderService.Available was true, the
            // season was running, and the screen that shows it did not exist. Its own opener decides
            // whether there is anything to open (LadderUI.BuildOpener), and in compact mode that
            // opener now lands in the More sheet.
            BuildLadder();
            BuildMarketUpgradeOpener();

            // The wallet screen is code-built and attaches its opener to the existing rail/More sheet;
            // no scene or prefab edit is needed for the compact HUD to reach it.
            WalletUI walletScreen = FindAnyObjectByType<WalletUI>(FindObjectsInactive.Include);
            if (walletScreen == null) walletScreen = new GameObject("CuzdanUI").AddComponent<WalletUI>();
            walletScreen.Initialize(this);

            if (_wallet != null) _wallet.GemsChanged += RefreshGems;
            RefreshGems();
            Refresh();

            // HUD hiç açılıp kapanmaz — sadece tıklama sesi, whoosh yok.
            UiPanelSound.AttachButtonsOnly(gameObject);
        }

        /// <summary>
        /// Shrinks the two large currency plates as one proportional pair. Their children are
        /// stretch-anchored inside the authored rectangles, so changing both axes by the same factor
        /// preserves the icon, plate and type proportions without introducing a second art layout.
        /// </summary>
        private void LayoutCurrencyIndicators()
        {
            if (goldButton == null || gemsButton == null) return;

            var gold = goldButton.transform as RectTransform;
            var gems = gemsButton.transform as RectTransform;
            if (gold == null || gems == null) return;

            float scale = Mathf.Clamp(currencyIndicatorScale, 0.6f, 1f);
            gold.sizeDelta *= scale;
            gems.sizeDelta *= scale;

            // Both plates use a top-left pivot. Keep the cash plate in its authored slot and solve the
            // gem plate from its new right edge, so shrinking cannot leave the oversized old gap.
            gems.anchoredPosition = new Vector2(gold.anchoredPosition.x + gold.sizeDelta.x
                                                + Mathf.Max(0f, currencyIndicatorGap),
                                                gold.anchoredPosition.y);
        }

        /// <summary>
        /// Puts the rate, boost and shield chips in one column under cash, hard against the left edge.
        /// The pills are nine-sliced and <see cref="PillFit"/> keeps their caps round at any height, so
        /// they are resized rather than scaled: scaling shrank the text along with the plate, which is
        /// the opposite of what a chip this small needs.
        /// </summary>
        private void LayoutTopInfoIndicators()
        {
            RectTransform rate = rateButton != null ? rateButton.transform as RectTransform : null;
            RectTransform boost = boostIndicator != null ? boostIndicator.transform as RectTransform : null;
            RectTransform shield = shieldIndicator != null ? shieldIndicator.transform as RectTransform : null;
            if (rate == null) return;

            float y = rate.anchoredPosition.y + Mathf.Max(0f, infoPillGap);
            float pitch = infoPillHeight + infoPillGap;
            PlaceInfoPill(rate, y);
            PlaceInfoPill(boost, y - pitch);
            PlaceInfoPill(shield, y - pitch * 2f);
            FitInfoPills();
        }

        private void PlaceInfoPill(RectTransform pill, float y)
        {
            if (pill == null) return;
            pill.localScale = Vector3.one;
            pill.anchoredPosition = new Vector2(infoPillLeft, y);
        }

        /// <summary>Re-fits each chip to its text, but only when the text has actually changed.</summary>
        private void FitInfoPills()
        {
            FitPill(rateButton != null ? rateButton.transform as RectTransform : null, rateValue,
                    ref _fitRate, infoPillHeight, infoPillFont, infoPillPadding, infoPillWidth);
            FitPill(boostIndicator != null ? boostIndicator.transform as RectTransform : null, boostValue,
                    ref _fitBoost, infoPillHeight, infoPillFont, infoPillPadding, infoPillWidth);
            FitPill(shieldIndicator != null ? shieldIndicator.transform as RectTransform : null, shieldValue,
                    ref _fitShield, infoPillHeight, infoPillFont, infoPillPadding, infoPillWidth);
        }

        /// <summary>
        /// Sizes a pill to hug its label at a fixed font size. The width steps in eights so a ticking
        /// countdown does not make the plate twitch as its digits change width; a label longer than the
        /// widest allowed pill falls back to auto-sizing instead of overflowing the caps.
        /// </summary>
        private static void FitPill(RectTransform pill, TMP_Text label, ref string shown,
                                    float height, float font, float padding, Vector2 widthRange)
        {
            if (pill == null || label == null) return;
            string text = label.text;
            if (shown == text) return;
            shown = text;

            label.enableAutoSizing = false;
            label.fontSize = font;
            float wanted = label.GetPreferredValues(text).x + padding;
            wanted = Mathf.Ceil(wanted / 8f) * 8f;
            bool tooWide = wanted > widthRange.y;
            if (tooWide)
            {
                label.enableAutoSizing = true;
                label.fontSizeMax = font;
                label.fontSizeMin = font * 0.6f;
            }
            pill.sizeDelta = new Vector2(Mathf.Clamp(wanted, widthRange.x, widthRange.y), height);
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(-padding, labelRect.sizeDelta.y);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.GemsChanged -= RefreshGems;
            if (_market != null) _market.MiningShopBusinessSold -= OnShopSold;
        }

        /// <summary>Inspector'daki siranin anahtarlari buradan basliyor; koddan eklenen acicilar
        /// solda durmak icin 0-9 arasi bir anahtar veriyor.</summary>
        private const int AuthoredOrder = 10;

        /// <summary>Reklam ve teklif butonlari rayin sonunda durur.</summary>
        private const int PromoOrder = 20;

        /// <summary>Where the scene keeps its code-built screens. A plain root object — deliberately
        /// outside every Canvas, which is what a screen building its own overlay canvas needs.</summary>
        private const string UiSystemsObject = "UI_Sistemler";

        /// <summary>
        /// Hangs a code-built screen's opener in the bottom row: same parent, same size, same line.
        ///
        /// <see cref="GoalsUI"/> and <see cref="ForemanRosterUI"/> are built in code and used to bring
        /// a canvas of their own, anchored at a fraction of the screen. The HUD is a portrait sheet
        /// scaled as one piece, so a fraction of the screen is not a fraction of the sheet: in
        /// landscape those two openers landed straight on top of the ads and offer buttons. Borrowing
        /// a real row button's rect is the only placement that holds on every aspect ratio, and it is
        /// also what makes the opener wear the row's own size instead of a lookalike.
        ///
        /// The row re-centres itself on every attach, so an opener that joins late slides the others
        /// over rather than hanging off the end.
        /// </summary>
        /// <param name="order">Sira anahtari; kucugu solda durur. Kurgulanmis dizi 10'dan basliyor.</param>
        public Button AttachBottomButton(int order, string name, Sprite icon,
                                         UnityEngine.Events.UnityAction onClick)
        {
            Sprite customIcon = MainHudIcon(name);
            if (customIcon != null) icon = customIcon;
            else
            {
                Sprite portraitIcon = PortraitOpenerIcon(name);
                if (portraitIcon != null) icon = portraitIcon;
            }
            // Compact mode keeps the rail down to primaries: sea combat is a primary loop in the
            // five-station game, and the two rosters are the whole of the collection layer. The rest
            // go into the More sheet rather than being dropped — a screen you cannot open is a
            // feature you do not have, and Goals, Chapter, Crafting, Events and the League were all
            // being built, ticked and left unreachable.
            if (compactShipyardHud && !IsCompactOpener(name))
                return AttachMoreRow(order, name, icon, customIcon != null, onClick);
            RectTransform model = FirstAuthored();
            if (model == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(model.parent, false);
            rect.anchorMin = model.anchorMin;
            rect.anchorMax = model.anchorMax;
            rect.pivot = model.pivot;
            rect.sizeDelta = model.sizeDelta;
            rect.anchoredPosition = model.anchoredPosition;

            var image = go.GetComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.useSpriteMesh = true;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);

            InsertBottom(order, rect);
            LayoutBottomRow();
            return button;
        }

        /// <summary>The first wired entry of the authored row: the shape every opener copies.</summary>
        private RectTransform FirstAuthored()
        {
            if (bottomRow == null) return null;
            for (int i = 0; i < bottomRow.Length; i++)
                if (bottomRow[i] != null) return bottomRow[i];
            return null;
        }

        private void InsertBottom(int order, RectTransform rect)
        {
            if (rect == null || _bottomRects.Contains(rect)) return;
            int i = 0;
            while (i < _bottomOrder.Count && _bottomOrder[i] <= order) i++;
            _bottomOrder.Insert(i, order);
            _bottomRects.Insert(i, rect);
        }

        /// <summary>
        /// Lays the openers out as a vertical rail down one side, wrapping into a second column when
        /// the count outgrows the height. Falls back to the old centred horizontal row when
        /// <see cref="sideRail"/> is off.
        ///
        /// Anchors are rewritten here rather than authored, because the buttons come from three
        /// places - the prefab's own row, the ad and offer buttons that were pinned to the top-left,
        /// and the openers <see cref="AttachBottomButton"/> hangs at runtime - and a rail is only
        /// straight if one piece of code owns every position in it.
        ///
        /// It used to be four buttons at three different gaps whose middle sat a hundred units right
        /// of centre: right on the phone it was authored against, visibly crooked on everything else.
        /// </summary>
        private void LayoutBottomRow()
        {
            float buttonSize = Mathf.Max(1f, railButtonSize);
            for (int i = 0; i < _bottomRects.Count; i++)
            {
                RectTransform rect = _bottomRects[i];
                if (rect != null) rect.sizeDelta = new Vector2(buttonSize, buttonSize);
            }

            if (!sideRail)
            {
                float span = (_bottomRects.Count - 1) * bottomPitch;
                for (int i = 0; i < _bottomRects.Count; i++)
                {
                    RectTransform rect = _bottomRects[i];
                    if (rect == null) continue;
                    rect.anchoredPosition = new Vector2(i * bottomPitch - span * 0.5f,
                                                        rect.anchoredPosition.y);
                }
                return;
            }

            if (compactShipyardHud && Screen.height >= Screen.width)
            {
                LayoutBalancedPortraitRails(buttonSize);
                return;
            }

            int count = 0;
            for (int i = 0; i < _bottomRects.Count; i++) if (_bottomRects[i] != null) count++;
            if (count == 0) return;

            float height = RailHeight();
            RectTransform sheet = transform as RectTransform;
            float width = sheet != null && sheet.rect.width > 100f ? sheet.rect.width : 1080f;
            Rect safe = Screen.safeArea;
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float sheetPerScreenX = width / screenWidth;
            // The band the button CENTRES may occupy. The pills are pinned to the top of the same
            // rect, so the rail starts under them rather than at the screen edge; half a pitch comes
            // off each end so the first and last buttons sit inside the band rather than straddling it.
            float safeTop = (safe.yMax / screenHeight - 0.5f) * height;
            float safeBottom = (safe.yMin / screenHeight - 0.5f) * height;
            float pitch = Mathf.Max(railPitch, buttonSize + 12f);
            float top = safeTop - railTopReserve - pitch * 0.5f;
            float bottom = safeBottom + pitch * 0.5f;
            float band = Mathf.Max(pitch, top - bottom);
            float centre = (top + bottom) * 0.5f;
            // A counter chip hangs RailChipDrop below its button, and at this pitch the gap under a
            // button is 12: "3 JOBS" sat on the upgrade button below it. A column keeps its foot where
            // this pitch puts it - the REPAIR ALL button sits just under the rail - and spreads upwards
            // into the room under the top pills, up to the pitch that clears a chip.
            float chipPitch = Mathf.Max(pitch, buttonSize + 12f + RailChipDrop);

            int perColumn = Mathf.Max(1, Mathf.FloorToInt(band / pitch) + 1);
            int columns = Mathf.CeilToInt(count / (float)perColumn);
            float safeEdgeInset = railOnLeft
                ? safe.xMin * sheetPerScreenX + railInset
                : (screenWidth - safe.xMax) * sheetPerScreenX + railInset;
            _railWidth = safeEdgeInset + (columns - 1) * pitch + pitch * 0.5f;
            float edge = railOnLeft ? 0f : 1f;
            float dir = railOnLeft ? 1f : -1f;

            int placed = 0;
            for (int i = 0; i < _bottomRects.Count; i++)
            {
                RectTransform rect = _bottomRects[i];
                if (rect == null) continue;

                int column = placed / perColumn;
                int row = placed - column * perColumn;
                // The last column is usually short; centre each column on its own contents so the
                // rail never ends in a ragged half-column hanging off the bottom.
                int inColumn = Mathf.Min(perColumn, count - column * perColumn);
                float foot = centre - (inColumn - 1) * pitch * 0.5f;
                float spread = inColumn > 1 ? Mathf.Clamp((top - foot) / (inColumn - 1), pitch, chipPitch) : pitch;

                rect.anchorMin = rect.anchorMax = new Vector2(edge, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(dir * (safeEdgeInset + column * pitch),
                                                    foot + (inColumn - 1 - row) * spread);
                for (int c = 0; c < rect.childCount; c++)
                    if (offerTimerChip != null && rect.GetChild(c).name == offerTimerChip.name)
                        HangChip((RectTransform)rect.GetChild(c));
                placed++;
            }

            // The objective strip is measured against the rail, so a late opener that adds a column
            // has to push it over rather than end up underneath it.
            SolveTopStrip(transform as RectTransform);
        }

        /// <summary>
        /// Portrait uses both empty side margins instead of stacking every opener on the left. Store
        /// and Daily keep the first two right-hand positions players already know; the remaining
        /// openers are divided as evenly as possible and both rails share one size, inset and pitch.
        /// </summary>
        private void LayoutBalancedPortraitRails(float buttonSize)
        {
            RectTransform authored = FirstAuthored();
            RectTransform host = authored != null ? authored.parent as RectTransform : null;
            if (host == null) return;

            RectTransform storeRect = storeButton != null ? storeButton.transform as RectTransform : null;
            RectTransform dailyRect = dailyButton != null ? dailyButton.transform as RectTransform : null;
            if (storeRect != null && storeRect.parent != host) storeRect.SetParent(host, false);
            if (dailyRect != null && dailyRect.parent != host) dailyRect.SetParent(host, false);
            if (storeRect != null) storeRect.sizeDelta = new Vector2(buttonSize, buttonSize);
            if (dailyRect != null) dailyRect.sizeDelta = new Vector2(buttonSize, buttonSize);

            int openerCount = 0;
            for (int i = 0; i < _bottomRects.Count; i++)
                if (_bottomRects[i] != null && _bottomRects[i].gameObject.activeSelf) openerCount++;
            int fixedRight = (storeRect != null && storeRect.gameObject.activeSelf ? 1 : 0)
                           + (dailyRect != null && dailyRect.gameObject.activeSelf ? 1 : 0);
            int total = openerCount + fixedRight;
            if (total == 0) return;

            int leftTarget = total / 2;
            int rightTarget = total - leftTarget;
            var left = new List<RectTransform>(leftTarget);
            var right = new List<RectTransform>(rightTarget);
            if (storeRect != null && storeRect.gameObject.activeSelf) right.Add(storeRect);
            if (dailyRect != null && dailyRect.gameObject.activeSelf) right.Add(dailyRect);

            for (int i = 0; i < _bottomRects.Count; i++)
            {
                RectTransform rect = _bottomRects[i];
                if (rect == null || !rect.gameObject.activeSelf) continue;
                if (left.Count < leftTarget) left.Add(rect);
                else right.Add(rect);
            }

            float height = host.rect.height > 100f ? host.rect.height : 2340f;
            float width = host.rect.width > 100f ? host.rect.width : 1080f;
            Rect safe = Screen.safeArea;
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float safeTop = (safe.yMax / screenHeight - 0.5f) * height;
            float safeBottom = (safe.yMin / screenHeight - 0.5f) * height;
            float top = safeTop - railTopReserve - buttonSize * 0.5f;
            float bottom = safeBottom + buttonSize * 0.5f;
            float minPitch = buttonSize + 12f + RailChipDrop;
            int rows = Mathf.Max(left.Count, right.Count);
            float pitch = rows > 1
                ? Mathf.Clamp((top - bottom) / (rows - 1), minPitch, minPitch + 24f)
                : minPitch;
            float inset = (safe.xMin / screenWidth) * width + railInset;
            float rightInset = ((screenWidth - safe.xMax) / screenWidth) * width + railInset;

            PlacePortraitRail(left, 0f, inset, pitch);
            PlacePortraitRail(right, 1f, -rightInset, pitch);
            _railWidth = Mathf.Max(inset, rightInset) + buttonSize * 0.5f;
            SolveTopStrip(transform as RectTransform);
        }

        private void PlacePortraitRail(List<RectTransform> rail, float anchorX, float x, float pitch)
        {
            if (rail == null || rail.Count == 0) return;
            float firstY = (rail.Count - 1) * pitch * 0.5f;
            for (int i = 0; i < rail.Count; i++)
            {
                RectTransform rect = rail[i];
                rect.anchorMin = rect.anchorMax = new Vector2(anchorX, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(x, firstY - i * pitch);
                for (int c = 0; c < rect.childCount; c++)
                    if (offerTimerChip != null && rect.GetChild(c).name == offerTimerChip.name)
                        HangChip((RectTransform)rect.GetChild(c));
            }
        }

        /// <summary>
        /// Height of the rect the rail is laid out in. Falls back to the canvas design height: at boot
        /// the safe-area rect can still be zero-sized on the frame Start runs, and a rail solved
        /// against zero stacks every button on one spot.
        /// </summary>
        private float RailHeight()
        {
            RectTransform parent = null;
            for (int i = 0; i < _bottomRects.Count && parent == null; i++)
                if (_bottomRects[i] != null) parent = _bottomRects[i].parent as RectTransform;
            float h = parent != null ? parent.rect.height : 0f;
            return h > 100f ? h : 1080f;
        }

        /// <summary>
        /// Returns the device safe area in normalized coordinates. Keeping this pure makes the inset
        /// contract testable without a device and avoids special-case portrait layout assets.
        /// </summary>
        public static Rect SafeAreaNormalized(Rect safeArea, Vector2 screenSize)
        {
            float width = Mathf.Max(1f, screenSize.x);
            float height = Mathf.Max(1f, screenSize.y);
            return new Rect(safeArea.xMin / width, safeArea.yMin / height,
                            safeArea.width / width, safeArea.height / height);
        }

        private bool SafeAreaChanged()
        {
            RectTransform sheet = transform as RectTransform;
            return Screen.safeArea != _appliedSafeArea
                   || _appliedScreenSize.x != Screen.width
                   || _appliedScreenSize.y != Screen.height
                   || (sheet != null && sheet.rect.size != _appliedSheetSize);
        }

        /// <summary>
        /// Clamps the authored top controls to the current safe rectangle. Buttons are moved rather
        /// than their text children so the currency and income pills remain internally aligned.
        /// </summary>
        private void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            _appliedSafeArea = safe;
            _appliedScreenSize = new Vector2Int(Screen.width, Screen.height);
            RectTransform sheet = transform as RectTransform;
            _appliedSheetSize = sheet != null ? sheet.rect.size : Vector2.zero;
            float margin = Mathf.Max(0f, safeAreaMargin);
            ClampToSafeArea(goldButton != null ? (RectTransform)goldButton.transform : null, safe, margin);
            ClampToSafeArea(gemsButton != null ? (RectTransform)gemsButton.transform : null, safe, margin);
            ClampToSafeArea(rateButton != null ? (RectTransform)rateButton.transform : null, safe, margin);
            ClampToSafeArea(settingsButton != null ? (RectTransform)settingsButton.transform : null, safe, margin);
            PlaceStageIndicator();
            ClampToSafeArea(boostIndicator != null ? (RectTransform)boostIndicator.transform : null, safe, margin);
            ClampToSafeArea(shieldIndicator != null ? (RectTransform)shieldIndicator.transform : null, safe, margin);
        }

        private static void ClampToSafeArea(RectTransform rect, Rect safe, float margin)
        {
            if (rect == null) return;
            rect.GetWorldCorners(Corners);
            float minX = Corners[0].x, maxX = Corners[0].x;
            float minY = Corners[0].y, maxY = Corners[0].y;
            for (int i = 1; i < Corners.Length; i++)
            {
                minX = Mathf.Min(minX, Corners[i].x);
                maxX = Mathf.Max(maxX, Corners[i].x);
                minY = Mathf.Min(minY, Corners[i].y);
                maxY = Mathf.Max(maxY, Corners[i].y);
            }

            float dx = 0f;
            if (minX < safe.xMin + margin) dx = safe.xMin + margin - minX;
            else if (maxX > safe.xMax - margin) dx = safe.xMax - margin - maxX;
            float dy = 0f;
            if (minY < safe.yMin + margin) dy = safe.yMin + margin - minY;
            else if (maxY > safe.yMax - margin) dy = safe.yMax - margin - maxY;
            if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f)
                rect.position += new Vector3(dx, dy, 0f);
        }

        /// <summary>
        /// Creates the compact stage label directly beneath Settings. Reusing the authored rate-pill
        /// hierarchy keeps future UI restyles in one place and means no HUD prefab needs a new wire.
        /// </summary>
        private void BuildStageIndicator()
        {
            if (_stageIndicator != null || settingsButton == null || rateButton == null) return;

            Transform topBar = settingsButton.transform.parent;
            if (topBar == null) return;

            GameObject clone = Instantiate(rateButton.gameObject, topBar, false);
            clone.name = "AsamaGostergesi";
            var button = clone.GetComponent<Button>();
            if (button != null) button.enabled = false;
            var touchPad = clone.GetComponent<TouchPad>();
            if (touchPad != null) Destroy(touchPad);

            _stageIndicator = clone.transform as RectTransform;
            _stageIndicator.localScale = Vector3.one;
            _stageIndicatorLabel = clone.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>The chapter pill hugs its number: "1-1" gets a small plate, "12-3" a slightly longer one.</summary>
        private void FitStagePill()
        {
            FitPill(_stageIndicator, _stageIndicatorLabel, ref _fitStage,
                    stagePillHeight, stagePillFont, stagePillPadding, stagePillWidth);
        }

        /// <summary>Positions the copied pill under the live Settings button after safe-area layout.</summary>
        private void PlaceStageIndicator()
        {
            if (_stageIndicator == null || settingsButton == null) return;

            RectTransform settingsRect = (RectTransform)settingsButton.transform;
            // The indicator and Settings are siblings in the authored top bar. Work in that shared
            // local coordinate system rather than measuring world corners: the latter are not yet
            // resolved in the first Start frame of a new canvas.
            _stageIndicator.anchorMin = settingsRect.anchorMin;
            _stageIndicator.anchorMax = settingsRect.anchorMax;
            _stageIndicator.pivot = new Vector2(1f, 1f);
            _stageIndicator.anchoredPosition = new Vector2(-stagePillRight,
                                                            settingsRect.anchoredPosition.y
                                                            - settingsRect.rect.height - 12f);
        }

        /// <summary>
        /// A copy of the offer button's counter chip, hung under <paramref name="owner"/>. Cloning the
        /// authored chip is what keeps a code-built opener wearing the same pill, font and offset as
        /// the contract and offer counters: a hand-built lookalike only stays alike until someone
        /// retouches the real one.
        /// </summary>
        public GameObject AttachCounterChip(Button owner)
        {
            if (owner == null || offerTimerChip == null) return null;
            GameObject chip = Instantiate(offerTimerChip, owner.transform, false);
            chip.name = offerTimerChip.name;
            chip.SetActive(true);
            // A rail button is a 150-unit square and the authored chip's own offsets land on it
            // correctly. A More row is a wide strip, so the same offsets would drop the chip somewhere
            // in the middle of the label; pin it to the icon's lower-right corner instead.
            if (_moreRows.Contains(owner.transform as RectTransform))
            {
                var chipRect = (RectTransform)chip.transform;
                chipRect.anchorMin = new Vector2(0f, 0.5f);
                chipRect.anchorMax = new Vector2(0f, 0.5f);
                chipRect.pivot = new Vector2(0.5f, 0.5f);
                chipRect.sizeDelta = new Vector2(MoreChipWidth, chipRect.sizeDelta.y);
                chipRect.anchoredPosition = MoreChipOffset;
                // Full size and fully inside the plate. At 0.68 on the row's top corner the count was
                // 17 units tall and sat half on the plate's rim; the "50" hung off the frame.
                chipRect.localScale = Vector3.one;
                TMP_Text count = chip.GetComponentInChildren<TMP_Text>(true);
                if (count != null) count.fontSize = 22f;
            }
            else HangChip((RectTransform)chip.transform);
            return chip;
        }

        // How far a counter chip hangs below its rail button. The authored chips overlapped the button
        // by 13 and hung 37-43 below it, into a 12-unit gap; they now straddle the button's foot (the
        // plate's rim and shadow) and the rail pitch leaves this much room under every button.
        private const float RailChipDrop = 26f;

        /// <summary>Pins a rail chip's top edge (its pivot) so its bottom ends RailChipDrop under the
        /// button, whatever the chip's height.</summary>
        private static void HangChip(RectTransform chip)
        {
            chip.anchoredPosition = new Vector2(chip.anchoredPosition.x, chip.sizeDelta.y - RailChipDrop);
        }

        // ---------------------------------------------------------------- more sheet

        /// <summary>Above the HUD (100) and below every screen a row opens (Goals 106 … League 111),
        /// so a screen launched from a row draws over the sheet that launched it.</summary>
        private const int MoreSortingOrder = 104;

        // The More screen deliberately shares the Settings exports rather than approximating them with
        // a tinted generic panel. Their native proportions are 2:3 for the card and 3:1 for a row;
        // preserving those ratios is what keeps the blue corners and capsule ends round on a phone.
        private const float MoreSheetWidth = 940f;
        private const float MoreSheetHeight = 1410f;
        private const float MoreRowHeight = 132f;
        private const float MoreRowGap = 20f;
        private const float MoreRowPadding = 18f;
        // The sheet's cream inlay is about 795 wide; 818 ran the two columns over the blue frame.
        private const float MoreGridWidth = 750f;
        // The count badge: a compact pill on the icon's lower-right corner, inside the plate. Anywhere
        // beside the label it costs label width, and the longest German word ("BERGBAUAUSRÜSTUNG")
        // needs about 210 of the 215 a row leaves it.
        private const float MoreChipWidth = 48f;
        private static readonly Vector2 MoreChipOffset = new Vector2(100f, -22f);
        private const float MoreGridTop = 300f;
        private const float MoreGridBottom = 150f;
        private const float MoreBareIconScale = 0.76f;   // main-menu icons draw their glyph at ~76% of the canvas
        private const float MoreHeaderHeight = 220f;
        private const string MoreIconResource = "UI/Buttons/dahafazla";

        [Header("Daha fazla sayfası")]
        [Tooltip("Sayfanın arkasındaki karartma. Dokunulunca sayfa kapanır.")]
        [SerializeField] private Color moreScrimColor = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        private static readonly Color MoreInk = new Color32(22, 57, 89, 255);

        private RectTransform _moreScrim;   // full-bleed dim, tap to dismiss
        private RectTransform _moreSheet;   // the panel the rows sit in
        private readonly List<int> _moreOrder = new List<int>();
        private readonly List<RectTransform> _moreRows = new List<RectTransform>();

        /// <summary>
        /// A secondary opener, hung as a row in the More sheet instead of on the rail.
        ///
        /// The five screens that land here — Goals, Chapter, Crafting, Events, League — do not know
        /// they moved: they still call <see cref="AttachBottomButton"/> and still get a real
        /// <see cref="Button"/> plus a working <see cref="AttachCounterChip"/> back. That is the whole
        /// point of routing here rather than making each screen build its own entry point.
        /// </summary>
        /// <param name="framedIcon">True for the main-menu icon set, whose art carries its own margin.
        /// Any other icon (the workshop kit's) fills its canvas edge to edge and is drawn smaller to
        /// match.</param>
        private Button AttachMoreRow(int order, string name, Sprite icon, bool framedIcon,
                                     UnityEngine.Events.UnityAction onClick)
        {
            EnsureMoreSheet();
            if (_moreSheet == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_moreSheet, false);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            // This is the Settings screen's empty row export at its own 3:1 ratio. It is a real sprite,
            // not a sliced approximation, so its capsule ends, highlight and outline retain their shape.
            var plate = go.GetComponent<Image>();
            Sprite rowArt = PortraitUiArt.Get("settings-settings-empty-button");
            if (rowArt != null) PortraitUiArt.Apply(plate, rowArt);
            else
            {
                plate.sprite = UiSkin.Panel;
                plate.type = Image.Type.Sliced;
                plate.color = Color.white;
            }

            float iconBox = MoreRowHeight - 2f * MoreRowPadding;
            float iconSize = framedIcon ? iconBox : iconBox * MoreBareIconScale;
            var iconGo = new GameObject("Simge", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = new Vector2(MoreRowPadding + (iconBox - iconSize) * 0.5f, 0f);
            var iconImage = iconGo.GetComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.useSpriteMesh = true;
            iconImage.raycastTarget = false;
            iconImage.enabled = icon != null;

            // The label starts clear of the icon and stops clear of the counter chip's corner, so a
            // long translation truncates instead of running under the badge.
            var slot = new GameObject("Ad", typeof(RectTransform));
            var slotRect = (RectTransform)slot.transform;
            slotRect.SetParent(rect, false);
            slotRect.anchorMin = new Vector2(0f, 0f);
            slotRect.anchorMax = new Vector2(1f, 1f);
            slotRect.offsetMin = new Vector2(iconBox + 2f * MoreRowPadding, 0f);
            slotRect.offsetMax = new Vector2(-MoreRowPadding, 0f);
            Text label = UiBuild.Label(slotRect, "Text", Loc.T(MoreRowKey(name)), 25, TextAnchor.MiddleLeft);
            label.color = MoreInk;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            // Best fit, so a long word shrinks instead of breaking mid-word ("VERANSTALTUNGE / N"). The
            // floor is 18, under the type scale's 22: a row is half the sheet and the longest German
            // names are single 15-17 letter words.
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 25;
            // Baked once at build time otherwise: a later language switch left this sheet stuck in
            // whatever language the HUD happened to build under, while every other screen kept up.
            label.gameObject.AddComponent<LocalizedText>().SetKey(MoreRowKey(name));

            var button = go.GetComponent<Button>();
            button.targetGraphic = plate;
            // Close first, then open: the sheet sorts below every screen it launches, but leaving it
            // up behind a screen means the player closes one thing and finds another still open.
            button.onClick.AddListener(HideMore);
            if (onClick != null) button.onClick.AddListener(onClick);

            InsertMoreRow(order, rect);
            LayoutMoreRows();
            return button;
        }

        /// <summary>The row's own screen title key, so the sheet says "EVENTS" rather than "BtnEtkinlik".
        /// Reuses each screen's existing heading key — no row here needs its own translation.</summary>
        private static Sprite PortraitOpenerIcon(string name)
        {
            switch (name)
            {
                case "BtnGorev": case "BtnHedefler": case "BtnGorevler": return PortraitUiArt.Get("general-goals-icon");
                case "BtnDepo": return PortraitUiArt.Get("general-warehouse-icon");
                case "BtnCuzdan": return PortraitUiArt.Get("general-wallet-icon");
                case "BtnEtkinlik": case "BtnCanliEtkinlikler": return PortraitUiArt.Get("general-live-events-icon");
                case "BtnLig": return PortraitUiArt.Get("general-trophy-icon");
                case CardCollectionUI.OpenerButtonName: return PortraitUiArt.Get("empty-state-0");
                default: return null;
            }
        }

        /// <summary>Loads one of the TYCOON STANDARD main-menu icons by opener name.</summary>
        private static Sprite MainHudIcon(string name)
        {
            string asset = MainHudIconAsset(name);
            return string.IsNullOrEmpty(asset) ? null : Resources.Load<Sprite>(MainHudIconRoot + asset);
        }

        private static string MainHudIconAsset(string name)
        {
            switch (name)
            {
                case "BtnUstabasi": return "01-ustalar";
                case "BtnBedava": return "02-odullu-reklam";
                case "BtnLig": return "03-lig";
                case "BtnMaden": return "04-maden-teçhizati";
                case "BtnKartKoleksiyonu": return "05-kart-koleksiyonu";
                case "BtnDenizDostlari": return "06-deniz-dostlari";
                case "BtnCuzdan": return "07-kaynaklar";
                case "BtnDahaFazla": return "08-daha-fazla";
                case "BtnMagaza": return "09-shop";
                case "BtnGunluk": return "10-haftalik-odul";
                case "BtnYukselt": return "11-upgrade";
                case "BtnKontrat": return "12-kontrat";
                case "BtnGorev": return "13-gorevler";
                case "BtnEtkinlik": return "14-etkinlikler";
                case "BtnBoost": return "15-iki-x-gelir";
                case "BtnDenizSavasi": return "17-denize-acil";
                case "BtnPazarGelistir": return "18-pazar-gelistir";
                case CaptainButtonName: return "19-kaptanlar";
                default: return null;
            }
        }

        /// <summary>Applies the same generated icon set to inspector-authored HUD buttons.</summary>
        private void ApplyMainHudIcons()
        {
            ApplyMainHudIcon(storeButton, "BtnMagaza");
            ApplyMainHudIcon(dailyButton, "BtnGunluk");
            ApplyMainHudIcon(contractButton, "BtnKontrat");
            ApplyMainHudIcon(adButton, "BtnBedava");
            ApplyMainHudIcon(upgradeButton, "BtnYukselt");
            ApplyMainHudIcon(boostButton, "BtnBoost");
            ApplyMainHudIcon(settingsButton, "BtnAyarlar");
        }

        private static void ApplyMainHudIcon(Button button, string openerName)
        {
            if (button == null) return;
            Sprite icon = MainHudIcon(openerName);
            if (icon == null) return;
            Image image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null) return;
            image.sprite = icon;
            image.preserveAspect = true;
            image.useSpriteMesh = true;
        }

        private static string MoreRowKey(string name)
        {
            switch (name)
            {
                case "BtnGorev": return "gorev.baslik";
                case "BtnBolum": return "bolum.baslik";
                case "BtnAtolye": return "atolye.baslik";
                case "BtnEtkinlik": return "etkinlik.baslik";
                case "BtnLig": return "lig.baslik";
                case "BtnMaden": return "madenci.baslik";
                case "BtnKartKoleksiyonu": return "koleksiyon.baslik";
                case "BtnDenizDostlari": return "dost.baslik";
                case SailButtonName: return "deniz.acil";
                case "BtnCuzdan": return "wallet.open";
                case "BtnPazarGelistir": return "hud.pazar_gelistir";
                default: return name;
            }
        }

        private void InsertMoreRow(int order, RectTransform rect)
        {
            if (rect == null || _moreRows.Contains(rect)) return;
            int i = 0;
            while (i < _moreOrder.Count && _moreOrder[i] <= order) i++;
            _moreOrder.Insert(i, order);
            _moreRows.Insert(i, rect);
        }

        /// <summary>Places secondary destinations in a balanced two-column Settings-style grid.</summary>
        private void LayoutMoreRows()
        {
            if (_moreSheet == null) return;
            int count = _moreRows.Count;
            int rowCount = Mathf.CeilToInt(count * 0.5f);
            float body = rowCount > 0 ? rowCount * MoreRowHeight + (rowCount - 1) * MoreRowGap : 0f;
            float availableHeight = MoreSheetHeight - MoreGridTop - MoreGridBottom;
            float gridTop = MoreGridTop + Mathf.Max(0f, (availableHeight - body) * 0.5f);
            float rowWidth = (MoreGridWidth - MoreRowGap) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                RectTransform rect = _moreRows[i];
                if (rect == null) continue;
                int column = i % 2;
                int row = i / 2;
                rect.sizeDelta = new Vector2(rowWidth, MoreRowHeight);
                bool lastUnpaired = count % 2 != 0 && i == count - 1;
                float x = lastUnpaired ? 0f : (column == 0 ? -0.5f : 0.5f) * (rowWidth + MoreRowGap);
                rect.anchoredPosition = new Vector2(x,
                    -(gridTop + row * (MoreRowHeight + MoreRowGap)));
            }
        }

        /// <summary>
        /// Builds the sheet on the first secondary opener, so a build with none grows no button.
        ///
        /// IT MUST NOT BE PARENTED TO THE HUD — see <see cref="BuildLadder"/> for the same rule and
        /// the same reason: this builds its own ScreenSpaceOverlay canvas, and a Canvas nested inside
        /// another Canvas has its render mode ignored and collapses into the parent's rect.
        /// </summary>
        private void EnsureMoreSheet()
        {
            if (_moreScrim != null) return;

            GameObject systems = GameObject.Find(UiSystemsObject);
            Transform host = systems != null ? systems.transform : null;
            RectTransform canvas = UiBuild.Canvas(host, "DahaFazlaKanvas", MoreSortingOrder);

            _moreScrim = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(moreScrimColor), Vector2.zero, Vector2.one);
            var dismiss = _moreScrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(HideMore);

            // Centred on the canvas and sized in reference units, so the sheet stays centred on every
            // aspect ratio instead of drifting the way a fraction-of-screen anchor does.
            var sheetGo = new GameObject("Zemin", typeof(RectTransform), typeof(Image));
            _moreSheet = (RectTransform)sheetGo.transform;
            _moreSheet.SetParent(_moreScrim, false);
            _moreSheet.anchorMin = new Vector2(0.5f, 0.5f);
            _moreSheet.anchorMax = new Vector2(0.5f, 0.5f);
            _moreSheet.pivot = new Vector2(0.5f, 0.5f);
            _moreSheet.anchoredPosition = Vector2.zero;
            var sheetImage = sheetGo.GetComponent<Image>();
            Sprite panelArt = PortraitUiArt.Get("settings-settings-panel");
            if (panelArt != null) PortraitUiArt.Apply(sheetImage, panelArt);
            else
            {
                sheetImage.sprite = UiSkin.Panel;
                sheetImage.type = Image.Type.Sliced;
                sheetImage.color = Color.white;
            }
            sheetImage.raycastTarget = true;              // eats its own taps so the scrim cannot fire through
            var eat = sheetGo.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
            _moreSheet.sizeDelta = new Vector2(MoreSheetWidth, MoreSheetHeight);

            var titleSlot = new GameObject("Baslik", typeof(RectTransform), typeof(Image));
            var titleRect = (RectTransform)titleSlot.transform;
            titleRect.SetParent(_moreSheet, false);
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(660f, MoreHeaderHeight);
            titleRect.anchoredPosition = new Vector2(0f, -110f);
            Sprite titleArt = PortraitUiArt.Get("general-title-plate");
            if (titleArt != null) PortraitUiArt.Apply(titleSlot.GetComponent<Image>(), titleArt);
            else titleSlot.GetComponent<Image>().enabled = false;
            Text title = UiBuild.Label(titleRect, "Text", Loc.T("hud.dahafazla"), 40,
                                       TextAnchor.MiddleCenter);
            title.color = MoreInk;
            title.gameObject.AddComponent<LocalizedText>().SetKey("hud.dahafazla");

            var closeGo = new GameObject("BtnKapat", typeof(RectTransform), typeof(Image), typeof(Button));
            var closeRect = (RectTransform)closeGo.transform;
            closeRect.SetParent(_moreSheet, false);
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 1f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.sizeDelta = new Vector2(96f, 96f);
            closeRect.anchoredPosition = new Vector2(412f, -72f);
            var closeImage = closeGo.GetComponent<Image>();
            Sprite closeArt = PortraitUiArt.Get("general-close-button");
            if (closeArt != null) PortraitUiArt.Apply(closeImage, closeArt);
            else { closeImage.sprite = UiSkin.ButtonGrey; closeImage.type = Image.Type.Sliced; }
            var close = closeGo.GetComponent<Button>();
            close.targetGraphic = closeImage;
            close.onClick.AddListener(HideMore);

            // Content into the safe area; the scrim above it keeps covering the notch. Safe here even
            // though the rows arrive later: they parent to _moreSheet, which this moves inside.
            UiBuild.InsetContent(_moreScrim);

            _moreScrim.gameObject.SetActive(false);
            EnsureMoreOpener();
        }

        /// <summary>The rail button that opens the sheet. Order 9 puts it after the two rosters and
        /// before the authored row, which starts at <see cref="AuthoredOrder"/>.
        ///
        /// Every other opener has its own glyph under Resources/UI/Buttons; this one has no art yet,
        /// so it wears the plain button plate with an ellipsis over it rather than a blank pill. Drop
        /// a <c>dahafazla.png</c> in beside the others and the glyph can go.</summary>
        private void EnsureMoreOpener()
        {
            Sprite icon = Resources.Load<Sprite>(MoreIconResource);
            Button open = AttachBottomButton(9, MoreButtonName,
                                             icon != null ? icon : UiSkin.ButtonGrey, ToggleMore);
            if (open == null || icon != null) return;
            Text glyph = UiBuild.Label(open.transform, "Text", "•••", 46, TextAnchor.MiddleCenter);
            glyph.color = MoreInk;
        }

        public void ToggleMore()
        {
            if (_moreScrim == null) return;
            bool show = !_moreScrim.gameObject.activeSelf;
            _moreScrim.gameObject.SetActive(show);
        }

        public void HideMore()
        {
            if (_moreScrim != null) _moreScrim.gameObject.SetActive(false);
        }

        /// <summary>
        /// Hangs a code-built strip across the HUD, directly under everything authored in the top area.
        ///
        /// <see cref="AttachBottomButton"/> explains why a code-built screen must not anchor to a
        /// fraction of the SCREEN: the HUD is a portrait sheet scaled as one piece, so in landscape a
        /// fraction of the screen is not a fraction of the sheet. This anchors inside the sheet, which
        /// is the coordinate space the authored bar already lives in, and takes its vertical position
        /// from the LOWEST authored rect in the top area — so it clears the two currency pills, the
        /// rate pill, the settings button and both indicator chips without being told where any of
        /// them are, and it keeps clearing them if that bar is ever re-laid out.
        ///
        /// The indicators are measured whether they are showing or not. A strip that rose when the
        /// boost chip expired would be a banner that jumps around while the player is reading it.
        ///
        /// Re-solved whenever the sheet's rect changes, which covers a rotation and also the ordinary
        /// case of the canvas not yet having been laid out when this is called from Start.
        /// </summary>
        public RectTransform AttachTopStrip(string name, float widthFraction, float height, float gap)
        {
            var sheet = transform as RectTransform;
            if (sheet == null) return null;

            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(sheet, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            _topStrip = rect;
            _topStripWidth = widthFraction;
            _topStripHeight = height;
            _topStripGap = gap;
            SolveTopStrip(sheet);
            return rect;
        }

        private void SolveTopStrip(RectTransform sheet)
        {
            if (_topStrip == null || sheet == null) return;

            float clear = sheet.rect.yMax;
            clear = Mathf.Min(clear, BottomOf(sheet, goldValue));
            clear = Mathf.Min(clear, BottomOf(sheet, gemsValue));
            clear = Mathf.Min(clear, BottomOf(sheet, rateValue));
            clear = Mathf.Min(clear, BottomOf(sheet, settingsButton));
            clear = Mathf.Min(clear, BottomOf(sheet, _stageIndicator));
            clear = Mathf.Min(clear, BottomOf(sheet, boostIndicator));
            clear = Mathf.Min(clear, BottomOf(sheet, shieldIndicator));

            // Keep clear of the side rail. The strip is 78% of the sheet and centred, which on a
            // landscape canvas reaches past the rail's second column; give the rail its width back and
            // slide what is left into the middle of the free area.
            float inset = sideRail ? _railWidth : 0f;
            float width = Mathf.Min(sheet.rect.width * _topStripWidth,
                                    Mathf.Max(100f, sheet.rect.width - inset - 24f));
            _topStrip.sizeDelta = new Vector2(width, _topStripHeight);
            _topStrip.anchoredPosition = new Vector2((railOnLeft ? 1f : -1f) * inset * 0.5f,
                                                     clear - sheet.rect.yMax - _topStripGap);
            _sheetSize = sheet.rect.size;
        }

        /// <summary>
        /// The lowest edge of an authored rect, in the sheet's own local space.
        ///
        /// Measured through world corners rather than read off anchoredPosition: the parts of the top
        /// bar are anchored every which way, and subtracting one anchored position from another is
        /// arithmetic across two different origins. Absent parts answer MaxValue so a Min over them
        /// ignores what is not wired.
        /// </summary>
        private static float BottomOf(RectTransform sheet, RectTransform of)
        {
            if (of == null) return float.MaxValue;
            of.GetWorldCorners(Corners);            // 0 = bottom-left
            return sheet.InverseTransformPoint(Corners[0]).y;
        }

        private static float BottomOf(RectTransform sheet, Component of)
            => of == null ? float.MaxValue : BottomOf(sheet, of.transform as RectTransform);

        private static float BottomOf(RectTransform sheet, GameObject of)
            => of == null ? float.MaxValue : BottomOf(sheet, of.transform as RectTransform);

        /// <summary>
        /// The objective strip. Made here rather than authored, for the reason
        /// <see cref="InventoryUI"/> is made by <see cref="CraftingUI"/>: the HUD is a prefab and the
        /// strip is built in code, so there is no authored sheet to wire it into. An
        /// <see cref="ObjectiveBannerUI"/> already in the scene is adopted instead, which is how its
        /// art becomes Inspector-tunable without this method changing.
        /// </summary>
        private void BuildObjectiveStrip()
        {
            var banner = FindAnyObjectByType<ObjectiveBannerUI>(FindObjectsInactive.Include);
            if (banner == null)
            {
                var go = new GameObject("HedefSeridiKurulum", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                banner = go.AddComponent<ObjectiveBannerUI>();
            }
            banner.Adopt(this);
        }

        /// <summary>
        /// The league screen, made here for the same reason the strip above is: it is built in code
        /// and there is no authored sheet to wire it into. One already in the scene is adopted instead,
        /// so its art becomes Inspector-tunable without this method changing.
        ///
        /// It attaches its own opener (order 6) from its Awake, and draws none at all when the build
        /// has no ladder registered — so a stub-backed build costs one dormant component.
        ///
        /// IT MUST NOT BE PARENTED TO THE HUD, however convenient that looks from in here. The screen
        /// builds its own ScreenSpaceOverlay canvas, and a Canvas nested inside another Canvas has its
        /// render mode ignored: it becomes a sub-canvas laid out inside the parent's RectTransform.
        /// Hung under the HUD it therefore inherited a zero-sized rect and every anchored child
        /// collapsed onto a single point — the whole screen drawn as one pile of overlapping text.
        /// It goes beside the other code-built screens on UI_Sistemler, which is a plain object, and
        /// falls back to the scene root, which is also outside every canvas.
        /// </summary>
        private void BuildLadder()
        {
            if (FindAnyObjectByType<LadderUI>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("LigEkrani");
            GameObject systems = GameObject.Find(UiSystemsObject);
            if (systems != null) go.transform.SetParent(systems.transform, false);
            go.AddComponent<LadderUI>();
        }

        /// <summary>Exposes the market-upgrade screen through the same More sheet as the other
        /// secondary HUD destinations. The screen itself remains code-built and owns its panel.</summary>
        private void BuildMarketUpgradeOpener()
        {
            AttachBottomButton(8, "BtnPazarGelistir", null, OnMarketUpgrades);
        }

        private void Update()
        {
            _rewardBadgeTime += Time.unscaledDeltaTime;
            float badgeScale = 1f + 0.08f * Mathf.Sin(_rewardBadgeTime * 4f);
            if (_rewardBadge != null && _rewardBadge.gameObject.activeSelf)
                _rewardBadge.localScale = new Vector3(badgeScale, badgeScale, 1f);
            if (_authoredRewardBadge != null && _authoredRewardBadge.gameObject.activeSelf)
                _authoredRewardBadge.localScale = new Vector3(badgeScale, badgeScale, 1f);
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_stages == null) _stages = ServiceLocator.Get<StageService>();
            if (_op == null || !_op.enabled) BindEnabledOp();
            if (SafeAreaChanged())
            {
                ApplySafeArea();
                LayoutBottomRow();
            }
            if (_topStrip != null)
            {
                var sheet = transform as RectTransform;
                if (sheet != null && sheet.rect.size != _sheetSize) SolveTopStrip(sheet);
            }
            // The port ship waits in the shop: its jobs count smelted ore, which the shop never makes.
            if (_contract != null && _shopContract == null) _contract.Tick(Time.deltaTime, IncomePerMinute());
            RollCash(Time.unscaledDeltaTime);
            RollGems(Time.unscaledDeltaTime);
            Punch(goldValue, ref _goldPunch, Time.unscaledDeltaTime);
            Punch(gemsValue, ref _gemPunch, Time.unscaledDeltaTime);
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            // The festival banks the goal counters only when something reads it, and nothing does
            // while its board is shut. Four times a second here bounds what the closing second of a
            // festival can swallow to a quarter of one — the same reason ContractService is ticked
            // from this Update rather than from its own screen.
            _festival?.Sync();
            _harborFestival?.Sync();
            _productionSprint?.Sync();
            Refresh();
        }

        /// <summary>
        /// Ease the displayed balance toward the real one instead of snapping every quarter second.
        /// The counter climbing is most of what makes the money feel like it's flowing in.
        /// </summary>
        private void RollCash(float dt)
        {
            if (_wallet == null || goldValue == null) return;
            double target = _wallet.Cash.ToDouble();
            if (!_haveShownCash) { _shownCash = target; _haveShownCash = true; }
            else
            {
                double diff = target - _shownCash;
                // snap on a big jump (a purchase, an offline grant) so the counter never crawls for seconds
                if (diff < 0d || System.Math.Abs(diff) > System.Math.Max(1d, target * 0.35d))
                {
                    // The jump is also the only cash worth celebrating. Income arrives every second of
                    // the game; a pop on that would be a counter that never stops twitching.
                    if (diff > 0d) _goldPunch = counterPunchSeconds;
                    _shownCash = target;
                }
                else _shownCash += diff * (1d - System.Math.Exp(-9d * dt));
            }
            goldValue.text = NumberFormatter.Format(new BigDouble(_shownCash));
        }

        /// <summary>
        /// Gems used to snap from one integer to the next, which made buying a hundred of them look
        /// exactly like spending one. They roll now, the same way cash does — and because gems only ever
        /// move when the player did something, every rise is worth a pop.
        ///
        /// The text is written only when the whole number it shows actually changes, so a settled
        /// counter costs nothing per frame.
        /// </summary>
        private void RollGems(float dt)
        {
            if (_wallet == null || gemsValue == null) return;
            double target = _wallet.Gems;
            if (!_haveShownGems) { _shownGems = target; _haveShownGems = true; }
            else if (target < _shownGems) _shownGems = target;    // spending lands at once
            else _shownGems += (target - _shownGems) * (1d - System.Math.Exp(-gemRollSpeed * dt));

            long show = (long)(_shownGems + 0.5d);
            if (show == _writtenGems) return;
            _writtenGems = show;
            gemsValue.text = show.ToString();
        }

        /// <summary>A short rise-and-fall on a counter that just grew. Rests at exactly 1.</summary>
        private void Punch(TMP_Text label, ref float left, float dt)
        {
            if (left <= 0f || label == null) return;
            left -= dt;
            float s = left > 0f
                ? 1f + counterPunch * Mathf.Sin(Mathf.Clamp01(1f - left / counterPunchSeconds) * Mathf.PI)
                : 1f;
            label.transform.localScale = new Vector3(s, s, 1f);
            if (left <= 0f) left = 0f;
        }

        /// <summary>Several operations live on the controller (one per island) — bind the enabled one.</summary>
        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { _op = ops[i]; return; }
            if (_op == null && ops.Length > 0) _op = ops[0];
        }

        /// <summary>
        /// What the empire earns a minute — sizes the next contract. Falls back to the active island
        /// alone if the world manager is missing, so this still works on a bare scene.
        /// </summary>
        private double IncomePerMinute()
        {
            if (_world != null)
            {
                double sum = 0d;
                for (int i = 0; i < _world.Count; i++) if (_world.IsOwned(i)) sum += _world.RatePerMin(i);
                if (sum > 0d) return sum;
            }
            // The live mining-shop business replaces the market's island meter on Main. Its steady-state rate is
            // the unboosted income offline earnings are paid from; the receipt window below only drives the pill.
            if (_market != null && _market.MiningShopBusiness != null) return _market.MiningShopIncomePerSec * 60d;
            return _op != null ? _op.CashPerMinute : 0d;
        }

        /// <summary>Unboosted empire income per minute for other HUD reward offers.</summary>
        public double CurrentUnboostedIncomePerMinute => IncomePerMinute();

        /// <summary>The gem counter, for rewards that fly gems into it. Null when the HUD has none wired.</summary>
        public RectTransform GemsCounter => gemsValue != null ? gemsValue.rectTransform : null;

        /// <summary>A shop receipt, remembered only for the rate pill. MarketService has already paid the wallet.</summary>
        private void OnShopSold(MiningShopBusinessSimulation.Sale sale)
        {
            _saleTimes[_saleHead] = Time.time;
            _saleCash[_saleHead] = sale.Cash;
            _saleHead = (_saleHead + 1) % _saleTimes.Length;
            if (_saleCount < _saleTimes.Length) _saleCount++;
        }

        /// <summary>
        /// The rate pill while the mining shop owns Main: items sold (the business's own counts) and what the
        /// receipts of the last <see cref="shopRateWindow"/> seconds came to, per minute. The ore meter it replaces
        /// read zero forever once the shop took over the economy.
        /// </summary>
        private string ShopStatus(in MiningShopBusinessSimulation.Snapshot view)
        {
            long sold = 0;
            for (int p = 0; p < view.AvailableProductCount; p++) sold += view.ProductAt(p).Sold;
            double recent = 0d;
            float since = Time.time - shopRateWindow;
            for (int i = 0; i < _saleCount; i++) if (_saleTimes[i] >= since) recent += _saleCash[i];
            return string.Format(Loc.T("maden_dukkani.hud_durum"), sold,
                                 "$" + NumberFormatter.Format(new BigDouble(recent * 60d / shopRateWindow)));
        }

        private void Refresh()
        {
            RefreshStageIndicator();
            if (rateValue != null && _market != null && _market.MiningShopBusiness != null)
                rateValue.text = ShopStatus(_market.MiningShopBusiness.View);
            else if (rateValue != null && _op != null)
                rateValue.text = string.Format(Loc.T("ortak.dakika_basina"),
                                               "$" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)));
            if (contractTimerValue != null && _shopContract != null) contractTimerValue.text = ShopContractChip();
            else if (contractTimerValue != null && _contract != null) contractTimerValue.text = ContractChip();

            RefreshOfferButton();

            bool boosted = _boost != null && _boost.IsActive;
            if (boostIndicator != null)
            {
                if (boostIndicator.activeSelf != boosted) boostIndicator.SetActive(boosted);
                if (boosted && boostValue != null)
                    boostValue.text = "×" + _boost.ActiveMultiplier.ToString("0.#",
                        System.Globalization.CultureInfo.InvariantCulture)
                        + "  " + LongClock(_boost.SecondsLeft);
            }
            RefreshBoostButton(boosted);
            RefreshBalloonButton();
            RefreshRewardBadge();

            bool shielded = _maintenance != null && _maintenance.ShieldActive;
            if (shieldIndicator != null)
            {
                if (shieldIndicator.activeSelf != shielded) shieldIndicator.SetActive(shielded);
                if (shielded)
                {
                    if (shieldValue != null)
                        shieldValue.text = Loc.T("hud.kalkan") + "  " + LongClock(_maintenance.ShieldSecondsLeft);
                    // The two chips are authored side by side, which only looks deliberate while both
                    // are up. A shield running on its own slides into the boost's slot rather than
                    // hanging off to the right of a gap.
                    ((RectTransform)shieldIndicator.transform).anchoredPosition =
                        boosted ? _shieldSlot : _boostSlot;
                }
            }
            FitInfoPills();
        }

        /// <summary>
        /// Stage completion is observed from the existing chapter objectives, so polling alongside
        /// the HUD's normal quarter-second refresh advances the label without a button, save write,
        /// or duplicate reward path.
        /// </summary>
        private void RefreshStageIndicator()
        {
            if (_stageIndicator == null) return;

            string label = _stages != null ? _stages.CurrentLabel() : string.Empty;
            bool visible = !string.IsNullOrEmpty(label);
            if (_stageIndicator.gameObject.activeSelf != visible) _stageIndicator.gameObject.SetActive(visible);
            if (!visible || _stageIndicatorLabel == null || _shownStageLabel == label) return;

            _shownStageLabel = label;
            _stageIndicatorLabel.text = label;
            FitStagePill();
        }

        /// <summary>
        /// A countdown that can run for a day. <see cref="ContractUI.ClockText"/> counts in minutes
        /// and seconds, which is right for a contract and reads as "1440:00" on a 24-hour shield —
        /// so anything past the hour mark gets an hour field of its own here.
        /// </summary>
        public static string LongClock(float seconds)
        {
            if (seconds < 3600f) return ContractUI.ClockText(seconds);
            int total = Mathf.CeilToInt(seconds);
            if (total >= 86400)
            {
                int d = total / 86400;
                int dh = (total - d * 86400) / 3600;
                return string.Format(Loc.T("ortak.sure_gun_sa"), d, dh);
            }
            int h = total / 3600;
            int m = (total - h * 3600) / 60;
            return string.Format(Loc.T("ortak.sure_sa_dk"), h, m);
        }

        /// <summary>
        /// The shortcut next to the upgrade button. It has no state of its own — everything it shows
        /// is read back off the ad screen's boost slot, so spending the charge from either place leaves
        /// both looking the same.
        /// </summary>
        private void RefreshBoostButton(bool boosted)
        {
            if (boostButton == null) return;
            // Not gated on "no boost running" any more. That gate existed because a second boost used to
            // wipe the first, so tapping this while a package ran destroyed the package. Boosts stack
            // now (BoostService.AddBoost), so locking the shortcut would only mean a player who bought
            // the 24-hour offer loses their three free charges for the day.
            bool ready = adScreen != null && adScreen.BoostReady;
            boostButton.interactable = ready;

            if (boostButtonImage != null)
                // uGUI's disabled tint latches onto the graphic; stamp the state's colour back on
                boostButtonImage.CrossFadeColor(ready ? Color.white : DimBoost, 0f, true, true);

            if (boostButtonTitle == null) return;
            // The label sits inside the button now, so it has to take the dim itself:
            // CrossFadeColor only paints the graphic it is called on, never the children.
            boostButtonTitle.color = ready ? Color.white : DimBoost;
            // While a boost runs, the headline is whatever is actually multiplying the income —
            // a store offer can set a different one, and the button must not claim the slot's.
            double mult = boosted ? _boost.ActiveMultiplier
                                  : (adScreen != null ? adScreen.BoostMultiplier : 2d);
            boostButtonTitle.text = string.Format(Loc.T("hud.gelir"),
                mult.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The offer shortcut is the quiet half of the pop-up: the window itself interrupts once, and
        /// from then on this button is the only reminder the player gets. It is furniture rather than a
        /// notification, so it never switches itself off — paying for a pack clears the offer and arms
        /// the next one, and a button tied to that would blink out and back in the player's face at the
        /// exact moment they handed over money. Only the clock chip comes and goes, and only where
        /// there is genuinely no clock: an island with nothing left to sell.
        /// </summary>
        private void RefreshOfferButton()
        {
            if (offerButton == null) return;
            bool live = offerScreen != null && offerScreen.HasLiveOffer;
            if (offerTimerChip != null && offerTimerChip.activeSelf != live) offerTimerChip.SetActive(live);
            if (!live || offerTimerValue == null) return;

            // The contract clock counts minutes because a contract runs for minutes; an offer runs for
            // a day, and "1439:56" is not a number anyone reads as "a day left".
            long left = offerScreen.SecondsLeft();
            offerTimerValue.text = left >= 3600L
                ? (left / 3600L) + ":" + (left / 60L % 60L).ToString("00")
                : ContractUI.ClockText(left);
        }

        /// <summary>
        /// The line under the contract button. It has to answer "is there anything at the port right
        /// now?" in one glance, so the states that want the player are words — READY to claim, READY to
        /// pick — and the states that do not are just a clock: the running job's, or the countdown to the
        /// next ship.
        /// </summary>
        private string ContractChip()
        {
            switch (_contract.State)
            {
                case ContractService.PortState.Reward:
                    return Loc.T("ortak.hazir");
                // A board and a finished job both want the player, but not for the same reason, and
                // "READY" for both made the chip say the same thing whether there was a reward sitting
                // there or a choice to make. The count also stops being a lie if a fourth card is ever
                // added, which "three offers" in the notification text used to be.
                case ContractService.PortState.Offering:
                    return string.Format(Loc.T("kontrat.is_sayisi"), _contract.OfferCount);
                case ContractService.PortState.Active:
                    return ContractUI.ClockText(_contract.SecondsLeft);
                case ContractService.PortState.Away:
                    return ContractUI.ClockText(_contract.SecondsToShip);
                default:
                    return Loc.T("kontrat.gemi_kisa");
            }
        }

        /// <summary>
        /// The chip in the shop: NEW when an offer can be taken, the contract customer's count while one runs, and
        /// otherwise the clock to the next offer.
        /// </summary>
        private string ShopContractChip()
        {
            if (!_shopContract.Unlocked) return string.Empty;
            MiningShopBusinessSimulation.Snapshot view = _market.MiningShopBusiness.View;
            if (view.ContractActive) return view.ContractDelivered + "/" + view.ContractQuantity;
            if (_shopContract.OfferReady) return Loc.T("dukkan_kontrat.yeni");
            return LongClock((float)_shopContract.SecondsToNextOffer);
        }

        /// <summary>
        /// The shop's contract screen and its "Contract Completed!" celebration, built in code on a root of their own
        /// — not under the HUD, which the opening tutorial switches off.
        /// </summary>
        private void BuildShopContractScreens()
        {
            var host = new GameObject("DukkanKontrati");
            _shopContractScreen = host.AddComponent<ShopContractUI>();
            var celebration = host.AddComponent<ShopContractCelebrationUI>();
            Canvas hud = GetComponentInParent<Canvas>();
            celebration.Bind(hud, _shopContractScreen.Canvas);
        }

        private static readonly Color DimBoost = new Color(0.55f, 0.58f, 0.66f, 1f);

        // ---- what the tutorial points at -------------------------------------------------------
        // Read-only rects, so the onboarding can cut a hole over a real control instead of drawing a
        // copy of it somewhere and hoping the two stay in the same place. Nothing here can move a
        // button; the screen the player taps is still this one's.
        public RectTransform ContractRect => Rect(contractButton);
        public RectTransform BoostRect => Rect(boostButton);
        public RectTransform DailyRect => Rect(dailyButton);
        public RectTransform GoldRect => Rect(goldButton);
        public RectTransform GemsRect => Rect(gemsButton);

        /// <summary>
        /// A code-built opener by its name, wherever this layout hung it: on the rail, or as a row in
        /// the More sheet (<paramref name="inMore"/>). Null when no screen attached it.
        /// </summary>
        public RectTransform OpenerRect(string name, out bool inMore)
        {
            for (int i = 0; i < _moreRows.Count; i++)
                if (_moreRows[i] != null && _moreRows[i].name == name) { inMore = true; return _moreRows[i]; }
            inMore = false;
            for (int i = 0; i < _bottomRects.Count; i++)
                if (_bottomRects[i] != null && _bottomRects[i].name == name) return _bottomRects[i];
            return null;
        }

        /// <summary>The rail button that opens the More sheet.</summary>
        public RectTransform MoreButtonRect
        {
            get
            {
                for (int i = 0; i < _bottomRects.Count; i++)
                    if (_bottomRects[i] != null && _bottomRects[i].name == MoreButtonName) return _bottomRects[i];
                return null;
            }
        }

        public bool MoreOpen => _moreScrim != null && _moreScrim.gameObject.activeInHierarchy;

        /// <summary>Whether a UI object belongs to the More sheet's own canvas.</summary>
        public bool InMoreSheet(Transform t) => _moreScrim != null && t != null && t.IsChildOf(_moreScrim.parent);

        /// <summary>Whether the ×2 shortcut has a charge — the tip about it waits for this.</summary>
        public bool BoostReady => adScreen != null && adScreen.BoostReady;

        private static RectTransform Rect(Button b) => b != null ? (RectTransform)b.transform : null;

        /// <summary>The number itself rolls in <see cref="RollGems"/>; this only notices that it went up.</summary>
        private void RefreshGems()
        {
            if (_wallet == null || !_haveShownGems) return;
            if (_wallet.Gems > _shownGems) _gemPunch = counterPunchSeconds;
        }

        private void OnStore()
        {
            if (store != null) store.Show();
        }

        private void OnDaily()
        {
            if (dailyScreen != null) dailyScreen.Toggle();
        }

        private void OnUpgrades()
        {
            if (stationScreen != null) stationScreen.Open();
        }

        private void OnMarketUpgrades()
        {
            IslandYardUpgradeUI yard = FindAnyObjectByType<IslandYardUpgradeUI>(FindObjectsInactive.Include);
            if (yard != null) yard.ShowActiveIsland();
        }

        /// <summary>The $/min pill answers for itself: where that money is coming from, stage by stage.</summary>
        private void OnRate()
        {
            if (stationScreen != null) stationScreen.OpenReport();
        }

        private void OnSettings()
        {
            if (settings != null) settings.Toggle();
        }

        private void OnContract()
        {
            if (_shopContractScreen != null) _shopContractScreen.Toggle();
            else if (contractScreen != null) contractScreen.Toggle();
        }

        private void OnAds()
        {
            if (adScreen != null) adScreen.Toggle();
        }

        private void OnOffer()
        {
            if (offerScreen != null) offerScreen.Open();
        }

        /// <summary>Straight to the ad — the shortcut exists precisely to skip opening the ad screen.</summary>
        private void OnBoost()
        {
            if (adScreen != null) adScreen.WatchBoost();
        }

        private void BuildBalloonButton()
        {
            Sprite icon = balloonIcon;
            if (icon == null && adButton != null && adButton.image != null) icon = adButton.image.sprite;
            // The fifth rail button is the single rewarded-ad entry point. Keep the balloon service
            // behind its own rules, but open the same centred ad panel instead of silently claiming
            // one of its rewards on tap.
            _balloonButton = AttachBottomButton(3, BalloonButtonName,
                                                icon != null ? icon : UiSkin.ButtonBlue, OnAds);
            if (_balloonButton == null) return;
            _balloonTimerChip = AttachCounterChip(_balloonButton);
            if (_balloonTimerChip != null)
                _balloonTimerValue = _balloonTimerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private static RectTransform BuildRewardBadge(Button opener)
        {
            if (opener == null) return null;
            GameObject badge = new GameObject("ReadyRewardBadge", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = badge.GetComponent<RectTransform>();
            rect.SetParent(opener.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.88f, 0.88f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(42f, 42f);
            Image image = badge.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("UI/Portrait/general-notification-badge");
            image.preserveAspect = true;
            image.color = new Color(0.96f, 0.12f, 0.15f, 1f);
            image.raycastTarget = false;
            badge.SetActive(false);
            return rect;
        }

        private void RefreshRewardBadge()
        {
            bool ready = adScreen != null && adScreen.HasAvailableReward;
            if (_rewardBadge != null && _rewardBadge.gameObject.activeSelf != ready)
                _rewardBadge.gameObject.SetActive(ready);
            if (_authoredRewardBadge != null && _authoredRewardBadge.gameObject.activeSelf != ready)
                _authoredRewardBadge.gameObject.SetActive(ready);
        }

        private bool BalloonAdReady => (_freeRewards != null && _freeRewards.AdsRemoved)
                                       || (_ad != null && _ad.Available);

        private void OnBalloon()
        {
            if (_balloon == null || !_balloon.Ready || !BalloonAdReady) return;
            if (_freeRewards != null && _freeRewards.AdsRemoved) { ClaimBalloon(); return; }
            _ad?.ShowRewarded(ClaimBalloon);
        }

        private void ClaimBalloon()
        {
            if (_balloon == null) return;
            BalloonRewardService.Receipt receipt = _balloon.TryClaim(IncomePerMinute(), balloonCashMinutes,
                                                                       balloonCashFloor, balloonDiamondChance,
                                                                       balloonDiamondAmount, UnityEngine.Random.value);
            if (!receipt.Paid) return;
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void RefreshBalloonButton()
        {
            if (_balloonButton == null || _balloon == null) return;
            bool ready = _balloon.Ready && BalloonAdReady;
            _balloonButton.interactable = ready;
            if (_balloonTimerChip != null)
            {
                bool waiting = !ready && _balloon.CooldownLeft > 0f;
                if (_balloonTimerChip.activeSelf != waiting) _balloonTimerChip.SetActive(waiting);
                if (waiting && _balloonTimerValue != null)
                    _balloonTimerValue.text = LongClock(_balloon.CooldownLeft);
            }
        }
    }
}
