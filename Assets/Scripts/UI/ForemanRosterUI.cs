using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The masters screen: the chest down the left, fifteen cards on the right — three at each of the
    /// five stations, each showing his rarity, how many of his five stars he carries, what he is worth
    /// and how many cards are left to the next star.
    ///
    /// ONE CARD PREFAB, INSTANCED FIFTEEN TIMES, when <see cref="cardPrefab"/> is wired; a code-built
    /// card when it is not. That fallback is the point rather than a leftover: the screen has to keep
    /// working while the art is still being drawn, and a roster that shows nothing until somebody
    /// authors a sheet is a feature nobody can play against. Either way the CONTENT comes from
    /// <see cref="Foremen"/>'s own tables through <see cref="RefreshCard"/>, so an authored card can
    /// restyle the roster but cannot disagree with the maths — which is what fifteen hand-wired copies
    /// would eventually do. <see cref="BindPrefabCard"/> lists the child names it binds.
    ///
    /// THE RARITY COLOUR IS NOT WIRED HERE. It comes from <see cref="ForemanService.RarityTint"/>,
    /// which the plinth under the master's feet on the island reads too — a Legendary that is gold on
    /// the card and purple on the ground is worse than no colour at all.
    ///
    /// Refreshed on open and on <see cref="ForemanService.RosterChanged"/>. The only per-frame work is
    /// the free chest's countdown and the reveal's flips, and both stop the moment the screen closes.
    /// </summary>
    public sealed class ForemanRosterUI : MonoBehaviour
    {
        [Header("Yerleşim")]
        [Tooltip("Kart ızgarası: yatayda kaç sütun. On beş kart PORTREDE 3x5 olarak oturur.")]
        [SerializeField] private int columns = 3;
        [SerializeField] private int sortingOrder = 105;

        [Header("Görseller")]
        [Tooltip("Kart gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("İşe al / seviye atla düğmesi — MaviSet/btn_hap_mavi.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Kart çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Üstteki iki gösterge — MaviSet/gosterge_grafit. HUD'un para ve elmas hapıyla aynı parça.")]
        [SerializeField] private Sprite chipPill;
        [Tooltip("Bedelin solundaki elmas ikonu — Ikonlar/ikon_elmas.")]
        [SerializeField] private Sprite gemIcon;
        [Tooltip("Kesenin solundaki elmas — HUD'un kullandığı diamond_128x128, artı rozetiyle birlikte.")]
        [SerializeField] private Sprite purseGem;
        [Tooltip("On beş portre, KADRO SIRASIYLA: her istasyonun Sıradan / Nadir / Efsanevi ustası " +
                 "arka arkaya — maden(3), depo(3), rafineri(3), liman(3), pazar(3). Game.Core.Foremen." +
                 "Roster ile aynı sıra; kaydırmak kartların altındaki adamı değiştirir.")]
        [SerializeField] private Sprite[] portraits;
        [Tooltip("Sandık görseli — Ikonlar/ikon_sandik.")]
        [SerializeField] private Sprite chestIcon;

        [Header("Kart prefabı")]
        [Tooltip("Bir usta kartının hazır hâli; boş bırakılırsa kart koddan çizilir. On beş kopya " +
                 "alınır ve İSİMLE bağlanır — beklenen çocuk isimleri BindPrefabCard'da yazılı: " +
                 "Portre, Ad, Nadirlik, Beceri, Kartlar, Dugme, Aktif, Cubuk/Dolgu, Yildiz0..Yildiz4.\n\n" +
                 "Eksik isim sorun değil: bağlanamayan parça atlanır, kalan kart yine çalışır. " +
                 "Yildiz0..4 varsa yıldızlar görsel, yoksa yazıyla (★) çizilir.")]
        [SerializeField] private GameObject cardPrefab;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color cardHired = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color cardLocked = new Color(0.11f, 0.12f, 0.17f, 1f);

        // Kademe renkleri BURADA DEĞİL: ForemanConfig'de, adadaki kaideyle aynı yerde. Bkz. sınıf notu.
        /// <summary>The progress bar's own blue, used only when no bar art is wired. Not a tier
        /// colour — the bar means "cards toward the next star" at every tier.</summary>
        private static readonly Color BarBlue = new Color(0.33f, 0.62f, 0.92f, 1f);

        /// <summary>The two state badges. Green means he is working; slate means you have not met him.
        /// Neither is a rarity colour — a Legendary you have not found is still slate.</summary>
        private static readonly Color Green = new Color(0.13f, 0.62f, 0.35f, 1f);
        private static readonly Color Locked = new Color(0.34f, 0.38f, 0.45f, 1f);

        /// <summary>The rail button's icon, loaded at runtime — this screen has no Inspector to wire.</summary>
        private const string OpenerIconResource = "UI/Buttons/ustabasi";

        /// <summary>The odds badge on the chest shelf. Loaded the same way, for the same reason.</summary>
        private const string InfoIconResource = "UI/Buttons/bilgi";

        // The card is white art now, so every label on it has to be ink rather than paper.
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        /// <summary>The two header chips are graphite, so their numbers go the other way.</summary>
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        /// <summary>
        /// Where the ribbon's flat band sits, measured on the sprite: its middle is 0.677 up from the
        /// bottom, because the tails hang below it. A label centred in the rect lands on the tails.
        /// </summary>
        private const float RibbonBand = 0.677f;

        // ------------------------------------------------------------------ portrait bands
        /// <summary>
        /// The screen is one column of horizontal bands, because the game is PORTRAIT — Player
        /// Settings allows portrait and nothing else.
        ///
        /// It used to be side by side: the chest shelf a tall panel down the left at x 0.035-0.288 and
        /// the cards in the remaining two thirds. That is a landscape composition, and on a 1080x1920
        /// sheet it made the shelf a 273px-wide sliver 1555px tall with five card columns crammed into
        /// what was left. Stacked, the shelf gets the full width and a sane height, and the cards get
        /// three columns of five rather than five of three.
        ///
        /// Every band is named here rather than written into each builder so the screen can be
        /// re-proportioned in one place, and so two bands cannot silently overlap.
        /// </summary>
        private const float PageLeft = 0.030f, PageRight = 0.970f;
        private const float HeaderBottom = 0.930f;
        private const float ShelfTop = 0.915f, ShelfBottom = 0.735f;
        private const float BrowseTop = 0.715f, BrowseBottom = 0.665f;
        private const float GridTop = 0.650f, GridBottom = 0.030f;

        /// <summary>
        /// The decimal separator is the game's, not the handset's. Left to the current culture, a
        /// Turkish phone draws "×1,50" here while the wallet an inch away draws "1.5K" out of
        /// <see cref="Game.Core.NumberFormatter"/> — two number languages on one screen. The
        /// language of this game is the one the player picked, not the one the device was sold in,
        /// so the formatting has no business coming from the device either.
        /// </summary>
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        private ForemanService _foremen;
        private WalletService _wallet;
        private RectTransform _root;
        private Text _multiplier;
        private Text _titleLabel;
        private LocalizationService _loc;
        private Text _balance;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        // One entry per slot, built once. No allocation after Build().
        private readonly Text[] _name = new Text[Foremen.Count];
        private readonly Text[] _level = new Text[Foremen.Count];
        private readonly Text[] _effect = new Text[Foremen.Count];
        private readonly Text[] _cards = new Text[Foremen.Count];
        private readonly Button[] _action = new Button[Foremen.Count];
        private readonly Text[] _actionText = new Text[Foremen.Count];
        private readonly Image[] _card = new Image[Foremen.Count];
        private readonly Image[] _portrait = new Image[Foremen.Count];
        private readonly Image[] _fill = new Image[Foremen.Count];
        private readonly Image[] _rule = new Image[Foremen.Count];
        private readonly GameObject[] _activeMark = new GameObject[Foremen.Count];
        private readonly GameObject[] _lockMark = new GameObject[Foremen.Count];
        /// <summary>Star pips, flat: master m's i'th pip is at m * Foremen.MaxStars + i. Null
        /// throughout unless the wired prefab carries Yildiz0..4 — see BindPrefabCard.</summary>
        private readonly Image[] _star = new Image[Foremen.Count * Foremen.MaxStars];
        private readonly RectTransform[] _cardRoot = new RectTransform[Foremen.Count];
        private readonly RosterCardState[] _cardState = new RosterCardState[Foremen.Count];
        private readonly int[] _visibleOrder = new int[Foremen.Count];
        private RosterSortMode _sortMode;
        private RosterFilterMode _filterMode;
        private Text _sortText, _filterText, _emptyText;
        private RosterInspectPanel _inspect;
        private OddsSheetUI _odds;
        private int _selected = -1;

        // ---- the chest shelf ----
        private Text _chestTitle, _chestBlurb, _chestSingle, _chestBulk, _freeLabel;
        private Button _singleButton, _bulkButton, _freeButton;
        private float _clockTick;

        // ---- the reveal ----
        // Fifteen tiles is the most a batch can ever show: cards are aggregated per master, and there
        // are fifteen masters. Built once, hidden, and reused for every open.
        private RectTransform _reveal;
        private readonly Image[] _tile = new Image[Foremen.Count];
        private readonly Image[] _tileArt = new Image[Foremen.Count];
        private readonly Text[] _tileName = new Text[Foremen.Count];
        private readonly Text[] _tileCount = new Text[Foremen.Count];
        private readonly RectTransform[] _tileRect = new RectTransform[Foremen.Count];
        private readonly int[] _tileSlot = new int[Foremen.Count];
        private readonly bool[] _tileFresh = new bool[Foremen.Count];
        private readonly bool[] _tileTurned = new bool[Foremen.Count];
        private readonly int[] _batch = new int[Foremen.Count];
        private readonly int[] _starsBefore = new int[Foremen.Count];
        private Text _revealTitle, _revealHint;
        private ConfettiBurst _confetti;
        private int _tilesShown;
        private float _revealClock;
        private bool _revealing;

        private const float FlipSeconds = 0.34f;
        private const float FlipStagger = 0.13f;

        private void Awake()
        {
            _foremen = ServiceLocator.Get<ForemanService>();
            _wallet = ServiceLocator.Get<WalletService>();
            Build();
            BuildOpener();
            if (_foremen != null) _foremen.RosterChanged += OnRosterChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_foremen != null) _foremen.RosterChanged -= OnRosterChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        /// <summary>
        /// Şerit başlığı <see cref="Awake"/>'te bir kez yazılıyor; ekranı yeniden kurmak ikinci bir
        /// kanvas açacağı için yalnız o yazı tazeleniyor. Kartların üstündeki her satırı
        /// <see cref="Refresh"/> zaten baştan yazıyor.
        /// </summary>
        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("usta.baslik");
            Refresh();
            RefreshOpener();
        }

        private void OnRosterChanged(int station) { Refresh(); RefreshOpener(); }

        /// <summary>
        /// The opener sits in the HUD's bottom row, next to the goals opener - see
        /// <see cref="HudUI.AttachBottomButton"/> for why a code-built screen borrows a row button's
        /// rect instead of anchoring itself to a fraction of the screen.
        ///
        /// The row's counter chip is a notification, not a collection counter: like the captain
        /// opener, it appears only when at least one card can be upgraded now.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Button open = hud.AttachBottomButton(1, HudUI.MasterButtonName,
                                                 Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _foremen == null) return;
            int pending = _foremen.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0 && _openerCount != null) _openerCount.text = pending.ToString();
        }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }

        /// <summary>
        /// Closes the screen, and the reveal with it. The ceremony lives on its OWN canvas so it can
        /// out-sort the HUD, which means hiding this screen does not hide it — closed from anywhere
        /// else while a chest was still turning over, it would be left painted across the game.
        /// </summary>
        public void Hide()
        {
            _revealing = false;
            if (_reveal != null) _reveal.gameObject.SetActive(false);
            if (_inspect != null) _inspect.Hide();
            if (_odds != null) _odds.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }
        public void Toggle()
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf) Hide(); else Show();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UstabasiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);

            BuildHeader();
            BuildChestShelf();
            BuildBrowseBar();

            int rows = (Foremen.Count + columns - 1) / columns;
            // The grid is the bottom band, under the shelf and the browse bar — see the band
            // constants for why the screen is stacked rather than side by side.
            const float left = PageLeft, right = PageRight, top = GridTop, bottom = GridBottom;
            float cellW = (right - left) / columns, cellH = (top - bottom) / rows;
            const float padX = 0.008f, padY = 0.008f;

            for (int s = 0; s < Foremen.Count; s++)
            {
                int col = s % columns, row = s / columns;
                var aMin = new Vector2(left + col * cellW + padX, top - (row + 1) * cellH + padY);
                var aMax = new Vector2(left + (col + 1) * cellW - padX, top - row * cellH - padY);
                BuildCard(s, aMin, aMax);
            }

            BuildReveal();
            _inspect = new RosterInspectPanel(_root);
            _odds = new OddsSheetUI(_root);
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
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

            _emptyText = UiBuild.Label(Slot(_root, "FiltreBos", new Vector2(PageLeft, 0.28f), new Vector2(PageRight, 0.46f)),
                                       "Text", Loc.T("kadro.bos"), 28, TextAnchor.MiddleCenter);
            _emptyText.color = Paper;
            Fit(_emptyText, 16, 28);
            _emptyText.gameObject.SetActive(false);
        }

        private void CycleSort()
        {
            _sortMode = (RosterSortMode)(((int)_sortMode + 1) % 4);
            Refresh();
        }

        private void CycleFilter()
        {
            _filterMode = (RosterFilterMode)(((int)_filterMode + 1) % 4);
            if (_inspect != null) _inspect.Hide();
            Refresh();
        }

        /// <summary>
        /// The chest: one column down the left, three ways to open it. The free one carries its own
        /// countdown as its label, so the screen answers "is there anything for me right now" before
        /// the player has read a single card.
        /// </summary>
        private void BuildChestShelf()
        {
            RectTransform shelf = Art(_root, "Sandik", cardPanel,
                                      new Vector2(PageLeft, ShelfBottom), new Vector2(PageRight, ShelfTop));
            if (cardPanel == null) shelf.GetComponent<Image>().color = cardHired;

            // Chest art left, its name and blurb in the middle, the three pills stacked right. All
            // three used to be full-width rows down a narrow column; across a wide band they read as
            // one shelf instead of a list.
            _chestTitle = UiBuild.Label(Slot(shelf, "Baslik", new Vector2(0.195f, 0.660f), new Vector2(0.470f, 0.945f)),
                                        "Text", Loc.T("usta.sandik"), 30, TextAnchor.MiddleLeft);
            _chestTitle.color = Ink;
            Fit(_chestTitle, 18, 30);

            // The odds badge. Gems buy this chest and gems are sold for money, which makes it a paid
            // randomised mechanic — both stores require the chance to be readable before the purchase,
            // so this sits on the shelf beside the price rather than behind a settings menu.
            Sprite infoIcon = Resources.Load<Sprite>(InfoIconResource);
            Button odds = UiBuild.Btn(shelf, "Oran", infoIcon != null ? string.Empty : "i",
                                      infoIcon != null ? infoIcon : UiSkin.ButtonGrey,
                                      new Color(0.45f, 0.49f, 0.56f, 1f), 22,
                                      () => { if (_odds != null && _foremen != null)
                                                  _odds.ShowMasterChest(_foremen.ChestTuning); });
            UiBuild.Anchor((RectTransform)odds.transform,
                           new Vector2(0.482f, 0.690f), new Vector2(0.556f, 0.920f));

            Icon(shelf, "Gorsel", chestIcon != null ? chestIcon : cardPanel,
                 new Vector2(0.020f, 0.140f), new Vector2(0.180f, 0.870f));

            _chestBlurb = UiBuild.Label(Slot(shelf, "Aciklama", new Vector2(0.195f, 0.090f), new Vector2(0.560f, 0.630f)),
                                        "Text", string.Empty, 19, TextAnchor.UpperLeft);
            _chestBlurb.color = InkFaint;

            _singleButton = ShelfButton(shelf, "Tek", new Vector2(0.580f, 0.690f), new Vector2(0.985f, 0.955f),
                                        () => Open(1), out _chestSingle);
            _bulkButton = ShelfButton(shelf, "Toplu", new Vector2(0.580f, 0.375f), new Vector2(0.985f, 0.640f),
                                      () => Open(_foremen != null ? _foremen.ChestTuning.BulkCount : 10),
                                      out _chestBulk);
            _freeButton = ShelfButton(shelf, "Bedava", new Vector2(0.580f, 0.060f), new Vector2(0.985f, 0.325f),
                                      () => Open(0), out _freeLabel);
        }

        /// <summary>One pill on the chest shelf. Returns the button and hands back its label.</summary>
        private Button ShelfButton(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
                                   UnityEngine.Events.UnityAction onClick, out Text label)
        {
            Button b = UiBuild.Btn(parent, name, string.Empty,
                                   actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                   new Color(0.24f, 0.68f, 0.36f, 1f), 24, onClick);
            UiBuild.Anchor((RectTransform)b.transform, aMin, aMax);
            PillFit.Wrap(b.GetComponent<Image>());
            label = b.GetComponentInChildren<Text>();
            Fit(label, 15, 26);
            return b;
        }

        /// <summary>The blue ribbon across the top, the income multiplier left of it, the purse right.</summary>
        private void BuildHeader()
        {
            // 0.310..0.690, not 0.230..0.610: same 0.38 width, but centred. It used to sit 8% of the
            // sheet left of centre — the worst offset of any screen here, and visible without measuring.
            RectTransform band = Art(_root, "Serit", ribbon,
                                     new Vector2(0.310f, HeaderBottom), new Vector2(0.690f, 0.998f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("usta.baslik"), 38, TextAnchor.MiddleCenter);

            // Both chips are the HUD's own graphite pill. They sit on the same line as the HUD's
            // money and gem counters and used to be white, so the top of the screen read as two
            // different games stacked on each other.
            RectTransform sol = Chip(_root, "Carpan",
                                     new Vector2(PageLeft, 0.943f), new Vector2(0.205f, 0.996f));
            _multiplier = UiBuild.Label(Slot(sol, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                        "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _multiplier.color = Paper;

            RectTransform sag = Chip(_root, "Kese",
                                     new Vector2(0.625f, 0.943f), new Vector2(0.830f, 0.996f));
            // The gem overhangs the pill's left cap, the way it does on the HUD — inside the capsule
            // it would be a diamond in a dark box, and the plus badge would lose its edge.
            Icon(sag, "Elmas", purseGem != null ? purseGem : gemIcon,
                 new Vector2(-0.06f, 0.02f), new Vector2(0.26f, 1.02f));
            _balance = UiBuild.Label(Slot(sag, "Yazi", new Vector2(0.30f, 0f), new Vector2(0.92f, 1f)),
                                     "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _balance.color = Paper;

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty, closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       cardLocked, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // Tam köşede değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta, bu ekranın üstünde
            // çiziliyor ve tam köşeye konan kapat düğmesinin üstüne biniyor.
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.855f, 0.940f), new Vector2(0.955f, 0.998f));
        }

        private void BuildCard(int station, Vector2 aMin, Vector2 aMax)
        {
            if (cardPrefab != null) { BuildPrefabCard(station, aMin, aMax); return; }

            RectTransform card = Art(_root, "Kart_" + station, cardPanel, aMin, aMax);
            _cardRoot[station] = card;
            _card[station] = card.GetComponent<Image>();
            _card[station].raycastTarget = true;
            var inspect = card.gameObject.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            int selected = station;
            inspect.onClick.AddListener(() => ShowDetails(selected));
            if (cardPanel == null) _card[station].color = cardHired;

            // The tier mark. It used to be fixed per slot and set once; a master's tier now moves every
            // second star, so it is kept and repainted. It is a rule under the name rather than a tab
            // on the card's edge: the white panel's rim carries its own soft glow, and anything laid
            // across it reads as a stray rectangle.
            // The cell is WIDE, not tall: fifteen cards three across a portrait sheet is roughly
            // 338x238, so the card reads left-to-right — face on the left, everything about him
            // stacked on the right — rather than as the tall eight-row column it was when there were
            // eight cards four across a landscape one.
            _rule[station] = UiBuild.Flat(card, "Sirad", InkFaint,
                                          new Vector2(0.300f, 0.505f), new Vector2(0.560f, 0.522f))
                                    .GetComponent<Image>();

            // "AKTİF": which of a station's three is actually posted there. A FILLED PILL rather than
            // bare green text — three cards to a row and fifteen to a screen, the one fact the player
            // is scanning for is which of the three is working, and a word the same size as every
            // other word on the card does not answer that from arm's length.
            _activeMark[station] = Badge(card, "Aktif", Loc.T("usta.aktif"), Green,
                                         new Vector2(0.015f, 0.855f), new Vector2(0.285f, 0.995f));

            // And its opposite. A card you have not found is dimmed all over, but dimming is a
            // comparison — it only reads next to a bright card, and a new player's screen has none.
            // The word does not need one.
            _lockMark[station] = Badge(card, "Kilit", Loc.T("usta.kilitli"), Locked,
                                       new Vector2(0.015f, 0.855f), new Vector2(0.285f, 0.995f));

            _portrait[station] = Icon(card, "Portre", Portrait(station),
                                      new Vector2(0.020f, 0.080f), new Vector2(0.280f, 0.840f));

            _name[station] = UiBuild.Label(
                Slot(card, "Ad", new Vector2(0.300f, 0.730f), new Vector2(0.975f, 0.960f)),
                "Text", string.Empty, 28, TextAnchor.MiddleLeft);
            // "Rıza the Weighbridge" tek satirda karta sigmiyor; en uzun ad ne kadar kuculmesi
            // gerekiyorsa o kadar kuculuyor, tasip komsu karta girmiyor.
            Fit(_name[station], 13, 26);

            _level[station] = UiBuild.Label(
                Slot(card, "Seviye", new Vector2(0.300f, 0.540f), new Vector2(0.975f, 0.720f)),
                "Text", string.Empty, 24, TextAnchor.MiddleLeft);

            _effect[station] = UiBuild.Label(
                Slot(card, "Etki", new Vector2(0.300f, 0.300f), new Vector2(0.975f, 0.495f)),
                "Text", string.Empty, 30, TextAnchor.MiddleLeft);
            Fit(_effect[station], 14, 30);

            // Cards-toward-next-level. The bar is the collection made visible: gems can be bought,
            // duplicates cannot, so this is the line that actually paces the roster.
            _fill[station] = Bar(card, new Vector2(0.300f, 0.200f), new Vector2(0.625f, 0.270f));

            // Cards toward the next star. No price line any more: gems are spent at the chest, and a
            // star costs the cards on this bar and nothing else.
            _cards[station] = UiBuild.Label(
                Slot(card, "Kartlar", new Vector2(0.300f, 0.030f), new Vector2(0.625f, 0.180f)),
                "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            Fit(_cards[station], 12, 22);

            int captured = station;
            _action[station] = UiBuild.Btn(card, "Dugme", string.Empty,
                                           actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => OnPressed(captured));
            // Sag alt kose: hap sanatinin kendi orani 4:1 ve uclari yatayda dilimleniyor, o yuzden
            // genis ve alcak duruyor. Kartin tam genisligine yayilamaz — solunda cubuk ve kart sayisi
            // var.
            UiBuild.Anchor((RectTransform)_action[station].transform,
                           new Vector2(0.650f, 0.045f), new Vector2(0.975f, 0.275f));
            PillFit.Wrap(_action[station].GetComponent<Image>());
            _actionText[station] = _action[station].GetComponentInChildren<Text>();
        }

        /// <summary>
        /// One instance of the authored card, dropped into the grid cell and bound by child name.
        /// Everything it fails to find is simply left null, and <see cref="RefreshCard"/> skips nulls,
        /// so a half-finished prefab shows a half-finished card rather than throwing on open.
        /// </summary>
        private void BuildPrefabCard(int station, Vector2 aMin, Vector2 aMax)
        {
            var go = Instantiate(cardPrefab, _root);
            go.name = "Kart_" + station;
            go.SetActive(true);
            var card = go.GetComponent<RectTransform>();
            if (card == null) card = go.AddComponent<RectTransform>();
            UiBuild.Anchor(card, aMin, aMax);

            _cardRoot[station] = card;
            _card[station] = go.GetComponent<Image>();

            // A prefab that ships without a body sprite borrows the screen's, so the authored card
            // looks like the code-built one from the first frame and the art can be dropped in later
            // rather than being a precondition for the prefab being worth wiring at all.
            if (_card[station] != null && _card[station].sprite == null && cardPanel != null)
            {
                _card[station].sprite = cardPanel;
                _card[station].type = cardPanel.border.sqrMagnitude > 0f
                    ? Image.Type.Sliced : Image.Type.Simple;
            }

            BindPrefabCard(station, card);

            // The whole card opens the detail sheet, exactly as the code-built one does. On the card's
            // own Image when it has one, on a raycast target added here when it does not — a prefab
            // whose root is a bare RectTransform would otherwise be untappable.
            if (_card[station] == null)
            {
                var pad = go.AddComponent<Image>();
                pad.color = Color.clear;
                _card[station] = pad;
            }
            _card[station].raycastTarget = true;
            var inspect = go.GetComponent<Button>();
            if (inspect == null) inspect = go.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            int selected = station;
            inspect.onClick.RemoveAllListeners();
            inspect.onClick.AddListener(() => ShowDetails(selected));
        }

        /// <summary>
        /// The contract between this screen and an authored card, by child name:
        ///   Portre    Image   the master's face
        ///   Ad        Text    his name
        ///   Nadirlik  Text    Sıradan / Nadir / Efsanevi, tinted to match
        ///   Beceri    Text    the headline skill, "+250%"
        ///   Kartlar   Text    "12 / 20 kart"
        ///   Dugme     Button  star him up; its first Text is the label
        ///   Aktif     any     shown only on the master posted at that station
        ///   Kilit     any     shown only on a master you have not found yet
        ///   Dolgu     Image   the progress bar's fill, driven by its right anchor
        ///   Sirad     Image   a rule tinted by rarity
        ///   Yildiz0-4 Image   five star pips; absent means the stars are drawn as ★ in Nadirlik
        /// Names are matched anywhere in the card's hierarchy, so they can be nested however the
        /// layout wants.
        /// </summary>
        private void BindPrefabCard(int station, RectTransform card)
        {
            _portrait[station] = FindIn<Image>(card, "Portre");
            _name[station] = FindIn<Text>(card, "Ad");
            _level[station] = FindIn<Text>(card, "Nadirlik");
            _effect[station] = FindIn<Text>(card, "Beceri");
            _cards[station] = FindIn<Text>(card, "Kartlar");
            _fill[station] = FindIn<Image>(card, "Dolgu");
            _rule[station] = FindIn<Image>(card, "Sirad");

            Transform mark = FindIn<Transform>(card, "Aktif");
            _activeMark[station] = mark != null ? mark.gameObject : null;
            Transform locked = FindIn<Transform>(card, "Kilit");
            _lockMark[station] = locked != null ? locked.gameObject : null;

            for (int i = 0; i < Foremen.MaxStars; i++)
                _star[station * Foremen.MaxStars + i] = FindIn<Image>(card, "Yildiz" + i);

            _action[station] = FindIn<Button>(card, "Dugme");
            if (_action[station] != null)
            {
                _actionText[station] = _action[station].GetComponentInChildren<Text>(true);
                int captured = station;
                _action[station].onClick.RemoveAllListeners();
                _action[station].onClick.AddListener(() => OnPressed(captured));
            }
        }

        /// <summary>The first descendant with this exact name carrying a T, or null. Inactive children
        /// included: a prefab may ship its "Aktif" mark switched off.</summary>
        private static T FindIn<T>(Transform root, string name) where T : Component
        {
            var all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }

        /// <summary>
        /// The chest-open ceremony: up to eight cards face down, turning over one after another.
        ///
        /// ON ITS OWN CANVAS, at a sorting order above the HUD's settings gear. The gear draws at 120
        /// and this screen at 105, which is already why the close button had to be pulled in from the
        /// corner — a reveal at the roster's own order would have the gear punched through it.
        ///
        /// Built once and reused. Everything about a turn is a scale on a pre-made rect, so the
        /// ceremony allocates nothing while it plays.
        /// </summary>
        private void BuildReveal()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UstaSandikKanvas", sortingOrder + 25);
            _reveal = UiBuild.Flat(canvas, "Karartma", new Color(0.03f, 0.04f, 0.07f, 1f),
                                   Vector2.zero, Vector2.one);

            // The whole sheet is the skip target — a reveal you have to aim at to get past is a
            // reveal that stops being a reward the second time you see it.
            var skip = _reveal.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(OnRevealTapped);

            _revealTitle = UiBuild.Label(Slot(_reveal, "Baslik", new Vector2(0.1f, 0.845f), new Vector2(0.9f, 0.945f)),
                                         "Text", string.Empty, 44, TextAnchor.MiddleCenter);
            _revealTitle.color = Paper;

            _revealHint = UiBuild.Label(Slot(_reveal, "Devam", new Vector2(0.1f, 0.045f), new Vector2(0.9f, 0.125f)),
                                        "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _revealHint.color = InkFaint;

            // Five across and as many rows as the roster needs. It was a hardcoded 4x2 for eight
            // masters; at fifteen that put seven tiles below the sheet's own bottom edge, off-screen
            // and overlapping — a batch can name every master at once, so the grid has to hold all of
            // them.
            const int cols = 3;
            int rows = (_tile.Length + cols - 1) / cols;
            const float left = 0.06f, right = 0.94f, top = 0.840f, bottom = 0.130f;
            float cellW = (right - left) / cols, cellH = (top - bottom) / rows;

            for (int t = 0; t < _tile.Length; t++)
            {
                int col = t % cols, row = t / cols;
                var aMin = new Vector2(left + col * cellW + 0.012f, top - (row + 1) * cellH + 0.022f);
                var aMax = new Vector2(left + (col + 1) * cellW - 0.012f, top - row * cellH - 0.022f);

                RectTransform tile = Art(_reveal, "Kart_" + t, cardPanel, aMin, aMax);
                _tileRect[t] = tile;
                _tile[t] = tile.GetComponent<Image>();
                _tileArt[t] = Icon(tile, "Portre", null, new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.94f));
                _tileName[t] = UiBuild.Label(
                    Slot(tile, "Ad", new Vector2(0.05f, 0.155f), new Vector2(0.95f, 0.285f)),
                    "Text", string.Empty, 22, TextAnchor.MiddleCenter);
                _tileName[t].color = Paper;
                Fit(_tileName[t], 13, 22);
                _tileCount[t] = UiBuild.Label(
                    Slot(tile, "Adet", new Vector2(0.05f, 0.030f), new Vector2(0.95f, 0.150f)),
                    "Text", string.Empty, 30, TextAnchor.MiddleCenter);
                _tileCount[t].color = Paper;

                tile.gameObject.SetActive(false);
            }

            _reveal.gameObject.SetActive(false);

            // The celebration lives on THIS canvas, added last so its pieces draw over the tiles.
            //
            // Not the shared pools: the three ConfettiBursts in the scene sit at sorting order 108-109,
            // under an all-but-opaque reveal sheet at 130, so a burst fired from a turning card would
            // have played entirely behind it. Two of the three also belong to screens that are switched
            // off while this one is open, and a pool on a disabled object never ticks — so hunting for
            // one with FindAnyObjectByType could pick a burst that simply never animates.
            //
            // It hangs off the canvas rather than off the reveal sheet so it still runs for a tier
            // promotion, which happens with the sheet down.
            _confetti = canvas.gameObject.AddComponent<ConfettiBurst>();
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_reveal);
        }

        // ------------------------------------------------------------------ pieces
        /// <summary>A sliced art panel, falling back to the flat skin when nothing is wired.</summary>
        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Panel;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = img.type == Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A graphite capsule for the multiplier and the purse — the HUD's counter pill.</summary>
        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

        /// <summary>A non-stretching icon. Returns the image so the caller can dim or hide it.</summary>
        private static Image Icon(RectTransform parent, string name, Sprite sprite, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        /// <summary>
        /// The capsule bar: a track, and inside it a fill whose WIDTH is driven — see
        /// <see cref="Progress"/>. The fill used to be an <see cref="Image.Type.Filled"/> draw, which
        /// crops a stretched sprite rather than slicing it, so the round left cap arrived as a wedge
        /// and the right end as a straight cut. Sliced art plus <see cref="PillFit"/> gives a capsule
        /// that is a capsule at any length.
        /// </summary>
        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (barTrack == null) bedImage.color = cardLocked;

            // Inset by the track's own rim. Flush with the edges the fill covers the rim entirely
            // and a full bar stops looking like a bar at all — it becomes one solid blue capsule.
            RectTransform alan = Slot(bed, "DolguAlani", Vector2.zero, Vector2.one);
            alan.offsetMin = new Vector2(3f, 3f);
            alan.offsetMax = new Vector2(-3f, -3f);

            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(alan, false);
            var img = go.GetComponent<Image>();
            img.sprite = barFill;
            img.type = Image.Type.Sliced;
            img.preserveAspect = false;
            img.raycastTarget = false;
            if (barFill == null) img.color = BarBlue;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(img);
            return img;
        }

        /// <summary>Drives a bar built by <see cref="Bar"/>: the fill's right anchor is the progress.</summary>
        private static void Progress(Image fill, float t)
        {
            ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }

        /// <summary>Shrinks a label until it fits its box, so a long station name cannot run off the card.</summary>
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

        private Sprite Portrait(int master)
            => portraits != null && master >= 0 && master < portraits.Length ? portraits[master] : null;

        /// <summary>
        /// A filled state pill with a word in it, returned switched off. Both badges are the same
        /// shape on purpose: they occupy one corner and are mutually exclusive, so the player learns
        /// one place to look rather than two.
        /// </summary>
        private RectTransform BadgeRect(RectTransform parent, string name, string text, Color fill,
                                        Vector2 aMin, Vector2 aMax)
        {
            RectTransform pill = Art(parent, name, actionButton, aMin, aMax);
            var img = pill.GetComponent<Image>();
            img.color = fill;
            img.raycastTarget = false;
            if (actionButton != null) { img.type = Image.Type.Sliced; PillFit.Wrap(img); }

            Text label = UiBuild.Label(Slot(pill, "Text", new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.98f)),
                                       "Text", text, 20, TextAnchor.MiddleCenter);
            label.color = Paper;
            Fit(label, 10, 20);
            pill.gameObject.SetActive(false);
            return pill;
        }

        private GameObject Badge(RectTransform parent, string name, string text, Color fill,
                                 Vector2 aMin, Vector2 aMax)
            => BadgeRect(parent, name, text, fill, aMin, aMax).gameObject;

        /// <summary>A child rect anchored inside the card — the shape UiBuild's helpers want.</summary>
        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A rarity tint deep enough to read as text on white, alpha untouched.</summary>
        private static Color Darken(Color c) => new Color(c.r * 0.68f, c.g * 0.68f, c.b * 0.68f, 1f);

        /// <summary>The rarity's colour, from the one place both this screen and the island read.</summary>
        private Color TintFor(int master)
            => _foremen != null ? _foremen.RarityTintOf(master) : InkFaint;

        /// <summary>The master's own name, or his station's when the loc table has no row for him
        /// yet — a blank card is worse than a card labelled by where the man works.</summary>
        private static string NameOf(int master)
        {
            string key = "usta.ad." + Foremen.IdOf(master);
            string name = Loc.T(key);
            return string.IsNullOrEmpty(name) || name == key
                ? Loc.Id("usta.istasyon", Foremen.StationIds[Foremen.StationOf(master)])
                : name;
        }

        /// <summary>The three skill lines, one per line, as the detail sheet prints them.</summary>
        private string SkillLines(int master, int stars)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < Foremen.SkillCount; k++)
            {
                var skill = (Foremen.Skill)k;
                double now = _foremen.SkillValueAtStar(master, stars, skill);
                if (k > 0) sb.Append('\n');
                sb.AppendFormat(Culture, "{0}   +{1:0.#}%", Loc.T("usta.beceri." + k), now * 100d);
            }
            return sb.ToString();
        }

        /// <summary>h:mm:ss while the wait is long, mm:ss once it is short — eight hours of "480:00"
        /// is a number nobody can read as a time.</summary>
        private static string Countdown(long seconds)
        {
            if (seconds < 0L) seconds = 0L;
            long h = seconds / 3600L, m = (seconds % 3600L) / 60L, s = seconds % 60L;
            return h > 0L ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                          : m + ":" + s.ToString("00");
        }

        // ------------------------------------------------------------------ press
        private void OnPressed(int master)
        {
            if (_foremen == null) return;

            if (!_foremen.TryLevelUp(master)) { Refresh(); return; }

            // The fifth star is the moment a master is finished — worth more than the tap feedback the
            // four before it get. It used to be the tier promotion, which no longer happens: rarity is
            // drawn now, so the only thing a star can still change is how far along he is.
            if (_foremen.IsMaxed(master))
            {
                ServiceLocator.Get<HapticService>()?.Medium();
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
                Confetti();
            }
            else ServiceLocator.Get<HapticService>()?.Light();

            Refresh();   // RosterChanged already refreshes on success; this covers the refusal too
        }

        // ----------------------------------------------------------------- chest
        /// <summary>
        /// Opens chests. <paramref name="chests"/> of 0 means the free one. The stars are snapshotted
        /// first because the service unlocks a master as his first card lands, and "NEW" is the only
        /// thing on the reveal worth a confetti burst — after the call there is no way to tell an
        /// unlock from a card that merely arrived.
        /// </summary>
        private void Open(int chests)
        {
            if (_foremen == null) return;
            for (int s = 0; s < Foremen.Count; s++) _starsBefore[s] = _foremen.LevelOf(s);

            int[] got = chests > 0 ? _foremen.TryOpenChest(chests) : _foremen.TryClaimFreeChest();
            if (got == null || got.Length == 0)
            {
                ServiceLocator.Get<HapticService>()?.Light();
                Refresh();
                return;
            }
            BeginReveal(got);
        }

        private void BeginReveal(int[] got)
        {
            for (int s = 0; s < Foremen.Count; s++) _batch[s] = 0;
            for (int i = 0; i < got.Length; i++)
            {
                int slot = got[i];
                if (slot >= 0 && slot < Foremen.Count) _batch[slot]++;
            }

            _tilesShown = 0;
            for (int s = 0; s < Foremen.Count && _tilesShown < _tile.Length; s++)
            {
                if (_batch[s] <= 0) continue;
                int t = _tilesShown++;
                _tileSlot[t] = s;
                _tileFresh[t] = _starsBefore[s] <= Foremen.NotHired;
                _tileTurned[t] = false;
                _tileArt[t].sprite = Portrait(s);
                _tileArt[t].enabled = false;               // face down until it turns
                _tileName[t].text = string.Empty;
                _tileCount[t].text = string.Empty;
                _tile[t].color = cardLocked;
                _tileRect[t].localScale = Vector3.one;
                _tileRect[t].gameObject.SetActive(true);
            }
            for (int t = _tilesShown; t < _tile.Length; t++) _tileRect[t].gameObject.SetActive(false);

            // Both written per open rather than at build, so a language change between chests lands.
            _revealTitle.text = Loc.T("usta.sandik");
            _revealHint.text = Loc.T("usta.devam");
            _revealClock = 0f;
            _revealing = true;
            _reveal.gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
        }

        /// <summary>
        /// Turns a tile face up. Called from the flip once it is edge-on, and by the skip tap for
        /// every tile still face down, so a player who taps through sees the same cards.
        /// </summary>
        private void TurnTile(int t)
        {
            if (_tileTurned[t]) return;
            _tileTurned[t] = true;

            int s = _tileSlot[t];
            _tileArt[t].enabled = _tileArt[t].sprite != null;
            _tileArt[t].color = Color.white;
            _tile[t].color = TintFor(s);
            _tileName[t].text = _tileFresh[t] ? Loc.T("usta.yeni") : NameOf(s);
            _tileCount[t].text = "x" + _batch[s];

            if (_tileFresh[t])
            {
                ServiceLocator.Get<HapticService>()?.Medium();
                Confetti();
            }
            else ServiceLocator.Get<HapticService>()?.Light();
        }

        private void DismissReveal()
        {
            _revealing = false;
            if (_reveal != null) _reveal.gameObject.SetActive(false);
            Refresh();
        }

        /// <summary>First tap finishes the flips, second one closes.</summary>
        private void OnRevealTapped()
        {
            bool pending = false;
            for (int t = 0; t < _tilesShown; t++) if (!_tileTurned[t]) pending = true;

            if (!pending) { DismissReveal(); return; }
            for (int t = 0; t < _tilesShown; t++)
            {
                TurnTile(t);
                _tileRect[t].localScale = Vector3.one;
            }
            _revealing = false;
        }

        /// <summary>This screen's own burst — see <see cref="BuildReveal"/> for why it is not shared.</summary>
        private void Confetti()
        {
            if (_confetti != null) _confetti.Play();
        }

        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            if (_revealing)
            {
                _revealClock += Time.unscaledDeltaTime;
                bool anyLeft = false;
                for (int t = 0; t < _tilesShown; t++)
                {
                    float local = _revealClock - t * FlipStagger;
                    if (local <= 0f) { anyLeft = true; _tileRect[t].localScale = Vector3.one; continue; }
                    if (local >= FlipSeconds) { TurnTile(t); _tileRect[t].localScale = Vector3.one; continue; }

                    anyLeft = true;
                    float half = FlipSeconds * 0.5f;
                    if (local >= half) TurnTile(t);
                    // Edge-on at the halfway point, so the face swap is hidden inside the turn.
                    float x = Mathf.Abs(local - half) / half;
                    _tileRect[t].localScale = new Vector3(Mathf.Max(x, 0.02f), 1f, 1f);
                }
                if (!anyLeft) _revealing = false;
                return;
            }

            // The free chest's countdown. Once a second is as often as a clock label can change.
            _clockTick += Time.unscaledDeltaTime;
            if (_clockTick < 1f) return;
            _clockTick = 0f;
            RefreshChest();
        }

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_foremen == null || _root == null || !_root.gameObject.activeSelf) return;

            _multiplier.text = string.Format(Culture, "×{0:0.00}", _foremen.IncomeMultiplier);
            _balance.text = (_wallet != null ? _wallet.Gems : 0L).ToString();
            _sortText.text = "↕ " + Loc.T("kadro.sirala." + (int)_sortMode);
            _filterText.text = "⌄ " + Loc.T("kadro.filtre." + (int)_filterMode);
            _emptyText.text = Loc.T("kadro.bos");

            RefreshChest();
            for (int s = 0; s < Foremen.Count; s++)
            {
                _cardState[s] = _foremen.CardState(s);
                RefreshCard(s);
            }
            ReflowCards();
            if (_selected >= 0 && _inspect != null && _inspect.Visible) ShowDetails(_selected);
        }

        private void ReflowCards()
        {
            int count = RosterCardQuery.Fill(_cardState, Foremen.Count, _sortMode, _filterMode, _visibleOrder);
            for (int s = 0; s < Foremen.Count; s++) _cardRoot[s].gameObject.SetActive(false);

            int safeColumns = Mathf.Max(1, columns);
            // Rows come off the FULL roster, not off how many the filter left showing: a filter that
            // shows one card would otherwise stretch it over the whole band, and the grid would jump
            // size every time the filter changed.
            int rows = Mathf.Max(1, (Foremen.Count + safeColumns - 1) / safeColumns);
            const float left = PageLeft, right = PageRight, top = GridTop, bottom = GridBottom;
            const float padX = 0.008f, padY = 0.008f;
            float cellW = (right - left) / safeColumns;
            float cellH = (top - bottom) / rows;

            for (int position = 0; position < count; position++)
            {
                int station = _visibleOrder[position];
                int col = position % safeColumns;
                int row = position / safeColumns;
                RectTransform card = _cardRoot[station];
                UiBuild.Anchor(card,
                    new Vector2(left + col * cellW + padX, top - (row + 1) * cellH + padY),
                    new Vector2(left + (col + 1) * cellW - padX, top - row * cellH - padY));
                card.gameObject.SetActive(true);
            }
            _emptyText.gameObject.SetActive(count == 0);
        }

        private void RefreshChest()
        {
            if (_foremen == null || _singleButton == null) return;

            long one = _foremen.ChestCost(1);
            int bulk = _foremen.ChestTuning.BulkCount;

            _chestTitle.text = Loc.T("usta.sandik");
            _chestBlurb.text = Loc.T("usta.nereden");
            _chestSingle.text = string.Format("{0} x1   {1}", Loc.T("usta.ac"), one);
            _chestBulk.text = string.Format("{0} x{1}   {2}", Loc.T("usta.ac"), bulk, _foremen.ChestCost(bulk));

            Dress(_singleButton, _chestSingle, _foremen.CanOpenChest(1));
            Dress(_bulkButton, _chestBulk, _foremen.CanOpenChest(bulk));

            bool free = _foremen.FreeChestReady;
            _freeLabel.text = free
                ? Loc.T("usta.bedava")
                : string.Format("{0}   {1}", Loc.T("usta.bedava"), Countdown(_foremen.FreeChestSecondsLeft));
            Dress(_freeButton, _freeLabel, free);
        }

        /// <summary>
        /// Repaints one card. Every field is null-checked rather than assumed, because an authored
        /// prefab is allowed to leave pieces out — see <see cref="BindPrefabCard"/>.
        /// </summary>
        private void RefreshCard(int m)
        {
            RosterCardState state = _cardState[m];
            bool owned = state.Owned;
            bool maxed = state.IsMaxed;
            int stars = state.Level;
            Color tint = TintFor(m);

            if (_card[m] != null && cardPanel == null) _card[m].color = owned ? cardHired : cardLocked;

            // A master you do not have yet keeps his own portrait, but as a near-silhouette — the
            // card is white, so blanking him leaves a hole where the only thing worth looking at
            // should be, and a merely-greyed one is indistinguishable from a dark portrait.
            if (_portrait[m] != null)
            {
                _portrait[m].sprite = Portrait(m);
                _portrait[m].enabled = _portrait[m].sprite != null;
                _portrait[m].color = owned ? Color.white : new Color(0.30f, 0.33f, 0.39f, 0.90f);
            }
            if (_rule[m] != null) _rule[m].color = owned ? tint : InkFaint;

            // One badge or the other, never both and never neither-when-it-matters: posted, or not
            // found. A card you own but have not posted carries no badge, which is the quiet state.
            if (_activeMark[m] != null) _activeMark[m].SetActive(state.Busy);
            if (_lockMark[m] != null) _lockMark[m].SetActive(!owned);

            if (_name[m] != null)
            {
                _name[m].text = NameOf(m);
                _name[m].color = owned ? Ink : InkSoft;
            }

            // Rarity is the word, stars are how far into him you are. Rarity is a fact about the card
            // and never moves; only the star count does.
            string rarity = Loc.T("usta.nadirlik." + (int)Foremen.RankOf(m));
            if (_level[m] != null)
            {
                _level[m].text = !owned ? Loc.T("usta.bulunmadi")
                    : HasStarPips(m) ? rarity
                    : new string('★', stars) + new string('☆', Foremen.MaxStars - stars) + "  " + rarity;
                _level[m].color = owned ? Darken(tint) : InkSoft;
                Fit(_level[m], 12, 26);
            }
            PaintStars(m, owned ? stars : 0, tint);

            // The headline skill: what this master is worth to his own station right now. The other two
            // are on the detail sheet — fifteen cards five across have room for one number.
            if (_effect[m] != null)
            {
                _effect[m].text = string.Format(Culture, "+{0:0.#}%", state.Effect * 100d);
                _effect[m].color = owned ? Darken(tint) : InkFaint;
            }

            int have = state.Duplicates;
            int need = state.DuplicatesRequired;
            if (_fill[m] != null) Progress(_fill[m], state.Progress);
            if (_cards[m] != null) _cards[m].color = InkFaint;

            string cardsLine, actionLine;
            bool live;
            if (maxed)
            {
                cardsLine = string.Format("{0} / {0} {1}", need > 0 ? need : have, Loc.T("ustabasi.kart"));
                actionLine = Loc.T("ustabasi.azami");
                live = false;
            }
            else if (owned)
            {
                cardsLine = string.Format("{0} / {1} {2}", have, need, Loc.T("ustabasi.kart"));
                actionLine = Loc.T("usta.yildizatla");
                live = state.CanUpgrade;
            }
            else
            {
                // Nothing to press and no price to quote: a master arrives in a chest, not at a till.
                cardsLine = Loc.T("usta.sandiktan");
                actionLine = Loc.T("usta.bulunmadi");
                live = false;
            }

            if (_cards[m] != null) _cards[m].text = cardsLine;
            if (_actionText[m] != null) _actionText[m].text = actionLine;
            if (_action[m] != null) Dress(_action[m], _actionText[m], live);
        }

        /// <summary>True when the wired prefab gave this card real star pips, so the rarity label does
        /// not have to spell them out in ★ as well.</summary>
        private bool HasStarPips(int m) => _star[m * Foremen.MaxStars] != null;

        /// <summary>Lights the first <paramref name="stars"/> pips and dims the rest. A no-op on a card
        /// with no pips wired.</summary>
        private void PaintStars(int m, int stars, Color tint)
        {
            for (int i = 0; i < Foremen.MaxStars; i++)
            {
                Image pip = _star[m * Foremen.MaxStars + i];
                if (pip == null) continue;
                pip.color = i < stars ? tint : new Color(0.78f, 0.80f, 0.84f, 1f);
            }
        }

        /// <summary>
        /// The detail sheet: all three skills, and the two things you can do about them — spend cards
        /// on a star, or post him to his station in place of whoever is there.
        /// </summary>
        private void ShowDetails(int master)
        {
            if (_foremen == null || _inspect == null || !Foremen.Exists(master)) return;
            _selected = master;

            RosterCardState state = _foremen.CardState(master);
            int station = Foremen.StationOf(master);
            string name = NameOf(master);
            string rarity = Loc.T("usta.nadirlik." + (int)Foremen.RankOf(master));
            string where = Loc.Id("usta.istasyon", Foremen.StationIds[station]);
            string identity = state.Owned
                ? rarity + " · " + where + " · " + string.Format(Loc.T("atolye.seviye"), state.Level)
                : rarity + " · " + where + " · " + Loc.T("usta.bulunmadi");

            string skills = SkillLines(master, Mathf.Max(1, state.Level));
            string progress = state.Owned && !state.IsMaxed
                ? string.Format(Loc.T("kadro.ilerleme"), state.Duplicates, state.DuplicatesRequired)
                : state.IsMaxed ? Loc.T("sefer.azami") : Loc.T("usta.sandiktan");
            string status = !state.Owned ? Loc.T("usta.bulunmadi")
                : state.Busy ? Loc.T("usta.aktif")
                : state.IsMaxed ? Loc.T("sefer.azami")
                : state.CanUpgrade ? Loc.T("usta.yildizatla") : string.Empty;

            // Posting is offered only when it would change something: an unowned card has nobody to
            // post, and the man already at the station is already there.
            //
            // HIDDEN rather than greyed when there is nothing to do. A station you own one card at
            // has that card posted already (ForemanService fills empty posts as cards arrive), so a
            // disabled button was the ONLY state most players ever saw — and a control that is always
            // dead reads as broken rather than as inapplicable. The status line above already says
            // AKTİF for the man who holds the post.
            bool canPost = state.Owned && !state.Busy;
            string post = canPost ? Loc.T("usta.goreveal") : null;

            int selected = master;
            _inspect.Show(name, identity, null, null, skills, progress, status,
                          Loc.T("usta.yildizatla"), state.CanUpgrade,
                          () => { _foremen.TryLevelUp(selected); ShowDetails(selected); },
                          post, canPost,
                          () => { _foremen.TrySetActive(selected); ShowDetails(selected); });
        }

        /// <summary>
        /// Greys a pill and its label together. The blue button is one pre-coloured sprite, so an
        /// unaffordable press is dimmed by tint rather than by swapping to a second piece of art the
        /// kit does not have.
        /// </summary>
        private static void Dress(Button b, Text label, bool live)
        {
            b.interactable = live;
            b.GetComponent<Image>().color = live ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
            if (label != null) label.color = live ? Paper : new Color(0.88f, 0.90f, 0.93f, 1f);
        }
    }
}
