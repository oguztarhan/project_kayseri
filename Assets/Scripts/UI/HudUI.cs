using Game.Core;
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

        [Header("Dikey tersane sade HUD")]
        [Tooltip("Yalnızca Inspector'daki dört ana eylemi kenar rayında tutar; eski bölüm bildirimi ve yinelenen kısayolları gizler.")]
        [SerializeField] private bool compactShipyardHud = true;
        [Header("Üst bar")]
        [SerializeField] private TMP_Text goldValue;
        [SerializeField] private TMP_Text gemsValue;
        [SerializeField] private TMP_Text rateValue;
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
        [SerializeField] private Button mapButton;
        [SerializeField] private Button contractButton;
        [Tooltip("Kontrat butonunun altındaki canlı sayaç.")]
        [SerializeField] private TMP_Text contractTimerValue;
        [SerializeField] private Button adButton;

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
        [SerializeField] private IslandMapUI islandMap;
        [SerializeField] private SettingsUI settings;
        [SerializeField] private DailyRewardUI dailyScreen;
        [SerializeField] private ContractUI contractScreen;
        [SerializeField] private AdRewardUI adScreen;
        [Tooltip("Açılır fırsat penceresi. Kendi zamanlamasını kendi yönetir; HUD sadece butonu ona açar.")]
        [SerializeField] private OfferPopupUI offerScreen;

        [SerializeField] private float refreshInterval = 0.25f;

        [Header("Sayaç vuruşu")]
        [Tooltip("Para geldiğinde sayının ne kadar büyüdüğü. Hapın kendisi değil, içindeki sayı zıplar — "
                 + "hapa dokunma yaylanması yazıyor, ikisi aynı ölçeği paylaşamaz.")]
        [SerializeField] private float counterPunch = 0.16f;
        [SerializeField] private float counterPunchSeconds = 0.28f;
        [Tooltip("Elmas sayacının dolma hızı. Nakitinki 9 — para saniyede bir damlıyor, elmas ise "
                 + "yılda birkaç kez; yavaş sayması izlenecek bir şey oluyor. Küçük değer = yavaş.")]
        [SerializeField] private float gemRollSpeed = 5.5f;

        private WalletService _wallet;
        private ContractService _contract;
        private FoundryFestivalService _festival;
        private HarborFestivalService _harborFestival;
        private ProductionSprintService _productionSprint;
        private BoostService _boost;
        private MaintenanceService _maintenance;
        private WorldIslands _world;
        private CoalOperation _op;
        private float _timer;
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

        // The objective strip under the currency bar. Its position is solved from the authored rects
        // above it rather than authored itself, so it is re-solved whenever the sheet changes size.
        private RectTransform _topStrip;
        private float _topStripWidth, _topStripHeight, _topStripGap;
        private Vector2 _sheetSize = new Vector2(-1f, -1f);
        private static readonly Vector3[] Corners = new Vector3[4];

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _contract = ServiceLocator.Get<ContractService>();
            _festival = ServiceLocator.Get<FoundryFestivalService>();
            _harborFestival = ServiceLocator.Get<HarborFestivalService>();
            _productionSprint = ServiceLocator.Get<ProductionSprintService>();
            _boost = ServiceLocator.Get<BoostService>();
            _maintenance = ServiceLocator.Get<MaintenanceService>();
            _world = FindAnyObjectByType<WorldIslands>();
            if (shieldIndicator != null)
                _shieldSlot = ((RectTransform)shieldIndicator.transform).anchoredPosition;
            if (boostIndicator != null)
                _boostSlot = ((RectTransform)boostIndicator.transform).anchoredPosition;
            BindEnabledOp();

            if (storeButton != null) storeButton.onClick.AddListener(OnStore);
            if (goldButton != null) goldButton.onClick.AddListener(OnStore);
            if (gemsButton != null) gemsButton.onClick.AddListener(OnStore);
            if (dailyButton != null) dailyButton.onClick.AddListener(OnDaily);
            if (mapButton != null) mapButton.onClick.AddListener(OnMap);
            if (contractButton != null) contractButton.onClick.AddListener(OnContract);
            if (contractButton != null) contractButton.gameObject.SetActive(true);
            if (adButton != null) adButton.onClick.AddListener(OnAds);
            if (offerButton != null) offerButton.onClick.AddListener(OnOffer);
            if (upgradeButton != null) upgradeButton.onClick.AddListener(OnUpgrades);
            if (rateButton != null) rateButton.onClick.AddListener(OnRate);
            if (boostButton != null) boostButton.onClick.AddListener(OnBoost);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);

            // Awake'ten eklenen acicilar zaten sirada; kurgulanmis dugmeler onlarin sagina
            // giriyor ve sira bir kez ortalaniyor.
            if (bottomRow != null)
                for (int i = 0; i < bottomRow.Length; i++) InsertBottom(AuthoredOrder + i, bottomRow[i]);
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
            LayoutBottomRow();
            if (!compactShipyardHud)
            {
                BuildObjectiveStrip();
                BuildLadder();
            }

            if (_wallet != null) _wallet.GemsChanged += RefreshGems;
            RefreshGems();
            Refresh();

            // HUD hiç açılıp kapanmaz — sadece tıklama sesi, whoosh yok.
            UiPanelSound.AttachButtonsOnly(gameObject);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.GemsChanged -= RefreshGems;
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
            // Compact mode rejects the old collection of secondary openers, but sea combat is a
            // primary loop in the new five-station game and keeps its dedicated rail button.
            if (compactShipyardHud && name != SailButtonName) return null;
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

            int count = 0;
            for (int i = 0; i < _bottomRects.Count; i++) if (_bottomRects[i] != null) count++;
            if (count == 0) return;

            float height = RailHeight();
            // The band the button CENTRES may occupy. The pills are pinned to the top of the same
            // rect, so the rail starts under them rather than at the screen edge; half a pitch comes
            // off each end so the first and last buttons sit inside the band rather than straddling it.
            float top = height * 0.5f - railTopReserve - railPitch * 0.5f;
            float bottom = -height * 0.5f + railPitch * 0.5f;
            float band = Mathf.Max(railPitch, top - bottom);
            float centre = (top + bottom) * 0.5f;

            int perColumn = Mathf.Max(1, Mathf.FloorToInt(band / railPitch) + 1);
            int columns = Mathf.CeilToInt(count / (float)perColumn);
            _railWidth = railInset + (columns - 1) * railPitch + railPitch * 0.5f;
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
                float columnSpan = (inColumn - 1) * railPitch;

                rect.anchorMin = rect.anchorMax = new Vector2(edge, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(dir * (railInset + column * railPitch),
                                                    centre + columnSpan * 0.5f - row * railPitch);
                placed++;
            }

            // The objective strip is measured against the rail, so a late opener that adds a column
            // has to push it over rather than end up underneath it.
            SolveTopStrip(transform as RectTransform);
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
            return chip;
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

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null || !_op.enabled) BindEnabledOp();
            if (_topStrip != null)
            {
                var sheet = transform as RectTransform;
                if (sheet != null && sheet.rect.size != _sheetSize) SolveTopStrip(sheet);
            }
            if (_contract != null) _contract.Tick(Time.deltaTime, IncomePerMinute());
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
            return _op != null ? _op.CashPerMinute : 0d;
        }

        private void Refresh()
        {
            if (rateValue != null && _op != null)
                rateValue.text = string.Format(Loc.T("ortak.dakika_basina"),
                                               "$" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)));
            if (contractTimerValue != null && _contract != null) contractTimerValue.text = ContractChip();

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

        private static readonly Color DimBoost = new Color(0.55f, 0.58f, 0.66f, 1f);

        // ---- what the tutorial points at -------------------------------------------------------
        // Read-only rects, so the onboarding can cut a hole over a real control instead of drawing a
        // copy of it somewhere and hoping the two stay in the same place. Nothing here can move a
        // button; the screen the player taps is still this one's.
        public RectTransform UpgradeRect => Rect(upgradeButton);
        public RectTransform ContractRect => Rect(contractButton);
        public RectTransform BoostRect => Rect(boostButton);
        public RectTransform DailyRect => Rect(dailyButton);
        public RectTransform MapRect => Rect(mapButton);
        public RectTransform GoldRect => Rect(goldButton);
        public RectTransform SettingsRect => Rect(settingsButton);
        public RectTransform StoreRect => Rect(storeButton);
        public RectTransform AdRect => Rect(adButton);
        public RectTransform OfferRect => Rect(offerButton);
        /// <summary>The $/min pill, not the label inside it — the highlight has to sit on the art.</summary>
        public RectTransform RateRect
        {
            get
            {
                if (rateValue == null) return null;
                var parent = rateValue.transform.parent as RectTransform;
                return parent != null ? parent : (RectTransform)rateValue.transform;
            }
        }

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

        private void OnMap()
        {
            if (islandMap != null) islandMap.ToggleMap();
        }

        private void OnUpgrades()
        {
            if (stationScreen != null) stationScreen.Open();
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
            if (contractScreen != null) contractScreen.Toggle();
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
    }
}
