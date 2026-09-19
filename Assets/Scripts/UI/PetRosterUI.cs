using System.Globalization;
using System.Text;
using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The pet collection sheet. It deliberately owns presentation only: every chest, bootstrap and
    /// fusion action goes through <see cref="PetService"/>, so the UI cannot create a second wallet
    /// or bypass the save-before-acknowledge transactions in the service.
    ///
    /// Equipment controls live on SeaFightUI in Phase 5. This sheet therefore explains which slot
    /// gates are open, while keeping the six collection cards, chest and fusion decision together.
    ///
    /// DRESSED FROM THE KITS, NOT THE INSPECTOR. The league sheet, ribbon, close disc and capsules come
    /// from <see cref="LigKit"/>/<see cref="EkranKit"/>/<see cref="AtolyeKit"/> as on the contracts,
    /// mining gear and events screens, and the rarity frames from the masters' UstaKiti atlas, so every
    /// piece loads at runtime and no scene has to be rewired. Only the pet art itself is authored data:
    /// the portraits and the chest come from <see cref="PetConfig"/>.
    /// </summary>
    public sealed class PetRosterUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 118;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);

        public const string OpenerButtonName = "BtnDenizDostlari";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color ButtonYellow = new Color(0.94f, 0.68f, 0.20f, 1f);
        private static readonly Color OpenGreen = new Color(0.13f, 0.62f, 0.35f, 1f);
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ kit pieces
        /// <summary>The masters' kit atlas. Its rarity frames are the pet ladder's too: one per rung,
        /// plus the padlocked frame for a species nobody has drawn.</summary>
        private const string UstaAtlasPath = "UI/Usta/UstaKiti";
        private static readonly string[] FrameNames =
            { "cerceve_siradan", "cerceve_nadir", "cerceve_destansi", "cerceve_efsanevi", "cerceve_mitik", "cerceve_kilitli" };
        private const int LockedFrame = 5;

        /// <summary>
        /// Each frame's own transparent window (xMin, yMin, xMax, yMax as fractions of the frame),
        /// measured on the exports off the centre lines so the gems and the crown do not skew it, and
        /// pulled in 0.01. The pale backdrop behind a pet fills exactly this, so its corners hide under
        /// the rim whichever frame the card is wearing.
        /// </summary>
        private static readonly Vector4[] FrameWindows =
        {
            new Vector4(0.164f, 0.204f, 0.833f, 0.878f),   // Sıradan
            new Vector4(0.178f, 0.113f, 0.819f, 0.825f),   // Nadir
            new Vector4(0.187f, 0.102f, 0.810f, 0.820f),   // Destansı
            new Vector4(0.177f, 0.110f, 0.820f, 0.680f),   // Efsanevi
            new Vector4(0.168f, 0.148f, 0.829f, 0.765f),   // Mitik
            new Vector4(0.152f, 0.133f, 0.841f, 0.794f),   // kilitli
        };

        /// <summary>Where the animal sits inside any frame: the part every window shares, which is
        /// square — the pets are drawn square-ish, so none of them ever runs onto a gem or a crown.</summary>
        private static readonly Vector2 PetBoxMin = new Vector2(0.18f, 0.235f);
        private static readonly Vector2 PetBoxMax = new Vector2(0.82f, 0.69f);

        /// <summary>The league board's crest and rail, lifted 0.06 above where the contracts screen
        /// wears them: six framed pets need the height, and the sheet's art is the same either way.</summary>
        private static readonly Vector2 SheetMin = new Vector2(0.030f, 0.040f), SheetMax = new Vector2(0.970f, 0.930f);
        private static readonly Vector2 RibbonMin = new Vector2(0.215f, 0.758f), RibbonMax = new Vector2(0.785f, 0.850f);
        private static readonly Vector2 CloseMin = new Vector2(0.848f, 0.796f), CloseMax = new Vector2(0.962f, 0.880f);

        /// <summary>The white page inside the board's rim, and the bands stacked down it.</summary>
        private const float PageLeft = 0.12f, PageRight = 0.88f;
        // The chest card grew from 0.14 to 0.17 of the screen so its guarantee lines have room at the type
        // scale; the pet grid gave back 0.015 of it and the rows below moved down by the rest.
        private const float ChestTop = 0.745f, ChestBottom = 0.575f;
        private const float SlotsTop = 0.567f, SlotsBottom = 0.495f;
        private const float GridTop = 0.487f, GridBottom = 0.165f;
        private const float FusionTop = 0.157f, FusionBottom = 0.100f;
        private const float GridGapX = 0.012f, GridGapY = 0.006f;

        private Sprite[] _frames;
        private Sprite _cardBack, _readyBadge, _card, _socket, _padlock;

        private PetService _pets;
        private PetConfig _config;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _title, _pearl, _essence, _pity, _last, _bonus, _fusionPreview;
        private Button _openOne, _openBulk, _fuse;
        private Text _openOneText, _openBulkText, _fuseText;
        private Image _fusePortrait;
        private readonly Image[] _cardArt = new Image[Pets.SpeciesCount];
        private readonly PetFrame[] _cardFrame = new PetFrame[Pets.SpeciesCount];
        private readonly Text[] _cardName = new Text[Pets.SpeciesCount];
        private readonly Text[] _cardBest = new Text[Pets.SpeciesCount];
        private readonly Text[] _cardEffect = new Text[Pets.SpeciesCount];
        private readonly Text[] _cardCounts = new Text[Pets.SpeciesCount];
        private readonly GameObject[] _cardReady = new GameObject[Pets.SpeciesCount];
        private readonly Button[] _cardButton = new Button[Pets.SpeciesCount];
        private readonly Image[] _slotFrame = new Image[Pets.SlotCount];
        private readonly Image[] _slotLock = new Image[Pets.SlotCount];
        private readonly Text[] _slotNumber = new Text[Pets.SlotCount];
        private readonly Text[] _slotCaption = new Text[Pets.SlotCount];
        private GameObject _openerChip;
        private TMP_Text _openerCount;
        private int _selectedSpecies;
        private PetService.FusionResult _fusion;
        private bool _hasFusion;

        /// <summary>A rarity frame with the pet seated in it: the backdrop and the animal under the
        /// frame's rim, the whole held at the frame's own aspect by a fitter.</summary>
        private struct PetFrame
        {
            public AspectRatioFitter Fit;
            public Image Window, Portrait, Frame;
        }

        // The chest reveal: its own canvas and a pooled row of tiles, the same ceremony
        // ForemanRosterUI ships for its crate — see BuildReveal.
        private const int RevealTiles = 10;
        private const float FlipSeconds = 0.34f;
        private const float FlipStagger = 0.13f;
        private static readonly Color Gain = new Color(0.20f, 0.60f, 0.30f, 1f);
        private static readonly Color Drop = new Color(0.80f, 0.26f, 0.22f, 1f);
        private RectTransform _reveal;
        private Text _revealTitle, _revealFooter, _revealHint;
        private Image _revealChest;
        private readonly RectTransform[] _tileRect = new RectTransform[RevealTiles];
        private readonly PetFrame[] _tile = new PetFrame[RevealTiles];
        private readonly Text[] _tileName = new Text[RevealTiles];
        private readonly Text[] _tileRarity = new Text[RevealTiles];
        private readonly bool[] _tileTurned = new bool[RevealTiles];
        private readonly int[] _tileSpecies = new int[RevealTiles];
        private readonly RosterCardState.Rarity[] _tileRarityOf = new RosterCardState.Rarity[RevealTiles];
        private readonly bool[] _tileEssence = new bool[RevealTiles];
        private int _tilesShown;
        private float _revealClock;
        private bool _revealing;
        private ConfettiBurst _confetti;
        private RewardRevealUI _rewardReveal;

        // The fusion confirm card: what is shown is exactly what is committed.
        private RectTransform _confirm;
        private Text _confirmTitle, _confirmInputs, _confirmResult, _confirmBonus, _cancelText, _confirmText;
        private PetFrame _confirmFrame;
        private PetService.FusionResult _pendingFusion;

        // What the summary line last reported, kept as data so a language change rewrites it.
        // Star 0 means a chest pull (rarity only); a fusion result carries its star.
        private int _lastSpecies = -1;
        private RosterCardState.Rarity _lastRarity;
        private int _lastStar;

        private Button _claim;
        private Text _claimText;

        public bool Visible => _root != null && _root.gameObject.activeSelf;
        public string FusionPreviewText => _fusionPreview != null ? _fusionPreview.text : string.Empty;
        public bool FusionAvailable => _hasFusion;

        private void Awake()
        {
            _pets = ServiceLocator.Get<PetService>();
            GameBootstrap bootstrap = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            _config = bootstrap != null ? bootstrap.PetConfig : null;
            _loc = ServiceLocator.Get<LocalizationService>();
            LoadKit();
            Build();
            BuildOpener();
            if (_pets != null) _pets.Changed += OnChanged;
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_pets != null) _pets.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            // The bootstrap grant is tied to the panel opening, not to Awake or an invisible reload.
            if (_pets != null)
            {
                long before = _pets.Pearls;
                if (_pets.TryGrantBootstrap()) PresentPearls(_pets.Pearls - before);
            }
            Refresh();
        }

        /// <summary>Closes everything this screen owns. A reveal cut short is only dismissed — the
        /// chest it shows was paid and saved before it started, so there is nothing to undo or redo.</summary>
        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            if (_confirm != null) _confirm.gameObject.SetActive(false);
            if (_reveal != null && _reveal.gameObject.activeSelf) DismissReveal();
        }

        private void OnChanged()
        {
            Refresh();
            RefreshOpener();
        }

        private void OnLanguageChanged()
        {
            Refresh();
            RefreshOpener();
        }

        // ------------------------------------------------------------------ build
        /// <summary>Takes every masters-kit sprite once. SpriteAtlas.GetSprite hands back a new clone
        /// per call, so nothing after this asks the atlas again.</summary>
        private void LoadKit()
        {
            var atlas = Resources.Load<SpriteAtlas>(UstaAtlasPath);
            _frames = new Sprite[FrameNames.Length];
            for (int i = 0; i < FrameNames.Length; i++) _frames[i] = atlas != null ? atlas.GetSprite(FrameNames[i]) : null;
            _readyBadge = atlas != null ? atlas.GetSprite("rozet_yukselt") : null;
            _cardBack = LigKit.Get("usta_kart");
            _card = AtolyeKit.Get("panel_kart");
            _socket = EkranKit.Get("yuva");
            _padlock = EkranKit.Get("kilit");
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DenizDostlariKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            Image sheet = EkranKit.Sliced(_root, "Zemin", LigKit.Board, SheetMin, SheetMax, false);
            sheet.raycastTarget = true;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // eats its own taps
            BuildHeader();
            BuildChest();
            BuildSlots();
            BuildCards();
            BuildFusion();
            BuildConfirm();
            UiBuild.InsetContent(_root);
            BuildReveal();
        }

        private void BuildHeader()
        {
            Image band = EkranKit.Sliced(_root, "Serit", LigKit.Get("serit"), RibbonMin, RibbonMax, true);
            // 0.25–0.75: the clasps ride the ribbon's caps, which PillFit grows with its height.
            _title = Line(band.rectTransform, "Text", new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.82f),
                          36, TextAnchor.MiddleCenter, EkranKit.Paper, 18);
            EkranKit.Close(_root, CloseMin, CloseMax, Hide);
        }

        /// <summary>
        /// The chest on a framed white card: the clam, the two balances, the guarantee, the two open
        /// buttons — and under a rule, what the last pull was and what the collection adds at sea.
        /// </summary>
        private void BuildChest()
        {
            RectTransform chest = Panel(_root, "Sandik", new Vector2(PageLeft, ChestBottom), new Vector2(PageRight, ChestTop));
            EkranKit.Icon(chest, "SandikResmi", _config != null ? _config.ChestArt : null,
                          new Vector2(0.035f, 0.290f), new Vector2(0.205f, 0.950f));

            // Two chips, then the guarantee as three short lines with a clear gap under the second chip: it
            // was one wrapped line under a 5 unit gap, at 19 shrinking to 11.
            _pearl = EtkinlikKit.Chip(chest, "Inciler", new Vector2(0.215f, 0.745f), new Vector2(0.600f, 0.945f));
            _essence = EtkinlikKit.Chip(chest, "Oz", new Vector2(0.215f, 0.535f), new Vector2(0.600f, 0.735f));
            _pity = Line(chest, "Merhamet", new Vector2(0.230f, 0.290f), new Vector2(0.605f, 0.515f),
                         UiType.Body, TextAnchor.MiddleLeft, InkSoft, UiType.MinSize);

            _openOne = EtkinlikKit.Capsule(chest, "AcBir", new Vector2(0.612f, 0.640f), new Vector2(0.962f, 0.930f),
                                           () => Open(1), out _openOneText);
            _openBulk = EtkinlikKit.Capsule(chest, "AcToplu", new Vector2(0.612f, 0.320f), new Vector2(0.962f, 0.610f),
                                            () => Open(_pets != null ? _pets.ChestTuning.BulkCount : 10), out _openBulkText);

            UiBuild.Flat(chest, "Cizgi", new Color(InkFaint.r, InkFaint.g, InkFaint.b, 0.45f),
                         new Vector2(0.045f, 0.252f), new Vector2(0.955f, 0.258f)).GetComponent<Image>().raycastTarget = false;
            RectTransform summary = EtkinlikKit.Slot(chest, "Durum", new Vector2(0.050f, 0.050f), new Vector2(0.950f, 0.235f));
            _last = Line(summary, "SonCekilis", Vector2.zero, new Vector2(0.48f, 1f), 24, TextAnchor.MiddleLeft, InkSoft, UiType.MinSize);
            _last.text = Loc.T("dost.son_yok");
            _bonus = Line(summary, "CanliBonus", new Vector2(0.52f, 0f), Vector2.one, 24, TextAnchor.MiddleRight, Ink, UiType.MinSize);
        }

        /// <summary>
        /// The three equip slots as the mining gear's bolted sockets: an open one shows its number in
        /// the well, a shut one its padlock, and the caption under each says open or how far off it is.
        /// Every free pearl source the panel pays sits beside them, behind one button that counts them.
        /// </summary>
        private void BuildSlots()
        {
            RectTransform slots = EtkinlikKit.Slot(_root, "Yuvalar", new Vector2(PageLeft, SlotsBottom), new Vector2(PageRight, SlotsTop));
            for (int slot = 0; slot < Pets.SlotCount; slot++)
            {
                float x0 = slot * 0.190f;
                RectTransform cell = EtkinlikKit.Slot(slots, "Yuva" + slot, new Vector2(x0, 0f), new Vector2(x0 + 0.180f, 1f));
                _slotFrame[slot] = EkranKit.Icon(cell, "Soket", _socket, new Vector2(0f, 0.36f), new Vector2(1f, 1f));
                RectTransform well = _slotFrame[slot].rectTransform;
                // The socket's dark well, measured on yuva: a little above its middle.
                _slotLock[slot] = EkranKit.Icon(well, "Kilit", _padlock, new Vector2(0.36f, 0.30f), new Vector2(0.64f, 0.76f));
                _slotNumber[slot] = Line(well, "No", new Vector2(0.25f, 0.24f), new Vector2(0.75f, 0.82f),
                                         34, TextAnchor.MiddleCenter, Paper, 14);
                _slotNumber[slot].text = (slot + 1).ToString(Culture);
                _slotCaption[slot] = Line(cell, "Bilgi" + slot, new Vector2(-0.04f, 0f), new Vector2(1.04f, 0.34f),
                                          19, TextAnchor.MiddleCenter, InkSoft, 10);
            }

            _claim = EtkinlikKit.Capsule(slots, "Oduller", new Vector2(0.600f, 0.200f), new Vector2(1f, 0.860f),
                                         ClaimRewards, out _claimText);
        }

        /// <summary>
        /// Six cards, three across and two down. Each is a framed white card holding the pet in its
        /// rarity frame — the padlocked one until it is found — with the name, best copy, the stat it
        /// feeds and the copies in hand underneath. Tapping one picks it for the fusion bar.
        /// </summary>
        private void BuildCards()
        {
            const int cols = 3;
            float width = (PageRight - PageLeft - GridGapX * (cols - 1)) / cols;
            float height = (GridTop - GridBottom - GridGapY) * 0.5f;
            for (int species = 0; species < Pets.SpeciesCount; species++)
            {
                int col = species % cols;
                int row = species / cols;
                float x0 = PageLeft + col * (width + GridGapX);
                float y1 = GridTop - row * (height + GridGapY);
                RectTransform card = Panel(_root, "Pet_" + species, new Vector2(x0, y1 - height), new Vector2(x0 + width, y1));
                _cardArt[species] = card.GetComponent<Image>();
                _cardArt[species].raycastTarget = true;
                int captured = species;
                _cardButton[species] = card.gameObject.AddComponent<Button>();
                _cardButton[species].transition = Selectable.Transition.None;
                _cardButton[species].onClick.AddListener(() => SelectSpecies(captured));

                _cardFrame[species] = Frame(card, "Cerceve", new Vector2(0.08f, 0.300f), new Vector2(0.92f, 0.950f));
                Image ready = EkranKit.Icon((RectTransform)_cardFrame[species].Fit.transform, "Yukselt", _readyBadge,
                                            new Vector2(0.70f, 0.80f), new Vector2(1.02f, 1.03f));
                _cardReady[species] = ready.gameObject;
                ready.gameObject.SetActive(false);

                _cardName[species] = Line(card, "Ad", new Vector2(0.06f, 0.225f), new Vector2(0.94f, 0.300f),
                                          25, TextAnchor.MiddleCenter, Ink, 12);
                _cardBest[species] = Line(card, "EnIyi", new Vector2(0.06f, 0.168f), new Vector2(0.94f, 0.225f),
                                          18, TextAnchor.MiddleCenter, InkSoft, 10);
                _cardEffect[species] = Line(card, "Etki", new Vector2(0.06f, 0.115f), new Vector2(0.94f, 0.168f),
                                            17, TextAnchor.MiddleCenter, InkSoft, 10);
                _cardCounts[species] = Line(card, "Sayilar", new Vector2(0.06f, 0.065f), new Vector2(0.94f, 0.115f),
                                            15, TextAnchor.MiddleCenter, InkFaint, 9);
            }
        }

        private void BuildFusion()
        {
            RectTransform fusion = Panel(_root, "Fusyon", new Vector2(PageLeft, FusionBottom), new Vector2(PageRight, FusionTop));
            _fusePortrait = EkranKit.Icon(fusion, "Secili", null, new Vector2(0.035f, 0.17f), new Vector2(0.125f, 0.83f));
            _fusionPreview = Line(fusion, "Onizleme", new Vector2(0.140f, 0.10f), new Vector2(0.640f, 0.90f),
                                  19, TextAnchor.MiddleLeft, Ink, 10);
            _fuse = EtkinlikKit.Capsule(fusion, "FusyonYap", new Vector2(0.655f, 0.15f), new Vector2(0.968f, 0.85f),
                                        FuseSelected, out _fuseText);
        }

        /// <summary>
        /// The fusion confirm: the exact inputs, the result, and what the pet's live bonus does —
        /// which can go DOWN on a rarity-crossing fusion (a fresh ★1 of the next rarity is worth less
        /// than a ★5 of this one), so the card says so rather than letting a tap discover it.
        /// Tapping outside the card cancels; the card itself swallows its own taps.
        /// </summary>
        private void BuildConfirm()
        {
            _confirm = UiBuild.Flat(_root, "FusyonOnay", new Color(0.02f, 0.03f, 0.06f, 0.78f), Vector2.zero, Vector2.one);
            Button outside = _confirm.gameObject.AddComponent<Button>();
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(CancelFusion);

            // The same league board, smaller: its crest and rail stay the art's own height, the white
            // page under them is what shrinks.
            Image board = EkranKit.Sliced(_confirm, "OnayKarti", LigKit.Board, new Vector2(0.05f, 0.27f), new Vector2(0.95f, 0.73f), false);
            board.raycastTarget = true;
            board.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            RectTransform card = board.rectTransform;

            Image band = EkranKit.Sliced(card, "OnayBaslik", LigKit.Get("serit"), new Vector2(0.20f, 0.605f), new Vector2(0.80f, 0.805f), true);
            _confirmTitle = Line(band.rectTransform, "Text", new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.82f),
                                 34, TextAnchor.MiddleCenter, EkranKit.Paper, 16);
            _confirmTitle.text = Loc.T("dost.fusyon");

            _confirmFrame = Frame(card, "SonucCercevesi", new Vector2(0.10f, 0.285f), new Vector2(0.34f, 0.595f));
            _confirmInputs = Line(card, "Girdiler", new Vector2(0.37f, 0.485f), new Vector2(0.91f, 0.585f),
                                  24, TextAnchor.MiddleLeft, InkSoft, 12);
            _confirmResult = Line(card, "Sonuc", new Vector2(0.37f, 0.385f), new Vector2(0.91f, 0.485f),
                                  30, TextAnchor.MiddleLeft, Ink, 14);
            _confirmBonus = Line(card, "OnayBonus", new Vector2(0.37f, 0.290f), new Vector2(0.91f, 0.385f),
                                 22, TextAnchor.MiddleLeft, Ink, 11);

            Button cancel = EtkinlikKit.Capsule(card, "Vazgec", new Vector2(0.10f, 0.150f), new Vector2(0.48f, 0.265f),
                                                CancelFusion, out _cancelText);
            EtkinlikKit.SetFace(cancel, _cancelText, EtkinlikKit.Face.Dead, true);
            Button confirm = EtkinlikKit.Capsule(card, "Onayla", new Vector2(0.52f, 0.150f), new Vector2(0.90f, 0.265f),
                                                 ConfirmFusion, out _confirmText);
            EtkinlikKit.SetFace(confirm, _confirmText, EtkinlikKit.Face.Claim, true);
            _cancelText.text = Loc.T("dost.vazgec");
            _confirmText.text = Loc.T("dost.onayla");

            _confirm.gameObject.SetActive(false);
        }

        /// <summary>
        /// The chest reveal, on its own canvas above the HUD gear — ForemanRosterUI's crate ceremony
        /// and its reasons: the whole sheet is the skip target, the confetti lives on this canvas so
        /// it is not drawn under an opaque sheet, and the tiles are built once so a reveal allocates
        /// nothing while it plays. The pull it shows is already paid and saved when it starts.
        /// Each tile is a card lying face down that turns over into the pet in its rarity frame.
        /// </summary>
        private void BuildReveal()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DenizDostlariSandikKanvas", sortingOrder + 25);
            _reveal = UiBuild.Flat(canvas, "Acilis", new Color(0.03f, 0.04f, 0.07f, 1f), Vector2.zero, Vector2.one);
            Button skip = _reveal.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(OnRevealTapped);

            Image band = EkranKit.Sliced(_reveal, "SandikSerit", LigKit.Get("serit"),
                                         new Vector2(0.140f, 0.862f), new Vector2(0.860f, 0.952f), true);
            _revealTitle = Line(band.rectTransform, "SandikBaslik", new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.82f),
                                40, TextAnchor.MiddleCenter, Paper, 18);
            _revealChest = EkranKit.Icon(_reveal, "Istiridye", _config != null ? _config.ChestArt : null,
                                         new Vector2(0.36f, 0.745f), new Vector2(0.64f, 0.855f));
            _revealFooter = Line(_reveal, "Ozet", new Vector2(0.05f, 0.125f), new Vector2(0.95f, 0.215f),
                                 24, TextAnchor.MiddleCenter, Paper, 13);
            _revealHint = Line(_reveal, "Devam", new Vector2(0.10f, 0.045f), new Vector2(0.90f, 0.110f),
                               22, TextAnchor.MiddleCenter, InkFaint, 11);

            for (int t = 0; t < RevealTiles; t++)
            {
                RectTransform tile = EtkinlikKit.Slot(_reveal, "Kart_" + t, Vector2.zero, Vector2.one);
                _tileRect[t] = tile;
                _tile[t] = Frame(tile, "Yuz", new Vector2(0.04f, 0.255f), new Vector2(0.96f, 1f));
                _tileName[t] = Line(tile, "Ad", new Vector2(0.02f, 0.135f), new Vector2(0.98f, 0.245f),
                                    44, TextAnchor.MiddleCenter, Paper, 11);
                _tileRarity[t] = Line(tile, "Nadirlik", new Vector2(0.02f, 0.040f), new Vector2(0.98f, 0.135f),
                                      34, TextAnchor.MiddleCenter, Paper, 10);
                tile.gameObject.SetActive(false);
            }

            _reveal.gameObject.SetActive(false);
            _confetti = canvas.gameObject.AddComponent<ConfettiBurst>();
            _rewardReveal = RewardRevealUI.Create(canvas, null, _config != null ? _config.PearlIcon : null);
            UiBuild.InsetContent(_reveal);
        }

        // ---------------------------------------------------------------- opener
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            Sprite icon = _config != null ? _config.ChestIcon : null;
            if (hud == null)
            {
                // Portrait Shipyard deliberately has no HudUI rail. Keep this entry point on its own
                // lightweight canvas so the collection remains reachable from the selected live
                // presentation without duplicating or changing the normal Main HUD layout.
                RectTransform canvas = UiBuild.Canvas(transform, "DenizDostlariAciciKanvas", sortingOrder - 1);
                Button standalone = UiBuild.Btn(canvas, OpenerButtonName, Loc.T("dost.baslik"),
                    icon != null ? icon : UiSkin.ButtonYellow, ButtonYellow, 20, Show);
                UiBuild.Anchor((RectTransform)standalone.transform,
                    new Vector2(0.650f, 0.035f), new Vector2(0.960f, 0.100f));
                return;
            }

            Button open = hud.AttachBottomButton(9, OpenerButtonName,
                icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;
            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        public int PendingCount()
            => _pets == null ? 0 : (_pets.CanOpenChest(1) ? 1 : 0) + _pets.ClaimableRewardCount;

        private void RefreshOpener()
        {
            if (_openerChip == null) return;
            int count = PendingCount();
            if (_openerChip.activeSelf != (count > 0)) _openerChip.SetActive(count > 0);
            if (count > 0 && _openerCount != null) _openerCount.text = count.ToString(Culture);
        }

        // --------------------------------------------------------------- actions
        /// <summary>Pays and saves through the service first, then plays the reveal from the
        /// receipt it returned — the animation only ever shows what is already on disk.</summary>
        private void Open(int count)
        {
            if (_pets == null || _revealing) return;
            var pulled = _pets.TryOpenChests(count);
            if (pulled == null || pulled.Length == 0)
            {
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Denied);
                ServiceLocator.Get<HapticService>()?.Light();
                return;
            }
            var last = pulled[pulled.Length - 1];
            _lastSpecies = last.Species;
            _lastRarity = last.Rarity;
            _lastStar = 0;
            RefreshLast();
            BeginReveal(pulled);
        }

        private void SelectSpecies(int species)
        {
            if (!Pets.Exists(species)) return;
            _selectedSpecies = species;
            RefreshFusion();
            RefreshCards();
        }

        /// <summary>FÜZYON only asks. The card shows the receipt the service would write, and
        /// nothing moves until ONAYLA.</summary>
        private void FuseSelected()
        {
            if (_pets == null || !_hasFusion || _confirm == null) return;
            _pendingFusion = _fusion;
            FillConfirm();
            _confirm.gameObject.SetActive(true);
            ServiceLocator.Get<HapticService>()?.Light();
        }

        /// <summary>
        /// Commits exactly what the card showed. If the materials moved while it was open (a chest
        /// landed a copy, say), the preview is re-read and re-shown instead — a confirm that could
        /// spend something other than what it said is not a confirm.
        /// </summary>
        private void ConfirmFusion()
        {
            if (_pets == null || _confirm == null) return;
            PetService.FusionResult shown = _pendingFusion;
            if (!_pets.TryGetFusionResult(shown.Species, shown.SourceRarity, shown.SourceStar,
                                          shown.UsedEssence, out var now)
                || now.RealCopiesConsumed != shown.RealCopiesConsumed || now.EssenceSpent != shown.EssenceSpent)
            {
                _hasFusion = FindFusion(_selectedSpecies, out _fusion);
                if (!_hasFusion) { CancelFusion(); RefreshFusion(); return; }
                _pendingFusion = _fusion;
                FillConfirm();
                return;
            }

            bool fused = shown.UsedEssence
                ? _pets.FuseWithEssence(shown.Species, shown.SourceRarity, shown.SourceStar, out var result)
                : _pets.Fuse(shown.Species, shown.SourceRarity, shown.SourceStar, out result);
            _confirm.gameObject.SetActive(false);
            if (!fused) return;

            _lastSpecies = result.Species;
            _lastRarity = result.ResultRarity;
            _lastStar = result.ResultStar;
            RefreshLast();
            if (result.CrossedRarity)
            {
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
                if (_confetti != null) _confetti.Play();
            }
            else ServiceLocator.Get<AudioService>()?.Play(SoundId.Upgrade);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void CancelFusion()
        {
            if (_confirm != null) _confirm.gameObject.SetActive(false);
        }

        /// <summary>Claims every free pearl source that is ready, each through its own idempotent
        /// call (each one saves its own receipt), and shows the total that actually landed.</summary>
        private void ClaimRewards()
        {
            if (_pets == null) return;
            long before = _pets.Pearls;
            bool any = _pets.TryClaimDaily();
            for (int i = 0; i < PetService.SeaFightMilestoneCount; i++) any |= _pets.TryClaimSeaFightMilestone(i);
            for (int i = 0; i < PetService.AchievementRewardTierCount; i++) any |= _pets.TryClaimAchievementReward(i);
            if (!any)
            {
                ServiceLocator.Get<HapticService>()?.Light();
                return;
            }
            PresentPearls(_pets.Pearls - before);
        }

        private void PresentPearls(long amount)
        {
            if (_rewardReveal == null || amount <= 0L) return;
            _rewardReveal.Present("+" + amount.ToString(Culture) + " ◉ " + Loc.T("dost.inci"));
        }

        // ---------------------------------------------------------------- reveal
        private void BeginReveal((int Species, RosterCardState.Rarity Rarity, bool Essence)[] pulled)
        {
            _tilesShown = Mathf.Min(pulled.Length, RevealTiles);
            LayoutTiles(_tilesShown);
            for (int t = 0; t < RevealTiles; t++)
            {
                bool on = t < _tilesShown;
                _tileRect[t].gameObject.SetActive(on);
                if (!on) continue;
                _tileSpecies[t] = pulled[t].Species;
                _tileRarityOf[t] = pulled[t].Rarity;
                _tileEssence[t] = pulled[t].Essence;
                _tileTurned[t] = false;
                ShowCardBack(_tile[t]);                    // face down until it turns
                _tileName[t].text = string.Empty;
                _tileRarity[t].text = string.Empty;
                _tileRect[t].localScale = Vector3.one;
            }

            // Written per open rather than at build, so a language change between chests lands.
            _revealTitle.text = EtkinlikKit.OneLine(pulled.Length > 1
                ? pulled.Length.ToString(Culture) + " × " + Loc.T("dost.sandik")
                : Loc.T("dost.sandik"));
            _revealFooter.text = RevealFooter();
            _revealHint.text = Loc.T("usta.devam");
            _revealClock = 0f;
            _revealing = true;
            _reveal.gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
        }

        /// <summary>One big card under the clam for a single chest; a three-wide grid for a batch,
        /// its short last row centred. The clam steps aside for a batch, whose grid needs the room.</summary>
        private void LayoutTiles(int count)
        {
            EtkinlikKit.SetActive(_revealChest, count <= 1 && _revealChest.sprite != null);
            if (count <= 1)
            {
                UiBuild.Anchor(_tileRect[0], new Vector2(0.26f, 0.30f), new Vector2(0.74f, 0.735f));
                return;
            }
            const int cols = 3;
            const float left = 0.06f, right = 0.94f, top = 0.845f, bottom = 0.225f;
            int rows = (count + cols - 1) / cols;
            float cellW = (right - left) / cols, cellH = (top - bottom) / rows;
            for (int t = 0; t < count; t++)
            {
                int col = t % cols, row = t / cols;
                int inRow = row == rows - 1 ? count - row * cols : cols;
                float x0 = left + (cols - inRow) * cellW * 0.5f + col * cellW;
                UiBuild.Anchor(_tileRect[t],
                    new Vector2(x0 + 0.012f, top - (row + 1) * cellH + 0.006f),
                    new Vector2(x0 + cellW - 0.012f, top - row * cellH - 0.006f));
            }
        }

        /// <summary>Turns one tile face up. Called by the flip at its edge-on frame, and by the
        /// skip tap for every tile still down, so tapping through shows the same cards.</summary>
        private void TurnTile(int t)
        {
            if (_tileTurned[t]) return;
            _tileTurned[t] = true;

            int species = _tileSpecies[t];
            RosterCardState.Rarity rarity = _tileRarityOf[t];
            ShowPet(_tile[t], species, true, rarity);
            _tileName[t].text = SpeciesName(species);
            _tileRarity[t].text = _tileEssence[t]
                ? "✦ +1 " + Loc.T("dost.oz")
                : RarityName(rarity) + " " + Stars(1);
            _tileRarity[t].color = _pets != null ? Color.Lerp(_pets.RarityTint(rarity), Color.white, 0.35f) : Paper;

            if (rarity >= RosterCardState.Rarity.Legendary)
            {
                ServiceLocator.Get<HapticService>()?.Medium();
                if (_confetti != null) _confetti.Play();
            }
            else if (rarity >= RosterCardState.Rarity.Epic) ServiceLocator.Get<HapticService>()?.Medium();
            else ServiceLocator.Get<HapticService>()?.Light();
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

        private void DismissReveal()
        {
            _revealing = false;
            if (_reveal != null) _reveal.gameObject.SetActive(false);
            Refresh();
        }

        private void Update()
        {
            if (!_revealing) return;
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
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_pets == null) return;
            if (_title != null) _title.text = EtkinlikKit.OneLine(Loc.T("dost.baslik"));
            _pearl.text = EtkinlikKit.OneLine("◉ " + Loc.T("dost.inci") + ": " + _pets.Pearls.ToString(Culture));
            _essence.text = EtkinlikKit.OneLine("✦ " + Loc.T("dost.oz") + ": " + _pets.PetEssence.ToString(Culture));
            _pity.text = PityText();
            _openOneText.text = EtkinlikKit.OneLine(Loc.T("kaptan.ac") + " ×1   " + _pets.ChestCost(1).ToString(Culture) + " ◉");
            int bulk = _pets.ChestTuning.BulkCount;
            _openBulkText.text = EtkinlikKit.OneLine(string.Format(Loc.T("kaptan.acCok"), bulk.ToString(Culture)) + "   "
                                                     + _pets.ChestCost(bulk).ToString(Culture) + " ◉");
            EtkinlikKit.SetFace(_openOne, _openOneText, _pets.CanOpenChest(1) ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead,
                                _pets.CanOpenChest(1));
            EtkinlikKit.SetFace(_openBulk, _openBulkText, _pets.CanOpenChest(bulk) ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead,
                                _pets.CanOpenChest(bulk));
            _bonus.text = BonusText();
            RefreshSlots();
            int claimable = _pets.ClaimableRewardCount;
            _claimText.text = EtkinlikKit.OneLine(Loc.T("dost.oduller")
                            + (claimable > 0 ? " (" + claimable.ToString(Culture) + ")" : string.Empty));
            EtkinlikKit.SetFace(_claim, _claimText, claimable > 0 ? EtkinlikKit.Face.Claim : EtkinlikKit.Face.Dead, claimable > 0);
            _confirmTitle.text = Loc.T("dost.fusyon");
            _cancelText.text = Loc.T("dost.vazgec");
            _confirmText.text = Loc.T("dost.onayla");
            RefreshLast();
            RefreshCards();
            RefreshFusion();
        }

        /// <summary>The summary line: the last chest pull (rarity only) or fusion result (with its
        /// star), rebuilt from data so it follows a language change.</summary>
        private void RefreshLast()
        {
            if (_lastSpecies < 0)
            {
                _last.text = Loc.T("dost.son_yok");
                _last.color = InkSoft;
                return;
            }
            string what = SpeciesName(_lastSpecies) + " · " + RarityName(_lastRarity);
            _last.text = _lastStar > 0
                ? what + " " + Stars(_lastStar)
                : string.Format(Loc.T("dost.son"), what);
            _last.color = _pets != null ? _pets.RarityTint(_lastRarity) : InkSoft;
        }

        private void RefreshSlots()
        {
            for (int slot = 0; slot < Pets.SlotCount; slot++)
            {
                bool open = slot < _pets.SlotsUnlocked;
                _slotFrame[slot].color = open ? Color.white : new Color(0.78f, 0.80f, 0.85f, 1f);
                EtkinlikKit.SetActive(_slotLock[slot], !open && _slotLock[slot].sprite != null);
                EtkinlikKit.SetActive(_slotNumber[slot], open || _slotLock[slot].sprite == null);
                _slotCaption[slot].text = open
                    ? Loc.T("dost.acik")
                    : string.Format(Loc.T("dost.yuva_kilit"), _pets.FightsUntilSlot(slot).ToString(Culture));
                _slotCaption[slot].color = open ? OpenGreen : InkSoft;
            }
        }

        private void RefreshCards()
        {
            if (_pets == null) return;
            for (int species = 0; species < Pets.SpeciesCount; species++)
            {
                bool owned = _pets.TryBestOwned(species, out var rarity, out int star);
                Color tint = owned ? _pets.RarityTint(rarity) : InkFaint;
                // The picked card goes a pale sea blue: the white fill takes the tint, the blue rim
                // barely moves, so it reads as lit rather than recoloured.
                _cardArt[species].color = species == _selectedSpecies ? new Color(0.80f, 0.90f, 1f, 1f) : Color.white;
                ShowPet(_cardFrame[species], species, owned, rarity);
                // One line each, shrinking rather than wrapping: a third of the page is narrow, and the
                // rung and its five stars side by side read like the masters' cards. The frame already
                // says this is the best copy, so the card drops the "Best:" prefix to make them fit.
                _cardName[species].text = EtkinlikKit.OneLine(SpeciesName(species));
                _cardName[species].color = owned ? Ink : InkSoft;
                _cardBest[species].text = owned
                    ? EtkinlikKit.OneLine(RarityName(rarity) + " " + Stars(star))
                    : Loc.T("kaptan.bulunmadi");
                _cardBest[species].color = owned ? tint : InkFaint;
                // The stat the species feeds, so an unowned card still says what it would do.
                Pets.EffectKind kind = Pets.EffectKindOf(species);
                _cardEffect[species].text = EtkinlikKit.OneLine(owned
                    ? EffectName(kind) + " " + EffectValue(kind, Pets.Bonus(kind, rarity, star, _pets.Tuning))
                    : EffectName(kind));
                _cardCounts[species].text = EtkinlikKit.OneLine(CountText(species));
                bool ready = FindFusion(species, out _);
                if (_cardReady[species].activeSelf != (ready && _readyBadge != null))
                    _cardReady[species].SetActive(ready && _readyBadge != null);
            }
        }

        private void RefreshFusion()
        {
            _hasFusion = FindFusion(_selectedSpecies, out _fusion);
            if (_hasFusion)
            {
                string inputs = _fusion.RealCopiesConsumed.ToString(Culture) + "× " + RarityName(_fusion.SourceRarity)
                              + " " + Stars(_fusion.SourceStar);
                if (_fusion.UsedEssence) inputs += " + 1 " + Loc.T("dost.oz");
                _fusionPreview.text = inputs + " → " + RarityName(_fusion.ResultRarity) + " " + Stars(_fusion.ResultStar);
                _fusionPreview.color = _pets.RarityTint(_fusion.ResultRarity);
            }
            else
            {
                _fusionPreview.text = ShortfallText(_selectedSpecies);
                _fusionPreview.color = InkSoft;
            }
            Sprite portrait = PortraitOf(_selectedSpecies);
            _fusePortrait.sprite = portrait;
            _fusePortrait.enabled = portrait != null;
            _fusePortrait.color = _pets != null && _pets.Owned(_selectedSpecies) ? Color.white : new Color(0.34f, 0.37f, 0.43f, 0.9f);
            _fuseText.text = Loc.T("dost.fusyon");
            EtkinlikKit.SetFace(_fuse, _fuseText, _hasFusion ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead, _hasFusion);
        }

        /// <summary>
        /// What exactly is missing, for the cell closest to fusing: the rung, how many of three are
        /// in hand, how many more it wants — and, one short, that a Pet Essence would do instead.
        /// </summary>
        private string ShortfallText(int species)
        {
            if (_pets == null || !_pets.Owned(species)) return Loc.T("kaptan.bulunmadi");

            int bestCount = 0, bestRarity = -1, bestStar = 0;
            for (int r = Pets.RarityCount - 1; r >= 0; r--)
                for (int star = Pets.MaxStars; star >= 1; star--)
                {
                    var rarity = (RosterCardState.Rarity)r;
                    if (Pets.IsMaxed(rarity, star)) continue;
                    int count = _pets.CountAt(species, rarity, star);
                    if (count > bestCount) { bestCount = count; bestRarity = r; bestStar = star; }
                }
            if (bestRarity < 0) return Loc.T("dost.fusyon_zirve");

            int need = Pets.FuseGroupSize - bestCount;
            string text = RarityName((RosterCardState.Rarity)bestRarity) + " " + Stars(bestStar) + ": "
                        + bestCount.ToString(Culture) + "/" + Pets.FuseGroupSize.ToString(Culture) + " — "
                        + string.Format(Loc.T("dost.fusyon_eksik"), need.ToString(Culture));
            if (need == 1) text += " " + Loc.T("dost.fusyon_oz_ile");
            return text;
        }

        /// <summary>The confirm card, from the pending receipt: inputs, result, and the pet's live
        /// bonus before and after — resolved with the same best-copy rule combat uses.</summary>
        private void FillConfirm()
        {
            PetService.FusionResult f = _pendingFusion;
            string inputs = f.RealCopiesConsumed.ToString(Culture) + "× " + RarityName(f.SourceRarity) + " " + Stars(f.SourceStar);
            if (f.UsedEssence) inputs += "  +  1 " + Loc.T("dost.oz");
            _confirmInputs.text = SpeciesName(f.Species) + ":  " + inputs;
            _confirmResult.text = "→ " + RarityName(f.ResultRarity) + " " + Stars(f.ResultStar);
            _confirmResult.color = _pets.RarityTint(f.ResultRarity);
            ShowPet(_confirmFrame, f.Species, true, f.ResultRarity);

            Pets.EffectKind kind = Pets.EffectKindOf(f.Species);
            double before = 0d;
            if (_pets.TryBestOwned(f.Species, out var nowRarity, out int nowStar))
                before = Pets.Bonus(kind, nowRarity, nowStar, _pets.Tuning);
            double after = BonusAfter(f, kind);
            _confirmBonus.text = EffectName(kind) + ":  " + EffectValue(kind, before) + "  →  " + EffectValue(kind, after);
            _confirmBonus.color = after >= before ? Gain : Drop;
        }

        /// <summary>The live bonus once this fusion lands: the grid with the fusion applied, read
        /// through the same best-owned rule the combat bonus uses. Copies one small array per tap.</summary>
        private double BonusAfter(PetService.FusionResult f, Pets.EffectKind kind)
        {
            var counts = new int[Pets.CountsLength];
            for (int r = 0; r < Pets.RarityCount; r++)
                for (int star = 1; star <= Pets.MaxStars; star++)
                {
                    int idx = Pets.CellIndex(f.Species, (RosterCardState.Rarity)r, star);
                    counts[idx] = _pets.CountAt(f.Species, (RosterCardState.Rarity)r, star);
                }
            counts[Pets.CellIndex(f.Species, f.SourceRarity, f.SourceStar)] -= f.RealCopiesConsumed;
            counts[Pets.CellIndex(f.Species, f.ResultRarity, f.ResultStar)] += 1;
            return Pets.TryBestOwned(counts, f.Species, out var rarity, out int starAfter)
                ? Pets.Bonus(kind, rarity, starAfter, _pets.Tuning) : 0d;
        }

        private string RevealFooter()
        {
            if (_pets == null) return string.Empty;
            var t = _pets.ChestTuning;
            return "◉ " + Loc.T("dost.inci") + ": " + _pets.Pearls.ToString(Culture)
                 + "     ✦ " + Loc.T("dost.oz") + ": " + _pets.PetEssence.ToString(Culture) + "\n"
                 + PityLine(RosterCardState.Rarity.Epic, t.EpicPity, _pets.SinceEpic, true) + "  ·  "
                 + PityLine(RosterCardState.Rarity.Legendary, t.LegendaryPity, _pets.SinceLegendary, true);
        }

        private bool FindFusion(int species, out PetService.FusionResult result)
        {
            result = default;
            if (_pets == null || !Pets.Exists(species)) return false;
            for (int rarity = Pets.RarityCount - 1; rarity >= 0; rarity--)
                for (int star = Pets.MaxStars; star >= 1; star--)
                    if (_pets.TryGetFusionResult(species, (RosterCardState.Rarity)rarity, star, true, out result))
                        return true;
            return false;
        }

        // ------------------------------------------------------------------ text
        /// <summary>The guarantee in two lines — its heading, then both rungs — which is what the
        /// chest card's column has room for.</summary>
        private string PityText()
        {
            var t = _pets.ChestTuning;
            return Loc.T("dost.merhamet") + "\n"
                 + PityLine(RosterCardState.Rarity.Epic, t.EpicPity, _pets.SinceEpic, false) + "\n"
                 + PityLine(RosterCardState.Rarity.Legendary, t.LegendaryPity, _pets.SinceLegendary, false);
        }

        /// <summary>Chests left until a rarity's guarantee: "EPIC: 7" for the narrow chest box, or the
        /// captain chest's own "EPIC: within 7 pulls" sentence for the reveal footer. A table with no
        /// guarantee for that rarity says "—" rather than promising one.</summary>
        private static string PityLine(RosterCardState.Rarity rarity, int pity, int since, bool sentence)
        {
            string name = RarityName(rarity);
            if (pity <= 0) return name + ": —";
            string left = Mathf.Max(1, pity - since).ToString(Culture);
            return sentence ? string.Format(Loc.T("kaptan.teselli"), name, left) : name + ": " + left;
        }

        private string BonusText()
        {
            double[] bonus = _pets.CombatBonus();
            var b = new StringBuilder();
            for (int i = 0; i < bonus.Length; i++)
            {
                if (bonus[i] <= 0d) continue;
                if (b.Length > 0) b.Append(" · ");
                b.Append(EffectName((Pets.EffectKind)i)).Append(' ')
                 .Append(EffectValue((Pets.EffectKind)i, bonus[i]));
            }
            return string.Format(Loc.T("dost.bonus"), b.Length > 0 ? b.ToString() : Loc.T("dost.yok"));
        }

        /// <summary>Copies in hand per rung, compact enough for a card's last line: "N2★×3" is three
        /// Rare two-stars.</summary>
        private string CountText(int species)
        {
            var b = new StringBuilder();
            for (int r = 0; r < Pets.RarityCount; r++)
                for (int star = 1; star <= Pets.MaxStars; star++)
                {
                    int count = _pets.CountAt(species, (RosterCardState.Rarity)r, star);
                    if (count <= 0) continue;
                    if (b.Length > 0) b.Append(" · ");
                    b.Append(RarityShort((RosterCardState.Rarity)r)).Append(star).Append('★').Append('×').Append(count);
                }
            return b.ToString();
        }

        private Sprite PortraitOf(int species)
            => _config != null ? _config.PortraitOf(Pets.IdOf(species)) : null;

        /// <summary>The species' name row: <c>dost.&lt;id&gt;.ad</c>, keyed by the save id rather than
        /// the roster index so a reordered roster still names each pet right.</summary>
        private static string SpeciesName(int species)
            => Loc.T("dost." + Pets.IdOf(species) + ".ad");

        private static string RarityName(RosterCardState.Rarity rarity)
            => Loc.T("kaptan.derece." + (int)rarity);

        private static string RarityShort(RosterCardState.Rarity rarity)
            => Loc.T("dost.kisa." + (int)rarity);

        private static string Stars(int value)
            => new string('★', Mathf.Clamp(value, 0, Pets.MaxStars))
             + new string('☆', Pets.MaxStars - Mathf.Clamp(value, 0, Pets.MaxStars));

        /// <summary>The fight sheet's own word for the stat, so both screens name a bonus alike.</summary>
        private static string EffectName(Pets.EffectKind kind)
            => Loc.T(SeaFightUI.EffectLabelKey(kind));

        private static string EffectValue(Pets.EffectKind kind, double value)
        {
            switch (kind)
            {
                case Pets.EffectKind.Dodge:
                case Pets.EffectKind.Salvo:
                case Pets.EffectKind.Stun:
                case Pets.EffectKind.Steal:
                    return "+" + (value * 100d).ToString("0.#", Culture) + "%";
                default:
                    return "+" + value.ToString("0.#", Culture);
            }
        }

        // ---------------------------------------------------------------- pieces
        /// <summary>The workshop's framed white card, nine-sliced so its rim holds one thickness —
        /// drawn at two thirds of the art's own rim, which at full size crowded the six small cards.</summary>
        private RectTransform Panel(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            Image card = EkranKit.Sliced(parent, name, _card, min, max, false);
            card.pixelsPerUnitMultiplier = 1.5f;
            return card.rectTransform;
        }

        /// <summary>
        /// A rarity frame in <paramref name="min"/>–<paramref name="max"/>, held to its art's aspect.
        /// Backdrop and animal are drawn first so the frame's rim covers their edges.
        /// </summary>
        private PetFrame Frame(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform area = EtkinlikKit.Slot(parent, name + "Alani", min, max);
            RectTransform box = EtkinlikKit.Slot(area, name, Vector2.zero, Vector2.one);
            var frame = new PetFrame { Fit = box.gameObject.AddComponent<AspectRatioFitter>() };
            frame.Fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            frame.Fit.aspectRatio = 321f / 448f;
            frame.Window = UiBuild.Flat(box, "Pencere", Color.white, Vector2.zero, Vector2.one).GetComponent<Image>();
            frame.Window.raycastTarget = false;
            frame.Portrait = EkranKit.Icon(box, "Portre", null, PetBoxMin, PetBoxMax);
            frame.Frame = EkranKit.Icon(box, "Kenar", null, Vector2.zero, Vector2.one);
            return frame;
        }

        /// <summary>A pet in its frame: the rung's frame and a pale wash of its colour behind the animal,
        /// or the padlocked frame with the animal in shadow for a species nobody has drawn.</summary>
        private void ShowPet(PetFrame f, int species, bool owned, RosterCardState.Rarity rarity)
        {
            int index = owned ? Mathf.Clamp((int)rarity, 0, LockedFrame - 1) : LockedFrame;
            SetFrame(f, _frames != null ? _frames[index] : null, index);
            Color tint = owned && _pets != null ? _pets.RarityTint(rarity) : InkFaint;
            f.Window.enabled = true;
            f.Window.color = Color.Lerp(tint, Color.white, owned ? 0.72f : 0.80f);
            Sprite portrait = PortraitOf(species);
            f.Portrait.sprite = portrait;
            f.Portrait.enabled = portrait != null;
            f.Portrait.color = owned ? Color.white : new Color(0.30f, 0.33f, 0.40f, 0.85f);
        }

        /// <summary>The face-down reveal tile: the kit's card back, nothing behind it.</summary>
        private void ShowCardBack(PetFrame f)
        {
            f.Portrait.enabled = false;
            f.Window.enabled = false;
            f.Frame.sprite = _cardBack;
            f.Frame.enabled = _cardBack != null;
            if (_cardBack != null) f.Fit.aspectRatio = _cardBack.rect.width / _cardBack.rect.height;
        }

        private static void SetFrame(PetFrame f, Sprite sprite, int index)
        {
            f.Frame.sprite = sprite;
            f.Frame.enabled = sprite != null;
            if (sprite != null) f.Fit.aspectRatio = sprite.rect.width / sprite.rect.height;
            Vector4 w = FrameWindows[index];
            RectTransform window = f.Window.rectTransform;
            window.anchorMin = new Vector2(w.x, w.y);
            window.anchorMax = new Vector2(w.z, w.w);
        }

        /// <summary>A label in its own band, shrinking to fit — the band is <paramref name="name"/>,
        /// the Text its child — honouring the accessibility text scale as ForemanRosterUI does.</summary>
        private static Text Line(RectTransform parent, string name, Vector2 min, Vector2 max, int size,
                                 TextAnchor anchor, Color color, int minSize)
        {
            Text label = UiBuild.Label(EtkinlikKit.Slot(parent, name, min, max), "Text", string.Empty, size, anchor);
            label.color = color;
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            float scale = accessibility != null ? accessibility.TextScale : 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(minSize * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(size * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
    }
}
