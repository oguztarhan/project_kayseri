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

        [Header("İstasyonlar")]
        [SerializeField] private List<StationEntry> stations = new List<StationEntry>();
        [SerializeField] private float refreshInterval = 0.25f;

        private sealed class Row
        {
            public int axis;
            public int unlock = -1;       // unlock row when >= 0, axis row otherwise
            public TMP_Text name, level, price, badgeText;
            public Button buyBtn;
            public Image buyImg, badge;
            public GameObject buyGO, badgeGO, lockGO;
        }

        private readonly List<Row> _rows = new List<Row>();
        private Image[] _slots;
        private Image[] _slotIcons;
        private ScrollRect _scroll;
        private float _cardHeight = 236f;
        private WalletService _wallet;
        private CoalOperation _op;
        private Transform _model;
        private Vector2 _sheetHome;
        private float _dimAlpha = 0.8f;
        private float _timer;
        private int _station = -1;
        private int _builtFor = -1;
        private bool _busy;

        private void Awake()
        {
            if (sheet != null) { _sheetHome = sheet.anchoredPosition; _scroll = sheet.GetComponent<ScrollRect>(); }
            if (dim != null) _dimAlpha = dim.color.a;
            if (cardTemplate != null)
            {
                var le = cardTemplate.GetComponent<LayoutElement>();
                if (le != null && le.preferredHeight > 0f) _cardHeight = le.preferredHeight;
                cardTemplate.SetActive(false);
            }
            HideFx();
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
            BuildDevButton();
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

        /// <summary>Opens the screen on one upgrade.</summary>
        public void Open(int station)
        {
            if (station != ExpansionPage && !Handles(station)) return;
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
            if (station != ExpansionPage && !Handles(station)) return;
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
        /// </summary>
        private void ApplyPage()
        {
            bool expansions = _station == ExpansionPage;
            if (stageGroup != null) stageGroup.SetActive(!expansions);
            if (phaseGroup != null) phaseGroup.SetActive(!expansions);
            if (sheet == null) return;

            float height = SheetStationHeight;
            if (expansions)
            {
                var area = sheet.parent as RectTransform;
                if (area != null)
                    height = Mathf.Max(SheetStationHeight, area.rect.height - ExpansionTop - SheetBottom);
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
            if (_phases != null) { _phases.PhaseChanged -= OnDistrictPhaseChanged; _phases = null; }
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
            if (_phases != null) _phases.PhaseChanged -= OnDistrictPhaseChanged;
            _phases = next;
            if (_phases != null) _phases.PhaseChanged += OnDistrictPhaseChanged;
        }

        private void OnDistrictPhaseChanged(string district, int phase)
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
            if (_station == ExpansionPage) { stage.Live = false; return; }
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

        private Row AddCard(string name, Sprite icon)
        {
            GameObject go = Instantiate(cardTemplate, sheetContent);
            go.name = name;
            go.SetActive(true);

            var row = new Row();
            Transform t = go.transform.Find("Ikon");
            if (t != null)
            {
                Image img = t.GetComponent<Image>();
                if (img != null) { img.sprite = icon; img.enabled = icon != null; }
            }
            t = go.transform.Find("Ad");     if (t != null) row.name = t.GetComponent<TMP_Text>();
            t = go.transform.Find("Seviye"); if (t != null) row.level = t.GetComponent<TMP_Text>();
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
            _rows.Add(row);
            return row;
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

            int n = stations.Count + (expansionIcon != null ? 1 : 0);
            _slots = new Image[n];
            _slotIcons = new Image[n];
            for (int i = 0; i < n; i++)
            {
                bool expansion = i >= stations.Count;
                GameObject go = Instantiate(stripTemplate, stripContent);
                go.name = expansion ? "Yuva_Genisletmeler" : "Yuva_" + stations[i].station;
                go.SetActive(true);

                _slots[i] = go.GetComponent<Image>();
                Transform t = go.transform.Find("Ikon");
                if (t != null)
                {
                    _slotIcons[i] = t.GetComponent<Image>();
                    if (_slotIcons[i] != null) _slotIcons[i].sprite = expansion ? expansionIcon : stations[i].icon;
                }

                var btn = go.GetComponent<Button>();
                if (btn == null) continue;
                int captured = expansion ? ExpansionPage : stations[i].station;
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
                bool live = i < stations.Count
                    ? stations[i].station == _station
                    : _station == ExpansionPage;
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
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station) return stations[i].icon;
            return null;
        }

        private string TitleFor(int station)
        {
            if (station == ExpansionPage) return Loc.T("yukseltme.genisletmeler");
            for (int i = 0; i < stations.Count; i++)
                if (stations[i] != null && stations[i].station == station && !string.IsNullOrEmpty(stations[i].title))
                    return stations[i].title;
            return _op != null ? Loc.Id("istasyon", _op.StationName(station)) : "";
        }

        // ---------- refresh ----------
        private void Refresh()
        {
            if (_op == null || _station == -1) return;

            if (titleText != null) titleText.text = TitleFor(_station);
            if (titleIcon != null)
            {
                titleIcon.sprite = IconFor(_station);
                titleIcon.enabled = titleIcon.sprite != null;
            }

            if (_station == ExpansionPage)
            {
                for (int i = 0; i < _rows.Count; i++) RefreshUnlock(_rows[i]);
                return;
            }

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
                return;
            }

            int lv = _op.AxisLevel(_station, r.axis);
            if (r.level != null) r.level.text = string.Format(Loc.T("yukseltme.seviye"), lv);

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
                return;
            }

            SetRow(r, true, false, false);
            string note = "kilit." + r.unlock + ".not";
            string line = Loc.T(note);
            if (r.level != null) r.level.text = line != note ? line : Loc.T("yukseltme.tek_seferlik");

            BigDouble cost = _op.UnlockCost(r.unlock);
            bool afford = _wallet != null && _wallet.CanAfford(cost);
            if (r.price != null) r.price.text = "$" + NumberFormatter.Format(cost);
            if (r.buyImg != null) r.buyImg.sprite = afford ? priceGreen : priceGrey;
            if (r.buyBtn != null) r.buyBtn.interactable = afford && !_busy;
        }

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
            // Ses ve titreşim OnDistrictPhaseChanged'den geliyor; o zaten bu karede çalıştı.
            Bind();

            // 1 · clear the stage
            yield return SlideSheet(_sheetHome.y, HiddenSheetY(), SheetOutSeconds);

            // 2 · the old building sinks, and the camera leans in after it
            Transform old = _model;
            float drop = stage != null ? stage.FocusRadius * 1.4f : 40f;
            float t = 0f;
            while (t < SinkSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / SinkSeconds);
                float ease = k * k;                              // slow to let go, then gone
                if (old != null)
                {
                    old.localPosition = Vector3.down * (drop * ease);
                    old.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, ease);
                }
                if (stage != null) stage.Zoom = Mathf.Lerp(1f, 1.14f, ease);
                yield return null;
            }
            if (old != null) Destroy(old.gameObject);
            _model = null;

            // 3 · the flash the new one arrives through
            _model = MountPhase(phase);
            if (_model != null)
            {
                _model.localPosition = Vector3.down * drop;
                _model.localScale = Vector3.one * 0.6f;
            }
            if (flash != null) flash.gameObject.SetActive(true);
            t = 0f;
            while (t < FlashSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FlashSeconds);
                if (flash != null) SetAlpha(flash, Mathf.Sin(k * Mathf.PI) * 0.9f);
                yield return null;
            }
            if (flash != null) { SetAlpha(flash, 0f); flash.gameObject.SetActive(false); }

            // 4 · it rises, overshoots, settles — with a sweep of light across it
            if (shine != null) shine.gameObject.SetActive(true);
            float shineFrom = -StageHalfWidth() - 220f;
            float shineTo = StageHalfWidth() + 220f;
            t = 0f;
            while (t < RiseSeconds)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RiseSeconds);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                float over = 1f + Mathf.Sin(k * Mathf.PI) * 0.07f;
                if (_model != null)
                {
                    _model.localPosition = Vector3.down * Mathf.Lerp(drop, 0f, ease);
                    _model.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, ease) * over;
                }
                if (stage != null) stage.Zoom = Mathf.Lerp(1.14f, 1f, ease);
                if (shine != null) shine.anchoredPosition = new Vector2(Mathf.Lerp(shineFrom, shineTo, ease), 0f);
                yield return null;
            }
            if (_model != null) { _model.localPosition = Vector3.zero; _model.localScale = Vector3.one; }
            if (stage != null) stage.Zoom = 1f;
            if (shine != null) shine.gameObject.SetActive(false);

            // 5 · name what just happened
            Refresh();
            if (phaseBanner != null)
            {
                if (phaseBannerText != null) phaseBannerText.text = string.Format(Loc.T("istasyon_ekrani.faz_bant"), phase);
                phaseBanner.gameObject.SetActive(true);
                float from = StageHalfWidth() * 2f + 400f;
                t = 0f;
                while (t < BannerInSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / BannerInSeconds);
                    float ease = 1f - Mathf.Pow(1f - k, 4f);
                    phaseBanner.anchoredPosition = new Vector2(Mathf.Lerp(from, 0f, ease), phaseBanner.anchoredPosition.y);
                    phaseBanner.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.10f);
                    yield return null;
                }
                phaseBanner.anchoredPosition = new Vector2(0f, phaseBanner.anchoredPosition.y);
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

            // 6 · and back to shopping
            yield return SlideSheet(HiddenSheetY(), _sheetHome.y, SheetInSeconds);
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

        /// <summary>The strip's last tile is not a station — it is the one-time expansions.</summary>
        private const int ExpansionPage = -2;
        private const float ExpansionTop = 410f;      // şeridin altı: tepsi genişletmelerde buraya kadar büyür
        private const float SheetBottom = 26f;
        private const float SheetStationHeight = 810f;

        private static readonly Color SlotIdleIcon = new Color(1f, 1f, 1f, 0.72f);
        private const float SlotLiveScale = 1.12f;
        private const float SwapSeconds = 0.24f;
        private const float SwapDrop = 110f;

        private const float OpenSeconds = 0.30f;
        private const float PunchSeconds = 0.45f;
        private const float SheetOutSeconds = 0.22f;
        private const float SinkSeconds = 0.45f;
        private const float FlashSeconds = 0.26f;
        private const float RiseSeconds = 0.70f;
        private const float BannerInSeconds = 0.45f;
        private const float BannerHoldSeconds = 0.55f;
        private const float BannerOutSeconds = 0.30f;
        private const float SheetInSeconds = 0.28f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // TEST MODU (yalnız geliştirici): her yerde bedava satın alma + oturum boyunca kayıt askıda,
        // böylece her ada ve her yükseltme gerçek kaydına dokunmadan denenebilir. Koddan kuruluyor —
        // hiçbir zaman yayına çıkmayacağı için prefabda yeri yok. Yükseltme paneliyle birlikte buraya
        // taşındı: satın almanın yaşadığı ekran artık burası.
        private Button _testBtn;
        private TMP_Text _testLabel;

        private void BuildDevButton()
        {
            var canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;
            var go = new GameObject("TestModu", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(12f, -12f); rt.sizeDelta = new Vector2(340f, 58f);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.24f, 0.27f, 0.32f, 0.92f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var tgo = new GameObject("Etiket", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _testLabel = tgo.AddComponent<TextMeshProUGUI>();
            _testLabel.fontSize = 26; _testLabel.alignment = TextAlignmentOptions.Center;
            _testLabel.text = "TEST MODU: KAPALI"; _testLabel.raycastTarget = false;
            _testBtn = btn;
            btn.onClick.AddListener(ToggleTestMode);
        }

        private void ToggleTestMode()
        {
            if (_wallet == null) return;
            bool on = !_wallet.FreePurchases;
            _wallet.FreePurchases = on;
            if (on)
            {
                var save = ServiceLocator.Get<SaveService>();
                if (save != null) save.Suspended = true;   // yapışkan: test modu bir kez çalıştıysa bu oturum kayıt yazmaz
            }
            _testLabel.text = on ? "TEST AÇIK — KAYIT YOK" : "TEST MODU: KAPALI";
            _testBtn.GetComponent<Image>().color = on ? new Color(0.75f, 0.20f, 0.20f, 0.92f) : new Color(0.24f, 0.27f, 0.32f, 0.92f);
            Refresh();
        }
#else
        private void BuildDevButton() { }
#endif
    }
}
