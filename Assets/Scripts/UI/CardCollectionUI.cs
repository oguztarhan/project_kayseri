using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The card collection screen (Docs/PLAN_14, slice 7): the pack card on top, one tab per set,
    /// and that set's eight cards in a two-column grid beneath.
    ///
    /// Built in code for the reason every roster screen here is — the tiles come out of
    /// <see cref="CardCollectionCatalogue"/>, so authoring a card costs one entry in that table and
    /// nothing here. Card faces, set banners and rarity tints are read off the service's
    /// <see cref="CardCollectionConfig"/>, the one asset the collection was built from; this
    /// component's own sprites are only the chrome every roster screen shares.
    ///
    /// ONE SET AT A TIME, NOT TWENTY-FOUR TILES. Eight tiles fit a portrait sheet at a readable size
    /// with no scroll view; twenty-four would not. The tabs are also the only place a set's bonus
    /// and its one-time reward are shown, and they belong beside the cards that earn them.
    ///
    /// THE SCREEN DECIDES NOTHING. It asks the service to open, level and claim and prints the
    /// receipt it is handed — see <see cref="CardCollectionService"/>'s class note for why.
    ///
    /// Refreshed on open and on <see cref="CardCollectionService.Changed"/>. The once-a-second Update
    /// drives only the daily countdown, whose label sits on its own sub-canvas so the tick does not
    /// rebuild the whole sheet's batch.
    /// </summary>
    public sealed class CardCollectionUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 116;
        [SerializeField] private bool _usePortraitArtwork;
        [SerializeField] private Font _portraitFont;
        [SerializeField] private float _portraitCardWidth = 392f;
        [SerializeField] private float _portraitCardGap = 28f;

        [Header("Görseller")]
        [Tooltip("Kart ve panel gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Aksiyon düğmesi — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Paket sayacı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [Tooltip("Kartların üstünde durduğu zemin. Panel sanatı bağlıysa onu boyar.")]
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);

        [Header("Nadirlik renkleri — Sıradan → Mitik")]
        [Tooltip("Yalnızca koleksiyon ayar varlığı bağlı değilken kullanılır; bağlıysa renkler oradan okunur.")]
        [SerializeField]
        private Color[] rarityTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),
            new Color(0.26f, 0.60f, 0.92f, 1f),
            new Color(0.62f, 0.40f, 0.86f, 1f),
            new Color(0.96f, 0.66f, 0.18f, 1f),
            new Color(0.90f, 0.32f, 0.46f, 1f),
        };

        public const string OpenerButtonName = "BtnKartKoleksiyonu";

        /// <summary>The More row's icon when no collection icon is wired on the config. Missing
        /// until the art lands — see Docs/PLAN_14, slice 8.</summary>
        private const string OpenerIconResource = "UI/Buttons/koleksiyon";
        private const string InfoIconResource = "UI/Buttons/bilgi";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Good = new Color(0.20f, 0.62f, 0.34f, 1f);
        private static readonly Color NewBadge = new Color(0.88f, 0.26f, 0.30f, 1f);
        private static readonly Color ButtonBlue = new Color(0.24f, 0.55f, 0.84f, 1f);
        private static readonly Color ButtonGreen = new Color(0.24f, 0.68f, 0.36f, 1f);
        private static readonly Color ButtonYellow = new Color(0.94f, 0.68f, 0.20f, 1f);
        private static readonly Color ButtonOff = new Color(0.72f, 0.75f, 0.80f, 1f);

        private const float RibbonBand = 0.677f;
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        // Keys built once: Refresh runs on every collection change and should not concatenate them.
        private static readonly string[] EffectKeys =
        {
            "koleksiyon.etki.0", "koleksiyon.etki.1", "koleksiyon.etki.2",
            "koleksiyon.etki.3", "koleksiyon.etki.4",
        };
        private static readonly string[] RarityKeys =
        {
            "kaptan.derece.0", "kaptan.derece.1", "kaptan.derece.2", "kaptan.derece.3", "kaptan.derece.4",
        };

        /// <summary>The page is one column of horizontal bands, like the captain screen, because the
        /// game is portrait-only.</summary>
        private const float PageLeft = 0.030f, PageRight = 0.970f;
        private const float PackTop = 0.828f, PackBottom = 0.640f;
        private const float TabsTop = 0.630f, TabsBottom = 0.585f;
        private const float SetInfoTop = 0.578f, SetInfoBottom = 0.498f;
        private const float BrowseTop = 0.490f, BrowseBottom = 0.448f;
        private const float GridTop = 0.438f, GridBottom = 0.030f;
        private const int GridColumns = 2;

        private CardCollectionService _cards;
        private LocalizationService _loc;
        private RectTransform _root;
        private RectTransform _portraitGrid;
        private ScrollRect _portraitScroll;
        private RectTransform _portraitResult;
        private Text _portraitResultText;
        private RectTransform _portraitPack, _portraitSet;
        private Text[] _portraitSummary, _portraitQuickLabels;
        private Text _portraitSetTitle, _portraitSetProgress, _portraitFooter;
        private Image[] _portraitState;
        private Image _portraitDetailState, _portraitEmptyArt;
        private RectTransform _portraitDetailSheet;
        private Text[] _portraitRewardLabels;
        private GameObject[] _portraitSelectedTabs;

        private Text _titleLabel, _packChipLabel, _collectedLabel, _bonusLabel;
        private Text _packCountLabel, _pityLabel, _lastPullLabel, _sourceLabel;
        private Button _openPack, _dailyPack, _claimSet;
        private Text _openPackText, _dailyPackText, _claimSetText, _setBonusLabel, _setRewardLabel, _setDescLabel;
        private Image _packIcon;
        private Text _sortText, _filterText, _emptyText;

        private Button[] _tab;
        private Text[] _tabText;
        private GameObject[] _tabBadge;

        private RectTransform[] _tile;
        private Image[] _tileStripe, _tileFace, _tileFrame;
        private Text[] _tileName, _tileEffect, _tileStars, _tileBtnText;
        private Button[] _tileBtn;
        private GameObject[] _tileNew;

        /// <summary>One set's worth of card states, reused — <see cref="RosterCardQuery"/> fills
        /// <see cref="_visibleOrder"/> from it without allocating.</summary>
        private RosterCardState[] _setState;
        private int[] _visibleOrder;
        private int _gridRows;

        private RosterSortMode _sortMode;
        private RosterFilterMode _filterMode;
        private int _selectedSet;
        private int _selected = -1;

        private RosterInspectPanel _inspect;
        private OddsSheetUI _odds;

        private GameObject _openerChip;
        private TMP_Text _openerCount;

        private float _pollTimer;
        private long _lastCountdown = -1L;
        private bool _lastDailyReady;

        private void Awake()
        {
            _cards = ServiceLocator.Get<CardCollectionService>();
            Build();
            BuildOpener();
            if (_cards != null) _cards.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            _lastDailyReady = _cards != null && _cards.DailyPackReady;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_cards != null) _cards.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("koleksiyon.baslik");
            if (_sourceLabel != null) _sourceLabel.text = Loc.T("koleksiyon.nereden");
            if (_openPackText != null) _openPackText.text = Loc.T("koleksiyon.ac");
            _lastCountdown = -1L;
            Refresh();
            RefreshOpener();
        }

        public bool Visible => _root != null && _root.gameObject.activeSelf;

        public void Show()
        {
            if (_root != null) _root.gameObject.SetActive(true);
            if (_lastPullLabel != null) _lastPullLabel.text = string.Empty;
            _lastCountdown = -1L;
            Refresh();
        }

        public void Hide()
        {
            if (_portraitPack != null) _portraitPack.gameObject.SetActive(false);
            if (_portraitSet != null) _portraitSet.gameObject.SetActive(false);
            if (_portraitResult != null) _portraitResult.gameObject.SetActive(false);
            if (_inspect != null) _inspect.Hide();
            if (_odds != null) _odds.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Once a second: the countdown while the screen is open, and the opener's badge when the
        /// UTC day rolls over. The badge check reads one bool and allocates nothing, so it runs
        /// while the screen is shut too — otherwise midnight's free pack would go unannounced until
        /// something else happened to change the collection.
        /// </summary>
        private void Update()
        {
            if (_cards == null) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;

            bool ready = _cards.DailyPackReady;
            if (ready != _lastDailyReady)
            {
                _lastDailyReady = ready;
                RefreshOpener();
                Refresh();
                return;
            }
            if (Visible && !ready) RefreshCountdown();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "KoleksiyonKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildPackCard();
            BuildSetTabs();
            BuildSetInfo();
            BuildBrowseBar();
            BuildGrid();

            _inspect = new RosterInspectPanel(_root);
            _odds = new OddsSheetUI(_root, OddsSheetUI.Skin.CardPack());
            // Content into the safe area; the scrim above it keeps covering the notch.
            RectTransform safe = UiBuild.InsetContent(_root);
            if (_usePortraitArtwork && PortraitUiArt.Get("collection-collection-card-template") != null) ApplyPortraitLayout(safe);
        }

        /// <summary>One opaque sheet behind everything, eating its own taps so the scrim's dismiss
        /// cannot fire through it — see <see cref="CaptainRosterUI"/> for why it exists at all.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.922f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon,
                                     new Vector2(0.250f, 0.928f), new Vector2(0.750f, 0.998f));
            // Unwired, the stand-in panel is white and so is the title: give it the ribbon's blue.
            if (ribbon == null) band.GetComponent<Image>().color = ButtonBlue;
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.10f, RibbonBand - 0.13f),
                                                   new Vector2(0.90f, RibbonBand + 0.13f)),
                                        "Text", Loc.T("koleksiyon.baslik"), 36, TextAnchor.MiddleCenter);
            Fit(_titleLabel, 18, 36);

            RectTransform chip = Chip(_root, "Paketler", new Vector2(PageLeft, 0.941f), new Vector2(0.235f, 0.995f));
            _packChipLabel = UiBuild.Label(Slot(chip, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                           "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            _packChipLabel.color = Paper;
            Fit(_packChipLabel, 14, 28);

            Button close = UiBuild.Btn(_root, "Kapat", closeIcon != null ? string.Empty : "X",
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey, track, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // Not in the very corner: the HUD's settings cog draws over it on its 120 canvas.
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.855f, 0.938f), new Vector2(0.955f, 0.996f));

            _collectedLabel = UiBuild.Label(Slot(_root, "Toplandi", new Vector2(PageLeft + 0.01f, 0.874f),
                                                                    new Vector2(PageRight - 0.01f, 0.912f)),
                                            "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _collectedLabel.color = Paper;
            Fit(_collectedLabel, 14, 26);

            _bonusLabel = UiBuild.Label(Slot(_root, "Bonuslar", new Vector2(PageLeft + 0.01f, 0.834f),
                                                                new Vector2(PageRight - 0.01f, 0.872f)),
                                        "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _bonusLabel.color = new Color(0.55f, 0.88f, 0.62f, 1f);
            Fit(_bonusLabel, 12, 22);
        }

        /// <summary>The pack card: how many are waiting, the free daily one, how far each guarantee
        /// is away, and what the last pack turned out to be.</summary>
        private void BuildPackCard()
        {
            RectTransform c = Art(_root, "Paket", cardPanel,
                                  new Vector2(PageLeft, PackBottom), new Vector2(PageRight, PackTop));

            // The pack's own icon, when one is wired on the config; the count moves over to make
            // room for it rather than leaving a gap on a build that has none.
            Sprite packArt = _cards != null && _cards.Config != null ? _cards.Config.PackIcon : null;
            _packIcon = Quad(c, "PaketSimge", new Vector2(0.030f, 0.720f), new Vector2(0.105f, 0.950f));
            _packIcon.sprite = packArt;
            _packIcon.type = Image.Type.Simple;
            _packIcon.preserveAspect = true;
            _packIcon.color = Color.white;
            _packIcon.enabled = packArt != null;

            float countLeft = packArt != null ? 0.115f : 0.030f;
            _packCountLabel = UiBuild.Label(Slot(c, "PaketSayisi", new Vector2(countLeft, 0.720f), new Vector2(0.540f, 0.950f)),
                                            "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            _packCountLabel.color = Ink;
            Fit(_packCountLabel, 14, 28);

            // Packs cannot be bought in v1, so this is outside the paid-loot-box rule — the badge is
            // here anyway, for the same reason the captain crate carries one.
            Sprite infoIcon = Resources.Load<Sprite>(InfoIconResource);
            Button odds = UiBuild.Btn(c, "Oran", infoIcon != null ? string.Empty : "i",
                                      infoIcon != null ? infoIcon : UiSkin.ButtonGrey,
                                      new Color(0.45f, 0.49f, 0.56f, 1f), 22, ShowOdds);
            UiBuild.Anchor((RectTransform)odds.transform, new Vector2(0.550f, 0.740f), new Vector2(0.615f, 0.935f));

            _openPack = UiBuild.Btn(c, "PaketAc", Loc.T("koleksiyon.ac"),
                                    actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                    new Color(0.24f, 0.68f, 0.36f, 1f), 26, OpenPack);
            UiBuild.Anchor((RectTransform)_openPack.transform, new Vector2(0.640f, 0.540f), new Vector2(0.970f, 0.930f));
            PillFit.Wrap(_openPack.GetComponent<Image>());
            _openPackText = _openPack.GetComponentInChildren<Text>();
            Fit(_openPackText, 13, 26);

            _dailyPack = UiBuild.Btn(c, "GunlukPaket", string.Empty,
                                     actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                     new Color(0.94f, 0.68f, 0.20f, 1f), 22, ClaimDaily);
            UiBuild.Anchor((RectTransform)_dailyPack.transform, new Vector2(0.640f, 0.090f), new Vector2(0.970f, 0.460f));
            PillFit.Wrap(_dailyPack.GetComponent<Image>());
            _dailyPackText = _dailyPack.GetComponentInChildren<Text>();
            Fit(_dailyPackText, 11, 22);
            // The countdown re-inks every second. Its own sub-canvas keeps that from dirtying the
            // whole sheet's batch; it is a label, so it needs no raycaster.
            _dailyPackText.gameObject.AddComponent<Canvas>();

            _pityLabel = UiBuild.Label(Slot(c, "Teselli", new Vector2(0.030f, 0.470f), new Vector2(0.615f, 0.710f)),
                                       "Text", string.Empty, 21, TextAnchor.UpperLeft);
            _pityLabel.color = InkSoft;
            Fit(_pityLabel, 12, 21);

            _lastPullLabel = UiBuild.Label(Slot(c, "SonCekilis", new Vector2(0.030f, 0.190f), new Vector2(0.615f, 0.460f)),
                                           "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            Fit(_lastPullLabel, 12, 23);

            _sourceLabel = UiBuild.Label(Slot(c, "Kaynak", new Vector2(0.030f, 0.030f), new Vector2(0.615f, 0.180f)),
                                         "Text", Loc.T("koleksiyon.nereden"), 18, TextAnchor.MiddleLeft);
            _sourceLabel.color = InkFaint;
            Fit(_sourceLabel, 10, 18);
        }

        private void BuildSetTabs()
        {
            int sets = CardCollectionCatalogue.SetCount;
            _tab = new Button[sets];
            _tabText = new Text[sets];
            _tabBadge = new GameObject[sets];

            const float gap = 0.010f;
            float width = sets > 0 ? (PageRight - PageLeft - gap * (sets - 1)) / sets : 0f;
            for (int s = 0; s < sets; s++)
            {
                int captured = s;
                float left = PageLeft + s * (width + gap);
                _tab[s] = UiBuild.Btn(_root, "SetSekme" + s, string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonBlue,
                                      new Color(0.24f, 0.55f, 0.84f, 1f), 20, () => SelectSet(captured));
                UiBuild.Anchor((RectTransform)_tab[s].transform,
                               new Vector2(left, TabsBottom), new Vector2(left + width, TabsTop));
                PillFit.Wrap(_tab[s].GetComponent<Image>());
                _tabText[s] = _tab[s].GetComponentInChildren<Text>();
                Fit(_tabText[s], 10, 20);

                // The set's banner is the tab itself when one is wired: a banner is a set's face,
                // and the tab is where a set is picked. The selected/unselected tint still applies.
                Sprite banner = _cards != null && _cards.Config != null
                    ? _cards.Config.BannerOf(CardCollectionCatalogue.Sets[s].Id) : null;
                if (banner != null)
                {
                    Image tabImage = _tab[s].GetComponent<Image>();
                    tabImage.sprite = banner;
                    tabImage.type = Image.Type.Simple;
                    tabImage.preserveAspect = false;
                    _tabText[s].gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.75f);
                }

                // A reward waiting, or a card in this set ready to level — the set a player should
                // look at next, marked from any other tab.
                RectTransform badge = UiBuild.Flat(_tab[s].transform, "Rozet", NewBadge,
                                                   new Vector2(0.86f, 0.55f), new Vector2(0.98f, 1.05f));
                badge.GetComponent<Image>().raycastTarget = false;
                UiBuild.Label(badge, "Text", "!", 20, TextAnchor.MiddleCenter);
                _tabBadge[s] = badge.gameObject;
                _tabBadge[s].SetActive(false);
            }
        }

        /// <summary>The selected set's two facts, kept apart on purpose: the permanent bonus (live the
        /// moment the set completes) and the one-time reward (claimed separately).</summary>
        private void BuildSetInfo()
        {
            RectTransform c = Art(_root, "SetBilgi", cardPanel,
                                  new Vector2(PageLeft, SetInfoBottom), new Vector2(PageRight, SetInfoTop));

            // Three lines: what the set is, what finishing it turns on, what it pays once.
            _setDescLabel = UiBuild.Label(Slot(c, "SetAciklama", new Vector2(0.030f, 0.660f), new Vector2(0.670f, 0.950f)),
                                          "Text", string.Empty, 18, TextAnchor.MiddleLeft);
            _setDescLabel.color = InkFaint;
            Fit(_setDescLabel, 9, 18);

            _setBonusLabel = UiBuild.Label(Slot(c, "SetBonus", new Vector2(0.030f, 0.360f), new Vector2(0.670f, 0.660f)),
                                           "Text", string.Empty, 21, TextAnchor.MiddleLeft);
            _setBonusLabel.color = Ink;
            Fit(_setBonusLabel, 10, 21);

            _setRewardLabel = UiBuild.Label(Slot(c, "SetOdul", new Vector2(0.030f, 0.060f), new Vector2(0.670f, 0.360f)),
                                            "Text", string.Empty, 20, TextAnchor.MiddleLeft);
            _setRewardLabel.color = InkSoft;
            Fit(_setRewardLabel, 10, 20);

            _claimSet = UiBuild.Btn(c, "SetOdulAl", string.Empty,
                                    actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                    new Color(0.24f, 0.68f, 0.36f, 1f), 22, ClaimSetReward);
            UiBuild.Anchor((RectTransform)_claimSet.transform, new Vector2(0.690f, 0.130f), new Vector2(0.970f, 0.870f));
            PillFit.Wrap(_claimSet.GetComponent<Image>());
            _claimSetText = _claimSet.GetComponentInChildren<Text>();
            Fit(_claimSetText, 11, 22);
        }

        private void BuildBrowseBar()
        {
            Button sort = UiBuild.Btn(_root, "Sirala", string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                      new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleSort);
            UiBuild.Anchor((RectTransform)sort.transform,
                           new Vector2(PageLeft, BrowseBottom), new Vector2(0.310f, BrowseTop));
            PillFit.Wrap(sort.GetComponent<Image>());
            _sortText = sort.GetComponentInChildren<Text>();
            Fit(_sortText, 12, 22);

            Button filter = UiBuild.Btn(_root, "Filtre", string.Empty,
                                        actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                        new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleFilter);
            UiBuild.Anchor((RectTransform)filter.transform,
                           new Vector2(0.330f, BrowseBottom), new Vector2(0.610f, BrowseTop));
            PillFit.Wrap(filter.GetComponent<Image>());
            _filterText = filter.GetComponentInChildren<Text>();
            Fit(_filterText, 12, 22);

            _emptyText = UiBuild.Label(Slot(_root, "FiltreBos", new Vector2(PageLeft, 0.20f), new Vector2(PageRight, 0.34f)),
                                       "Text", Loc.T("kadro.bos"), 28, TextAnchor.MiddleCenter);
            _emptyText.color = Paper;
            Fit(_emptyText, 16, 28);
            _emptyText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Every card's tile is built once, named <c>Kart_&lt;catalogue index&gt;</c>, and parked;
        /// <see cref="ReflowTiles"/> shows the selected set's and lays them out. The grid is sized off
        /// the largest set rather than the one showing, so switching tabs never resizes a tile.
        /// </summary>
        private void BuildGrid()
        {
            int count = CardCollectionCatalogue.Count;
            int largest = 0;
            for (int s = 0; s < CardCollectionCatalogue.SetCount; s++)
                largest = Mathf.Max(largest, CardCollectionCatalogue.CardsInSetCount(s));
            _setState = new RosterCardState[largest];
            _visibleOrder = new int[largest];
            _gridRows = Mathf.Max(1, (largest + GridColumns - 1) / GridColumns);

            _tile = new RectTransform[count];
            _tileStripe = new Image[count];
            _tileFace = new Image[count];
            _tileFrame = new Image[count];
            _tileName = new Text[count];
            _tileEffect = new Text[count];
            _tileStars = new Text[count];
            _tileBtn = new Button[count];
            _tileBtnText = new Text[count];
            _tileNew = new GameObject[count];

            for (int card = 0; card < count; card++) BuildTile(card);
        }

        private void BuildTile(int card)
        {
            RectTransform tile = Art(_root, "Kart_" + card, cardPanel, new Vector2(PageLeft, GridBottom),
                                     new Vector2(PageRight, GridTop));
            var art = tile.GetComponent<Image>();
            art.raycastTarget = true;
            var open = tile.gameObject.AddComponent<Button>();
            open.transition = Selectable.Transition.None;
            int captured = card;
            open.onClick.AddListener(() => ShowDetails(captured));
            _tile[card] = tile;

            _tileStripe[card] = Quad(tile, "Cizgi", new Vector2(0f, 0f), new Vector2(0.035f, 1f));

            // The face. A tinted plate stands in until the art is wired, so an unauthored card still
            // reads as a card of its rarity rather than a hole.
            _tileFace[card] = Quad(tile, "Yuz", new Vector2(0.060f, 0.120f), new Vector2(0.300f, 0.880f));
            _tileFace[card].preserveAspect = true;

            // The rarity frame, over the face and the stripe but under every word. Its sprite never
            // changes — a card's rarity is authored — so it is set here, once, and simply stays off
            // for a rarity nobody has drawn a frame for.
            _tileFrame[card] = Quad(tile, "Cerceve", Vector2.zero, Vector2.one);
            Sprite frame = _cards != null && _cards.Config != null
                ? _cards.Config.FrameOf(CardCollectionCatalogue.RarityOf(card)) : null;
            _tileFrame[card].sprite = frame;
            _tileFrame[card].type = frame != null && frame.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            _tileFrame[card].color = Color.white;
            _tileFrame[card].enabled = frame != null;

            _tileName[card] = UiBuild.Label(Slot(tile, "Ad", new Vector2(0.330f, 0.600f), new Vector2(0.970f, 0.930f)),
                                            "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            Fit(_tileName[card], 11, 22);

            _tileEffect[card] = UiBuild.Label(Slot(tile, "Etki", new Vector2(0.330f, 0.360f), new Vector2(0.970f, 0.600f)),
                                              "Text", string.Empty, 17, TextAnchor.MiddleLeft);
            Fit(_tileEffect[card], 9, 17);

            _tileStars[card] = UiBuild.Label(Slot(tile, "Yildiz", new Vector2(0.330f, 0.070f), new Vector2(0.620f, 0.350f)),
                                             "Text", string.Empty, 20, TextAnchor.MiddleLeft);
            Fit(_tileStars[card], 10, 20);

            _tileBtn[card] = UiBuild.Btn(tile, "Yukselt", string.Empty,
                                         actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                         new Color(0.24f, 0.68f, 0.36f, 1f), 18, () => Upgrade(captured));
            UiBuild.Anchor((RectTransform)_tileBtn[card].transform, new Vector2(0.640f, 0.070f), new Vector2(0.970f, 0.350f));
            PillFit.Wrap(_tileBtn[card].GetComponent<Image>());
            _tileBtnText[card] = _tileBtn[card].GetComponentInChildren<Text>();
            Fit(_tileBtnText[card], 9, 18);

            RectTransform badge = UiBuild.Flat(tile, "Yeni", NewBadge, new Vector2(0.040f, 0.700f), new Vector2(0.250f, 0.960f));
            badge.GetComponent<Image>().raycastTarget = false;
            Text word = UiBuild.Label(badge, "Text", Loc.T("koleksiyon.yeni"), 16, TextAnchor.MiddleCenter);
            Fit(word, 8, 16);
            _tileNew[card] = badge.gameObject;
            _tileNew[card].SetActive(false);

            tile.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>Order 8 — after the mining gear and before the More button, so it lands in the
        /// More sheet in compact mode. The chip counts everything waiting: unopened packs, today's
        /// free one, cards ready to level and set rewards ready to take.</summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = _cards != null && _cards.Config != null ? _cards.Config.CollectionIcon : null;
            if (icon == null) icon = Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(8, OpenerButtonName,
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>What the opener's chip counts. Public so the smoke test can hold it to the same
        /// sum the screen promises.</summary>
        public int PendingCount()
        {
            if (_cards == null) return 0;
            return _cards.UnopenedPackCount + (_cards.DailyPackReady ? 1 : 0)
                 + _cards.UpgradeReadyCount() + _cards.ClaimableSetRewardCount();
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _cards == null) return;
            int pending = PendingCount();
            bool show = pending > 0;
            if (_openerChip.activeSelf != show) _openerChip.SetActive(show);
            if (show && _openerCount != null)
            {
                string text = pending > 99 ? "99+" : pending.ToString(Culture);
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // ------------------------------------------------------------------- act
        private void OpenPack()
        {
            if (_cards == null || !_cards.TryOpenPack(out CardCollectionService.PackOpenReceipt receipt)) return;

            Ping();
            // Jump to the set the card belongs to, so the tile that just changed is on screen.
            if (CardCollectionCatalogue.SetExists(receipt.Set) && receipt.Set != _selectedSet)
            {
                _selectedSet = receipt.Set;
                Refresh();
            }
            _lastPullLabel.text = PullLine(receipt);
            _lastPullLabel.color = TintOf(receipt.Rarity);
            ShowPortraitResult(PullLine(receipt));
            if (_portraitRewardLabels != null)
            {
                _portraitRewardLabels[0].text = receipt.WasNew ? "+1" : "—";
                _portraitRewardLabels[1].text = !receipt.WasNew && !receipt.WasOverflow ? "+1" : "—";
                _portraitRewardLabels[2].text = receipt.WasOverflow ? "+" + receipt.GemsPaid.ToString(Culture) : "—";
            }
        }

        private void ClaimDaily()
        {
            if (_cards != null && _cards.TryClaimDailyPack()) Ping();
        }

        private void Upgrade(int card)
        {
            if (_cards != null && _cards.TryUpgrade(card)) Ping();
        }

        private void ClaimSetReward()
        {
            if (_cards == null || !_cards.TryClaimSetReward(_selectedSet, out CardCollectionService.SetRewardReceipt receipt))
                return;
            // A reward recorded but not paid (no payer wired) is not celebrated — see the receipt.
            if (!receipt.Paid) return;
            Ping();
            _lastPullLabel.text = Loc.T("gorev.odul_alindi") + "\n" + RewardText(receipt.Kind, receipt.Amount);
            _lastPullLabel.color = Good;
            ShowPortraitResult(_lastPullLabel.text);
            if (_portraitRewardLabels != null)
                for (int i = 0; i < _portraitRewardLabels.Length; i++) _portraitRewardLabels[i].text = "—";
        }

        private void ShowOdds()
        {
            if (_odds != null && _cards != null)
                _odds.ShowCardPack(_cards.PackTuning, _cards.RarityCensus(), r => TintOf((RosterCardState.Rarity)r));
        }

        private void SelectSet(int set)
        {
            if (!CardCollectionCatalogue.SetExists(set) || set == _selectedSet) return;
            _selectedSet = set;
            if (_portraitScroll != null) _portraitScroll.verticalNormalizedPosition = 1f;
            if (_inspect != null) _inspect.Hide();
            Refresh();
        }

        private void CycleSort()
        {
            _sortMode = (RosterSortMode)(((int)_sortMode + 1) % 4);
            if (_portraitScroll != null) _portraitScroll.verticalNormalizedPosition = 1f;
            Refresh();
        }

        private void CycleFilter()
        {
            _filterMode = (RosterFilterMode)(((int)_filterMode + 1) % 4);
            if (_portraitScroll != null) _portraitScroll.verticalNormalizedPosition = 1f;
            if (_inspect != null) _inspect.Hide();
            Refresh();
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_cards == null || !Visible) return;

            _packChipLabel.text = "▣ " + _cards.UnopenedPackCount.ToString(Culture);
            _collectedLabel.text = string.Format(Loc.T("koleksiyon.toplandi"), _cards.OwnedCardCount, _cards.CardCount)
                                 + "   ·   "
                                 + string.Format(Loc.T("koleksiyon.setler"), _cards.CompletedSetCount, _cards.SetCount);
            _bonusLabel.text = BonusSummary(_cards.Effects);

            _packCountLabel.text = string.Format(Loc.T("koleksiyon.paket"), _cards.UnopenedPackCount);
            Dress(_openPack, _cards.CanOpenPack, ButtonGreen);
            _pityLabel.text = PityLine(_cards.PackTuning);
            RefreshDaily();

            _sortText.text = "↕ " + Loc.T("kadro.sirala." + (int)_sortMode);
            _filterText.text = "⌄ " + Loc.T("kadro.filtre." + (int)_filterMode);
            _emptyText.text = Loc.T("kadro.bos");

            RefreshTabs();
            RefreshSetInfo();

            int[] cards = CardCollectionCatalogue.CardsInSet(_selectedSet);
            for (int i = 0; i < cards.Length; i++)
            {
                _setState[i] = _cards.CardState(cards[i]);
                RefreshTile(cards[i], _setState[i]);
            }
            ReflowTiles(cards);

            if (_portraitSummary != null)
            {
                _portraitSummary[0].text = _cards.OwnedCardCount + "/" + _cards.CardCount;
                _portraitSummary[1].text = _cards.CompletedSetCount + "/" + _cards.SetCount;
                _portraitSummary[2].text = _cards.UpgradeReadyCount().ToString(Culture);
                _portraitSummary[3].text = _cards.DailyPackReady ? "+1" : "—";
                _portraitFooter.text = string.Format(Loc.T("koleksiyon.paket"), _cards.UnopenedPackCount);
                _packChipLabel.text = _portraitFooter.text;
                _sortText.text = Loc.T("kadro.sirala." + (int)_sortMode);
                _portraitQuickLabels[0].text = Loc.T("kadro.filtre.1");
                _portraitQuickLabels[1].text = Loc.T("kadro.filtre.3");
                _portraitQuickLabels[2].text = Loc.T("kadro.filtre.0");
                _portraitSetTitle.text = SetName(_selectedSet);
                var set = _cards.SetState(_selectedSet);
                _portraitSetProgress.text = set.OwnedCount + " / " + set.TotalCount;
                _setBonusLabel.text = EffectLine(set.Bonus, set.BonusValue);
                _setBonusLabel.color = Ink;
                _packCountLabel.text = _cards.UnopenedPackCount.ToString(Culture);
                _portraitSetTitle.text = SetName(_selectedSet);
                for (int i = 0; i < 3; i++)
                {
                    var mode = i == 0 ? RosterFilterMode.Owned : i == 1 ? RosterFilterMode.UpgradeReady : RosterFilterMode.All;
                    _portraitQuickLabels[i].fontStyle = mode == _filterMode ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            if (_selected >= 0 && _inspect != null && _inspect.Visible) ShowDetails(_selected);
        }

        private void RefreshDaily()
        {
            bool ready = _cards.DailyPackReady;
            Dress(_dailyPack, ready, ButtonYellow);
            if (ready) _dailyPackText.text = Loc.T("koleksiyon.gunluk");
            else RefreshCountdown();
        }

        /// <summary>Re-inks the countdown only when the shown second has actually moved.</summary>
        private void RefreshCountdown()
        {
            if (_cards == null || _dailyPackText == null) return;
            long left = _cards.DailyPackSecondsLeft;
            if (left == _lastCountdown) return;
            _lastCountdown = left;
            _dailyPackText.text = string.Format(Loc.T("koleksiyon.gunluk_sure"), HoursClock(left));
        }

        private void RefreshTabs()
        {
            for (int s = 0; s < _tab.Length; s++)
            {
                CollectionSetState set = _cards.SetState(s);
                _tabText[s].text = SetName(s) + "\n" + set.OwnedCount.ToString(Culture) + "/" + set.TotalCount.ToString(Culture);
                bool selected = s == _selectedSet;
                _tab[s].GetComponent<Image>().color = !selected ? new Color(0.62f, 0.66f, 0.74f, 1f)
                                                    : actionButton != null ? Color.white : ButtonBlue;
                _tabBadge[s].SetActive(set.NeedsAttention || AnyUpgradeIn(s));
                if (_portraitGrid != null)
                {
                    _tab[s].GetComponent<Image>().color = Color.clear;
                    _tabText[s].color = selected ? new Color(0.04f, 0.20f, 0.35f, 1) : new Color(0.23f, 0.33f, 0.45f, 1);
                    _tabText[s].fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                    _tabText[s].text = SetName(s);
                    _tabBadge[s].SetActive(false);
                    _portraitSelectedTabs[s].SetActive(selected);
                }
            }
        }

        private bool AnyUpgradeIn(int set)
        {
            int[] cards = CardCollectionCatalogue.CardsInSet(set);
            for (int i = 0; i < cards.Length; i++) if (_cards.CanUpgrade(cards[i])) return true;
            return false;
        }

        private void RefreshSetInfo()
        {
            CollectionSetState set = _cards.SetState(_selectedSet);
            _setDescLabel.text = Described(CardCollectionCatalogue.SetDescriptionKey(_selectedSet));
            string bonus = string.Format(Loc.T("koleksiyon.set_bonus"), EffectLine(set.Bonus, set.BonusValue));
            // The bonus is live the moment the set completes, whether or not the reward is taken.
            _setBonusLabel.text = bonus + "   ·   " + (set.BonusActive
                ? Loc.T("koleksiyon.set_aktif")
                : string.Format(Loc.T("koleksiyon.set_kalan"), set.Remaining));
            _setBonusLabel.color = set.BonusActive ? Good : _portraitGrid != null ? Paper : Ink;

            _setRewardLabel.text = string.Format(Loc.T("koleksiyon.odul"), RewardText(set.RewardKind, set.RewardAmount));
            _claimSetText.text = set.RewardClaimed ? Loc.T("gorev.alindi")
                               : Loc.T(set.RewardClaimable ? "gorev.al" : "gorev.kilitli");
            Dress(_claimSet, set.RewardClaimable, ButtonGreen);
        }

        private void RefreshTile(int card, in RosterCardState state)
        {
            bool owned = state.Owned;
            Color tint = TintOf(state.Tier);

            _tileStripe[card].color = tint;

            Sprite face = _cards.Config != null ? _cards.Config.FaceOf(CardCollectionCatalogue.IdOf(card)) : null;
            _tileFace[card].sprite = face != null ? face : UiSkin.Flat;
            _tileFace[card].type = face != null ? Image.Type.Simple : Image.Type.Sliced;
            // A near-silhouette for a card not yet found — the captain screen's rule, same reason.
            _tileFace[card].color = face != null
                ? (owned ? Color.white : new Color(0.30f, 0.33f, 0.39f, 0.90f))
                : new Color(tint.r, tint.g, tint.b, owned ? 0.85f : 0.25f);

            _tileName[card].text = CardName(card);
            _tileName[card].color = owned ? Ink : InkFaint;

            var kind = (CardCollection.EffectKind)state.Role;
            string rarity = Loc.T(RarityKeys[(int)state.Tier]);
            _tileEffect[card].text = owned
                ? rarity + " · " + EffectLine(kind, state.Effect)
                : rarity + " · " + Loc.T("kaptan.bulunmadi");
            _tileEffect[card].color = owned ? InkSoft : InkFaint;

            _tileStars[card].text = StarText(owned ? state.Level : 0);
            _tileStars[card].color = owned ? tint : InkFaint;

            string label;
            bool live;
            if (!owned) { label = "—"; live = false; }
            else if (state.IsMaxed) { label = Loc.T("sefer.azami"); live = false; }
            else
            {
                // The action when it can be taken, the progress when it cannot.
                live = state.CanUpgrade;
                label = live ? Loc.T("kaptan.yukselt")
                             : state.Duplicates.ToString(Culture) + "/" + state.DuplicatesRequired.ToString(Culture);
            }
            _tileBtnText[card].text = label;
            Dress(_tileBtn[card], live, ButtonGreen);

            _tileNew[card].SetActive(_cards.IsNew(card));
            if (_portraitGrid != null)
            {
                _tileName[card].color = Paper;
                _tileEffect[card].color = Ink;
                _tileFace[card].enabled = face != null;
                _tileStars[card].text = owned ? state.Level + "/" + CardCollection.MaxLevel : Loc.T("gorev.kilitli");
                _tileStars[card].color = Paper;
                _tileEffect[card].text = owned ? EffectLine(kind, state.Effect) : rarity;
                _tileBtnText[card].text = !owned ? Loc.T("gorev.kilitli") : label;
                _tileBtnText[card].color = Paper;
                int visualState = !owned ? 1 : _cards.IsNew(card) ? 2 : state.CanUpgrade ? 3 : state.IsMaxed ? 5 : state.Level > 1 ? 4 : 0;
                PortraitUiArt.Apply(_portraitState[card], "card-state-" + visualState);
                _portraitState[card].enabled = face == null;
            }
        }

        private void ReflowTiles(int[] cards)
        {
            for (int card = 0; card < _tile.Length; card++) _tile[card].gameObject.SetActive(false);

            int shown = RosterCardQuery.Fill(_setState, cards.Length, _sortMode, _filterMode, _visibleOrder);

            if (_portraitGrid != null)
            {
                var sprite = PortraitUiArt.Get("collection-collection-card-template");
                float height = _portraitCardWidth * sprite.rect.height / sprite.rect.width;
                float left = (868 - _portraitCardWidth * 2 - _portraitCardGap) * 0.5f;
                for (int position = 0; position < shown; position++)
                {
                    int card = cards[_visibleOrder[position]];
                    RectTransform tile = _tile[card];
                    tile.anchorMin = tile.anchorMax = new Vector2(0, 1);
                    tile.pivot = new Vector2(0, 1);
                    tile.sizeDelta = new Vector2(_portraitCardWidth, height);
                    tile.anchoredPosition = new Vector2(left + (position % 2) * (_portraitCardWidth + _portraitCardGap), -8 - (position / 2) * (height + _portraitCardGap));
                    tile.gameObject.SetActive(true);
                }
                _portraitGrid.sizeDelta = new Vector2(0, Mathf.Max(622, ((shown + 1) / 2) * (height + _portraitCardGap) + 8));
                _emptyText.gameObject.SetActive(shown == 0);
                return;
            }

            // Laid out off the FULL grid rather than off how many the filter left, so one match stays
            // a tile rather than stretching over the whole band — the captain screen's lesson.
            const float pad = 0.006f;
            float colWidth = (PageRight - PageLeft) / GridColumns;
            float rowHeight = (GridTop - GridBottom) / _gridRows;
            for (int position = 0; position < shown; position++)
            {
                int card = cards[_visibleOrder[position]];
                int col = position % GridColumns, row = position / GridColumns;
                UiBuild.Anchor(_tile[card],
                    new Vector2(PageLeft + col * colWidth + pad, GridTop - (row + 1) * rowHeight + pad),
                    new Vector2(PageLeft + (col + 1) * colWidth - pad, GridTop - row * rowHeight - pad));
                _tile[card].gameObject.SetActive(true);
            }
            _emptyText.gameObject.SetActive(shown == 0);
        }

        private void ShowDetails(int card)
        {
            if (_cards == null || _inspect == null || !CardCollectionCatalogue.Exists(card)) return;
            _selected = card;
            // Looking is what clears NEW. It saves and raises Changed, whose refresh comes back here
            // with the badge already gone — MarkSeen is false the second time, so this cannot loop.
            _cards.MarkSeen(card);

            RosterCardState state = _cards.CardState(card);
            var kind = (CardCollection.EffectKind)state.Role;
            string rarity = Loc.T(RarityKeys[(int)state.Tier]);
            string identity = rarity + " · " + SetName(CardCollectionCatalogue.SetIndexOfCard(card))
                            + (state.Owned ? " · " + StarText(state.Level) : string.Empty);

            CardCollection.Tuning t = _cards.Tuning;
            string current = state.Owned
                ? string.Format(Loc.T("kadro.simdi"), EffectLine(kind, state.Effect))
                : Loc.T("kaptan.bulunmadi");
            int nextLevel = state.Owned ? Mathf.Min(CardCollection.MaxLevel, state.Level + 1) : 1;
            string next = state.IsMaxed
                ? Loc.T("sefer.azami")
                : string.Format(Loc.T("kadro.sonraki"),
                                EffectLine(kind, CardCollectionCatalogue.EffectValue(card, nextLevel, t)));
            string progress = !state.Owned ? Loc.T("kaptan.bulunmadi")
                            : state.IsMaxed ? Loc.T("sefer.azami")
                            : string.Format(Loc.T("kadro.ilerleme"), state.Duplicates, state.DuplicatesRequired);
            // The status line carries the card's own words. It used to repeat "LEVEL UP", which the
            // action button directly under it already says.
            string description = Described(CardCollectionCatalogue.DescriptionKey(card));

            int selected = card;
            _inspect.Show(CardName(card), identity, current, next, progress, description,
                          Loc.T("kaptan.yukselt"), state.CanUpgrade,
                          () => { if (_cards.TryUpgrade(selected)) Ping(); ShowDetails(selected); });
            if (_portraitDetailSheet != null)
            {
                var action = _portraitDetailSheet.Find("Aksiyon").GetComponent<Button>();
                UiBuild.Anchor((RectTransform)action.transform, new Vector2(0.46f, 0.055f), new Vector2(0.92f, 0.24f));
                action.GetComponentInChildren<Text>().color = Ink;
                Fit(action.GetComponentInChildren<Text>(), 28, 32);
                int visual = !state.Owned ? 1 : state.CanUpgrade ? 3 : state.IsMaxed ? 5 : state.Level > 1 ? 4 : 0;
                PortraitUiArt.Apply(_portraitDetailState, "card-state-" + visual);
                _portraitDetailSheet.Find("Mevcut").GetComponentInChildren<Text>().text = state.Owned ? EffectLine(kind, state.Effect) : Loc.T("kaptan.bulunmadi");
                _portraitDetailSheet.Find("Sonraki").GetComponentInChildren<Text>().text = state.IsMaxed ? Loc.T("sefer.azami")
                    : EffectLine(kind, CardCollectionCatalogue.EffectValue(card, nextLevel, t));
            }
        }

        // ------------------------------------------------------------------ text
        /// <summary>What one pack turned out to be, in the receipt's own terms: a new card, a copy
        /// banked toward a level, or a maxed card's copy paid out in gems — plus the set it finished.</summary>
        private string PullLine(in CardCollectionService.PackOpenReceipt r)
        {
            string name = CardName(r.Card);
            string line;
            if (r.WasNew)
                line = string.Format(Loc.T("koleksiyon.cekilen.yeni"), name);
            else if (r.WasOverflow)
                line = string.Format(Loc.T("koleksiyon.cekilen.tasma"), name, r.GemsPaid);
            else
                line = string.Format(Loc.T("koleksiyon.cekilen.kopya"), name, r.Duplicates,
                                     _cards.CardState(r.Card).DuplicatesRequired);

            line = Loc.T(RarityKeys[(int)r.Rarity]) + " · " + line;
            if (r.CompletedSet)
                line += "\n" + string.Format(Loc.T("koleksiyon.cekilen.set"), SetName(r.Set));
            return line;
        }

        /// <summary>Every live effect on one line, or a plain "none yet". Only non-zero kinds are
        /// listed — five "+0%" entries would bury the one a player has actually earned.</summary>
        private static string BonusSummary(in CardCollectionEffects e)
        {
            string line = string.Empty;
            for (int k = 0; k < CardCollection.EffectKindCount; k++)
            {
                double v = e.Of((CardCollection.EffectKind)k);
                if (v <= 0d) continue;
                if (line.Length > 0) line += "   ·   ";
                line += EffectLine((CardCollection.EffectKind)k, v);
            }
            return line.Length > 0
                ? string.Format(Loc.T("koleksiyon.aktif"), line)
                : Loc.T("koleksiyon.bonus_yok");
        }

        private static string EffectLine(CardCollection.EffectKind kind, double value)
        {
            int k = (int)kind;
            if (k < 0 || k >= EffectKeys.Length) return string.Empty;
            return string.Format(Loc.T(EffectKeys[k]), (value * 100d).ToString("0.#", Culture));
        }

        private static string RewardText(CardCollection.SetRewardKind kind, long amount)
        {
            switch (kind)
            {
                case CardCollection.SetRewardKind.Gems:
                    return CurrencyText.Gain(CurrencyId.Gems, amount);
                case CardCollection.SetRewardKind.ForemanCards:
                    return amount.ToString(Culture) + " " + Loc.T("ustabasi.kart");
                case CardCollection.SetRewardKind.CraftPoints:
                    return CurrencyText.Amount(CurrencyId.CraftPoints, amount);
                case CardCollection.SetRewardKind.Charts:
                    return CurrencyText.Gain(CurrencyId.Charts, amount);
                case CardCollection.SetRewardKind.Salvage:
                    return CurrencyText.Gain(CurrencyId.Salvage, amount);
                case CardCollection.SetRewardKind.Pack:
                    return string.Format(Loc.T("koleksiyon.paket_x"), amount);
                default:
                    return amount.ToString(Culture);
            }
        }

        /// <summary>How far each guarantee is away — the captain crate's line, word for word.</summary>
        private string PityLine(in CardCollectionPack.Tuning t)
        {
            string line = string.Empty;
            if (t.EpicPity > 0)
            {
                int left = Mathf.Max(1, t.EpicPity - _cards.PullsSinceEpic);
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.2"), left);
            }
            if (t.LegendaryPity > 0)
            {
                int left = Mathf.Max(1, t.LegendaryPity - _cards.PullsSinceLegendary);
                if (line.Length > 0) line += "\n";
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.3"), left);
            }
            return line;
        }

        /// <summary>
        /// A card's display name, from its <c>koleksiyon.kart.&lt;id&gt;.ad</c> row. A card authored
        /// without a row would print its key, so the id's own name part is shown instead
        /// ("Ore Scale") — the safety net for a card added to the catalogue before its translations.
        /// </summary>
        private static string CardName(int card)
        {
            string key = CardCollectionCatalogue.NameKey(card);
            string text = Loc.T(key);
            if (text != key) return text;
            string id = CardCollectionCatalogue.IdOf(card);
            int cut = id.IndexOf(CardCollectionCatalogue.IdSeparator);
            return Readable(cut >= 0 ? id.Substring(cut + 1) : id);
        }

        private static string SetName(int set)
        {
            string key = CardCollectionCatalogue.SetNameKey(set);
            if (key.Length == 0) return string.Empty;
            string text = Loc.T(key);
            return text != key ? text : Readable(CardCollectionCatalogue.Sets[set].Id);
        }

        /// <summary>A description row, or nothing at all when it is missing — a key printed as prose
        /// is worse than a blank line.</summary>
        private static string Described(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string text = Loc.T(key);
            return text != key ? text : string.Empty;
        }

        /// <summary>"pit_charter" → "Pit Charter".</summary>
        private static string Readable(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            char[] chars = id.Replace('_', ' ').ToCharArray();
            bool start = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (start && char.IsLetter(chars[i])) chars[i] = char.ToUpperInvariant(chars[i]);
                start = chars[i] == ' ';
            }
            return new string(chars);
        }

        private static string StarText(int level)
            => new string('★', Mathf.Clamp(level, 0, CardCollection.MaxLevel))
             + new string('☆', CardCollection.MaxLevel - Mathf.Clamp(level, 0, CardCollection.MaxLevel));

        /// <summary>h:mm:ss — the daily pack is up to a day away, which UiBuild.Clock's m:ss would
        /// print as "1439:59".</summary>
        private static string HoursClock(long seconds)
        {
            if (seconds < 0L) seconds = 0L;
            long h = seconds / 3600L, m = seconds / 60L % 60L, s = seconds % 60L;
            return h.ToString(Culture) + ":" + m.ToString("00", Culture) + ":" + s.ToString("00", Culture);
        }

        /// <summary>A rarity's colour, from the config the collection was built from; the serialized
        /// array is the fallback for a build (or a smoke test) with no config wired.</summary>
        private Color TintOf(RosterCardState.Rarity rarity)
        {
            int r = (int)rarity;
            Color[] fromConfig = _cards != null && _cards.Config != null ? _cards.Config.RarityTint : null;
            if (fromConfig != null && r >= 0 && r < fromConfig.Length) return fromConfig[r];
            return rarityTint != null && r >= 0 && r < rarityTint.Length ? rarityTint[r] : InkSoft;
        }

        private void ApplyPortraitLayout(RectTransform safe)
        {
            var page = new GameObject("PortraitPage", typeof(RectTransform));
            page.transform.SetParent(safe, false);
            var body = (RectTransform)page.transform;
            body.sizeDelta = new Vector2(1000, 1900);
            var children = new Transform[safe.childCount - 1];
            int index = 0;
            for (int i = 0; i < safe.childCount; i++)
                if (safe.GetChild(i) != page.transform) children[index++] = safe.GetChild(i);
            for (int i = 0; i < children.Length; i++) children[i].SetParent(body, false);
            page.AddComponent<PortraitPageFit>();

            // Every artwork surface is sized from its trimmed sprite bounds, never stretched.
            var background = (RectTransform)body.Find("Zemin");
            SetArtwork(background, "collection-collection-screen-background", 5, 65, 990);
            var paper = UiBuild.Flat(body, "CollectionContentBacking", new Color(0.025f, 0.12f, 0.25f, 1), Vector2.zero, Vector2.one);
            Place(paper, 59, 250, 882, 1407);
            paper.SetSiblingIndex(background.GetSiblingIndex() + 1);
            paper.GetComponent<Image>().raycastTarget = false;

            var titleBand = (RectTransform)body.Find("Serit");
            titleBand.GetComponent<Image>().enabled = false;
            Place(titleBand, 218, 94, 565, 95);
            UiBuild.Anchor((RectTransform)_titleLabel.transform.parent, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f));
            _titleLabel.color = Paper; Fit(_titleLabel, 32, 38);
            var close = body.Find("Kapat").GetComponent<Button>();
            SetArtwork((RectTransform)close.transform, "general-close-button", 835, 102, 88);
            close.GetComponentInChildren<Text>().text = string.Empty;

            var header = Art(body, "CollectionHeader", PortraitUiArt.Get("collection-collection-header"), Vector2.zero, Vector2.one);
            SetArtwork(header, "collection-collection-header", 74, 259, 852);
            _portraitSummary = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                _portraitSummary[i] = UiBuild.Label(Slot(header, "Summary" + i,
                    new Vector2(0.129f + i * 0.244f, 0.28f), new Vector2(0.241f + i * 0.244f, 0.68f)),
                    "Text", "0", 27, TextAnchor.MiddleCenter);
                _portraitSummary[i].color = Ink; Fit(_portraitSummary[i], 23, 27);
            }
            var setShortcut = Hit(header, "SetInfoShortcut", "", () => OpenPortraitPanel(_portraitSet));
            UiBuild.Anchor((RectTransform)setShortcut.transform, new Vector2(0.25f, 0), new Vector2(0.49f, 1));
            var dailyShortcut = Hit(header, "PackShortcut", "", () => OpenPortraitPanel(_portraitPack));
            UiBuild.Anchor((RectTransform)dailyShortcut.transform, new Vector2(0.75f, 0), Vector2.one);
            _collectedLabel.transform.parent.gameObject.SetActive(false);
            Place((RectTransform)_bonusLabel.transform.parent, 82, 373, 836, 44);
            _bonusLabel.alignment = TextAnchor.MiddleCenter; Fit(_bonusLabel, 23, 25);

            var tabs = Art(body, "CollectionSetTabs", PortraitUiArt.Get("collection-collection-set-tabs"), Vector2.zero, Vector2.one);
            SetArtwork(tabs, "collection-collection-set-tabs", 80, 432, 840);
            _portraitSelectedTabs = new GameObject[_tab.Length];
            // The three tabs form one contiguous export; transparent hit areas preserve their outlines.
            for (int s = 0; s < _tab.Length; s++)
            {
                _tab[s].transform.SetParent(tabs, false);
                UiBuild.Anchor((RectTransform)_tab[s].transform, new Vector2(s / 3f, 0), new Vector2((s + 1) / 3f, 1));
                TransparentControl(_tab[s]);
                UiBuild.Anchor((RectTransform)_tabText[s].transform, new Vector2(0.045f, 0.105f), new Vector2(0.955f, 0.29f));
                Fit(_tabText[s], 22, 24);
                _tabText[s].color = Ink;
                _tabBadge[s].SetActive(false);
                var marker = UiBuild.Flat(_tab[s].transform, "SelectedSet", new Color(0.15f, 0.91f, 1f, 1),
                    new Vector2(0.20f, -0.055f), new Vector2(0.80f, -0.03f));
                marker.GetComponent<Image>().raycastTarget = false;
                _portraitSelectedTabs[s] = marker.gameObject;
            }

            var browse = Art(body, "BrowseArt", PortraitUiArt.Get("collection-sort-filter-panel"), Vector2.zero, Vector2.one);
            SetArtwork(browse, "collection-sort-filter-panel", 100, 672, 800);
            var sort = body.Find("Sirala").GetComponent<Button>();
            sort.transform.SetParent(browse, false); TransparentControl(sort);
            UiBuild.Anchor((RectTransform)sort.transform, new Vector2(0.015f, 0.49f), new Vector2(0.76f, 0.97f));
            UiBuild.Anchor((RectTransform)_sortText.transform, new Vector2(0.26f, 0.13f), new Vector2(0.82f, 0.89f));
            _sortText.color = Ink; Fit(_sortText, 26, 30);
            var filter = body.Find("Filtre").GetComponent<Button>();
            filter.transform.SetParent(browse, false); TransparentControl(filter);
            UiBuild.Anchor((RectTransform)filter.transform, new Vector2(0.79f, 0.49f), new Vector2(0.995f, 0.99f));
            _filterText.transform.SetParent(body, false);
            Place((RectTransform)_filterText.transform, 125, 982, 750, 40);
            _filterText.color = Paper; Fit(_filterText, 23, 25);
            _portraitQuickLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                int choice = i;
                var quick = Hit(browse, "QuickFilter" + i, "", () =>
                {
                    _filterMode = choice == 0 ? RosterFilterMode.Owned : choice == 1 ? RosterFilterMode.UpgradeReady : RosterFilterMode.All;
                    if (_portraitScroll != null) _portraitScroll.verticalNormalizedPosition = 1;
                    Refresh();
                });
                UiBuild.Anchor((RectTransform)quick.transform, new Vector2(0.015f + i * 0.335f, 0.02f), new Vector2(0.325f + i * 0.335f, 0.41f));
                var label = quick.GetComponentInChildren<Text>();
                UiBuild.Anchor((RectTransform)label.transform, new Vector2(0.43f, 0.20f), new Vector2(0.95f, 0.84f));
                label.color = Ink; Fit(label, 20, 24);
                _portraitQuickLabels[i] = label;
            }

            var viewport = UiBuild.Flat(body, "CardViewport", new Color(0, 0, 0, 0.001f), Vector2.zero, Vector2.one);
            Place(viewport, 66, 1040, 868, 622);
            viewport.gameObject.AddComponent<RectMask2D>();
            _portraitScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _portraitScroll.horizontal = false; _portraitScroll.vertical = true;
            _portraitScroll.movementType = ScrollRect.MovementType.Clamped;
            _portraitScroll.scrollSensitivity = 55;
            _portraitScroll.viewport = viewport;
            _portraitGrid = Slot(viewport, "CardContent", new Vector2(0, 1), Vector2.one);
            _portraitGrid.pivot = new Vector2(0.5f, 1);
            _portraitScroll.content = _portraitGrid;
            var scrollTrack = UiBuild.Flat(viewport, "ScrollTrack", new Color(0.12f, 0.29f, 0.43f, 1), Vector2.zero, Vector2.one);
            Place(scrollTrack, 855, 8, 8, 606);
            var scrollbar = scrollTrack.gameObject.AddComponent<Scrollbar>();
            var thumb = UiBuild.Flat(scrollTrack, "ScrollThumb", new Color(0.25f, 0.80f, 1f, 1), Vector2.zero, Vector2.one);
            thumb.GetComponent<Image>().raycastTarget = false;
            scrollbar.handleRect = thumb; scrollbar.targetGraphic = thumb.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _portraitScroll.verticalScrollbar = scrollbar;
            _portraitScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _portraitState = new Image[_tile.Length];
            for (int c = 0; c < _tile.Length; c++)
            {
                var tile = _tile[c]; tile.SetParent(_portraitGrid, false);
                PortraitUiArt.Apply(tile.GetComponent<Image>(), "collection-collection-card-template");
                _tileStripe[c].enabled = false; _tileFrame[c].enabled = false;
                UiBuild.Anchor(_tileFace[c].rectTransform, new Vector2(0.17f, 0.41f), new Vector2(0.83f, 0.81f));
                var state = Art(tile, "CardState", PortraitUiArt.Get("card-state-1"), new Vector2(0.34f, 0.49f), new Vector2(0.66f, 0.79f));
                _portraitState[c] = state.GetComponent<Image>();
                UiBuild.Anchor((RectTransform)_tileName[c].transform.parent, new Vector2(0.19f, 0.885f), new Vector2(0.83f, 0.982f));
                UiBuild.Anchor((RectTransform)_tileEffect[c].transform.parent, new Vector2(0.29f, 0.236f), new Vector2(0.86f, 0.299f));
                UiBuild.Anchor((RectTransform)_tileStars[c].transform.parent, new Vector2(0.19f, 0.433f), new Vector2(0.88f, 0.485f));
                UiBuild.Anchor((RectTransform)_tileBtn[c].transform, new Vector2(0.12f, 0.055f), new Vector2(0.90f, 0.20f));
                TransparentControl(_tileBtn[c]);
                UiBuild.Anchor((RectTransform)_tileBtnText[c].transform, new Vector2(0.25f, 0.1f), new Vector2(0.96f, 0.9f));
                Fit(_tileName[c], 24, 28); Fit(_tileEffect[c], 21, 24); Fit(_tileStars[c], 21, 25); Fit(_tileBtnText[c], 24, 28);
                _tileName[c].alignment = _tileEffect[c].alignment = _tileStars[c].alignment = TextAnchor.MiddleCenter;
                _tileNew[c].GetComponent<Image>().enabled = false;
                _tileNew[c].GetComponentInChildren<Text>().enabled = false;
            }
            Place((RectTransform)_emptyText.transform.parent, 130, 1440, 740, 96);
            _emptyText.color = Paper; Fit(_emptyText, 28, 32);
            _portraitEmptyArt = Art((RectTransform)_emptyText.transform, "EmptyArt", PortraitUiArt.Get("empty-state-1"),
                new Vector2(0.33f, 1.30f), new Vector2(0.67f, 4.0f)).GetComponent<Image>();

            var footer = Hit(body, "OpenCollectionPacks", "", () => OpenPortraitPanel(_portraitPack));
            Place((RectTransform)footer.transform, 225, 1655, 550, 92);
            _portraitFooter = footer.GetComponentInChildren<Text>(); Fit(_portraitFooter, 28, 34);

            BuildPortraitPack(body);
            BuildPortraitSet(body);
            StylePortraitModal(body, "KadroDetayKarartma", "KadroDetay", "collection-card-detail-window");
            BuildPortraitResult(body);
            foreach (var label in body.GetComponentsInChildren<Text>(true))
            {
                if (_portraitFont != null) label.font = _portraitFont;
                label.raycastTarget = false;
            }
        }

        private void BuildPortraitPack(RectTransform body)
        {
            _portraitPack = PortraitOverlay(body, "CollectionPack");
            var pack = (RectTransform)body.Find("Paket");
            pack.SetParent(_portraitPack, false);
            SetArtwork(pack, "collection-pack-opening-panel", 75, 480, 850);
            _packIcon.enabled = false;
            var chip = (RectTransform)body.Find("Paketler");
            chip.SetParent(_portraitPack, false);
            chip.GetComponent<Image>().enabled = false;
            Place(chip, 190, 370, 620, 80);
            _packChipLabel.color = Paper; Fit(_packChipLabel, 32, 40);
            UiBuild.Anchor((RectTransform)_packCountLabel.transform.parent, new Vector2(0.75f, 0.53f), new Vector2(0.935f, 0.62f));
            _packCountLabel.color = Ink; _packCountLabel.alignment = TextAnchor.MiddleCenter; Fit(_packCountLabel, 27, 33);
            TransparentControl(_openPack); TransparentControl(_dailyPack);
            UiBuild.Anchor((RectTransform)_openPack.transform, new Vector2(0.065f, 0.085f), new Vector2(0.48f, 0.275f));
            UiBuild.Anchor((RectTransform)_dailyPack.transform, new Vector2(0.51f, 0.085f), new Vector2(0.935f, 0.275f));
            _openPackText.color = _dailyPackText.color = Ink;
            Fit(_openPackText, 29, 34); Fit(_dailyPackText, 25, 30);
            var odds = pack.Find("Oran").GetComponent<Button>();
            SetArtwork((RectTransform)odds.transform, "general-info-button", 756, 360, 70);
            odds.GetComponentInChildren<Text>().text = "";
            foreach (var label in new[] { _pityLabel, _sourceLabel })
                label.transform.parent.SetParent(_portraitPack, false);
            Place((RectTransform)_pityLabel.transform.parent, 140, 1120, 720, 128);
            Place((RectTransform)_sourceLabel.transform.parent, 140, 1270, 720, 130);
            _pityLabel.alignment = _sourceLabel.alignment = TextAnchor.MiddleCenter;
            _pityLabel.color = Paper; _sourceLabel.color = new Color(0.67f, 0.79f, 0.92f, 1);
            Fit(_pityLabel, 28, 31); Fit(_sourceLabel, 25, 28);
            _lastPullLabel.gameObject.SetActive(false);
            AddPortraitClose(_portraitPack, 850, 370);
            _portraitPack.gameObject.SetActive(false);
        }

        private void BuildPortraitSet(RectTransform body)
        {
            _portraitSet = PortraitOverlay(body, "CollectionSetInfo");
            var sheet = (RectTransform)body.Find("SetBilgi"); sheet.SetParent(_portraitSet, false);
            SetArtwork(sheet, "collection-set-info-panel", 170, 405, 660);
            _portraitSetTitle = UiBuild.Label(Slot(sheet, "SetTitle", new Vector2(0.12f, 0.64f), new Vector2(0.88f, 0.73f)),
                "Text", "", 32, TextAnchor.MiddleCenter);
            _portraitSetTitle.color = Ink; Fit(_portraitSetTitle, 30, 35);
            UiBuild.Anchor((RectTransform)_setDescLabel.transform.parent, new Vector2(0.12f, 0.51f), new Vector2(0.88f, 0.60f));
            UiBuild.Anchor((RectTransform)_setBonusLabel.transform.parent, new Vector2(0.12f, 0.435f), new Vector2(0.88f, 0.51f));
            _setDescLabel.alignment = _setBonusLabel.alignment = TextAnchor.MiddleCenter;
            _setDescLabel.color = Ink; _setBonusLabel.color = Ink;
            Fit(_setDescLabel, 27, 31); Fit(_setBonusLabel, 27, 31);
            _portraitSetProgress = UiBuild.Label(Slot(sheet, "SetProgress", new Vector2(0.15f, 0.33f), new Vector2(0.85f, 0.40f)),
                "Text", "", 28, TextAnchor.MiddleCenter);
            _portraitSetProgress.color = Ink; Fit(_portraitSetProgress, 26, 30);
            _setRewardLabel.transform.parent.SetParent(_portraitSet, false);
            Place((RectTransform)_setRewardLabel.transform.parent, 150, 1235, 700, 105);
            _setRewardLabel.color = Paper; _setRewardLabel.alignment = TextAnchor.MiddleCenter;
            Fit(_setRewardLabel, 28, 32);
            TransparentControl(_claimSet);
            UiBuild.Anchor((RectTransform)_claimSet.transform, new Vector2(0.13f, 0.065f), new Vector2(0.87f, 0.22f));
            _claimSetText.color = Ink; Fit(_claimSetText, 30, 36);
            AddPortraitClose(_portraitSet, 825, 365);
            _portraitSet.gameObject.SetActive(false);
        }

        private void StylePortraitModal(RectTransform body, string overlayName, string sheetName, string sprite)
        {
            var overlay = (RectTransform)body.Find(overlayName);
            var sheet = (RectTransform)overlay.Find(sheetName);
            overlay.GetComponent<Image>().sprite = null;
            overlay.GetComponent<Image>().color = new Color(0.015f, 0.045f, 0.10f, 0.995f);
            SetArtwork(sheet, sprite, 65, sheetName == "KadroDetay" ? 530 : 385, 870);
            if (sheetName == "KadroDetay")
            {
                _portraitDetailSheet = sheet;
                SetLabelSlot(sheet, "Baslik", 0.47f, 0.75f, 0.91f, 0.86f, 30);
                SetLabelSlot(sheet, "Kimlik", 0.47f, 0.64f, 0.91f, 0.72f, 23);
                SetLabelSlot(sheet, "Mevcut", 0.59f, 0.485f, 0.91f, 0.55f, 23);
                SetLabelSlot(sheet, "Sonraki", 0.59f, 0.285f, 0.91f, 0.35f, 23);
                var state = Art(sheet, "DetailCardState", PortraitUiArt.Get("card-state-1"), new Vector2(0.13f, 0.35f), new Vector2(0.36f, 0.72f));
                _portraitDetailState = state.GetComponent<Image>();
                var progress = (RectTransform)sheet.Find("Ilerleme"); progress.SetParent(overlay, false);
                Place(progress, 140, 1160, 720, 70);
                var status = (RectTransform)sheet.Find("Durum"); status.SetParent(overlay, false);
                Place(status, 140, 1240, 720, 130);
                foreach (var parent in new[] { progress, status })
                {
                    var label = parent.GetComponentInChildren<Text>(); label.color = Paper; Fit(label, 28, 31);
                }
                TransparentControl(sheet.Find("Aksiyon").GetComponent<Button>());
                var close = Hit(sheet, "DetailClose", "", () => _inspect.Hide());
                UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.89f, 0.81f), new Vector2(1, 1));
            }
        }

        private void BuildPortraitResult(RectTransform body)
        {
            _portraitResult = PortraitOverlay(body, "CollectionResult");
            var art = Art(_portraitResult, "ResultArt", PortraitUiArt.Get("collection-reward-result-screen"), Vector2.zero, Vector2.one);
            SetArtwork(art, "collection-reward-result-screen", 90, 440, 820);
            _portraitRewardLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var label = UiBuild.Label(Slot(art, "RewardValue" + i, new Vector2(0.10f + i * 0.29f, 0.26f),
                    new Vector2(0.29f + i * 0.29f, 0.33f)), "Text", "—", 28, TextAnchor.MiddleCenter);
                label.color = Ink; Fit(label, 27, 32); _portraitRewardLabels[i] = label;
            }
            _portraitResultText = UiBuild.Label(Slot(_portraitResult, "Receipt", Vector2.zero, Vector2.one), "Text", "", 32, TextAnchor.MiddleCenter);
            Place((RectTransform)_portraitResultText.transform.parent, 130, 1320, 740, 190);
            Fit(_portraitResultText, 30, 35);
            var done = Hit(art, "Done", "", () => _portraitResult.gameObject.SetActive(false));
            UiBuild.Anchor((RectTransform)done.transform, new Vector2(0.26f, 0.015f), new Vector2(0.77f, 0.225f));
            _portraitResult.gameObject.SetActive(false);
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
            rect.localScale = Vector3.one;
        }

        private static void SetArtwork(RectTransform rect, string name, float x, float y, float width)
        {
            Sprite sprite = PortraitUiArt.Get(name);
            PortraitUiArt.Apply(rect.GetComponent<Image>(), sprite);
            Place(rect, x, y, width, width * sprite.rect.height / sprite.rect.width);
        }

        private static void TransparentControl(Button button)
        {
            var image = button.GetComponent<Image>();
            image.sprite = null; image.overrideSprite = null; image.color = Color.clear;
            image.raycastTarget = true; button.transition = Selectable.Transition.None;
        }

        private static Button Hit(RectTransform parent, string name, string text, UnityEngine.Events.UnityAction click)
        {
            var button = UiBuild.Btn(parent, name, text, null, Color.clear, 28, click);
            TransparentControl(button);
            return button;
        }

        private static RectTransform PortraitOverlay(RectTransform body, string name)
        {
            var overlay = UiBuild.Flat(body, name, new Color(0.015f, 0.045f, 0.10f, 0.98f), Vector2.zero, Vector2.one);
            overlay.GetComponent<Image>().sprite = null;
            var dismiss = overlay.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => overlay.gameObject.SetActive(false));
            return overlay;
        }

        private static void OpenPortraitPanel(RectTransform panel)
        {
            if (panel == null) return;
            panel.SetAsLastSibling(); panel.gameObject.SetActive(true);
        }

        private static void AddPortraitClose(RectTransform overlay, float x, float y)
        {
            var close = Hit(overlay, "ClosePanel", "", () => overlay.gameObject.SetActive(false));
            SetArtwork((RectTransform)close.transform, "general-close-button", x, y, 80);
        }

        private static void SetLabelSlot(RectTransform parent, string name, float x0, float y0, float x1, float y1, int size)
        {
            var slot = (RectTransform)parent.Find(name);
            UiBuild.Anchor(slot, new Vector2(x0, y0), new Vector2(x1, y1));
            var label = slot.GetComponentInChildren<Text>();
            label.color = Ink; Fit(label, size - 2, size);
        }

        private void ShowPortraitResult(string text)
        {
            if (_portraitResult == null) return;
            _portraitResultText.text = text;
            _portraitResult.SetAsLastSibling(); _portraitResult.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- pieces
        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

        /// <summary>
        /// A live button is left untinted over the wired pill art, as on every roster screen. With
        /// no <see cref="actionButton"/> wired the fallback sprite is near-white, and untinted would
        /// put white text on a white pill — so an unwired screen keeps the button's own colour.
        /// </summary>
        private void Dress(Button b, bool live, Color tint)
        {
            b.interactable = live;
            if (_portraitGrid != null && b.GetComponent<Image>().sprite == null)
            {
                b.GetComponent<Image>().color = Color.clear;
                b.GetComponentInChildren<Text>().color = live ? Ink : InkSoft;
                return;
            }
            b.GetComponent<Image>().color = !live ? ButtonOff : actionButton != null ? Color.white : tint;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Panel;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = img.type == Image.Type.Simple && sprite != null;
            img.color = Color.white;
            img.raycastTarget = false;
            if (sprite != null && PortraitUiArt.MeshOf(sprite, out _))
                go.AddComponent<PortraitSpriteMesh>();
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = chipPill != null ? chipPill : UiSkin.Pill;
            img.type = Image.Type.Sliced;
            // The graphite pill is dark art under light text; its stand-in is not, so tint that one.
            img.color = chipPill != null ? Color.white : new Color(0.16f, 0.20f, 0.28f, 0.95f);
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A plain quad that keeps its own colour — the rarity stripe and the face plate.</summary>
        private static Image Quad(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        private static void Fit(Text label, int min, int max)
        {
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            float scale = accessibility != null ? accessibility.TextScale : 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(min * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(max * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }
}
