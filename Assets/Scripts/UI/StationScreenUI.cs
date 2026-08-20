using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The upgrade screen. Every station on the island has a page here, and the strip of icons above the
    /// model is how you get between them.
    ///
    /// Upgrades used to be one long scrolling list behind a button in the corner, which hid the only
    /// thing the money is really buying — the building growing. Here what is being bought fills the top
    /// of the screen on a slowly swaying turntable while its levels slide up from the bottom like a
    /// keyboard, and the strip says what else there is without having to go back anywhere. The last tile
    /// is the one-time expansions: they buy ground rather than levels, so that page drops the model and
    /// the phase bar and lets the tray grow into the space instead.
    ///
    /// There is no close button. The screen covers the island, so the way out is to tap the island.
    ///
    /// Two moments are animated, and they are deliberately different sizes. A level is a punch: the
    /// model squashes, a ring goes out, the bar ticks up, half a second and you can buy the next one.
    /// A phase is a rebuild: the tray drops out of the way, the old building sinks into the ground, and
    /// the new one rises through the flash it left behind. That one is allowed to take its time, because
    /// it happens twice per building in the life of an island.
    ///
    /// The model is not art in a sprite — it is a clone of the island's own district geometry, shot in
    /// <see cref="StationPreviewStage"/>. What the player is looking at is what they are about to walk
    /// back out to.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class StationScreenUI : MonoBehaviour
    {
        /// <summary>One building this screen owns. Its index is a <see cref="CoalOperation"/> station.</summary>
        [Serializable]
        public sealed class StationEntry
        {
            [Tooltip("CoalOperation istasyon indeksi — 0 maden, 2 depo, 4 izabe, 6 pazar.")]
            public int station;
            [Tooltip("Başlıkta yazacak ad. Boş bırakılırsa istasyonun kendi adı kullanılır.")]
            public string title = "";
            public Sprite icon;
        }

        [Header("Ekran (UI_IstasyonEkrani prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Arkadaki karartma. Üstündeki Button ekranı kapatır — bu ekranın kapatma tuşu yok, " +
                 "boşluğa basmak çıkarır.")]
        [SerializeField] private Image dim;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image titleIcon;
        [Tooltip("Cepteki altın. Bu ekran HUD'un üstünü örtüyor, oysa fiyatlara bakarken bakiyeyi " +
                 "görmek tam da burada gerekiyor.")]
        [SerializeField] private TMP_Text goldValue;

        [Header("Model sahnesi")]
        [SerializeField] private StationPreviewStage stage;
        [SerializeField] private RawImage modelView;
        [SerializeField] private RectTransform stageFrame;
        [Tooltip("Model sahnesinin ve faz çubuğunun kökleri. Genişletmeler sayfasında ikisi de kapanır " +
                 "ve tepsi yukarı büyüyüp yerlerini alır — genişletmelerin ne modeli ne fazı var.")]
        [SerializeField] private GameObject stageGroup;
        [SerializeField] private GameObject phaseGroup;

        [Header("Faz göstergesi")]
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private Image phaseFill;
        [SerializeField] private TMP_Text phaseHint;
        [SerializeField] private Image[] phasePips;
        [SerializeField] private Sprite pipFull;
        [SerializeField] private Sprite pipEmpty;

        [Header("Tepsi")]
        [SerializeField] private RectTransform sheet;
        [SerializeField] private RectTransform sheetContent;
        [Tooltip("Pasif şablon: yükseltme kartı (İkon/Ad/Seviye/BtnFiyat/Rozet).")]
        [SerializeField] private GameObject cardTemplate;

        [Header("Yükseltme şeridi")]
        [Tooltip("İkon yuvalarının dizildiği satır — yuvalar buraya klonlanır.")]
        [SerializeField] private RectTransform stripContent;
        [Tooltip("Pasif şablon: tek bir ikon yuvası (Ikon çocuğuyla).")]
        [SerializeField] private GameObject stripTemplate;
        [Tooltip("Seçili olmayan yuvanın rengi. Seçili olan beyaz ve bir tık büyük çizilir.")]
        [SerializeField] private Color slotIdleTint = new Color(0.70f, 0.77f, 0.88f, 0.62f);
        [Tooltip("Şeridin sonundaki genişletmeler yuvasının ikonu. Boş bırakılırsa o yuva hiç kurulmaz " +
                 "ve tek seferlik satın alımlara ulaşacak bir kapı kalmaz.")]
        [SerializeField] private Sprite expansionIcon;
        [Tooltip("Şeridin en sonundaki ZİNCİR sayfasının ikonu — adanın dakikada ne ürettiğini gösteren " +
                 "rapor. Boş bırakılırsa yuva kurulmaz; HUD'daki $/dk hapı yine de bu sayfayı açar.")]
        [SerializeField] private Sprite reportIcon;

        [Header("Genişletmeler")]
        [Tooltip("Tek seferlik satın alımların ikonları, kilit sırasıyla. Eksik bırakılan yuva ikonsuz kalır.")]
        [SerializeField] private Sprite[] unlockIcons;

        [Header("Durum görselleri")]
        [SerializeField] private Sprite priceGreen;
        [SerializeField] private Sprite priceGrey;
        [SerializeField] private Sprite badgeMax;
        [Tooltip("rozet_tamam — tek seferlik satın alım yapıldı.")]
        [SerializeField] private Sprite badgeBuilt;

        [Header("Efekt")]
        [Tooltip("Satın alımda modelin tabanından yayılan halka.")]
        [SerializeField] private Image halo;
        [Tooltip("Faz değişiminde sahneyi basan beyazlık.")]
        [SerializeField] private Image flash;
        [Tooltip("Yeni bina yükselirken üstünden geçen parlama.")]
        [SerializeField] private RectTransform shine;
        [SerializeField] private RectTransform phaseBanner;
        [SerializeField] private TMP_Text phaseBannerText;
        [Tooltip("Faz atlayınca patlayan konfeti. Kendi canvas'ında durur; boşsa sessizce atlanır.")]
        [SerializeField] private ConfettiBurst confetti;

        [Header("İstasyonlar")]
        [SerializeField] private List<StationEntry> stations = new List<StationEntry>();
        [SerializeField] private float refreshInterval = 0.25f;

        private sealed class Row
        {
            public int axis;
            public int unlock = -1;       // unlock row when >= 0, axis row otherwise
            public RectTransform icon;
            public TMP_Text name, level, detail, price, badgeText;
            public Button buyBtn;
            public Image buyImg, badge;
            public GameObject buyGO, badgeGO, lockGO;
        }

        private readonly List<Row> _rows = new List<Row>();
        private Image[] _slots;
        private Image[] _slotIcons;
        private int[] _slotPage;      // which page each tile opens — a station index, or one of the two virtual pages
        private ScrollRect _scroll;
        private float _cardHeight = 300f;   // fallback only — Awake reads the template's own LayoutElement
        private WalletService _wallet;
        private CoalOperation _op;
        private Transform _model;
        private Vector2 _sheetHome;
        private Vector2 _bannerHome;
        private LetterboxRoot _letterbox;
        private float _dimAlpha = 0.8f;
        private float _timer;
        private int _station = -1;
        private int _builtFor = -1;
        private bool _busy;
        private bool _landscapeLayout;
        private RectTransform _titleRibbon;
        private RectTransform _goldPill;
        private readonly Vector2 _stationSheetHome = new Vector2(500f, -360f);
        private readonly Vector2 _listSheetHome = new Vector2(0f, -420f);

        private void Awake()
        {
            ApplyLandscapeLayout();
            // Down, not up: this script sits on the prefab root and the letterbox is inside Pencere.
            _letterbox = GetComponentInChildren<LetterboxRoot>(true);
            if (sheet != null) { _sheetHome = sheet.anchoredPosition; _scroll = sheet.GetComponent<ScrollRect>(); }
            // Read, not assumed to be zero: in landscape LetterboxRoot has already folded the sheet into
            // two columns by now, so the banner's resting x is wherever its column put it.
            if (phaseBanner != null) _bannerHome = phaseBanner.anchoredPosition;
            if (dim != null) _dimAlpha = dim.color.a;
            if (cardTemplate != null)
            {
                var le = cardTemplate.GetComponent<LayoutElement>();
                if (le != null && le.preferredHeight > 0f) _cardHeight = le.preferredHeight;
                cardTemplate.SetActive(false);
            }
            HideFx();
        }

        private void ApplyLandscapeLayout()
        {
            if (Screen.width <= Screen.height || sheet == null) return;
            _landscapeLayout = true;
            _titleRibbon = titleText != null ? titleText.rectTransform.parent as RectTransform : null;
            _goldPill = goldValue != null ? goldValue.rectTransform.parent as RectTransform : null;

            // The selector is one centred header, not part of either content column: ribbon first,
            // all ten page buttons below it, then the balance. This also leaves both columns with
            // an identical, quiet top edge instead of making the left side look like a second panel.
            SetCentered(_titleRibbon, new Vector2(0f, 365f), new Vector2(660f, 150f));
            SetCentered(stripContent, new Vector2(0f, 245f), new Vector2(860f, 108f));
            SetCentered(_goldPill, new Vector2(0f, 140f), new Vector2(300f, 86f));

            // Landscape titles use the ribbon only for text. Detailed station art already appears in
            // the selector row and the preview; repeating it here steals the width long translations
            // need and leaves icons stranded on the ribbon tails.
            if (titleIcon != null)
            {
                titleIcon.enabled = false;
                titleIcon.gameObject.SetActive(false);
            }
            if (titleText != null)
            {
                SetCentered(titleText.rectTransform, new Vector2(0f, -6f), new Vector2(540f, 100f));
                titleText.enableAutoSizing = true;
                titleText.fontSizeMin = 18f;
                titleText.fontSizeMax = 54f;
                titleText.textWrappingMode = TextWrappingModes.NoWrap;
                titleText.overflowMode = TextOverflowModes.Ellipsis;
                titleText.alignment = TextAlignmentOptions.Center;
            }
            if (_titleRibbon != null && _titleRibbon.GetComponent<RectMask2D>() == null)
                _titleRibbon.gameObject.AddComponent<RectMask2D>();

            // Ten compact selectors fit on one line without touching. The selected one still gets
            // its 1.12x emphasis, but remains inside the row instead of growing over its neighbours.
            if (stripTemplate != null)
            {
                RectTransform slotRect = stripTemplate.transform as RectTransform;
                if (slotRect != null) slotRect.sizeDelta = new Vector2(74f, 74f);
                Transform slotIcon = stripTemplate.transform.Find("Ikon");
                RectTransform slotIconRect = slotIcon != null ? slotIcon as RectTransform : null;
                if (slotIconRect != null) slotIconRect.sizeDelta = new Vector2(56f, 56f);
            }

            // Preview on the left, purchase sheet on the right. The phase meter belongs to the
            // building it describes, so it sits directly below the preview rather than above the
            // upgrade cards. Its authored children are 900 units wide; scaling the group preserves
            // their spacing while fitting the narrower left column.
            SetCentered(stageGroup != null ? stageGroup.transform as RectTransform : null,
                        new Vector2(-500f, -100f), new Vector2(760f, 520f));
            RectTransform phaseRect = phaseGroup != null ? phaseGroup.transform as RectTransform : null;
            SetCentered(phaseRect, new Vector2(-500f, -430f), new Vector2(900f, 150f));
            if (phaseRect != null) phaseRect.localScale = Vector3.one * 0.82f;
            SetBottom(sheet, _stationSheetHome, new Vector2(760f, 520f));
            SetCentered(phaseBanner, new Vector2(-500f, -100f), new Vector2(720f, 170f));

            // Two compact 220-high cards fill the same-height panel as the preview without forcing
            // text or price controls to overlap; their child anchors already fit this shorter card.
            if (cardTemplate != null)
            {
                LayoutElement cardLayout = cardTemplate.GetComponent<LayoutElement>();
                if (cardLayout != null) cardLayout.preferredHeight = LandscapeCardHeight;
            }
            VerticalLayoutGroup cardGroup = sheetContent != null
                ? sheetContent.GetComponent<VerticalLayoutGroup>() : null;
            if (cardGroup != null) cardGroup.spacing = 12f;
        }

        private static void SetCentered(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetBottom(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            BindEnabledOp();
            if (dim != null)
            {
                var outside = dim.GetComponent<Button>();
                if (outside != null) outside.onClick.AddListener(Hide);
            }
            BuildStrip();
            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void Sound(SoundId id)
        {
            Bind();
            if (_audio != null) _audio.Play(id);
        }

        private void Bind()
        {
            if (_audio == null) _audio = ServiceLocator.Get<AudioService>();
            if (_haptic == null) _haptic = ServiceLocator.Get<HapticService>();
        }

        private AudioService _audio;
        private HapticService _haptic;

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null || !_op.enabled) BindEnabledOp();
            if (panelRoot == null || !panelRoot.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        /// <summary>Whether the screen is up, and which upgrade it is showing — what the tutorial waits on.</summary>
        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        public int OpenStation => _station;

        /// <summary>
        /// The price button for one axis of the open page, so the onboarding can cut its hole over the
        /// real control. Null while that row is maxed, locked, or the tray has not been rebuilt yet.
        /// </summary>
        public RectTransform BuyRect(int axis)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.unlock >= 0 || r.axis != axis) continue;
                if (r.buyGO == null || !r.buyGO.activeInHierarchy) return null;
                return (RectTransform)r.buyGO.transform;
            }
            return null;
        }

        /// <summary>Whether this screen owns a station — what the map chips and the old panel ask.</summary>
        public bool Handles(int station)
        {
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station) return true;
            return false;
        }

        /// <summary>
        /// Opens on whichever upgrade was looked at last — what the HUD's UPGRADES button does. The screen
        /// is the whole upgrade list now, so it has to open somewhere, and the place the player left is a
        /// better guess than any fixed one: buying is done in runs on one building.
        /// </summary>
        public void Open()
        {
            Open(_station != -1 ? _station : (stations.Count > 0 ? stations[0].station : 0));
        }

        /// <summary>
        /// Opens straight onto the chain report — what the HUD's $/min pill does. The pill states a
        /// number and used to be the end of the conversation; this is the rest of the sentence.
        /// </summary>
        public void OpenReport() => Open(ReportPage);

        /// <summary>Opens the screen on one upgrade.</summary>
        public void Open(int station)
        {
            if (station >= 0 && !Handles(station)) return;
            if (_op == null) BindEnabledOp();
            if (_op == null || panelRoot == null) return;

            StopAllCoroutines();
            _busy = false;
            _station = station;
            panelRoot.SetActive(true);
            ApplyPage();
            BuildCards();
            MountModel();
            Refresh();
            PaintStrip();
            StartCoroutine(OpenAnim());
        }

        /// <summary>
        /// Switches to another upgrade without replaying the open — what a tap on the strip does. The tray
        /// drops out from under the cursor and comes back, so a new set of cards reads as new rather than
        /// as the same rows with different words on them.
        /// </summary>
        private void Select(int station)
        {
            if (_busy || station == _station) return;
            if (station >= 0 && !Handles(station)) return;
            _station = station;
            ApplyPage();
            BuildCards();
            MountModel();
            Refresh();
            PaintStrip();
            StopAllCoroutines();
            StartCoroutine(SwapAnim());
        }

        /// <summary>
        /// Turns the screen into whichever of its two shapes the current page needs. The expansions have
        /// no building and no phase, so both of those leave and the tray takes the room they were using —
        /// the same sheet, further up, rather than a second screen on top of this one.
        ///
        /// The islands past copper are generated rather than authored: they have no phase art at all, so
        /// there is no building to photograph and no rebuild to count toward. They take the same shape.
        /// Leaving the studio up on those islands is what an empty picture frame in the middle of the
        /// screen was — the player reads a missing model, not an island that never had one.
        /// </summary>
        private void ApplyPage()
        {
            // The chain report takes the same shape as the expansions for the same reason: it is a
            // list about the whole island, so there is no one building to photograph above it.
            bool listPage = _station < 0 || Phases == null;
            if (stageGroup != null) stageGroup.SetActive(!listPage);
            if (phaseGroup != null) phaseGroup.SetActive(!listPage);
            if (sheet == null) return;

            if (_landscapeLayout)
            {
                Vector2 sheetHome = listPage ? _listSheetHome : _stationSheetHome;
                sheet.anchoredPosition = sheetHome;
                _sheetHome = sheetHome;
                sheet.sizeDelta = new Vector2(listPage ? LandscapeListPanelWidth : 760f, sheet.sizeDelta.y);
                SetCentered(_titleRibbon, new Vector2(0f, 365f), new Vector2(660f, 150f));
                SetCentered(stripContent, new Vector2(0f, 245f), new Vector2(860f, 108f));
                SetCentered(_goldPill, new Vector2(0f, 140f), new Vector2(300f, 86f));
            }

            float height = _landscapeLayout
                ? (listPage ? LandscapeListPanelHeight : LandscapePanelHeight)
                : SheetStationHeight;
            if (listPage)
            {
                // Against what is on screen, not against the parent. The parent is the full 2340-tall
                // design sheet however little of it the screen is actually showing, so in landscape —
                // where the sheet is scaled down and folded into columns — sizing off it grows the tray
                // to more than twice the height there is, and since the tray's pivot is its bottom edge
                // the extra goes straight up off the top of the screen.
                if (!_landscapeLayout)
                {
                    float room = _letterbox != null ? _letterbox.VisibleHeight
                                                    : (sheet.parent as RectTransform)?.rect.height ?? 0f;
                    if (room > 0f) height = Mathf.Max(SheetStationHeight, room - ExpansionTop - SheetBottom);
                }
            }
            sheet.sizeDelta = new Vector2(sheet.sizeDelta.x, height);
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        public void Hide()
        {
            if (_busy) return;
            StopAllCoroutines();
            HideFx();
            if (stage != null) { stage.Live = false; stage.Clear(); }
            _model = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            BindPhases();
        }

        private void OnDisable()
        {
            if (stage != null) stage.Live = false;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
            if (_phases != null) { _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted; _phases = null; }
        }

        /// <summary>
        /// Listens to the island the screen is bound to. This object stays alive whether the screen is
        /// open or shut — only its window is switched off — so this is where the phase fanfare belongs:
        /// a rebuild is heard whichever screen the purchase came from, and it follows the player across
        /// islands instead of staying wired to whichever controller happened to be found first.
        /// </summary>
        private void BindPhases()
        {
            Kayseri.Island.IslandPhaseController next = _op != null ? _op.Phases : null;
            if (next == _phases) return;
            if (_phases != null) _phases.PhaseRefreshCompleted -= OnPhaseRefreshCompleted;
            _phases = next;
            if (_phases != null) _phases.PhaseRefreshCompleted += OnPhaseRefreshCompleted;
        }

        private void OnPhaseRefreshCompleted()
        {
            // Açılışta değil. Ada önce faz 1'de kuruluyor, sonra kayıttaki seviyeler uygulanınca
            // bulunduğu faza sıçrıyor; bu da bölge bölge PhaseChanged olarak rapor ediliyor. Oyuncu
            // hiçbir şey yapmadan fanfar duyardı. Ölçüldü: sahne yüklendikten ~1 sn sonra oluyor.
            if (Time.timeSinceLevelLoad < 3f) return;
            Sound(SoundId.PhaseUp);
            if (_haptic != null) _haptic.Heavy();
        }

        private Kayseri.Island.IslandPhaseController _phases;

        /// <summary>Axis names are stamped onto the cards as they are cloned, so the next open has to
        /// clone them again.</summary>
        private void OnLanguageChanged()
        {
            _builtFor = -1;
            if (panelRoot != null && panelRoot.activeSelf) { BuildCards(); Refresh(); }
        }

        private LocalizationService _loc;

        /// <summary>Retarget at another island's operation — the catalog is identical everywhere.</summary>
        public void SetOperation(CoalOperation op)
        {
            if (op == null || op == _op) return;
            _op = op;
            _builtFor = -1;                       // axis names are shared, but the model is not
            BindPhases();
            if (panelRoot != null && panelRoot.activeSelf) Hide();
        }

        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { SetOperation(ops[i]); return; }
        }

        // ---------- the building ----------
        private Kayseri.Island.IslandPhaseController Phases
        {
            get { return _op != null ? _op.Phases : null; }
        }

        private int CurrentPhase()
        {
            var ph = Phases;
            if (ph == null || _station < 0) return 1;
            return ph.PhaseForStation(_op.StationName(_station));
        }

        /// <summary>
        /// Puts one phase's building on the turntable. The geometry is cloned from the prefab, because
        /// the island's own copy is welded into a static batch and cannot be moved; its size, though,
        /// has to be read off that scene copy, since the batching is also the only thing that ever gave
        /// those meshes correct bounds.
        /// </summary>
        private Transform MountPhase(int phase)
        {
            var ph = Phases;
            if (stage == null || ph == null) return null;
            string station = _op.StationName(_station);
            Transform template = ph.DistrictModel(station, phase);
            if (template == null) return null;
            return stage.Mount(template, LocalBounds(ph.DistrictArt(station, phase)));
        }

        private static Bounds LocalBounds(Transform district)
        {
            if (district == null) return new Bounds(Vector3.zero, Vector3.one * 40f);
            var rs = district.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 40f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            b.center -= district.position;      // districts sit unrotated and unscaled on the island root
            return b;
        }

        private void MountModel()
        {
            if (stage == null) return;
            stage.Clear();
            _model = null;
            if (_station < 0 || Phases == null) { stage.Live = false; return; }
            stage.Zoom = 1f;
            stage.Live = true;
            if (modelView != null) modelView.texture = stage.Texture;
            _model = MountPhase(CurrentPhase());
        }

        // ---------- construction ----------
        private void BuildCards()
        {
            if (_builtFor == _station) return;
            if (sheetContent == null || cardTemplate == null || _op == null) return;
            _builtFor = _station;

            for (int i = sheetContent.childCount - 1; i >= 0; i--)
            {
                GameObject child = sheetContent.GetChild(i).gameObject;
                if (child != cardTemplate) Destroy(child);
            }
            _rows.Clear();

            if (_station == ExpansionPage) BuildUnlockCards();
            else if (_station == ReportPage) BuildReportCards();
            else BuildAxisCards();
            CentreCards();
        }

        private void BuildAxisCards()
        {
            for (int a = 0; a < _op.AxisCount(_station); a++)
            {
                Row row = AddCard("Kart_" + _op.AxisName(_station, a), IconFor(_station));
                row.axis = a;
                if (row.name != null) row.name.text = Loc.Id("eksen", _op.AxisName(_station, a));
                if (row.buyBtn != null)
                {
                    Row captured = row;
                    row.buyBtn.onClick.AddListener(() => Buy(captured));
                }
            }
        }

        /// <summary>
        /// The one-time purchases, as cards in the same tray. They are not levels and they are not a
        /// station, so they carry no phase and no model — what a player wants from this page is the list
        /// and the prices, which is exactly what the tray already is.
        /// </summary>
        private void BuildUnlockCards()
        {
            for (int u = 0; u < _op.UnlockCount; u++)
            {
                Sprite icon = unlockIcons != null && u < unlockIcons.Length ? unlockIcons[u] : expansionIcon;
                Row row = AddCard("Kart_" + _op.UnlockName(u), icon);
                row.unlock = u;
                // Başlık ve etkisi tabloda iki ayrı satır ("kilit.5" / "kilit.5.not"), tek bir dizede
                // parantezle değil — bir çevirmen noktalama korumak zorunda kalmasın diye.
                if (row.name != null)
                    row.name.text = string.Format(Loc.T("kilit." + u), Loc.Id("cevher", _op.IslandKey));
                if (row.buyBtn != null)
                {
                    Row captured = row;
                    row.buyBtn.onClick.AddListener(() => Buy(captured));
                }
            }
        }

        /// <summary>
        /// The chain, as seven cards. Ore is followed from the mountain to the counter, one row per
        /// stage, each saying what it is MANAGING rather than what it is capable of — the two are
        /// only the same number on an island with no bottleneck, and finding the bottleneck is the
        /// entire reason a player opens this page.
        ///
        /// It buys no levels, so every row is a card with its price button switched off. That is not
        /// a compromise: the strip is two taps from every station on it, so a report that named the
        /// wall AND sold the fix would be a second, worse copy of the screen it is already inside.
        /// </summary>
        private void BuildReportCards()
        {
            for (int i = 0; i < ReportStations.Length; i++)
            {
                int st = ReportStations[i];
                Row row = AddCard("Kart_Zincir" + i, st >= 0 ? IconFor(st) : reportIcon);
                if (row.name != null)
                    row.name.text = st >= 0 ? Loc.Id("istasyon", _op.StationName(st)) : Loc.T("rapor.toplam");
            }
        }

        /// <summary>Which station each report row is about; -1 is the total at the foot of the list.</summary>
        private static readonly int[] ReportStations =
        {
            IslandEconomy.Mine, IslandEconomy.Storage, IslandEconomy.OreTrucks,
            IslandEconomy.Smelter, IslandEconomy.CargoTrucks, IslandEconomy.Market, -1,
        };

        private void RefreshReport()
        {
            IslandEconomy e = _op.Economy;
            if (e == null) return;
            int wall = Bottleneck();
            string ore = Loc.T("rapor.cevher_dk");
            string bars = Loc.T("rapor.kulce_dk");

            for (int i = 0; i < _rows.Count && i < ReportStations.Length; i++)
            {
                Row r = _rows[i];
                SetRow(r, false, false, false);
                string rate, note;

                switch (i)
                {
                    case 0:   // MINE — ore out of the mountains
                        rate = string.Format(ore, Whole(_op.OreMinedPerMinute));
                        note = string.Format(Loc.T("rapor.maden_not"), Tenth(e.TrainOre), e.ActiveWagons);
                        break;
                    case 1:   // STORAGE — the buffer that stops the trains when it fills
                        rate = string.Format(Loc.T("rapor.depo_dolu"),
                                             Whole(_op.StorageOre), Whole(e.StorageFull));
                        note = Loc.T("rapor.depo_not");
                        break;
                    case 2:   // ORE TRUCKS — yard to furnace
                        rate = string.Format(ore, Whole(_op.OreHauledPerMinute));
                        note = string.Format(Loc.T("rapor.filo_not"), e.OreTruckCount, Tenth(e.OreTruckLoad));
                        break;
                    case 3:   // SMELTER — ore becomes bars, one for one
                        rate = string.Format(bars, Whole(_op.BarsRefinedPerMinute));
                        note = string.Format(Loc.T("rapor.izabe_not"),
                                             Whole(e.SmeltRate * 60f), Whole(_op.RefineQueue));
                        break;
                    case 4:   // CARGO TRUCKS — furnace to the market's pads
                        rate = string.Format(bars, Whole(_op.BarsDeliveredPerMinute));
                        note = string.Format(Loc.T("rapor.filo_not"), e.CargoTruckCount, Tenth(e.CargoTruckLoad));
                        break;
                    case 5:   // MARKET — the only place cash enters the game
                        rate = string.Format(Loc.T("ortak.dakika_basina"),
                                             "$" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)));
                        note = string.Format(Loc.T("rapor.pazar_not"),
                                             "$" + NumberFormatter.Format(new BigDouble(e.BarPrice)),
                                             Whole(MarketStock()));
                        break;
                    default:  // the line under the sum
                        rate = string.Format(Loc.T("ortak.dakika_basina"),
                                             "$" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)));
                        note = Multipliers();
                        break;
                }

                if (r.level != null) r.level.text = rate;
                if (r.detail != null)
                    r.detail.text = i == wall
                        ? "<color=#" + WarnHex + ">" + Loc.T("rapor.darbogaz") + "</color>  " + note
                        : note;
            }
        }

        /// <summary>
        /// Which stage everything else is waiting on.
        ///
        /// Not the slowest measured rate — in a chain at rest every stage measures the SAME rate,
        /// because the wall sets the pace for everything behind it. What gives the wall away is the
        /// buffer in front of it: ore piling up in the yard means the trucks cannot clear it, bars
        /// at the furnace's ceiling means the cargo fleet cannot. So this reads the chain from the
        /// market backwards and stops at the first full pile, which is the real one — a yard is
        /// only full because the leg after it is the bottleneck, never because of anything upstream.
        ///
        /// "Full" is measured in seconds rather than sampled here: this screen refreshes four times
        /// a second and every pile on the island is a sawtooth, so the level at the moment of asking
        /// is as likely to be a trough as a ceiling. The operation keeps the clocks.
        ///
        /// Nothing backed up anywhere means the island is supply-limited, and the mine is the wall.
        /// </summary>
        private int Bottleneck()
        {
            IslandEconomy e = _op.Economy;
            if (e == null) return ProductionBottleneck.Unknown;

            if (_market == null) _market = ServiceLocator.Get<MarketService>();

            return ProductionBottleneck.Find(
                _op.FlowReady,
                _op.OreMinedPerMinute,
                _op.OreHauledPerMinute,
                _op.BarsRefinedPerMinute,
                _op.BarsDeliveredPerMinute,
                _op.YardFullSeconds,
                _op.FurnaceQueueSeconds,
                _op.BarStoreFullSeconds,
                _market != null ? _market.OverflowSeconds(_op.IslandKey) : 0d);
        }

        /// <summary>Bars waiting on the market's pads to be sold, or 0 before the yard has a reading.</summary>
        private double MarketStock()
        {
            if (_market == null) _market = ServiceLocator.Get<MarketService>();
            return _market != null ? _market.Stock(_op.IslandKey) : 0d;
        }

        /// <summary>
        /// What is multiplying the total, in the order it is applied. Investors survive a prestige
        /// and the ad boost does not, so a player looking at a doubled counter deserves to be told
        /// which half of it is temporary.
        /// </summary>
        private string Multipliers()
        {
            if (_boost == null) _boost = ServiceLocator.Get<BoostService>();

            double legacy = _market != null ? _market.LegacyIncomeMultiplier : 1d;
            double permanent = (_boost != null ? _boost.PermanentMultiplier : 1d) * legacy;
            double boost = _boost != null && _boost.IsActive ? _boost.ActiveMultiplier : 1d;
            if (permanent <= 1.0001d && boost <= 1.0001d)
                return Loc.T("rapor.carpan_yok");

            string line = "";
            if (permanent > 1.0001d)
            {
                if (line.Length > 0) line += "  ·  ";
                line += string.Format(Loc.T("rapor.kalici_hiz"), permanent.ToString("0.#", Culture));
            }
            if (boost > 1.0001d)
            {
                if (line.Length > 0) line += "  ·  ";
                line += "<color=#" + BrightHex + ">"
                      + string.Format(Loc.T("rapor.hizlandirma"), boost.ToString("0.#", Culture))
                      + "</color>";
            }
            return line;
        }

        private static string Whole(double v) => Mathf.RoundToInt((float)v).ToString(Culture);
        private static string Tenth(double v) => ((float)v).ToString("0.0", Culture);

        private MarketService _market;
        private BoostService _boost;

        private Row AddCard(string name, Sprite icon)
        {
            GameObject go = Instantiate(cardTemplate, sheetContent);
            go.name = name;
            go.SetActive(true);

            var row = new Row();
            Transform t = go.transform.Find("Ikon");
            if (t != null)
            {
                row.icon = t as RectTransform;
                Image img = t.GetComponent<Image>();
                if (img != null) { img.sprite = icon; img.enabled = icon != null; }
            }
            t = go.transform.Find("Ad");     if (t != null) row.name = t.GetComponent<TMP_Text>();
            t = go.transform.Find("Seviye"); if (t != null) row.level = t.GetComponent<TMP_Text>();
            t = go.transform.Find("Detay");  if (t != null) row.detail = t.GetComponent<TMP_Text>();
            t = go.transform.Find("BtnFiyat");
            if (t != null)
            {
                row.buyGO = t.gameObject;
                row.buyBtn = t.GetComponent<Button>();
                row.buyImg = t.GetComponent<Image>();
                Transform ft = t.Find("Fiyat");
                if (ft != null) row.price = ft.GetComponent<TMP_Text>();
            }
            t = go.transform.Find("Rozet");
            if (t != null)
            {
                row.badgeGO = t.gameObject;
                row.badge = t.GetComponent<Image>();
                Transform bt = t.Find("Yazi");
                if (bt != null) row.badgeText = bt.GetComponent<TMP_Text>();
            }
            t = go.transform.Find("Kilit");
            if (t != null) { row.lockGO = t.gameObject; t.gameObject.SetActive(false); }
            if (_landscapeLayout) LayoutLandscapeCard(row, _station < 0 || Phases == null);
            _rows.Add(row);
            return row;
        }

        /// <summary>
        /// Keeps every line inside the shorter landscape card. The old portrait offsets placed the
        /// detail beneath the card and gave the title only a narrow column, which is why Turkish words
        /// split in half and the next panel covered the description.
        /// </summary>
    private static void LayoutLandscapeCard(Row row, bool wide)
    {
        if (row == null) return;

        if (wide)
        {
            SetLeftMiddle(row.icon, new Vector2(28f, 18f), new Vector2(82f, 82f));
            SetLeftMiddle(TextRect(row.name), new Vector2(138f, 48f), new Vector2(640f, 40f));
            SetLeftMiddle(TextRect(row.level), new Vector2(138f, 8f), new Vector2(640f, 32f));
            SetLeftMiddle(TextRect(row.detail), new Vector2(138f, -34f), new Vector2(640f, 34f));
            SetRightMiddle(ObjectRect(row.buyGO), new Vector2(-26f, 17f), new Vector2(250f, 102f));
            SetRightMiddle(ObjectRect(row.badgeGO), new Vector2(-26f, 17f), new Vector2(250f, 102f));
            FitCardText(row.name, 32f, 19f);
            FitCardText(row.level, 25f, 17f);
            FitCardText(row.detail, 22f, 16f);
            LayoutButtonText(row.price, 68f, 12f, 34f, 22f);
            LayoutButtonText(row.badgeText, 16f, 16f, 42f, 28f);
        }
        else
        {
            SetLeftMiddle(row.icon, new Vector2(20f, 18f), new Vector2(76f, 76f));
            SetLeftMiddle(TextRect(row.name), new Vector2(112f, 48f), new Vector2(270f, 40f));
            SetLeftMiddle(TextRect(row.level), new Vector2(112f, 8f), new Vector2(270f, 32f));
            SetLeftMiddle(TextRect(row.detail), new Vector2(112f, -34f), new Vector2(270f, 34f));
            SetRightMiddle(ObjectRect(row.buyGO), new Vector2(-18f, 17f), new Vector2(220f, 102f));
            SetRightMiddle(ObjectRect(row.badgeGO), new Vector2(-18f, 17f), new Vector2(220f, 102f));
            FitCardText(row.name, 31f, 19f);
            FitCardText(row.level, 25f, 17f);
            FitCardText(row.detail, 21f, 15f);
            LayoutButtonText(row.price, 68f, 12f, 34f, 22f);
            LayoutButtonText(row.badgeText, 16f, 16f, 40f, 27f);
        }
    }

        private static RectTransform TextRect(TMP_Text text) => text != null ? text.rectTransform : null;
        private static RectTransform ObjectRect(GameObject go) => go != null ? go.transform as RectTransform : null;

        private static void SetLeftMiddle(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetRightMiddle(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

    private static void FitCardText(TMP_Text text, float maximum, float minimum)
    {
        if (text == null) return;
        text.enableAutoSizing = true;
            text.fontSizeMax = maximum;
            text.fontSizeMin = minimum;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void LayoutButtonText(TMP_Text text, float left, float right, float maximum, float minimum)
    {
        if (text == null) return;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, 10f);
        rect.offsetMax = new Vector2(-right, -10f);

        text.alignment = TextAlignmentOptions.Center;
        FitCardText(text, maximum, minimum);
    }

        /// <summary>
        /// Pushes a short list down so it sits in the middle of the tray instead of clinging to the top,
        /// and leaves a long one alone so it can scroll. Padding rather than child alignment: the size
        /// fitter measures the padding too, so the content still reports a height the scroll rect can use.
        /// </summary>
        private void CentreCards()
        {
            var group = sheetContent != null ? sheetContent.GetComponent<VerticalLayoutGroup>() : null;
            RectTransform view = _scroll != null ? _scroll.viewport : null;
            if (group == null || view == null || _rows.Count == 0) return;
            float cards = _rows.Count * _cardHeight + (_rows.Count - 1) * group.spacing;
            int pad = Mathf.Max(0, Mathf.RoundToInt((view.rect.height - cards) * 0.5f));
            if (group.padding.top == pad) return;
            group.padding.top = pad;
            LayoutRebuilder.MarkLayoutForRebuild(sheetContent);
        }

        /// <summary>
        /// The row of icons above the model — every upgrade on the island in one line, so the screen says
        /// what else there is to buy without being opened again. Built once: the catalog is the same on
        /// every island and the tiles carry no words, so neither travel nor a language change touches it.
        /// </summary>
        private void BuildStrip()
        {
            if (stripContent == null || stripTemplate == null) return;
            stripTemplate.SetActive(false);

            // The stations, then the two pages that are not stations: the one-time expansions, and the
            // chain report. Both are optional and both drop out silently when their icon is missing,
            // which is what keeps this list a page order rather than a set of special cases.
            int n = stations.Count + (expansionIcon != null ? 1 : 0) + (reportIcon != null ? 1 : 0);
            _slots = new Image[n];
            _slotIcons = new Image[n];
            _slotPage = new int[n];
            for (int i = 0; i < n; i++)
            {
                bool station = i < stations.Count;
                bool expansion = !station && expansionIcon != null && i == stations.Count;
                _slotPage[i] = station ? stations[i].station : expansion ? ExpansionPage : ReportPage;

                GameObject go = Instantiate(stripTemplate, stripContent);
                go.name = station ? "Yuva_" + stations[i].station
                                  : expansion ? "Yuva_Genisletmeler" : "Yuva_Zincir";
                go.SetActive(true);

                _slots[i] = go.GetComponent<Image>();
                Transform t = go.transform.Find("Ikon");
                if (t != null)
                {
                    _slotIcons[i] = t.GetComponent<Image>();
                    if (_slotIcons[i] != null)
                        _slotIcons[i].sprite = station ? stations[i].icon
                                             : expansion ? expansionIcon : reportIcon;
                }

                var btn = go.GetComponent<Button>();
                if (btn == null) continue;
                int captured = _slotPage[i];
                btn.onClick.AddListener(() => Select(captured));
            }
        }

        /// <summary>Marks which tile the screen is showing. The live one is full colour and a little larger;
        /// the rest sit back, so the strip reads as one selection rather than nine buttons.</summary>
        private void PaintStrip()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                bool live = _slotPage[i] == _station;
                if (_slots[i] != null)
                {
                    _slots[i].color = live ? Color.white : slotIdleTint;
                    _slots[i].rectTransform.localScale = Vector3.one * (live ? SlotLiveScale : 1f);
                }
                if (_slotIcons[i] != null) _slotIcons[i].color = live ? Color.white : SlotIdleIcon;
            }
        }

        private Sprite IconFor(int station)
        {
            if (station == ExpansionPage) return expansionIcon;
            if (station == ReportPage) return reportIcon;
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station) return stations[i].icon;
            return null;
        }

        private string TitleFor(int station)
        {
            if (station == ExpansionPage) return Loc.T("yukseltme.genisletmeler");
            if (station == ReportPage) return Loc.T("rapor.baslik");
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station && !string.IsNullOrEmpty(stations[i].title))
                    return stations[i].title;
            return _op != null ? Loc.Id("istasyon", _op.StationName(station)) : "";
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            // Bakiye istasyondan bağımsız — genişletmeler sayfasında da, operasyon bağlanmadan
            // önce de yazılmalı, o yüzden aşağıdaki erken çıkışların üstünde duruyor.
            if (goldValue != null && _wallet != null)
                goldValue.text = NumberFormatter.Format(_wallet.Cash);

            if (_op == null || _station == -1) return;

            if (titleText != null) titleText.text = TitleFor(_station);
            if (titleIcon != null)
            {
                titleIcon.sprite = IconFor(_station);
                titleIcon.enabled = !_landscapeLayout && titleIcon.sprite != null;
                if (_landscapeLayout && titleIcon.gameObject.activeSelf) titleIcon.gameObject.SetActive(false);
            }

            if (_station == ExpansionPage)
            {
                for (int i = 0; i < _rows.Count; i++) RefreshUnlock(_rows[i]);
                return;
            }

            if (_station == ReportPage) { RefreshReport(); return; }

            int phase = CurrentPhase();
            int cap = _op.StationLevelCap(_station);
            int lv = _op.StationLevelTotal(_station);
            float third = cap / 3f;
            float lo = (phase - 1) * third;
            float hi = phase < 3 ? phase * third : cap;
            float p = hi > lo ? Mathf.Clamp01((lv - lo) / (hi - lo)) : 1f;

            if (phaseText != null) phaseText.text = string.Format(Loc.T("istasyon_ekrani.faz"), phase);
            if (phaseFill != null) phaseFill.fillAmount = p;
            if (phaseHint != null)
            {
                int need = Mathf.Max(0, Mathf.CeilToInt(hi) - lv);
                phaseHint.text = phase < 3
                    ? string.Format(Loc.T("istasyon_ekrani.sonraki_faz"), need)
                    : (need > 0 ? string.Format(Loc.T("istasyon_ekrani.tam_dolu"), need)
                                : Loc.T("ortak.tamamlandi"));
            }
            if (phasePips != null)
                for (int i = 0; i < phasePips.Length; i++)
                {
                    if (phasePips[i] == null) continue;
                    Sprite s = i < phase ? pipFull : pipEmpty;
                    if (s != null) phasePips[i].sprite = s;
                }

            for (int i = 0; i < _rows.Count; i++) RefreshRow(_rows[i]);
        }

        private void RefreshRow(Row r)
        {
            // The power plant's levels do not exist until its ghost building is bought. This used to
            // fall through to the price branch, so the station showed live green buttons that TryUpgrade
            // refused — the one station on the island where spending did nothing. The row now says which
            // expansion opens it, and that expansion is two taps away on the strip.
            if (_op.AxisLocked(_station, r.axis))
            {
                SetRow(r, false, false, true);
                if (r.level != null)
                    r.level.text = string.Format(Loc.T("yukseltme.kilitli_ile"),
                                                 string.Format(Loc.T("kilit." + CoalOperation.UnlockPowerPlant),
                                                               Loc.Id("cevher", _op.IslandKey)));
                // A locked axis has a perfectly computable readout and showing it would be a lie by
                // arithmetic: the multiplier is real, it is just not applied to anything yet.
                if (r.detail != null) r.detail.text = "";
                return;
            }

            int lv = _op.AxisLevel(_station, r.axis);
            if (r.level != null) r.level.text = string.Format(Loc.T("yukseltme.seviye"), lv);
            if (r.detail != null) r.detail.text = AxisDetail(r.axis);

            if (_op.AxisMaxed(_station, r.axis))
            {
                SetRow(r, false, true, false);
                if (r.badge != null && badgeMax != null) r.badge.sprite = badgeMax;
                if (r.badgeText != null) r.badgeText.text = "MAX";
                return;
            }

            SetRow(r, true, false, false);
            BigDouble cost = _op.AxisCost(_station, r.axis);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford && !_busy;
        }

        private void RefreshUnlock(Row r)
        {
            if (_op.IsUnlocked(r.unlock))
            {
                SetRow(r, false, true, false);
                if (r.badge != null && badgeBuilt != null) r.badge.sprite = badgeBuilt;
                // Yeşil tik zaten "bitti" diyor; üstüne MAX yazmak ikinci bir söz olurdu.
                if (r.badgeText != null) r.badgeText.text = "";
                if (r.level != null) r.level.text = Loc.T("yukseltme.insa_edildi");
                if (r.detail != null) r.detail.text = UnlockDetail(r.unlock);
                return;
            }

            SetRow(r, true, false, false);
            // The effect moved down to the detail line, where it is read off the live tuning. This
            // line cannot also carry it: the shipping note said "2× smelt" where the tuning says
            // 1.25, so a card that showed both showed the player two different prices for the same
            // thing. What is left here is the one fact about an expansion that never goes stale.
            if (r.level != null) r.level.text = Loc.T("yukseltme.tek_seferlik");
            if (r.detail != null) r.detail.text = UnlockDetail(r.unlock);

            BigDouble cost = _op.UnlockCost(r.unlock);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford && !_busy;
        }

        // ---------- what a level is worth ----------
        //
        // A card used to say RICHNESS · Lv 12 · $1,847 and stop there, which meant the only way to
        // find out what a level of Richness bought was to buy one and watch the trains. The numbers
        // were never missing — they are the same ones the simulation runs on — they were simply
        // never asked for. Everything below asks IslandEconomy and writes down the answer; none of
        // it does arithmetic of its own, because a card that computes its own preview is a card that
        // will quietly disagree with the game the first time a coefficient moves.

        /// <summary>"Ore per trip  12.4 → 13.1" — what this axis moves, now and one level from now.</summary>
        private string AxisDetail(int axis)
        {
            IslandEconomy econ = _op.Economy;
            if (econ == null) return "";
            IslandEconomy.AxisReadout g = econ.Readout(_station, axis);
            string label = Loc.T("etki." + g.Key);
            string now = Stat(g.Now, g.Shape);
            // A maxed axis has no next level to promise, so it states what it is and stops. The
            // arrow is a sales pitch; there is nothing left to sell.
            if (_op.AxisMaxed(_station, axis)) return label + "  " + Bright(now);
            return label + "  " + Dim(now) + " " + Arrow + " " + Bright(Stat(g.Next, g.Shape));
        }

        /// <summary>
        /// What an expansion actually multiplies, read off the live tuning.
        ///
        /// This replaces nothing — it corrects something. The hand-written notes beside these cards
        /// still promised "2× smelt" and "+50% price" long after the tuning had settled on 1.25 and
        /// 1.20, so a player comparing two expansions was comparing two numbers that were not true.
        ///
        /// The ones that buy a building rather than a multiplier fall back to that note, because
        /// there is no figure to quote for them and "new upgrades" was never a claim about a rate.
        /// </summary>
        private string UnlockDetail(int unlock)
        {
            float bonus = _op.UnlockBonus(unlock);
            if (bonus > 1f)
                return string.Format(Loc.T("yukseltme.carpan"),
                                     bonus.ToString("0.00", Culture),
                                     Loc.T("etki." + UnlockEffect(unlock)));

            string key = "kilit." + unlock + ".not";
            string note = Loc.T(key);
            return note != key ? note : "";
        }

        /// <summary>Which figure an expansion's multiplier lands on — the same labels the axis cards use.</summary>
        private static string UnlockEffect(int unlock)
        {
            switch (unlock)
            {
                case CoalOperation.UnlockSecondSmelter: return "eritme_hiz";
                case CoalOperation.UnlockTradePost:     return "kulce_fiyat";
                case CoalOperation.UnlockWarehouse:     return "depo_kapasite";
                case CoalOperation.UnlockDepot:         return "tren_hiz";
                case CoalOperation.UnlockExportDock:    return "ihracat_yuk";
                default:                                return "tren_cevher";   // deep shaft
            }
        }

        /// <summary>One economy figure, written the way its own units want to be read.</summary>
        private static string Stat(float v, IslandEconomy.NumberShape shape)
        {
            switch (shape)
            {
                case IslandEconomy.NumberShape.Whole:
                    return Mathf.RoundToInt(v).ToString(Culture);
                case IslandEconomy.NumberShape.Money:
                    return "$" + NumberFormatter.Format(new BigDouble(v));
                case IslandEconomy.NumberShape.Times:
                    return "×" + v.ToString("0.00", Culture);
                case IslandEconomy.NumberShape.Seconds:
                    return v.ToString("0.00", Culture) + Loc.T("birim.saniye");
                case IslandEconomy.NumberShape.Speed:
                    // 0.0 rather than 0.#, or a level reads "20 → 20.3" and the pair looks like
                    // two different kinds of number instead of a before and an after.
                    return v.ToString("0.0", Culture) + Loc.T("birim.hiz");
                default:
                    return v.ToString("0.0", Culture);
            }
        }

        private static string Dim(string s) => "<color=#" + DimHex + ">" + s + "</color>";
        private static string Bright(string s) => "<color=#" + BrightHex + ">" + s + "</color>";

        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        private const string Arrow = "→";
        private const string DimHex = "8C96AC";      // what you already own
        private const string BrightHex = "7FE39B";   // what the price button buys
        private const string WarnHex = "FFC24D";     // the stage everything else is waiting on

        private static void SetRow(Row r, bool buy, bool badge, bool locked)
        {
            if (r.buyGO != null && r.buyGO.activeSelf != buy) r.buyGO.SetActive(buy);
            if (r.badgeGO != null && r.badgeGO.activeSelf != badge) r.badgeGO.SetActive(badge);
            if (r.lockGO != null && r.lockGO.activeSelf != locked) r.lockGO.SetActive(locked);
        }

        // ---------- buying ----------
        private void Buy(Row row)
        {
            if (_busy || _op == null) return;

            if (row.unlock >= 0)
            {
                if (!_op.TryUnlock(row.unlock)) { Sound(SoundId.Denied); return; }
                Sound(SoundId.Upgrade);
                Bind();
                if (_haptic != null) _haptic.Medium();
                Refresh();
                StartCoroutine(LevelPunch(row));
                return;
            }

            int before = CurrentPhase();
            if (!_op.TryUpgrade(_station, row.axis)) { Sound(SoundId.Denied); return; }
            Refresh();

            int after = CurrentPhase();
            if (after != before)
            {
                // Faz sesi PhaseSequence'in kendi içinde, yükselişin tepesinde çalar.
                StartCoroutine(PhaseSequence(after));
            }
            else
            {
                Sound(SoundId.Upgrade);
                if (_haptic != null) _haptic.Medium();
                StartCoroutine(LevelPunch(row));
            }
        }

        // ---------- animation ----------
        private IEnumerator OpenAnim()
        {
            float hidden = HiddenSheetY();
            if (sheet != null) sheet.anchoredPosition = new Vector2(_sheetHome.x, hidden);
            if (dim != null) SetAlpha(dim, 0f);
            if (stageFrame != null) stageFrame.localScale = Vector3.one * 0.94f;

            float t = 0f;
            while (t < OpenSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / OpenSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);        // the phone-keyboard curve: fast out, soft stop
                if (dim != null) SetAlpha(dim, _dimAlpha * Mathf.Clamp01(k * 2f));
                if (sheet != null) sheet.anchoredPosition = new Vector2(_sheetHome.x, Mathf.Lerp(hidden, _sheetHome.y, ease));
                if (stageFrame != null) stageFrame.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, ease);
                yield return null;
            }
            if (sheet != null) sheet.anchoredPosition = _sheetHome;
            if (dim != null) SetAlpha(dim, _dimAlpha);
            if (stageFrame != null) stageFrame.localScale = Vector3.one;
        }

        /// <summary>
        /// What one level looks like. Short on purpose — a player buying six in a row must never wait
        /// on it, so nothing here locks the buttons and the whole thing is over in half a second.
        /// </summary>
        private IEnumerator LevelPunch(Row row)
        {
            RectTransform btn = row.buyGO != null ? (RectTransform)row.buyGO.transform : null;
            RectTransform lvl = row.level != null ? row.level.rectTransform : null;
            if (halo != null) { halo.gameObject.SetActive(true); halo.rectTransform.localScale = Vector3.one * 0.3f; }

            float t = 0f;
            while (t < PunchSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PunchSeconds);
                float pop = Mathf.Sin(k * Mathf.PI);            // 0 → 1 → 0

                // A building squashes wider as it settles rather than swelling evenly — the same
                // shape a stamped foundation makes, which is what a bought level is supposed to be.
                if (_model != null)
                    _model.localScale = new Vector3(1f + pop * 0.055f, 1f + pop * 0.10f, 1f + pop * 0.055f);
                if (btn != null) btn.localScale = Vector3.one * (1f + pop * 0.13f);
                if (lvl != null) lvl.localScale = Vector3.one * (1f + pop * 0.18f);
                if (halo != null)
                {
                    halo.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.55f, k);
                    SetAlpha(halo, (1f - k) * 0.8f);
                }
                yield return null;
            }

            if (_model != null) _model.localScale = Vector3.one;
            if (btn != null) btn.localScale = Vector3.one;
            if (lvl != null) lvl.localScale = Vector3.one;
            if (halo != null) halo.gameObject.SetActive(false);
        }

        /// <summary>
        /// What a phase looks like. The tray leaves, the old building goes into the ground, and the new
        /// one comes up through the flash — which is only possible because the stage can hold both at
        /// once. Buttons stay dead for the whole sequence: this is the payoff for two thirds of a
        /// station's upgrade track, and buying through it would step on it.
        /// </summary>
        private IEnumerator PhaseSequence(int phase)
        {
            _busy = true;
            SetButtons(false);
            // Ses ve titreşim toplu faz bildirimiyle geliyor; o zaten bu karede bir kez çalıştı.
            Bind();

            // The island rebuild already happened in the purchase frame. Let that frame finish before
            // rebuilding the preview too; doing both hierarchies together caused the visible hitch.
            // The purchase panel stays exactly where it is throughout the phase celebration.
            if (sheet != null) sheet.anchoredPosition = _sheetHome;
            yield return null;

            Transform old = _model;
            float t = 0f;
            while (t < PhaseSwapOutSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PhaseSwapOutSeconds);
                if (old != null) old.localScale = Vector3.one * Mathf.Lerp(1f, 0.88f, k);
                yield return null;
            }
            if (stage != null) stage.Clear();
            _model = null;
            yield return null;

            _model = MountPhase(phase);
            if (_model != null) _model.localScale = Vector3.one * 0.82f;
            if (flash != null) flash.gameObject.SetActive(true);
            t = 0f;
            while (t < PhaseSwapInSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PhaseSwapInSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                float over = 1f + Mathf.Sin(k * Mathf.PI) * 0.05f;
                if (_model != null) _model.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, ease) * over;
                if (flash != null) SetAlpha(flash, Mathf.Sin(k * Mathf.PI) * 0.72f);
                yield return null;
            }
            if (_model != null) _model.localScale = Vector3.one;
            if (flash != null) { SetAlpha(flash, 0f); flash.gameObject.SetActive(false); }

            Refresh();
            if (confetti != null)
            {
                if (_landscapeLayout) confetti.PlayAt(new Vector2(-500f, -100f));
                else confetti.Play();
            }
            if (phaseBanner != null)
            {
                if (phaseBannerText != null) phaseBannerText.text = string.Format(Loc.T("istasyon_ekrani.faz_bant"), phase);
                phaseBanner.gameObject.SetActive(true);
                float from = _bannerHome.x + StageHalfWidth() * 2f + 400f;
                t = 0f;
                while (t < BannerInSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / BannerInSeconds);
                    float ease = 1f - Mathf.Pow(1f - k, 4f);
                    phaseBanner.anchoredPosition = new Vector2(Mathf.Lerp(from, _bannerHome.x, ease), phaseBanner.anchoredPosition.y);
                    phaseBanner.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.10f);
                    yield return null;
                }
                phaseBanner.anchoredPosition = new Vector2(_bannerHome.x, phaseBanner.anchoredPosition.y);
                phaseBanner.localScale = Vector3.one;

                t = 0f;
                while (t < BannerHoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }

                var group = phaseBanner.GetComponent<CanvasGroup>();
                t = 0f;
                while (t < BannerOutSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / BannerOutSeconds);
                    if (group != null) group.alpha = 1f - k;
                    phaseBanner.localScale = Vector3.one * (1f - k * 0.12f);
                    yield return null;
                }
                if (group != null) group.alpha = 1f;
                phaseBanner.localScale = Vector3.one;
                phaseBanner.gameObject.SetActive(false);
            }

            if (sheet != null) sheet.anchoredPosition = _sheetHome;
            _busy = false;
            Refresh();
        }

        /// <summary>The short version of the open, for a strip tap: no dim, no fade, just the tray landing
        /// again under a model that has grown into place.</summary>
        private IEnumerator SwapAnim()
        {
            float from = _sheetHome.y - SwapDrop;
            float t = 0f;
            while (t < SwapSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / SwapSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                if (sheet != null) sheet.anchoredPosition = new Vector2(_sheetHome.x, Mathf.Lerp(from, _sheetHome.y, ease));
                if (_model != null) _model.localScale = Vector3.one * Mathf.Lerp(0.74f, 1f, ease);
                yield return null;
            }
            if (sheet != null) sheet.anchoredPosition = _sheetHome;
            if (_model != null) _model.localScale = Vector3.one;
        }

        private IEnumerator SlideSheet(float from, float to, float seconds)
        {
            if (sheet == null) yield break;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / seconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                sheet.anchoredPosition = new Vector2(_sheetHome.x, Mathf.Lerp(from, to, ease));
                yield return null;
            }
            sheet.anchoredPosition = new Vector2(_sheetHome.x, to);
        }

        private float HiddenSheetY()
        {
            float h = sheet != null ? sheet.rect.height : 700f;
            return _sheetHome.y - h - 60f;
        }

        private float StageHalfWidth()
        {
            return stageFrame != null ? stageFrame.rect.width * 0.5f : 480f;
        }

        private void SetButtons(bool on)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].buyBtn != null) _rows[i].buyBtn.interactable = on;
        }

        private void HideFx()
        {
            if (halo != null) halo.gameObject.SetActive(false);
            if (flash != null) flash.gameObject.SetActive(false);
            if (shine != null) shine.gameObject.SetActive(false);
            if (phaseBanner != null) phaseBanner.gameObject.SetActive(false);
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            if (Mathf.Abs(c.a - a) < 0.004f) return;
            c.a = a;
            g.color = c;
        }

        /// <summary>The two tiles at the end of the strip that are not stations. Both are negative, which
        /// is what every "is this a building?" test on this screen actually asks.</summary>
        private const int ExpansionPage = -2;
        private const int ReportPage = -3;
        private const float ExpansionTop = 530f;      // altın hapının altı: tepsi genişletmelerde buraya kadar büyür
        private const float SheetBottom = 26f;
        private const float LandscapePanelHeight = 520f;
        private const float LandscapeListPanelWidth = 1200f;
        private const float LandscapeListPanelHeight = 550f;
        private const float LandscapeCardHeight = 212f;
        private const float SheetStationHeight = 810f;

        private static readonly Color SlotIdleIcon = new Color(1f, 1f, 1f, 0.72f);
        private const float SlotLiveScale = 1.12f;
        private const float SwapSeconds = 0.24f;
        private const float SwapDrop = 110f;

        private const float OpenSeconds = 0.30f;
        private const float PunchSeconds = 0.45f;
        private const float PhaseSwapOutSeconds = 0.08f;
        private const float PhaseSwapInSeconds = 0.18f;
        private const float SheetOutSeconds = 0.22f;
        private const float SinkSeconds = 0.45f;
        private const float FlashSeconds = 0.26f;
        private const float RiseSeconds = 0.70f;
        private const float BannerInSeconds = 0.24f;
        private const float BannerHoldSeconds = 0.25f;
        private const float BannerOutSeconds = 0.18f;
        private const float SheetInSeconds = 0.28f;

    }
}
