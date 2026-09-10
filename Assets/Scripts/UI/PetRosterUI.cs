using System.Globalization;
using System.Text;
using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
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
    /// </summary>
    public sealed class PetRosterUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 118;

        [Header("Görseller")]
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite ribbon;
        [SerializeField] private Sprite actionButton;
        [SerializeField] private Sprite closeIcon;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        [SerializeField] private Color backdrop = new Color(0.15f, 0.18f, 0.26f, 1f);

        public const string OpenerButtonName = "BtnDenizDostlari";

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color ButtonGreen = new Color(0.24f, 0.68f, 0.36f, 1f);
        private static readonly Color ButtonYellow = new Color(0.94f, 0.68f, 0.20f, 1f);
        private static readonly Color ButtonOff = new Color(0.72f, 0.75f, 0.80f, 1f);
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        private PetService _pets;
        private PetConfig _config;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _title, _pearl, _essence, _pity, _last, _bonus, _slotInfo, _fusionPreview;
        private Button _openOne, _openBulk, _fuse;
        private Text _openOneText, _openBulkText, _fuseText;
        private readonly Image[] _cardArt = new Image[Pets.SpeciesCount];
        private readonly Image[] _cardPortrait = new Image[Pets.SpeciesCount];
        private readonly Text[] _cardName = new Text[Pets.SpeciesCount];
        private readonly Text[] _cardBest = new Text[Pets.SpeciesCount];
        private readonly Text[] _cardCounts = new Text[Pets.SpeciesCount];
        private readonly Button[] _cardButton = new Button[Pets.SpeciesCount];
        private GameObject _openerChip;
        private TMP_Text _openerCount;
        private int _selectedSpecies;
        private PetService.FusionResult _fusion;
        private bool _hasFusion;

        // The chest reveal: its own canvas and a pooled row of tiles, the same ceremony
        // ForemanRosterUI ships for its crate — see BuildReveal.
        private const int RevealTiles = 10;
        private const float FlipSeconds = 0.34f;
        private const float FlipStagger = 0.13f;
        private static readonly Color CardDown = new Color(0.11f, 0.12f, 0.17f, 1f);
        private static readonly Color Gain = new Color(0.20f, 0.60f, 0.30f, 1f);
        private static readonly Color Drop = new Color(0.80f, 0.26f, 0.22f, 1f);
        private RectTransform _reveal;
        private Text _revealTitle, _revealFooter, _revealHint;
        private readonly RectTransform[] _tileRect = new RectTransform[RevealTiles];
        private readonly Image[] _tile = new Image[RevealTiles];
        private readonly Image[] _tileArt = new Image[RevealTiles];
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
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DenizDostlariKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform panel = Art(_root, "Zemin", new Vector2(0.025f, 0.018f), new Vector2(0.975f, 0.982f));
            BuildHeader(panel);
            BuildChest(panel);
            BuildSummary(panel);
            BuildSlots(panel);
            BuildCards(panel);
            BuildFusion(panel);
            BuildConfirm();
            UiBuild.InsetContent(_root);
            BuildReveal();
        }

        private void BuildHeader(RectTransform panel)
        {
            RectTransform band = Art(panel, "Serit", new Vector2(0.285f, 0.930f), new Vector2(0.715f, 0.992f));
            _title = UiBuild.Label(band, "Text", Loc.T("dost.baslik"), 35, TextAnchor.MiddleCenter);
            _title.color = Ink;
            Fit(_title, 18, 35);   // "COMPANHEIROS MARINHOS" is wider than the ribbon at full size

            Button close = UiBuild.Btn(panel, "Kapat", string.Empty,
                closeIcon != null ? closeIcon : UiSkin.ButtonGrey, InkSoft, 28, Hide);
            Image closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.875f, 0.936f), new Vector2(0.955f, 0.992f));
        }

        private void BuildChest(RectTransform panel)
        {
            RectTransform chest = Art(panel, "Sandik", new Vector2(0.035f, 0.765f), new Vector2(0.965f, 0.920f));
            _pearl = UiBuild.Label(Slot(chest, "Inciler", new Vector2(0.030f, 0.580f), new Vector2(0.430f, 0.930f)),
                "Text", string.Empty, 27, TextAnchor.MiddleLeft);
            _pearl.color = Ink;
            _essence = UiBuild.Label(Slot(chest, "Oz", new Vector2(0.030f, 0.220f), new Vector2(0.430f, 0.560f)),
                "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            _essence.color = InkSoft;
            _pity = UiBuild.Label(Slot(chest, "Merhamet", new Vector2(0.430f, 0.130f), new Vector2(0.635f, 0.920f)),
                "Text", string.Empty, 18, TextAnchor.MiddleLeft);
            _pity.color = InkSoft;

            _openOne = UiBuild.Btn(chest, "AcBir", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen, ButtonGreen, 21, () => Open(1));
            UiBuild.Anchor((RectTransform)_openOne.transform, new Vector2(0.650f, 0.535f), new Vector2(0.970f, 0.925f));
            _openOneText = _openOne.GetComponentInChildren<Text>();

            _openBulk = UiBuild.Btn(chest, "AcToplu", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonYellow, ButtonYellow, 20,
                () => Open(_pets != null ? _pets.ChestTuning.BulkCount : 10));
            UiBuild.Anchor((RectTransform)_openBulk.transform, new Vector2(0.650f, 0.090f), new Vector2(0.970f, 0.480f));
            _openBulkText = _openBulk.GetComponentInChildren<Text>();
        }

        private void BuildSummary(RectTransform panel)
        {
            RectTransform summary = Art(panel, "Durum", new Vector2(0.035f, 0.670f), new Vector2(0.965f, 0.750f));
            _last = UiBuild.Label(Slot(summary, "SonCekilis", new Vector2(0.025f, 0.060f), new Vector2(0.485f, 0.940f)),
                "Text", Loc.T("dost.son_yok"), 18, TextAnchor.MiddleLeft);
            _last.color = InkSoft;
            _bonus = UiBuild.Label(Slot(summary, "CanliBonus", new Vector2(0.505f, 0.060f), new Vector2(0.975f, 0.940f)),
                "Text", string.Empty, 17, TextAnchor.MiddleLeft);
            _bonus.color = Ink;
        }

        private void BuildSlots(RectTransform panel)
        {
            RectTransform slots = Art(panel, "Yuvalar", new Vector2(0.035f, 0.595f), new Vector2(0.965f, 0.660f));
            _slotInfo = UiBuild.Label(Slot(slots, "Bilgi", new Vector2(0.025f, 0.050f), new Vector2(0.640f, 0.950f)),
                "Text", string.Empty, 18, TextAnchor.MiddleCenter);
            _slotInfo.color = InkSoft;
            Fit(_slotInfo, 11, 18);

            // Every free pearl source the panel pays — daily, reached sea milestones, reached
            // collection tiers — behind one button that says how many are waiting.
            _claim = UiBuild.Btn(slots, "Oduller", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonYellow, ButtonYellow, 19, ClaimRewards);
            UiBuild.Anchor((RectTransform)_claim.transform, new Vector2(0.655f, 0.090f), new Vector2(0.975f, 0.910f));
            _claimText = _claim.GetComponentInChildren<Text>();
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

            RectTransform card = Art(_confirm, "OnayKarti", new Vector2(0.07f, 0.33f), new Vector2(0.93f, 0.67f));
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            _confirmTitle = UiBuild.Label(Slot(card, "OnayBaslik", new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.95f)),
                "Text", Loc.T("dost.fusyon"), 34, TextAnchor.MiddleCenter);
            _confirmTitle.color = Ink;
            _confirmInputs = UiBuild.Label(Slot(card, "Girdiler", new Vector2(0.05f, 0.63f), new Vector2(0.95f, 0.79f)),
                "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _confirmInputs.color = InkSoft;
            Fit(_confirmInputs, 13, 24);
            _confirmResult = UiBuild.Label(Slot(card, "Sonuc", new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.63f)),
                "Text", string.Empty, 30, TextAnchor.MiddleCenter);
            Fit(_confirmResult, 15, 30);
            _confirmBonus = UiBuild.Label(Slot(card, "OnayBonus", new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.45f)),
                "Text", string.Empty, 21, TextAnchor.MiddleCenter);
            Fit(_confirmBonus, 12, 21);

            Button cancel = UiBuild.Btn(card, "Vazgec", Loc.T("dost.vazgec"),
                actionButton != null ? actionButton : UiSkin.ButtonGrey, ButtonOff, 22, CancelFusion);
            UiBuild.Anchor((RectTransform)cancel.transform, new Vector2(0.06f, 0.06f), new Vector2(0.47f, 0.24f));
            _cancelText = cancel.GetComponentInChildren<Text>();
            Button confirm = UiBuild.Btn(card, "Onayla", Loc.T("dost.onayla"),
                actionButton != null ? actionButton : UiSkin.ButtonGreen, ButtonGreen, 22, ConfirmFusion);
            UiBuild.Anchor((RectTransform)confirm.transform, new Vector2(0.53f, 0.06f), new Vector2(0.94f, 0.24f));
            _confirmText = confirm.GetComponentInChildren<Text>();

            _confirm.gameObject.SetActive(false);
        }

        /// <summary>
        /// The chest reveal, on its own canvas above the HUD gear — ForemanRosterUI's crate ceremony
        /// and its reasons: the whole sheet is the skip target, the confetti lives on this canvas so
        /// it is not drawn under an opaque sheet, and the tiles are built once so a reveal allocates
        /// nothing while it plays. The pull it shows is already paid and saved when it starts.
        /// </summary>
        private void BuildReveal()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DenizDostlariSandikKanvas", sortingOrder + 25);
            _reveal = UiBuild.Flat(canvas, "Acilis", new Color(0.03f, 0.04f, 0.07f, 1f), Vector2.zero, Vector2.one);
            Button skip = _reveal.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(OnRevealTapped);

            _revealTitle = UiBuild.Label(Slot(_reveal, "SandikBaslik", new Vector2(0.08f, 0.860f), new Vector2(0.92f, 0.950f)),
                "Text", string.Empty, 44, TextAnchor.MiddleCenter);
            _revealTitle.color = Paper;
            Fit(_revealTitle, 22, 44);
            _revealFooter = UiBuild.Label(Slot(_reveal, "Ozet", new Vector2(0.05f, 0.125f), new Vector2(0.95f, 0.215f)),
                "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _revealFooter.color = Paper;
            Fit(_revealFooter, 13, 24);
            _revealHint = UiBuild.Label(Slot(_reveal, "Devam", new Vector2(0.10f, 0.045f), new Vector2(0.90f, 0.110f)),
                "Text", string.Empty, 22, TextAnchor.MiddleCenter);
            _revealHint.color = InkFaint;

            for (int t = 0; t < RevealTiles; t++)
            {
                RectTransform tile = UiBuild.Flat(_reveal, "Kart_" + t, CardDown, Vector2.zero, Vector2.one);
                Image image = tile.GetComponent<Image>();
                if (cardPanel != null) { image.sprite = cardPanel; image.type = Image.Type.Sliced; }
                image.raycastTarget = false;
                _tileRect[t] = tile;
                _tile[t] = image;
                _tileArt[t] = Icon(tile, "Portre", null, new Vector2(0.14f, 0.36f), new Vector2(0.86f, 0.95f));
                _tileName[t] = UiBuild.Label(Slot(tile, "Ad", new Vector2(0.04f, 0.19f), new Vector2(0.96f, 0.35f)),
                    "Text", string.Empty, 22, TextAnchor.MiddleCenter);
                _tileName[t].color = Paper;
                Fit(_tileName[t], 11, 22);
                _tileRarity[t] = UiBuild.Label(Slot(tile, "Nadirlik", new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.19f)),
                    "Text", string.Empty, 20, TextAnchor.MiddleCenter);
                _tileRarity[t].color = Paper;
                Fit(_tileRarity[t], 10, 20);
                tile.gameObject.SetActive(false);
            }

            _reveal.gameObject.SetActive(false);
            _confetti = canvas.gameObject.AddComponent<ConfettiBurst>();
            _rewardReveal = RewardRevealUI.Create(canvas, cardPanel, _config != null ? _config.PearlIcon : null);
            UiBuild.InsetContent(_reveal);
        }

        private void BuildCards(RectTransform panel)
        {
            const float left = 0.035f, right = 0.965f, top = 0.580f, bottom = 0.155f;
            const int cols = 2, rows = 3;
            float width = (right - left) / cols;
            float height = (top - bottom) / rows;
            for (int species = 0; species < Pets.SpeciesCount; species++)
            {
                int col = species % cols;
                int row = species / cols;
                RectTransform card = Art(panel, "Pet_" + species,
                    new Vector2(left + col * width + 0.006f, top - (row + 1) * height + 0.006f),
                    new Vector2(left + (col + 1) * width - 0.006f, top - row * height - 0.006f));
                _cardArt[species] = card.GetComponent<Image>();
                int captured = species;
                _cardButton[species] = card.gameObject.AddComponent<Button>();
                _cardButton[species].transition = Selectable.Transition.None;
                _cardButton[species].onClick.AddListener(() => SelectSpecies(captured));

                _cardPortrait[species] = Icon(card, "Portre", PortraitOf(species),
                    new Vector2(0.030f, 0.220f), new Vector2(0.215f, 0.875f));
                _cardName[species] = UiBuild.Label(Slot(card, "Ad", new Vector2(0.235f, 0.650f), new Vector2(0.965f, 0.940f)),
                    "Text", string.Empty, 20, TextAnchor.MiddleLeft);
                _cardName[species].color = Ink;
                Fit(_cardName[species], 11, 20);
                _cardBest[species] = UiBuild.Label(Slot(card, "EnIyi", new Vector2(0.235f, 0.425f), new Vector2(0.965f, 0.660f)),
                    "Text", string.Empty, 16, TextAnchor.MiddleLeft);
                _cardBest[species].color = InkSoft;
                _cardCounts[species] = UiBuild.Label(Slot(card, "Sayilar", new Vector2(0.030f, 0.060f), new Vector2(0.965f, 0.400f)),
                    "Text", string.Empty, 13, TextAnchor.UpperLeft);
                _cardCounts[species].color = InkSoft;
            }
        }

        private void BuildFusion(RectTransform panel)
        {
            RectTransform fusion = Art(panel, "Fusyon", new Vector2(0.035f, 0.030f), new Vector2(0.965f, 0.140f));
            _fusionPreview = UiBuild.Label(Slot(fusion, "Onizleme", new Vector2(0.025f, 0.080f), new Vector2(0.640f, 0.920f)),
                "Text", string.Empty, 17, TextAnchor.MiddleLeft);
            _fusionPreview.color = Ink;
            _fuse = UiBuild.Btn(fusion, "FusyonYap", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen, ButtonGreen, 19, FuseSelected);
            UiBuild.Anchor((RectTransform)_fuse.transform, new Vector2(0.665f, 0.120f), new Vector2(0.970f, 0.880f));
            _fuseText = _fuse.GetComponentInChildren<Text>();
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
                _tileArt[t].enabled = false;               // face down until it turns
                _tileName[t].text = string.Empty;
                _tileRarity[t].text = string.Empty;
                _tile[t].color = CardDown;
                _tileRect[t].localScale = Vector3.one;
            }

            // Written per open rather than at build, so a language change between chests lands.
            _revealTitle.text = pulled.Length > 1
                ? pulled.Length.ToString(Culture) + " × " + Loc.T("dost.sandik")
                : Loc.T("dost.sandik");
            _revealFooter.text = RevealFooter();
            _revealHint.text = Loc.T("usta.devam");
            _revealClock = 0f;
            _revealing = true;
            _reveal.gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
        }

        /// <summary>One big card for a single chest; a three-wide grid for a batch.</summary>
        private void LayoutTiles(int count)
        {
            if (count <= 1)
            {
                UiBuild.Anchor(_tileRect[0], new Vector2(0.28f, 0.40f), new Vector2(0.72f, 0.74f));
                return;
            }
            const int cols = 3;
            const float left = 0.06f, right = 0.94f, top = 0.840f, bottom = 0.230f;
            int rows = (count + cols - 1) / cols;
            float cellW = (right - left) / cols, cellH = (top - bottom) / rows;
            for (int t = 0; t < count; t++)
            {
                int col = t % cols, row = t / cols;
                UiBuild.Anchor(_tileRect[t],
                    new Vector2(left + col * cellW + 0.012f, top - (row + 1) * cellH + 0.010f),
                    new Vector2(left + (col + 1) * cellW - 0.012f, top - row * cellH - 0.010f));
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
            _tile[t].color = _pets != null ? _pets.RarityTint(rarity) : Paper;
            Sprite portrait = PortraitOf(species);
            _tileArt[t].sprite = portrait != null ? portrait : UiSkin.Flat;
            _tileArt[t].enabled = portrait != null;
            _tileName[t].text = SpeciesName(species);
            _tileRarity[t].text = _tileEssence[t]
                ? "✦ +1 " + Loc.T("dost.oz")
                : RarityName(rarity) + " " + Stars(1);

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
            if (_title != null) _title.text = Loc.T("dost.baslik");
            _pearl.text = "◉ " + Loc.T("dost.inci") + ": " + _pets.Pearls.ToString(Culture);
            _essence.text = "✦ " + Loc.T("dost.oz") + ": " + _pets.PetEssence.ToString(Culture);
            _pity.text = PityText();
            _openOneText.text = Loc.T("kaptan.ac") + "\n" + _pets.ChestCost(1).ToString(Culture) + " ◉";
            int bulk = _pets.ChestTuning.BulkCount;
            _openBulkText.text = string.Format(Loc.T("kaptan.acCok"), bulk.ToString(Culture)) + "\n"
                              + _pets.ChestCost(bulk).ToString(Culture) + " ◉";
            Dress(_openOne, _pets.CanOpenChest(1), ButtonGreen);
            Dress(_openBulk, _pets.CanOpenChest(bulk), ButtonYellow);
            _bonus.text = BonusText();
            _slotInfo.text = SlotText();
            int claimable = _pets.ClaimableRewardCount;
            _claimText.text = Loc.T("dost.oduller")
                            + (claimable > 0 ? " (" + claimable.ToString(Culture) + ")" : string.Empty);
            Dress(_claim, claimable > 0, ButtonYellow);
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

        private void RefreshCards()
        {
            if (_pets == null) return;
            for (int species = 0; species < Pets.SpeciesCount; species++)
            {
                bool owned = _pets.TryBestOwned(species, out var rarity, out int star);
                Color tint = owned ? _pets.RarityTint(rarity) : InkFaint;
                _cardArt[species].color = species == _selectedSpecies
                    ? new Color(tint.r, tint.g, tint.b, 0.46f)
                    : new Color(1f, 1f, 1f, 1f);
                Sprite portrait = PortraitOf(species);
                _cardPortrait[species].sprite = portrait != null ? portrait : UiSkin.Flat;
                _cardPortrait[species].color = portrait != null
                    ? (owned ? Color.white : new Color(0.34f, 0.37f, 0.43f, 0.9f))
                    : new Color(tint.r, tint.g, tint.b, owned ? 0.8f : 0.25f);
                // The species and the stat it feeds, so an unowned card still says what it would do.
                _cardName[species].text = SpeciesName(species) + "  ·  " + EffectName(Pets.EffectKindOf(species));
                _cardName[species].color = owned ? Ink : InkFaint;
                _cardBest[species].text = owned
                    ? string.Format(Loc.T("dost.en_iyi"), RarityName(rarity) + " " + Stars(star))
                    : Loc.T("kaptan.bulunmadi");
                _cardBest[species].color = owned ? tint : InkFaint;
                _cardCounts[species].text = CountText(species);
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
            _fuseText.text = Loc.T("dost.fusyon");
            Dress(_fuse, _hasFusion, ButtonGreen);
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

        private string SlotText()
        {
            var b = new StringBuilder();
            for (int slot = 0; slot < Pets.SlotCount; slot++)
            {
                if (slot > 0) b.Append("   ·   ");
                b.Append(string.Format(Loc.T("dost.yuva"), (slot + 1).ToString(Culture))).Append(": ");
                if (slot < _pets.SlotsUnlocked) b.Append(Loc.T("dost.acik"));
                else b.Append(string.Format(Loc.T("dost.yuva_kilit"), _pets.FightsUntilSlot(slot).ToString(Culture)));
            }
            return b.ToString();
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

        private string CountText(int species)
        {
            var b = new StringBuilder();
            for (int r = 0; r < Pets.RarityCount; r++)
                for (int star = 1; star <= Pets.MaxStars; star++)
                {
                    int count = _pets.CountAt(species, (RosterCardState.Rarity)r, star);
                    if (count <= 0) continue;
                    if (b.Length > 0) b.Append(" · ");
                    b.Append(RarityShort((RosterCardState.Rarity)r)).Append(Stars(star)).Append('×').Append(count);
                }
            return b.Length > 0 ? b.ToString() : "—";
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
        private RectTransform Art(Transform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform rect = UiBuild.Box(parent, name, backdrop, min, max);
            Image image = rect.GetComponent<Image>();
            if (cardPanel != null)
            {
                image.sprite = cardPanel;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }
            return rect;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        private static Image Icon(RectTransform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
        {
            RectTransform rect = UiBuild.Flat(parent, name, Color.white, min, max);
            Image image = rect.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.type = sprite != null ? Image.Type.Simple : Image.Type.Sliced;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Shrinks a label until it fits its box — ForemanRosterUI's helper, honouring the
        /// accessibility text scale the same way.</summary>
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

        private void Dress(Button button, bool live, Color tint)
        {
            if (button == null) return;
            button.interactable = live;
            button.GetComponent<Image>().color = !live ? ButtonOff : actionButton != null ? Color.white : tint;
        }
    }
}
