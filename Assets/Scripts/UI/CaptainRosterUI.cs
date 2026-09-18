using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The captain screen: the crate across the top, the fifteen captains in one scrolling column
    /// below it.
    ///
    /// Built in code for the same reason <see cref="GoalsUI"/>, <see cref="ForemanRosterUI"/> and
    /// <see cref="ChapterUI"/> are — the rows come out of <see cref="Captains.Roster"/>, so appending
    /// a captain should cost one entry in that table and nothing here.
    ///
    /// ONE COLUMN THAT SCROLLS, not a band divided by the roster. The game is portrait, so width is
    /// the scarce thing and height is not: a full-width row fits a portrait, a name, a role, five
    /// stars, a bar and a button, and two columns of that fit none of them. Dividing a fixed band by
    /// fifteen would have left each row about seventy pixels tall, which is the letterbox problem
    /// GoalsUI's comment already describes; a fixed row height that scrolls has no such ceiling.
    ///
    /// THE CRATE SHOWS ITS PITY. Both counters are on the card, in words, because a crate that hides
    /// them is a crate the player has to take on faith — and the whole reason the pity exists is that
    /// a run of bad luck should be visibly finite.
    /// </summary>
    public sealed class CaptainRosterUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 108;

        [Header("Görseller")]
        [Tooltip("Satır gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Aksiyon düğmesi — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("İlerleme çubuğunun yatağı ve dolgusu — Gostergeler/slider_yatak, bar_dolgu.")]
        [SerializeField] private Sprite barTrack;
        [SerializeField] private Sprite barFill;
        [Tooltip("Harita sayacı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Tooltip("Ekranın zemini — KaptanKiti/captain_card_panel. Boşsa kart paneli 'backdrop' " +
                 "rengiyle boyanır. Kendi koyu fonu olan bir görsel: satırlarda değil, yalnızca " +
                 "burada kullanılır, yoksa her satır kendi fonunu ve köşe parıltısını tekrarlar.")]
        [SerializeField] private Sprite backdropArt;

        [Tooltip("Zemin çerçevesinin kalınlığını böler. 1'de sanatın 146 piksellik kenarı olduğu gibi " +
                 "çizilir ve mavi çerçeve kartların arasından taşar; 2 onu ince bir kenara indirir.")]
        [SerializeField] private float backdropBorderScale = 2f;

        [Tooltip("Sandık kartının simgesi — KaptanKiti/captain_chest_icon. Boşsa çizilmez.")]
        [SerializeField] private Sprite chestIcon;

        [Tooltip("Harita sayacının simgesi — KaptanKiti/captain_gem_icon. Boşsa sayaç yalnız yazı.")]
        [SerializeField] private Sprite gemIcon;

        [Tooltip("Alt çubuktaki açıcı düğmenin simgesi — KaptanKiti/captain_opener_icon. " +
                 "Boşsa Resources/" + OpenerIconResource + " yüklenir.")]
        [SerializeField] private Sprite openerIcon;

        [Tooltip("Sandıktaki oran rozeti — KaptanKiti/captain_info_icon. " +
                 "Boşsa Resources/" + InfoIconResource + " yüklenir.")]
        [SerializeField] private Sprite infoIcon;

        [Tooltip("On beş portre, KADRO SIRASIYLA: Hasan, Bekir, Fikri, Cemil, Rıza, Şükrü, Necla, " +
                 "Sema, Sedef, Nazmi, Zeki, Leyla, Mahmut, Hikmet, Rasim. " +
                 "Game.Core.Captains.Roster ile aynı sıra.")]
        [SerializeField] private Sprite[] portraits;

        [Header("Kart prefabı")]
        [Tooltip("Bir kaptan kartının hazır hâli; boş bırakılırsa satır koddan çizilir. Beş kopya " +
                 "alınır ve İSİMLE bağlanır — beklenen çocuk isimleri BindPrefabRow'da yazılı: " +
                 "Portre, Ad, Gorev, Derece, Dolgu, Yukselt, Yildiz0..Yildiz4.\n\n" +
                 "Eksik isim sorun değil: bağlanamayan parça atlanır, kalan kart yine çalışır. " +
                 "Yildiz0..4 varsa yıldızlar görsel, yoksa yazıyla (★) çizilir.")]
        [SerializeField] private GameObject cardPrefab;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [Tooltip("Kartların üstünde durduğu zemin. Panel sanatı bağlıysa onu boyar.")]
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);
        [SerializeField] private Color track = new Color(0.10f, 0.11f, 0.16f, 1f);
        [SerializeField] private Color dupeFill = new Color(0.35f, 0.72f, 0.98f, 1f);

        [Header("Derece renkleri — Sıradan → Mitik")]
        [SerializeField]
        private Color[] gradeTint =
        {
            new Color(0.26f, 0.60f, 0.92f, 1f),   // Common
            new Color(0.20f, 0.80f, 0.70f, 1f),   // Rare
            new Color(0.62f, 0.38f, 0.92f, 1f),   // Epic
            new Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
            new Color(0.94f, 0.28f, 0.42f, 1f),   // Mythic
        };

        /// <summary>The rail button's icon. Missing until the art lands — see Docs/ASSETS.md.</summary>
        private const string OpenerIconResource = "UI/Buttons/kaptan";

        /// <summary>The odds badge on the crate card. Loaded the same way, for the same reason.</summary>
        private const string InfoIconResource = "UI/Buttons/bilgi";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);

        /// <summary>The state badge's three fills. Green is at the wheel, blue is away on a voyage,
        /// slate is not found. None of them is a grade colour — a Mythic you have not pulled is
        /// slate, the same as a Common you have not pulled.</summary>
        private static readonly Color BadgeHelm = new Color(0.13f, 0.62f, 0.35f, 1f);
        private static readonly Color BadgeSea = new Color(0.24f, 0.55f, 0.84f, 1f);
        private static readonly Color BadgeLocked = new Color(0.34f, 0.38f, 0.45f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        private const float RibbonBand = 0.560f;
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>A row's height in canvas reference pixels — the canvas is 1080x1920. Five rows
        /// divided the band exactly; fifteen do not fit it at any readable height, so the band scrolls
        /// and this is what holds a row at the size it already was instead of a fifteenth of a screen.</summary>
        private const float RowPixels = 221f;

        /// <summary>Gap between two rows, split half above and half below. Was 0.006 of the sheet.</summary>
        private const float RowGap = 12f;

        private CaptainService _captains;
        private LocalizationService _loc;
        private RectTransform _root;

        /// <summary>The scrolling content the rows live in. Its height is the VISIBLE row count times
        /// <see cref="RowPixels"/>, so a filter shortens the scroll instead of stretching the rows.</summary>
        private RectTransform _rowsContent;

        private Text _titleLabel, _chartsLabel, _collectedLabel, _pityLabel, _lastPullLabel, _sourceLabel;
        private RectTransform _chartsChip;
        private Button _openOne, _openBulk;
        private Text _openOneText, _openBulkText;
        private TMP_Text _openerCount;
        private GameObject _openerChip;

        private readonly Image[] _rowArt = new Image[Captains.Count];
        private readonly Image[] _rowPortrait = new Image[Captains.Count];
        private readonly GameObject[] _rowBadge = new GameObject[Captains.Count];
        private readonly Text[] _rowBadgeText = new Text[Captains.Count];
        private readonly Image[] _rowBadgeFill = new Image[Captains.Count];
        /// <summary>Star pips, flat: captain c's i'th pip is at c * Captains.MaxLevel + i. Null
        /// throughout unless the wired prefab carries Yildiz0..4 — see BindPrefabRow.</summary>
        private readonly Image[] _rowStar = new Image[Captains.Count * Captains.MaxLevel];
        private readonly Image[] _rowGrade = new Image[Captains.Count];
        private readonly Text[] _rowName = new Text[Captains.Count];
        private readonly Text[] _rowRole = new Text[Captains.Count];
        private readonly Image[] _rowFill = new Image[Captains.Count];
        private readonly Button[] _rowBtn = new Button[Captains.Count];
        private readonly Text[] _rowBtnText = new Text[Captains.Count];
        private readonly RectTransform[] _rowRoot = new RectTransform[Captains.Count];
        private readonly RosterCardState[] _cardState = new RosterCardState[Captains.Count];
        private readonly int[] _visibleOrder = new int[Captains.Count];
        private RosterSortMode _sortMode;
        private RosterFilterMode _filterMode;
        private Text _sortText, _filterText, _emptyText;
        private RosterInspectPanel _inspect;
        private OddsSheetUI _odds;
        private int _selected = -1;

        private void Awake()
        {
            _captains = ServiceLocator.Get<CaptainService>();
            Build();
            BuildOpener();
            if (_captains != null) _captains.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_captains != null) _captains.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("kaptan.baslik");
            if (_sourceLabel != null) _sourceLabel.text = Loc.T("kaptan.nereden");
            Refresh();
            RefreshOpener();
        }

        private void OnChanged() { Refresh(); RefreshOpener(); }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide()
        {
            if (_inspect != null) _inspect.Hide();
            if (_odds != null) _odds.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "KaptanKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();

            BuildHeader();
            BuildCrate();
            BuildBrowseBar();

            // Tek sütunda geniş satırlar, sayfanın tam genişliğinde — ama artık on beş kaptan var ve
            // bant beşi alacak kadar yüksek. Satırlar kaydırılan bir yüzeye kurulur; yerleşimlerini
            // ReflowRows yapar, çünkü sıralama ve filtre zaten her açılışta oraya uğruyor.
            BuildRowScroll();

            for (int c = 0; c < Captains.Count; c++)
                BuildRow(c, Vector2.zero, Vector2.one);
            _inspect = new RosterInspectPanel(_root);
            _odds = new OddsSheetUI(_root, OddsSheetUI.Skin.Captains(backdropArt, ribbon, actionButton, closeIcon));
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        private void BuildBrowseBar()
        {
            Button sort = UiBuild.Btn(_root, "Sirala", string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                      new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleSort);
            // Two equal buttons across the full content width. They used to stop at 0.610 and leave a
            // third of the bar empty, which put the whole row off-centre under a centred title.
            UiBuild.Anchor((RectTransform)sort.transform,
                           new Vector2(PageLeft, BrowseBottom), new Vector2(BrowseSplitLeft, BrowseTop));
            PillFit.Wrap(sort.GetComponent<Image>());
            _sortText = sort.GetComponentInChildren<Text>();
            Fit(_sortText, 12, 22);

            Button filter = UiBuild.Btn(_root, "Filtre", string.Empty,
                                        actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                        new Color(0.24f, 0.55f, 0.84f, 1f), 22, CycleFilter);
            UiBuild.Anchor((RectTransform)filter.transform,
                           new Vector2(BrowseSplitRight, BrowseBottom), new Vector2(PageRight, BrowseTop));
            PillFit.Wrap(filter.GetComponent<Image>());
            _filterText = filter.GetComponentInChildren<Text>();
            Fit(_filterText, 12, 22);

            _emptyText = UiBuild.Label(Slot(_root, "FiltreBos", new Vector2(PageLeft, 0.24f), new Vector2(PageRight, 0.42f)),
                                       "Text", Loc.T("kadro.bos"), 28, TextAnchor.MiddleCenter);
            // Ink on the kit's white sheet; paper only when the screen is still bare scrim behind it.
            _emptyText.color = backdropArt != null ? Ink : Paper;
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

        private void BuildHeader()
        {
            // 0.310..0.690, not 0.270..0.650: same 0.38 width, but centred. It used to sit 4% of the
            // sheet left of centre, which reads as a crooked title beside the Zemin behind it.
            RectTransform band = Art(_root, "Serit", ribbon,
                                     new Vector2(0.310f, 0.928f), new Vector2(0.690f, 0.998f));
            // Legacy Text draws its capitals high in the line box, so a label centred on the ribbon's
            // flat middle (0.64 of its height) put the title on the ribbon's top edge.
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.20f, RibbonBand - 0.18f),
                                        new Vector2(0.80f, RibbonBand + 0.18f)),
                                   "Text", Loc.T("kaptan.baslik"), 38, TextAnchor.MiddleCenter);
            Fit(_titleLabel, 20, 38);

            // Header chips hang off the sheet's outer edge, not the content margin — same line as the
            // masters screen's multiplier chip, so the two rosters share one header.
            _chartsChip = Chip(_root, "Harita", new Vector2(HeaderLeft, 0.941f),
                               new Vector2(0.290f, 0.995f));
            if (gemIcon != null)
            {
                RectTransform gem = Art(_chartsChip, "Elmas", gemIcon,
                                        new Vector2(0.045f, 0.12f), new Vector2(0.270f, 0.88f));
                var gemImage = gem.GetComponent<Image>();
                gemImage.preserveAspect = true;
                gemImage.raycastTarget = false;
            }
            _chartsLabel = UiBuild.Label(Slot(_chartsChip, "Yazi",
                                         new Vector2(gemIcon != null ? 0.300f : 0.08f, 0f),
                                         new Vector2(0.84f, 1f)),   // clear of the pill's right cap
                                         "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            _chartsLabel.color = Paper;
            Fit(_chartsLabel, 16, 30);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey, track, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            // UiBuild.Btn tints the face with its fallback colour; the kit's cross is pre-coloured and
            // came out nearly black under the dark track tint.
            if (closeIcon != null) closeImage.color = Color.white;
            // Tam köşede değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta bunun üstünde çiziliyor.
            // Sağ kenarı sol göstergenin sol kenarıyla aynı payda, ustalar ekranıyla aynı yerde.
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.845f, 0.940f), new Vector2(1f - HeaderLeft, 0.998f));
        }

        /// <summary>The crate card: what it costs, what the two counters are at, and what came out.</summary>
        private void BuildCrate()
        {
            RectTransform c = Art(_root, "Sandik", cardPanel,
                                  new Vector2(PageLeft, CrateBottom), new Vector2(PageRight, CrateTop));

            // Two columns. Everything the card SAYS stacks down the left — name, count, pity, last
            // pull — and everything it DOES stacks down the right, the two open pills. The pity text
            // used to be a narrow third column wedged between them, wrapping every other word.
            //
            // The chest sits left of the title and pushes it right, rather than going in the empty
            // middle: a crate card whose picture is not beside its name reads as two cards.
            bool chest = chestIcon != null;
            if (chest)
            {
                RectTransform box = Art(c, "SandikSimge", chestIcon,
                                        new Vector2(0.035f, 0.735f), new Vector2(0.160f, 0.955f));
                var boxImage = box.GetComponent<Image>();
                boxImage.preserveAspect = true;
                boxImage.raycastTarget = false;
            }
            Text title = UiBuild.Label(Slot(c, "Baslik", new Vector2(chest ? 0.175f : 0.035f, 0.745f),
                                            new Vector2(0.480f, 0.945f)),
                                       "Text", Loc.T("kaptan.sandik"), 34, TextAnchor.MiddleLeft);
            title.color = Ink;
            Fit(title, 18, 34);

            // Charts cannot be bought, so this crate is outside the platforms' paid-loot-box rule. The
            // badge is here anyway: the card already shows how far each guarantee is away, and the
            // weights behind it are the half that was still taken on faith.
            Sprite badge = infoIcon != null ? infoIcon : Resources.Load<Sprite>(InfoIconResource);
            Button odds = UiBuild.Btn(c, "Oran", badge != null ? string.Empty : "i",
                                      badge != null ? badge : UiSkin.ButtonGrey,
                                      new Color(0.45f, 0.49f, 0.56f, 1f), 22,
                                      () => { if (_odds != null && _captains != null)
                                                  _odds.ShowCaptainCrate(_captains.CrateTuning,
                                                      g => _captains.GradeTint((Captains.Grade)g)); });
            var oddsImage = odds.GetComponent<Image>();
            if (badge != null)
            {
                oddsImage.type = Image.Type.Simple;
                oddsImage.preserveAspect = true;
                oddsImage.color = Color.white;
            }
            UiBuild.Anchor((RectTransform)odds.transform,
                           new Vector2(0.500f, 0.765f), new Vector2(0.580f, 0.935f));

            _collectedLabel = UiBuild.Label(Slot(c, "Toplandi", new Vector2(0.035f, 0.615f), new Vector2(0.590f, 0.725f)),
                                            "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _collectedLabel.color = InkSoft;
            Fit(_collectedLabel, 14, 26);

            _openOne = UiBuild.Btn(c, "AcBir", string.Empty,
                                   actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                   new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => Open(1));
            UiBuild.Anchor((RectTransform)_openOne.transform, new Vector2(0.620f, 0.580f), new Vector2(0.965f, 0.910f));
            PillFit.Wrap(_openOne.GetComponent<Image>());
            _openOneText = _openOne.GetComponentInChildren<Text>();
            UiBuild.Anchor(_openOneText.rectTransform, new Vector2(0.14f, 0.12f), new Vector2(0.86f, 0.88f));
            Fit(_openOneText, 14, 26);

            _openBulk = UiBuild.Btn(c, "AcCok", string.Empty,
                                    actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                    new Color(0.94f, 0.68f, 0.20f, 1f), 26,
                                    () => Open(_captains != null ? _captains.CrateTuning.BulkCount : 10));
            UiBuild.Anchor((RectTransform)_openBulk.transform, new Vector2(0.620f, 0.200f), new Vector2(0.965f, 0.530f));
            PillFit.Wrap(_openBulk.GetComponent<Image>());
            _openBulkText = _openBulk.GetComponentInChildren<Text>();
            UiBuild.Anchor(_openBulkText.rectTransform, new Vector2(0.14f, 0.12f), new Vector2(0.86f, 0.88f));
            Fit(_openBulkText, 14, 26);

            _pityLabel = UiBuild.Label(Slot(c, "Teselli", new Vector2(0.035f, 0.405f), new Vector2(0.590f, 0.605f)),
                                       "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _pityLabel.color = InkSoft;
            Fit(_pityLabel, 13, 22);

            _lastPullLabel = UiBuild.Label(Slot(c, "SonCekilis", new Vector2(0.035f, 0.160f), new Vector2(0.590f, 0.395f)),
                                           "Text", string.Empty, 24, TextAnchor.MiddleLeft);
            Fit(_lastPullLabel, 14, 24);

            // A thin rule over the footer, so the source line reads as the card's caption rather than
            // as one more thing floating under the second pill.
            Image rule = Flat(c, "Cizgi", new Vector2(0.035f, 0.140f), new Vector2(0.965f, 0.146f));
            rule.color = new Color(InkFaint.r, InkFaint.g, InkFaint.b, 0.35f);

            _sourceLabel = UiBuild.Label(Slot(c, "Kaynak", new Vector2(0.035f, 0.030f), new Vector2(0.965f, 0.130f)),
                                         "Text", Loc.T("kaptan.nereden"), 20, TextAnchor.MiddleCenter);
            _sourceLabel.color = InkFaint;
            Fit(_sourceLabel, 12, 20);
        }

        private void BuildRow(int captain, Vector2 aMin, Vector2 aMax)
        {
            if (cardPrefab != null) { BuildPrefabRow(captain, aMin, aMax); return; }

            RectTransform c = Art(_rowsContent, "Kaptan_" + captain, cardPanel, aMin, aMax);
            _rowRoot[captain] = c;
            _rowArt[captain] = c.GetComponent<Image>();
            _rowArt[captain].raycastTarget = true;
            var inspect = c.gameObject.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            int selected = captain;
            inspect.onClick.AddListener(() => ShowDetails(selected));

            // A grade stripe down the left edge — the fastest read on the screen, and the one thing a
            // collection row has to answer before anything else.
            _rowGrade[captain] = Flat(c, "Derece", new Vector2(0.020f, 0.12f), new Vector2(0.055f, 0.88f));

            // The face, between the grade stripe and the name. Blank until the art is wired, which is
            // why every label below still starts at the same x it did before there was one.
            _rowPortrait[captain] = Icon(c, "Portre", Portrait(captain),
                                         new Vector2(0.065f, 0.10f), new Vector2(0.205f, 0.90f));

            _rowName[captain] = UiBuild.Label(Slot(c, "Ad", new Vector2(0.215f, 0.55f), new Vector2(0.690f, 0.93f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _rowName[captain].color = Ink;
            Fit(_rowName[captain], 13, 26);

            _rowRole[captain] = UiBuild.Label(Slot(c, "Gorev", new Vector2(0.215f, 0.30f), new Vector2(0.690f, 0.53f)),
                                              "Text", string.Empty, 21, TextAnchor.MiddleLeft);
            _rowRole[captain].color = InkSoft;
            Fit(_rowRole[captain], 11, 21);

            _rowFill[captain] = Bar(c, new Vector2(0.215f, 0.09f), new Vector2(0.690f, 0.26f), dupeFill);

            // The state badge. Which captain is ACTUALLY SAILING was the one thing this screen never
            // said: the ship picks the best one you own by itself (ExpeditionService.CaptainAboard),
            // so before this you could level a captain and have no way of knowing whether he was the
            // one at the wheel.
            BuildBadge(captain, c);

            int captured = captain;
            _rowBtn[captain] = UiBuild.Btn(c, "Yukselt", string.Empty,
                                           actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 22,
                                           () => { if (_captains != null && _captains.TryLevelUp(captured)) Ping(); });
            UiBuild.Anchor((RectTransform)_rowBtn[captain].transform,
                           new Vector2(0.715f, 0.300f), new Vector2(0.972f, 0.700f));
            PillFit.Wrap(_rowBtn[captain].GetComponent<Image>());
            _rowBtnText[captain] = _rowBtn[captain].GetComponentInChildren<Text>();
            Fit(_rowBtnText[captain], 11, 22);   // "LEVEL UP" is longer in most languages than "3/2"
        }

        // ------------------------------------------------------------------ act
        private void Open(int crates)
        {
            if (_captains == null) return;
            int[] got = _captains.TryOpen(crates);
            if (got == null || got.Length == 0) return;

            Ping();
            _lastPullLabel.text = PullLine(got);
            _lastPullLabel.color = TintOf(Best(got));
        }

        /// <summary>The best thing in a batch — what the card leads with after a bulk open.</summary>
        private static int Best(int[] got)
        {
            int best = got[0];
            for (int i = 1; i < got.Length; i++)
                if (Captains.RankOf(got[i]) > Captains.RankOf(best)) best = got[i];
            return best;
        }

        /// <summary>
        /// One name for a single open; the best of the batch plus a count for a bulk one. Listing ten
        /// names would not fit and would bury the only one the player is looking for.
        /// </summary>
        private static string PullLine(int[] got)
        {
            int best = Best(got);
            string name = Loc.T("kaptan.ad." + Captains.IdOf(best));
            string grade = Loc.T("kaptan.derece." + (int)Captains.RankOf(best));
            return got.Length == 1
                ? grade + "\n" + name
                : grade + "\n" + name + "\n+" + (got.Length - 1);
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_captains == null || _root == null || !_root.gameObject.activeSelf) return;

            // The word, then the number. A chip reading "47K" on its own does not say what 47K IS,
            // and this is the only place the player ever sees charts counted.
            // One line: the chip grows taller on a tall phone, and best fit wraps before it shrinks,
            // so without non-breaking spaces the number dropped under the word.
            _chartsLabel.text = EtkinlikKit.OneLine(Loc.T("kaptan.harita") + "  "
                              + NumberFormatter.Format((double)_captains.Charts, 0));
            _collectedLabel.text = string.Format(Loc.T("kaptan.toplandi"),
                                                 _captains.OwnedCount, Captains.Count);
            _sortText.text = "↕ " + Loc.T("kadro.sirala." + (int)_sortMode);
            _filterText.text = "⌄ " + Loc.T("kadro.filtre." + (int)_filterMode);
            _emptyText.text = Loc.T("kadro.bos");

            CaptainCrate.Tuning ct = _captains.CrateTuning;
            // COUNT, THEN PRICE, WITH A SEPARATOR. These read "OPEN 100" and "OPEN 10 900" before,
            // so the single-open button quoted a price with no count and the ten-open button quoted
            // what looked like one number: ten thousand nine hundred. Both now say the same two things
            // in the same order. The price names what it is paid in — the charts chip is a screen away
            // from the thumb on the button — and takes the second line, since it no longer fits beside.
            _openOneText.text = string.Format("{0} ×1\n{1}", Loc.T("kaptan.ac"),
                                              CurrencyText.Amount(CurrencyId.Charts, _captains.CrateCost(1)));
            _openBulkText.text = string.Format("{0} ×{1}\n{2}", Loc.T("kaptan.ac"), ct.BulkCount,
                                               CurrencyText.Amount(CurrencyId.Charts, _captains.CrateCost(ct.BulkCount)));
            Dress(_openOne, _captains.CanOpen(1));
            Dress(_openBulk, _captains.CanOpen(ct.BulkCount));

            _pityLabel.text = PityLine(ct);

            for (int c = 0; c < Captains.Count; c++)
            {
                _cardState[c] = _captains.CardState(c);
                RefreshRow(c);
            }
            ReflowRows();
            if (_selected >= 0 && _inspect != null && _inspect.Visible) ShowDetails(_selected);
        }

        private void ReflowRows()
        {
            int count = RosterCardQuery.Fill(_cardState, Captains.Count, _sortMode, _filterMode, _visibleOrder);
            for (int c = 0; c < Captains.Count; c++) _rowRoot[c].gameObject.SetActive(false);

            // One full-width column. The row is a FIXED PIXEL height and the content grows to hold
            // however many the filter left showing — the opposite of the old band, which divided a
            // fixed band by the roster and would have made a fifteen-captain row 74px tall.
            _rowsContent.sizeDelta = new Vector2(0f, count * RowPixels);

            for (int position = 0; position < count; position++)
            {
                int captain = _visibleOrder[position];
                RectTransform card = _rowRoot[captain];
                UiBuild.Anchor(card,
                    new Vector2(0f, 1f - (position + 1) / (float)count),
                    new Vector2(1f, 1f - position / (float)count));
                card.offsetMin = new Vector2(0f, RowGap * 0.5f);
                card.offsetMax = new Vector2(0f, -RowGap * 0.5f);
                card.gameObject.SetActive(true);
            }
            _emptyText.gameObject.SetActive(count == 0);
        }

        /// <summary>
        /// The band the rows scroll inside. Clamped rather than elastic: the list is short enough that
        /// a rubber-band overshoot reads as the screen coming apart rather than as give.
        ///
        /// The mask, the ScrollRect and the viewport are one object on purpose — a ScrollRect whose
        /// viewport is its own rect is the simplest thing that works, and the row band has no header
        /// or scrollbar that would need to sit outside the mask.
        /// </summary>
        private void BuildRowScroll()
        {
            var viewGo = new GameObject("KadroGorunum", typeof(RectTransform), typeof(Image),
                                        typeof(ScrollRect), typeof(RectMask2D));
            viewGo.transform.SetParent(_root, false);
            RectTransform view = (RectTransform)viewGo.transform;
            UiBuild.Anchor(view, new Vector2(PageLeft, RowsBottom), new Vector2(PageRight, RowsTop));

            // Clear rather than absent: the ScrollRect needs something that takes a drag, and a
            // transparent Image is the cheapest raycast target that does not paint.
            var pad = viewGo.GetComponent<Image>();
            pad.color = Color.clear;

            var contentGo = new GameObject("Icerik", typeof(RectTransform));
            contentGo.transform.SetParent(view, false);
            _rowsContent = (RectTransform)contentGo.transform;
            _rowsContent.anchorMin = new Vector2(0f, 1f);
            _rowsContent.anchorMax = new Vector2(1f, 1f);
            _rowsContent.pivot = new Vector2(0.5f, 1f);
            _rowsContent.offsetMin = Vector2.zero;
            _rowsContent.offsetMax = Vector2.zero;
            _rowsContent.sizeDelta = new Vector2(0f, Captains.Count * RowPixels);

            ScrollRect scroll = viewGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 55f;
            scroll.viewport = view;
            scroll.content = _rowsContent;
        }

        /// <summary>
        /// How far each guarantee is away, in pulls. Shown rather than hidden: a run of bad luck the
        /// player can see the end of is a very different experience from one they cannot.
        /// </summary>
        private string PityLine(in CaptainCrate.Tuning ct)
        {
            string line = string.Empty;
            if (ct.EpicPity > 0)
            {
                int left = ct.EpicPity - _captains.SinceEpic;
                if (left < 1) left = 1;
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.2"), left);
            }
            if (ct.LegendaryPity > 0)
            {
                int left = ct.LegendaryPity - _captains.SinceLegendary;
                if (left < 1) left = 1;
                if (line.Length > 0) line += "\n";
                line += string.Format(Loc.T("kaptan.teselli"), Loc.T("kaptan.derece.3"), left);
            }
            return line;
        }

        private void RefreshRow(int captain)
        {
            RosterCardState state = _cardState[captain];
            bool owned = state.Owned;
            int level = state.Level;
            var grade = Captains.RankOf(captain);

            Color tint = TintOf(captain);
            if (_rowGrade[captain] != null) _rowGrade[captain].color = tint;
            if (_rowArt[captain] != null)
                _rowArt[captain].color = owned ? Color.white : new Color(0.82f, 0.84f, 0.88f, 1f);
            if (_rowPortrait[captain] != null)
            {
                _rowPortrait[captain].sprite = Portrait(captain);
                _rowPortrait[captain].enabled = _rowPortrait[captain].sprite != null;
                // A near-silhouette rather than a light grey: dimming only reads NEXT TO a bright
                // card, and a new player's screen has none to compare against.
                _rowPortrait[captain].color = owned ? Color.white
                                                    : new Color(0.30f, 0.33f, 0.39f, 0.90f);
            }

            // The prefab puts the badge and the star pips in the SAME top-right slot, so a visible
            // badge owns it — otherwise the pip it fails to cover pokes out from behind its cap.
            bool badged = RefreshBadge(captain, owned, state.Busy);

            // Locked entries stay named and visible as collection goals. Ownership is stated on the
            // second line and by the disabled action; hiding the name would turn a goal into a blank.
            if (_rowName[captain] != null)
            {
                _rowName[captain].text = Loc.T("kaptan.ad." + Captains.IdOf(captain));
                _rowName[captain].color = owned ? Ink : InkFaint;
            }

            // Grade, role, and how far along he is as STARS rather than "Lv 2". Five levels drawn as
            // five pips is the same ladder the masters use, and a collection screen counts things.
            string role = Loc.T("kaptan.rol." + Captains.RoleOf(captain));
            string rank = Loc.T("kaptan.derece." + (int)grade);
            if (_rowRole[captain] != null)
            {
                // Pips when they are on show, written stars when the badge has taken their slot —
                // a captain at the helm still has to say how far he is levelled.
                // One line, shrunk rather than wrapped: "Henüz bulunmadı" broke onto a second line
                // over the progress bar on tall phones.
                _rowRole[captain].text = EtkinlikKit.OneLine(!owned
                    ? string.Format("{0} · {1} · {2}", rank, role, Loc.T("kaptan.bulunmadi"))
                    : HasStarPips(captain) && !badged
                        ? string.Format("{0} · {1}", rank, role)
                        : string.Format("{0} · {1} · {2}", rank, role, StarText(level)));
                _rowRole[captain].color = owned ? InkSoft : InkFaint;
            }
            PaintStars(captain, owned ? level : 0, tint, !badged);

            int need = state.DuplicatesRequired;
            int have = state.Duplicates;
            if (_rowFill[captain] != null) Progress(_rowFill[captain], state.Progress);

            string label;
            bool live;
            if (!owned) { label = "—"; live = false; }
            else if (level >= Captains.MaxLevel) { label = Loc.T("sefer.azami"); live = false; }
            else
            {
                // The ACTION when it can be taken, the PROGRESS when it cannot. A button that only
                // ever reads "3/2" is a readout somebody has made clickable.
                live = state.CanUpgrade;
                label = live ? Loc.T("kaptan.yukselt") : have + "/" + need;
            }

            if (_rowBtnText[captain] != null) _rowBtnText[captain].text = label;
            if (_rowBtn[captain] != null) Dress(_rowBtn[captain], live);
        }

        /// <summary>
        /// The one badge, in three states: at the wheel, away on a voyage, or not found. Nothing at
        /// all for a captain you own who is simply ashore — that is the quiet state, and a badge on
        /// every row would say nothing.
        /// </summary>
        /// <summary>True when the badge ended up on show, so the caller knows the slot is taken.</summary>
        private bool RefreshBadge(int captain, bool owned, bool onVoyage)
        {
            if (_rowBadge[captain] == null) return false;

            string word;
            Color fill;
            if (!owned) { word = Loc.T("kaptan.kilitli"); fill = BadgeLocked; }
            else if (onVoyage) { word = Loc.T("kaptan.denizde"); fill = BadgeSea; }
            else if (captain == AtTheHelm()) { word = Loc.T("kaptan.dumende"); fill = BadgeHelm; }
            else { _rowBadge[captain].SetActive(false); return false; }

            if (_rowBadgeText[captain] != null) _rowBadgeText[captain].text = word;
            if (_rowBadgeFill[captain] != null) _rowBadgeFill[captain].color = fill;
            _rowBadge[captain].SetActive(true);
            return true;
        }

        /// <summary>
        /// Who the ship takes when you sail. The player never chose him — ExpeditionService picks the
        /// best-levelled captain owned — so this screen has to say who it landed on, or levelling a
        /// captain is an investment with no visible consequence until you are already at sea.
        ///
        /// Read on refresh rather than cached: a crate opened on this very screen can change it.
        /// </summary>
        private static int AtTheHelm()
        {
            var sea = ServiceLocator.Get<ExpeditionService>();
            return sea != null ? sea.CaptainAboard : -1;
        }

        private void ShowDetails(int captain)
        {
            if (_captains == null || _inspect == null || !Captains.Exists(captain)) return;
            _selected = captain;
            RosterCardState state = _captains.CardState(captain);
            string name = Loc.T("kaptan.ad." + Captains.IdOf(captain));
            string rarity = Loc.T("kaptan.derece." + (int)state.Tier);
            string role = Loc.T("kaptan.rol." + state.Role);
            string identity = state.Owned
                ? rarity + " · " + role + " · " + StarText(state.Level)
                : rarity + " · " + role;
            int nextLevel = state.Owned ? Mathf.Min(Captains.MaxLevel, state.Level + 1) : 1;
            string next = state.IsMaxed ? Loc.T("sefer.azami")
                                        : EffectDeltaAt(captain, state.Level, nextLevel)
                                          + "\n" + IncomeLine(captain, nextLevel);
            string progress = state.Owned && !state.IsMaxed
                ? string.Format(Loc.T("kadro.ilerleme"), state.Duplicates, state.DuplicatesRequired)
                : state.IsMaxed ? Loc.T("sefer.azami") : Loc.T("kaptan.bulunmadi");
            string status = !state.Owned ? Loc.T("kaptan.bulunmadi")
                : state.Busy ? Loc.T("kaptan.denizde")
                : captain == AtTheHelm() ? Loc.T("kaptan.dumende")
                : state.IsMaxed ? Loc.T("sefer.azami")
                : state.CanUpgrade ? Loc.T("kaptan.yukselt") : string.Empty;

            int selected = captain;
            _inspect.Show(name, identity,
                          string.Format(Loc.T("kadro.simdi"), EffectAt(captain, state.Level)
                              + "\n" + IncomeLine(captain, state.Level)),
                          string.Format(Loc.T("kadro.sonraki"), next),
                          progress, status, Loc.T("kaptan.yukselt"), state.CanUpgrade,
                          () => { _captains.TryLevelUp(selected); ShowDetails(selected); });
        }

        private string EffectAt(int captain, int level)
        {
            int role = Captains.RoleOf(captain);
            double first;
            double second;
            switch (role)
            {
                case Captains.Gunner:
                    first = (Captains.SalvageMultiplier(captain, level, _captains.Tuning) - 1d) * 100d;
                    return string.Format(Loc.T("kaptan.rol.1.not"), Percent(first));
                case Captains.Bosun:
                    first = Captains.RiskReduction(captain, level, _captains.Tuning) * 100d;
                    second = (1d - Captains.RepairMultiplier(captain, level, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.2.not"), Percent(first), Percent(second));
                case Captains.Purser:
                    first = Captains.DirectedShare(captain, level, _captains.Tuning) * 100d;
                    return string.Format(Loc.T("kaptan.rol.3.not"), Percent(first));
                default:
                    first = (Captains.ChartMultiplier(captain, level, _captains.Tuning) - 1d) * 100d;
                    return string.Format(Loc.T("kaptan.rol.0.not"), Percent(first));
            }
        }

        private string EffectDeltaAt(int captain, int fromLevel, int toLevel)
        {
            int role = Captains.RoleOf(captain);
            double first;
            double second;
            switch (role)
            {
                case Captains.Gunner:
                    first = (Captains.SalvageMultiplier(captain, toLevel, _captains.Tuning)
                           - Captains.SalvageMultiplier(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.1.not"), Percent(first));
                case Captains.Bosun:
                    first = (Captains.RiskReduction(captain, toLevel, _captains.Tuning)
                           - Captains.RiskReduction(captain, fromLevel, _captains.Tuning)) * 100d;
                    second = (Captains.RepairMultiplier(captain, fromLevel, _captains.Tuning)
                            - Captains.RepairMultiplier(captain, toLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.2.not"), Percent(first), Percent(second));
                case Captains.Purser:
                    first = (Captains.DirectedShare(captain, toLevel, _captains.Tuning)
                           - Captains.DirectedShare(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.3.not"), Percent(first));
                default:
                    first = (Captains.ChartMultiplier(captain, toLevel, _captains.Tuning)
                           - Captains.ChartMultiplier(captain, fromLevel, _captains.Tuning)) * 100d;
                    return string.Format(Loc.T("kaptan.rol.0.not"), Percent(first));
            }
        }

        private string IncomeLine(int captain, int level)
        {
            double bonus = (Captains.IncomeMultiplier(captain, level, _captains.Tuning) - 1d) * 100d;
            return string.Format(Loc.T("kaptan.gelir"), IncomePercent(bonus));
        }

        private static string Percent(double value) => value.ToString("0.#", Culture);

        private static string IncomePercent(double value) => Captains.IncomePercent(value);

        /// <summary>
        /// A grade's colour, from the service so the card and the captain at the ship's wheel agree.
        /// The serialized array below it is the fallback for a screen built with no service — the
        /// smoke tests do exactly that.
        /// </summary>
        private Color TintOf(int captain)
        {
            if (_captains != null) return _captains.GradeTintOf(captain);
            int g = (int)Captains.RankOf(captain);
            return gradeTint != null && g >= 0 && g < gradeTint.Length ? gradeTint[g] : InkSoft;
        }

        // ---------------------------------------------------------------- opener
        /// <summary>
        /// Order 3 in the HUD's bottom row, after goals, roster and chapters — see
        /// <see cref="HudUI.AttachBottomButton"/> for why a code-built screen borrows a real row
        /// button's rect. The chip counts captains ready to be levelled.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = openerIcon != null ? openerIcon : Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(3, HudUI.CaptainButtonName,
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _captains == null) return;
            int pending = _captains.PendingCount();
            _openerChip.SetActive(pending > 0);
            if (pending > 0 && _openerCount != null) _openerCount.text = pending.ToString();
        }

        /// <summary>
        /// The sheet everything else sits on.
        ///
        /// WHY THIS EXISTS. The screen used to be loose cards floating on a translucent scrim with the
        /// island still moving between them, which is legible in a mock-up and unreadable in motion —
        /// the eye has nothing to anchor on and every gap is a moving picture. One opaque sheet behind
        /// the content is what the dock panel already does (VoyageUI's SeferPaneli), and it is the
        /// difference between a window and a heads-up display.
        ///
        /// Built FIRST so sibling order puts it behind every card, and it eats its own taps so the
        /// scrim's dismiss cannot fire through it.
        /// </summary>
        /// <summary>
        /// The screen is one column of horizontal bands, because the game is PORTRAIT — Player
        /// Settings allows portrait and nothing else.
        ///
        /// It used to be side by side: the crate a tall panel down the left at x 0.035-0.330 and the
        /// captain rows in the remaining two thirds. On a 1080x1920 sheet that made the crate a
        /// 318px-wide sliver 1500px tall and squeezed five rows into 660px of width. Stacked, the
        /// crate gets a wide band and the rows get the whole width. The masters screen is laid out the
        /// same way for the same reason.
        /// </summary>
        /// <summary>
        /// The content margin, and it is set by the SHEET rather than by taste. The kit's backdrop is
        /// a framed panel: at 0.030 the cards sat on top of its blue rim and left a sliver of it
        /// showing down each side, which reads as a stray bar rather than as a border. 0.062 clears
        /// the rim, so the frame is either fully seen or not seen at all.
        /// </summary>
        /// It is now inside the sheet's WHITE, not just clear of its rim: with the rim drawn at
        /// <see cref="backdropBorderScale"/> the white starts about 0.085 in, and a card that crossed
        /// that line left a blue sliver between every row.
        private const float PageLeft = 0.105f, PageRight = 0.895f;
        /// <summary>The header row's outer edge — the chips and close button sit above the sheet, so
        /// they line up with its frame rather than with the content inside it.</summary>
        private const float HeaderLeft = 0.062f;
        /// <summary>Where the sort and filter buttons meet, leaving a 0.020 gap between them and an
        /// equal half of the content width each.</summary>
        private const float BrowseSplitLeft = (PageLeft + PageRight) * 0.5f - 0.010f;
        private const float BrowseSplitRight = (PageLeft + PageRight) * 0.5f + 0.010f;

        private const float CrateTop = 0.900f, CrateBottom = 0.685f;
        private const float BrowseTop = 0.669f, BrowseBottom = 0.621f;
        private const float RowsTop = 0.606f, RowsBottom = 0.072f;

        private void BuildBackdrop()
        {
            // The kit's own sheet if it is wired, otherwise the shared panel under the backdrop tint.
            // The kit art carries its own dark ground and corner glow, which is exactly what a single
            // full-screen sheet wants and exactly what stacked row cards do not — hence one use here
            // and none on the rows.
            Sprite art = backdropArt != null ? backdropArt : cardPanel;
            RectTransform sheet = Art(_root, "Zemin", art,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.955f));
            var image = sheet.GetComponent<Image>();
            image.color = backdropArt != null ? Color.white : backdrop;
            if (backdropArt != null)
            {
                image.type = backdropArt.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, backdropBorderScale);
            }
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        /// <summary>
        /// One instance of the authored card, dropped into the row's slot and bound by child name.
        /// Everything it fails to find is left null and skipped on refresh, so a half-finished prefab
        /// shows a half-finished row rather than throwing on open. The masters screen does the same
        /// thing for the same reason — see <see cref="ForemanRosterUI"/>.
        /// </summary>
        private void BuildPrefabRow(int captain, Vector2 aMin, Vector2 aMax)
        {
            var go = Instantiate(cardPrefab, _rowsContent);
            go.name = "Kaptan_" + captain;
            go.SetActive(true);
            var c = go.GetComponent<RectTransform>();
            if (c == null) c = go.AddComponent<RectTransform>();
            UiBuild.Anchor(c, aMin, aMax);

            _rowRoot[captain] = c;
            _rowArt[captain] = go.GetComponent<Image>();
            if (_rowArt[captain] == null)
            {
                var pad = go.AddComponent<Image>();
                pad.color = Color.clear;
                _rowArt[captain] = pad;
            }
            _rowArt[captain].raycastTarget = true;

            // A prefab that ships without a body sprite borrows the screen's — see the masters screen
            // for why the art is a later step rather than a precondition.
            if (_rowArt[captain].sprite == null && cardPanel != null)
            {
                _rowArt[captain].sprite = cardPanel;
                _rowArt[captain].type = cardPanel.border.sqrMagnitude > 0f
                    ? Image.Type.Sliced : Image.Type.Simple;
            }

            BindPrefabRow(captain, c);

            var inspect = go.GetComponent<Button>();
            if (inspect == null) inspect = go.AddComponent<Button>();
            inspect.transition = Selectable.Transition.None;
            int selected = captain;
            inspect.onClick.RemoveAllListeners();
            inspect.onClick.AddListener(() => ShowDetails(selected));
        }

        /// <summary>
        /// The contract between this screen and an authored card, by child name:
        ///   Portre    Image   the captain's face
        ///   Ad        Text    his name
        ///   Gorev     Text    grade, role and stars
        ///   Derece    Image   the grade stripe, tinted
        ///   Dolgu     Image   the progress bar's fill, driven by its right anchor
        ///   Yukselt   Button  level him up; its first Text is the label
        ///   Durum     Image   the state pill (at the helm / at sea / locked); its Text is the word
        ///   Yildiz0-4 Image   five star pips; absent means the stars are drawn as ★ in Gorev
        /// Names are matched anywhere in the card's hierarchy, so they can be nested however the
        /// layout wants.
        /// </summary>
        private void BindPrefabRow(int captain, RectTransform card)
        {
            _rowPortrait[captain] = FindIn<Image>(card, "Portre");
            _rowName[captain] = FindIn<Text>(card, "Ad");
            _rowRole[captain] = FindIn<Text>(card, "Gorev");
            _rowGrade[captain] = FindIn<Image>(card, "Derece");
            _rowFill[captain] = FindIn<Image>(card, "Dolgu");

            Transform badge = FindIn<Transform>(card, "Durum");
            _rowBadge[captain] = badge != null ? badge.gameObject : null;
            if (badge != null)
            {
                _rowBadgeFill[captain] = badge.GetComponent<Image>();
                _rowBadgeText[captain] = badge.GetComponentInChildren<Text>(true);
            }

            for (int i = 0; i < Captains.MaxLevel; i++)
                _rowStar[captain * Captains.MaxLevel + i] = FindIn<Image>(card, "Yildiz" + i);

            _rowBtn[captain] = FindIn<Button>(card, "Yukselt");
            if (_rowBtn[captain] != null)
            {
                _rowBtnText[captain] = _rowBtn[captain].GetComponentInChildren<Text>(true);
                int captured = captain;
                _rowBtn[captain].onClick.RemoveAllListeners();
                _rowBtn[captain].onClick.AddListener(
                    () => { if (_captains != null && _captains.TryLevelUp(captured)) Ping(); });
            }

            // The authored card ships Yukselt, Durum, Cubuk and Dolgu with no sprite at all, so they
            // draw as flat sharp-cornered blocks on a rounded card while the crate's own buttons wear
            // the kit. Same rule the card body already follows in BuildPrefabRow: borrow the screen's
            // art, and only where the prefab brought none of its own.
            Adopt(_rowBtn[captain] != null ? _rowBtn[captain].GetComponent<Image>() : null, actionButton);
            Adopt(_rowBadgeFill[captain], chipPill);
            Adopt(FindIn<Image>(card, "Cubuk"), barTrack);
            Adopt(_rowFill[captain], barFill);
        }

        /// <summary>
        /// Dresses a prefab image in the screen's art, but only if it brought none of its own — an
        /// authored card that ships with real art keeps it. Sliced when the sprite carries a border,
        /// so a pill stretched across a row keeps its round caps instead of smearing them.
        /// </summary>
        private static void Adopt(Image target, Sprite art)
        {
            if (target == null || art == null || target.sprite != null) return;
            target.sprite = art;
            target.type = art.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            target.preserveAspect = false;
            PillFit.Wrap(target);
        }

        /// <summary>The first descendant with this exact name carrying a T, or null. Inactive children
        /// included.</summary>
        private static T FindIn<T>(Transform root, string name) where T : Component
        {
            var all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            return null;
        }

        private Sprite Portrait(int captain)
            => portraits != null && captain >= 0 && captain < portraits.Length ? portraits[captain] : null;

        /// <summary>True when the wired prefab gave this row real star pips.</summary>
        private bool HasStarPips(int captain) => _rowStar[captain * Captains.MaxLevel] != null;

        /// <summary>Lights the first <paramref name="stars"/> pips and dims the rest, or switches the
        /// row off entirely when something else has the slot. A no-op on a row with no pips wired.</summary>
        private void PaintStars(int captain, int stars, Color tint, bool visible)
        {
            for (int i = 0; i < Captains.MaxLevel; i++)
            {
                Image pip = _rowStar[captain * Captains.MaxLevel + i];
                if (pip == null) continue;
                pip.enabled = visible;
                pip.color = i < stars ? tint : new Color(0.78f, 0.80f, 0.84f, 1f);
            }
        }

        /// <summary>Stars as text, for a row with no pips wired.</summary>
        private static string StarText(int stars)
            => new string('★', Mathf.Clamp(stars, 0, Captains.MaxLevel))
             + new string('☆', Captains.MaxLevel - Mathf.Clamp(stars, 0, Captains.MaxLevel));

        // ------------------------------------------------------------------ pieces
        /// <summary>
        /// One row's state pill, switched off until a state applies. Same shape and same corner on
        /// both rosters, so the player learns one place to look.
        /// </summary>
        private void BuildBadge(int captain, RectTransform parent)
        {
            RectTransform pill = Art(parent, "Durum", actionButton,
                                     new Vector2(0.715f, 0.560f), new Vector2(0.972f, 0.900f));
            _rowBadgeFill[captain] = pill.GetComponent<Image>();
            _rowBadgeFill[captain].raycastTarget = false;
            if (actionButton != null)
            {
                _rowBadgeFill[captain].type = Image.Type.Sliced;
                PillFit.Wrap(_rowBadgeFill[captain]);
            }

            _rowBadgeText[captain] = UiBuild.Label(
                Slot(pill, "Text", new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.98f)),
                "Text", string.Empty, 20, TextAnchor.MiddleCenter);
            _rowBadgeText[captain].color = Paper;
            Fit(_rowBadgeText[captain], 10, 20);

            pill.gameObject.SetActive(false);
            _rowBadge[captain] = pill.gameObject;
        }

        /// <summary>An aspect-preserving image with no panel art behind it — a portrait.</summary>
        private static Image Icon(RectTransform parent, string name, Sprite sprite,
                                  Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.enabled = sprite != null;
            img.preserveAspect = true;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

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
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return (RectTransform)go.transform;
        }

        /// <summary>A plain coloured quad — the grade stripe, which must keep its own colour.</summary>
        private static Image Flat(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
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

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            Sprite art = chipPill != null ? chipPill : cardPanel;
            RectTransform rt = Art(parent, name, art, aMin, aMax);
            var img = rt.GetComponent<Image>();
            if (art != null) { img.type = Image.Type.Sliced; img.preserveAspect = false; PillFit.Wrap(img); }
            return rt;
        }

        private Image Bar(RectTransform parent, Vector2 aMin, Vector2 aMax, Color fallback)
        {
            RectTransform bed = Art(parent, "Cubuk", barTrack, aMin, aMax);
            var bedImage = bed.GetComponent<Image>();
            bedImage.type = Image.Type.Sliced;
            bedImage.preserveAspect = false;
            PillFit.Wrap(bedImage);
            if (barTrack == null) bedImage.color = track;

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
            if (barFill == null) img.color = fallback;
            UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            PillFit.Wrap(img);
            return img;
        }

        private static void Progress(Image fill, float t)
            => ((RectTransform)fill.transform).anchorMax = new Vector2(Mathf.Clamp01(t), 1f);

        private static void Dress(Button b, bool live)
        {
            b.interactable = live;
            b.GetComponent<Image>().color = live ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }

        private static void Ping() => ServiceLocator.Get<HapticService>()?.Medium();

        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }
    }
}
