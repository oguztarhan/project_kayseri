using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The masters screen: the chest down the left, eight cards on the right — one per station, each
    /// showing how many stars its master carries, what tier that puts him in, and how many cards are
    /// left to the next one.
    ///
    /// BUILT IN CODE rather than authored as a prefab, the same way <see cref="UiBuild"/>'s other
    /// screens are. That is a deliberate trade: an authored sheet would look better, but the roster
    /// is exactly eight identical cards driven off <see cref="Foremen"/>'s own tables, and a prefab
    /// would mean eight hand-wired copies that fall out of step the moment the tuning changes. The
    /// card layout here reads the tables, so it cannot disagree with the maths.
    ///
    /// THE TIER COLOUR IS NOT WIRED HERE. It comes from <see cref="ForemanService.TierTint"/>, which
    /// the plinth under the master's feet on the island reads too — a Legendary that is gold on the
    /// card and purple on the ground is worse than no colour at all.
    ///
    /// Refreshed on open and on <see cref="ForemanService.RosterChanged"/>. The only per-frame work is
    /// the free chest's countdown and the reveal's flips, and both stop the moment the screen closes.
    /// </summary>
    public sealed class ForemanRosterUI : MonoBehaviour
    {
        [Header("Yerleşim")]
        [Tooltip("Kart ızgarası: yatayda kaç sütun. Sekiz slot 4x2 olarak oturur.")]
        [SerializeField] private int columns = 4;
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
        [Tooltip("Sekiz portre, istasyon sırasıyla: maden, tren, depo, cevher kamyonu, fabrika, yük kamyonu, pazar, enerji santrali.")]
        [SerializeField] private Sprite[] portraits;
        [Tooltip("Sandık görseli — Ikonlar/ikon_sandik.")]
        [SerializeField] private Sprite chestIcon;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.86f);
        [SerializeField] private Color cardHired = new Color(0.16f, 0.19f, 0.27f, 1f);
        [SerializeField] private Color cardLocked = new Color(0.11f, 0.12f, 0.17f, 1f);

        // Kademe renkleri BURADA DEĞİL: ForemanConfig'de, adadaki kaideyle aynı yerde. Bkz. sınıf notu.
        /// <summary>The progress bar's own blue, used only when no bar art is wired. Not a tier
        /// colour — the bar means "cards toward the next star" at every tier.</summary>
        private static readonly Color BarBlue = new Color(0.33f, 0.62f, 0.92f, 1f);

        /// <summary>The rail button's icon, loaded at runtime — this screen has no Inspector to wire.</summary>
        private const string OpenerIconResource = "UI/Buttons/ustabasi";

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

        // ---- the chest shelf ----
        private Text _chestTitle, _chestBlurb, _chestSingle, _chestBulk, _freeLabel;
        private Button _singleButton, _bulkButton, _freeButton;
        private float _clockTick;

        // ---- the reveal ----
        // Eight tiles is the most a batch can ever show: cards are aggregated per master, and there
        // are eight masters. Built once, hidden, and reused for every open.
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
        /// How many slots are hired rides under it in the row's own counter chip, so the button says
        /// whether there is anything to come back for.
        /// </summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Button open = hud.AttachBottomButton(1, "BtnUstabasi", Resources.Load<Sprite>(OpenerIconResource), Show);
            if (open == null) return;

            GameObject chip = hud.AttachCounterChip(open);
            if (chip != null) _openerCount = chip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerCount == null || _foremen == null) return;
            _openerCount.text = string.Format("{0}/{1}", _foremen.HiredCount, Foremen.Count);
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
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);

            BuildHeader();
            BuildChestShelf();

            int rows = (Foremen.Count + columns - 1) / columns;
            // The grid starts where the chest shelf ends. The shelf is the reason to come back to this
            // screen once the roster is complete, so it gets the reading position rather than a corner.
            const float left = 0.305f, right = 0.965f, top = 0.845f, bottom = 0.035f;
            float cellW = (right - left) / columns, cellH = (top - bottom) / rows;
            const float padX = 0.006f, padY = 0.014f;

            for (int s = 0; s < Foremen.Count; s++)
            {
                int col = s % columns, row = s / columns;
                var aMin = new Vector2(left + col * cellW + padX, top - (row + 1) * cellH + padY);
                var aMax = new Vector2(left + (col + 1) * cellW - padX, top - row * cellH - padY);
                BuildCard(s, aMin, aMax);
            }

            BuildReveal();
        }

        /// <summary>
        /// The chest: one column down the left, three ways to open it. The free one carries its own
        /// countdown as its label, so the screen answers "is there anything for me right now" before
        /// the player has read a single card.
        /// </summary>
        private void BuildChestShelf()
        {
            RectTransform shelf = Art(_root, "Sandik", cardPanel,
                                      new Vector2(0.035f, 0.035f), new Vector2(0.288f, 0.845f));
            if (cardPanel == null) shelf.GetComponent<Image>().color = cardHired;

            _chestTitle = UiBuild.Label(Slot(shelf, "Baslik", new Vector2(0.06f, 0.885f), new Vector2(0.94f, 0.975f)),
                                        "Text", Loc.T("usta.sandik"), 30, TextAnchor.MiddleCenter);
            _chestTitle.color = Ink;
            Fit(_chestTitle, 18, 30);

            Icon(shelf, "Gorsel", chestIcon != null ? chestIcon : cardPanel,
                 new Vector2(0.13f, 0.520f), new Vector2(0.87f, 0.875f));

            _chestBlurb = UiBuild.Label(Slot(shelf, "Aciklama", new Vector2(0.07f, 0.425f), new Vector2(0.93f, 0.510f)),
                                        "Text", string.Empty, 19, TextAnchor.UpperCenter);
            _chestBlurb.color = InkFaint;

            _singleButton = ShelfButton(shelf, "Tek", new Vector2(0.075f, 0.285f), new Vector2(0.925f, 0.400f),
                                        () => Open(1), out _chestSingle);
            _bulkButton = ShelfButton(shelf, "Toplu", new Vector2(0.075f, 0.155f), new Vector2(0.925f, 0.270f),
                                      () => Open(_foremen != null ? _foremen.ChestTuning.BulkCount : 10),
                                      out _chestBulk);
            _freeButton = ShelfButton(shelf, "Bedava", new Vector2(0.075f, 0.025f), new Vector2(0.925f, 0.140f),
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
            RectTransform band = Art(_root, "Serit", ribbon, new Vector2(0.355f, 0.855f), new Vector2(0.645f, 0.995f));
            _titleLabel = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                        new Vector2(0.87f, RibbonBand + 0.13f)),
                                   "Text", Loc.T("usta.baslik"), 38, TextAnchor.MiddleCenter);

            // Both chips are the HUD's own graphite pill. They sit on the same line as the HUD's
            // money and gem counters and used to be white, so the top of the screen read as two
            // different games stacked on each other.
            RectTransform sol = Chip(_root, "Carpan", new Vector2(0.035f, 0.885f), new Vector2(0.175f, 0.968f));
            _multiplier = UiBuild.Label(Slot(sol, "Yazi", new Vector2(0.08f, 0f), new Vector2(0.92f, 1f)),
                                        "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _multiplier.color = Paper;

            RectTransform sag = Chip(_root, "Kese", new Vector2(0.672f, 0.885f), new Vector2(0.845f, 0.968f));
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
            // Sağ kenarda değil: HUD'un ayarlar dişlisi 120 sıralı kanvasta, bu ekranın üstünde
            // çiziliyor ve tam köşeye konan kapat düğmesinin üstüne biniyor.
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.878f), new Vector2(0.938f, 0.975f));
        }

        private void BuildCard(int station, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = Art(_root, "Kart_" + station, cardPanel, aMin, aMax);
            _card[station] = card.GetComponent<Image>();
            if (cardPanel == null) _card[station].color = cardHired;

            // The tier mark. It used to be fixed per slot and set once; a master's tier now moves every
            // second star, so it is kept and repainted. It is a rule under the name rather than a tab
            // on the card's edge: the white panel's rim carries its own soft glow, and anything laid
            // across it reads as a stray rectangle.
            _rule[station] = UiBuild.Flat(card, "Sirad", InkFaint,
                                          new Vector2(0.575f, 0.752f), new Vector2(0.820f, 0.768f))
                                    .GetComponent<Image>();

            _portrait[station] = Icon(card, "Portre", Portrait(station),
                                      new Vector2(0.01f, 0.245f), new Vector2(0.44f, 0.900f));

            _name[station] = UiBuild.Label(
                Slot(card, "Ad", new Vector2(0.44f, 0.775f), new Vector2(0.930f, 0.905f)),
                "Text", string.Empty, 28, TextAnchor.MiddleCenter);
            // "CEVHER KAMYONLARI" tek satirda karta sigmiyor; en uzun ad ne kadar kuculmesi
            // gerekiyorsa o kadar kuculuyor, tasip komsu karta girmiyor.
            Fit(_name[station], 15, 28);

            _level[station] = UiBuild.Label(
                Slot(card, "Seviye", new Vector2(0.44f, 0.635f), new Vector2(0.955f, 0.740f)),
                "Text", string.Empty, 26, TextAnchor.MiddleCenter);

            _effect[station] = UiBuild.Label(
                Slot(card, "Etki", new Vector2(0.44f, 0.520f), new Vector2(0.955f, 0.628f)),
                "Text", string.Empty, 32, TextAnchor.MiddleCenter);

            // Cards-toward-next-level. The bar is the collection made visible: gems can be bought,
            // duplicates cannot, so this is the line that actually paces the roster.
            _fill[station] = Bar(card, new Vector2(0.455f, 0.435f), new Vector2(0.955f, 0.510f));

            // Cards toward the next star. No price line any more: gems are spent at the chest, and a
            // star costs the cards on this bar and nothing else.
            _cards[station] = UiBuild.Label(
                Slot(card, "Kartlar", new Vector2(0.44f, 0.245f), new Vector2(0.955f, 0.395f)),
                "Text", string.Empty, 24, TextAnchor.MiddleCenter);

            int captured = station;
            _action[station] = UiBuild.Btn(card, "Dugme", string.Empty,
                                           actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                           new Color(0.24f, 0.68f, 0.36f, 1f), 26, () => OnPressed(captured));
            // Dar ve alcak: hap sanatinin kendi orani 4:1 ve uclari yatayda dilimleniyor. Kartin
            // tam genisligine yayilinca sekli bozulmadan da olsa cok iri duruyordu; kenarlardan
            // biraz iceri cekildi.
            UiBuild.Anchor((RectTransform)_action[station].transform,
                           new Vector2(0.105f, 0.040f), new Vector2(0.895f, 0.170f));
            PillFit.Wrap(_action[station].GetComponent<Image>());
            _actionText[station] = _action[station].GetComponentInChildren<Text>();
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
            _reveal = UiBuild.Flat(canvas, "Karartma", new Color(0.03f, 0.04f, 0.07f, 0.93f),
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

            const int cols = 4;
            const float left = 0.09f, right = 0.91f, top = 0.800f, bottom = 0.170f;
            float cellW = (right - left) / cols, cellH = (top - bottom) / 2f;

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
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private Sprite Portrait(int station)
            => portraits != null && station >= 0 && station < portraits.Length ? portraits[station] : null;

        /// <summary>A child rect anchored inside the card — the shape UiBuild's helpers want.</summary>
        private static RectTransform Slot(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        /// <summary>A tier tint deep enough to read as text on white, alpha untouched.</summary>
        private static Color Darken(Color c) => new Color(c.r * 0.68f, c.g * 0.68f, c.b * 0.68f, 1f);

        /// <summary>The tier's colour, from the one place both this screen and the island read.</summary>
        private Color TintFor(int station)
            => _foremen != null ? _foremen.TierTintOf(station) : InkFaint;

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
        private void OnPressed(int station)
        {
            if (_foremen == null) return;

            Foremen.Tier was = _foremen.TierOf(station);
            if (!_foremen.TryLevelUp(station)) { Refresh(); return; }

            // A promotion is the moment the card, the plinth and his size on the island all change at
            // once — worth more than the tap feedback a plain star gets.
            if (_foremen.TierOf(station) != was)
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
            _tileName[t].text = _tileFresh[t]
                ? Loc.T("usta.yeni")
                : Loc.Id("istasyon", IslandEconomy.Stations[s]);
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

            RefreshChest();
            for (int s = 0; s < Foremen.Count; s++) RefreshCard(s);
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

        private void RefreshCard(int s)
        {
            bool owned = _foremen.IsHired(s);
            bool maxed = _foremen.IsMaxed(s);
            int stars = _foremen.LevelOf(s);
            Color tint = TintFor(s);

            if (cardPanel == null) _card[s].color = owned ? cardHired : cardLocked;
            // A master you do not have yet keeps his own portrait, greyed — the card is white, so
            // blanking him leaves a hole where the only thing worth looking at should be.
            _portrait[s].color = owned ? Color.white : new Color(0.62f, 0.66f, 0.73f, 0.85f);
            _rule[s].color = owned ? tint : InkFaint;

            _name[s].text = Loc.Id("istasyon", IslandEconomy.Stations[s]);
            _name[s].color = owned ? Ink : InkSoft;

            // Stars and the word they add up to. The tier is what the player talks about; the stars
            // are how far into it he is.
            _level[s].text = owned
                ? new string('★', stars) + "  " + Loc.T("usta.kademe." + (int)_foremen.TierOf(s))
                : Loc.T("usta.bulunmadi");
            _level[s].color = owned ? Darken(tint) : InkSoft;
            Fit(_level[s], 12, 26);

            // What this master is worth to his own station right now. The empire gets a share of the
            // same number rather than a second one — see Game.Core.Foremen.
            _effect[s].text = string.Format(Culture, "+{0:0.#}%", Foremen.Boost(stars, _foremen.Tuning) * 100d);
            _effect[s].color = owned ? Darken(tint) : InkFaint;

            int have = _foremen.DuplicatesOf(s);
            int need = owned && !maxed ? _foremen.DuplicatesToLevel(s) : 0;
            float t = need > 0 ? Mathf.Clamp01(have / (float)need) : (maxed ? 1f : 0f);
            Progress(_fill[s], t);
            _cards[s].color = InkFaint;

            if (maxed)
            {
                _cards[s].text = string.Format("{0} / {0} {1}", need > 0 ? need : have, Loc.T("ustabasi.kart"));
                _actionText[s].text = Loc.T("ustabasi.azami");
                _action[s].interactable = false;
            }
            else if (owned)
            {
                _cards[s].text = string.Format("{0} / {1} {2}", have, need, Loc.T("ustabasi.kart"));
                _actionText[s].text = Loc.T("usta.yildizatla");
                _action[s].interactable = _foremen.CanLevel(s);
            }
            else
            {
                // Nothing to press and no price to quote: a master arrives in a chest, not at a till.
                _cards[s].text = Loc.T("usta.sandiktan");
                _actionText[s].text = Loc.T("usta.bulunmadi");
                _action[s].interactable = false;
            }

            Dress(_action[s], _actionText[s], _action[s].interactable);
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
