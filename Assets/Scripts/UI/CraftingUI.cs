using Game.Core;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The workshop screen: the bench on the left, the odds table on the right, and — when a craft
    /// is waiting — the decision card on top of both.
    ///
    /// Built in code for the same reason <see cref="CaptainRosterUI"/> is: the rows come out of
    /// <see cref="Crafting"/>'s own tables, so appending a grade or retouching a bracket should
    /// cost one cell there and nothing here. The opener borrows a real HUD row button, order 4,
    /// after goals, foremen, chapters and captains.
    ///
    /// THE ODDS ARE PRINTED, not implied. Every grade row shows either its share at the CURRENT
    /// level or the level it opens at — the "possibilities per level" the design asks for, and the
    /// disclosure Google Play expects the day points are ever sold.
    ///
    /// THE ART IS THE DESIGN KIT, loaded through <see cref="AtolyeKit"/> and shared with
    /// <see cref="ChapterUI"/> and <see cref="GoalsUI"/>. The kit is pre-coloured, so the three
    /// decision buttons are three sprites rather than one pill tinted three ways, and the retooling
    /// strip is the set's blue capsule instead of the flat rectangle it was. The sprites are handed
    /// on to the depo through <see cref="BuildDepo"/>, so that screen wears the same skin without a
    /// scene edit.
    ///
    /// Refreshed on open and on <see cref="CraftingService.Changed"/>; the once-a-second Update
    /// only drives the retooling clock, and only while the screen is open.
    /// </summary>
    public sealed class CraftingUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 110;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.62f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);

        /// <summary>
        /// The kit art, fetched once in <see cref="Awake"/>. See <see cref="AtolyeKit"/>.
        ///
        /// THE THREE DECISION BUTTONS ARE THREE SPRITES — green to wear it, blue to shelve it, the
        /// pale capsule to scrap it. The kit ships no red or yellow, and scrapping is the one of the
        /// three that should not be shouting anyway.
        /// </summary>
        private Sprite _panel, _ribbon, _btnGreen, _btnBlue, _btnPale, _closeIcon, _barTrack,
                       _barFill, _chip, _gatePill, _pointsIcon;

        [Header("Ödüllü otomatik üretim")]
        [Tooltip("Reklam tamamlanınca otomatik üretimin açık kalacağı süre (saniye).")]
        [SerializeField, Min(1f)] private float rewardedAutoCraftSeconds = 900f;

        private const string OpenerIconResource = "UI/Buttons/atolye";

        /// <summary>The grade ladder's ink — the same five the captain screen and the sea wear.</summary>
        private static readonly Color[] GradeTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),
            new Color(0.26f, 0.60f, 0.92f, 1f),
            new Color(0.62f, 0.38f, 0.92f, 1f),
            new Color(0.96f, 0.66f, 0.18f, 1f),
            new Color(0.94f, 0.28f, 0.42f, 1f),
        };

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Good = new Color(0.24f, 0.68f, 0.36f, 1f);
        private static readonly Color Bad = new Color(0.86f, 0.30f, 0.26f, 1f);
        private const float RibbonBand = 0.677f;

        private CraftingService _crafting;
        private CaptainService _captains;
        private ExpeditionService _sea;
        private FreeRewardService _freeRewards;
        private IAdService _ad;
        private LocalizationService _loc;
        private InventoryUI _depo;
        private RectTransform _root;

        private Text _titleLabel, _pointsLabel, _levelLabel, _tierLabel, _xpLabel, _craftLabel,
                     _gateLabel, _gateClockLabel, _bankLabel, _sourceLabel, _oddsTitleLabel,
                     _captainLabel, _unlockLabel;
        private RectTransform _xpFill;
        private Button _craftBtn;
        private Button _autoCraftAdBtn;
        private Text _autoCraftAdLabel;
        private RectTransform _gateCard;

        private readonly Image[] _oddsStripe = new Image[Captains.GradeCount];
        private readonly Text[] _oddsName = new Text[Captains.GradeCount];
        private readonly Text[] _oddsValue = new Text[Captains.GradeCount];
        private Button _captainPrevBtn, _captainNextBtn;

        private RectTransform _decideCard;
        private Text _decideTitle, _decideScore, _decideRows, _decideWorn, _equipLabel, _salvageLabel;
        private Button _stowBtn;
        private Text _stowLabel, _depoLabel;

        private GameObject _openerChip;
        private TMP_Text _openerCount;

        private float _pollTimer;
        private string _writtenClock;

        private void Awake()
        {
            _crafting = ServiceLocator.Get<CraftingService>();
            _captains = ServiceLocator.Get<CaptainService>();
            _sea = ServiceLocator.Get<ExpeditionService>();
            _freeRewards = ServiceLocator.Get<FreeRewardService>();
            _ad = ServiceLocator.Get<IAdService>();
            LoadKit();
            BuildDepo();
            Build();
            BuildOpener();
            if (_crafting != null) _crafting.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            Hide();
            RefreshOpener();
        }

        private void OnDestroy()
        {
            if (_crafting != null) _crafting.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnChanged() { if (_root != null && _root.gameObject.activeSelf) Refresh(); RefreshOpener(); }

        private void OnLanguageChanged()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("atolye.baslik");
            if (_oddsTitleLabel != null) _oddsTitleLabel.text = Loc.T("atolye.oranlar");
            if (_gateLabel != null) _gateLabel.text = Loc.T("atolye.yenileniyor");
            if (_bankLabel != null) _bankLabel.text = Loc.T("atolye.birikiyor");
            if (_sourceLabel != null) _sourceLabel.text = Loc.T("atolye.nereden");
            if (_captainLabel != null) RefreshCaptainAssignment();
            if (_unlockLabel != null) RefreshNextUnlock();
            if (_root != null && _root.gameObject.activeSelf) Refresh();
            RefreshOpener();
        }

        public void Show() { if (_root != null) _root.gameObject.SetActive(true); Refresh(); }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }

        /// <summary>Only the retooling clock needs a pulse, and only while someone is looking.</summary>
        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;
            _crafting?.Poll();   // opens a stop whose deadline has passed; raises Changed if it did
            RefreshGateClock();
            Refresh();           // also advances the rewarded auto-craft countdown
        }

        // ------------------------------------------------------------------ build
        /// <summary>Before <see cref="BuildDepo"/> and <see cref="Build"/>, which read all of these.</summary>
        private void LoadKit()
        {
            _panel = AtolyeKit.Get("panel_kart");
            _ribbon = AtolyeKit.Get("serit_baslik");
            _btnGreen = AtolyeKit.Get("btn_yesil");
            _btnBlue = AtolyeKit.Get("btn_mavi");
            _btnPale = AtolyeKit.Get("btn_al");
            _closeIcon = AtolyeKit.Get("kapat");
            _barTrack = AtolyeKit.Get("cubuk_yatak");
            _barFill = AtolyeKit.Get("cubuk_yesil");
            _chip = AtolyeKit.Get("hap_cip");
            _gatePill = AtolyeKit.Get("durak_kart");
            _pointsIcon = AtolyeKit.Get("zanaat_puani");
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "AtolyeKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildBench();
            BuildOdds();
            BuildDecideCard();
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>One opaque sheet behind everything — see CaptainRosterUI.BuildBackdrop for why.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", _panel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", _ribbon, new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Zone(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                             new Vector2(0.87f, RibbonBand + 0.13f)),
                                        "Text", Loc.T("atolye.baslik"), 38, TextAnchor.MiddleCenter);

            // The points chip now says WHICH points. It carried a bare number — on a screen whose
            // ÜRET button spells out "3 CRAFT POINTS" in full, the one place the balance lives was
            // the only thing not naming its currency. The kit's gear-and-spark is that name.
            // Wider and lower than it was: the chip carries a whole phrase ("0 CRAFT POINTS"), and
            // the kit capsule spends its height on two end caps, so a tall box left nothing between
            // them. Art draws a borderless sprite Simple and aspect-locked, which is what an icon
            // wants — and the icon is why the chip can say which points these are.
            RectTransform chip = Chip(_root, "Puan", new Vector2(0.030f, 0.893f), new Vector2(0.300f, 0.957f));
            bool named = _pointsIcon != null;
            if (named) Art(chip, "Ikon", _pointsIcon, new Vector2(0.06f, 0.14f), new Vector2(0.26f, 0.86f));
            _pointsLabel = UiBuild.Label(Zone(chip, "Yazi", new Vector2(named ? 0.29f : 0.10f, 0.08f),
                                              new Vector2(0.90f, 0.92f)),
                                         "Text", string.Empty, 26, TextAnchor.MiddleCenter);
            _pointsLabel.color = Paper;
            Fit(_pointsLabel, 11, 26);

            // The way through to the shelf. Beside the title rather than on the bench card: the depo
            // is a screen of its own, not one more control on the bench, and the header is where
            // this screen already keeps what is true of the whole workshop.
            Button depo = UiBuild.Btn(_root, "Depo", string.Empty,
                                      _btnBlue != null ? _btnBlue : UiSkin.ButtonBlue,
                                      Color.white, 22, OnDepo);
            UiBuild.Anchor((RectTransform)depo.transform,
                           new Vector2(0.655f, 0.880f), new Vector2(0.860f, 0.963f));
            PillFit.Wrap(depo.GetComponent<Image>());
            _depoLabel = AtolyeKit.Label(depo, 11, 22);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       _closeIcon != null ? _closeIcon : UiSkin.ButtonGrey,
                                       Color.white, 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>The bench card: level, XP, the ÜRET button, and the retooling stop when one runs.</summary>
        private void BuildBench()
        {
            RectTransform c = Art(_root, "Tezgah", _panel, new Vector2(0.035f, 0.030f), new Vector2(0.475f, 0.815f));

            _levelLabel = UiBuild.Label(Zone(c, "Seviye", new Vector2(0.07f, 0.880f), new Vector2(0.93f, 0.970f)),
                                        "Text", string.Empty, 40, TextAnchor.MiddleCenter);
            _levelLabel.color = Ink;

            _tierLabel = UiBuild.Label(Zone(c, "Kademe", new Vector2(0.07f, 0.820f), new Vector2(0.93f, 0.878f)),
                                       "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _tierLabel.color = InkSoft;

            RectTransform track = Bar(c, "XpCubuk", new Vector2(0.09f, 0.740f), new Vector2(0.91f, 0.800f), out _xpFill);
            _xpLabel = UiBuild.Label(track, "Yazi", string.Empty, 22, TextAnchor.MiddleCenter);
            _xpLabel.color = Paper;

            _craftBtn = UiBuild.Btn(c, "Uret", string.Empty,
                                    _btnGreen != null ? _btnGreen : UiSkin.ButtonGreen,
                                    Color.white, 30, OnCraft);
            UiBuild.Anchor((RectTransform)_craftBtn.transform, new Vector2(0.10f, 0.560f), new Vector2(0.90f, 0.690f));
            PillFit.Wrap(_craftBtn.GetComponent<Image>());
            _craftLabel = _craftBtn.GetComponentInChildren<Text>();
            // The price names its currency now — "3 CRAFT POINTS", not "3 PTS" — so it takes a line of
            // its own, kept inside the pill's round end caps.
            UiBuild.Anchor(_craftLabel.rectTransform, new Vector2(0.14f, 0.12f), new Vector2(0.86f, 0.88f));
            Fit(_craftLabel, 16, 30);
            _craftLabel.verticalOverflow = VerticalWrapMode.Truncate;

            _autoCraftAdBtn = UiBuild.Btn(c, "OtoUretReklam", string.Empty,
                                          _btnBlue != null ? _btnBlue : UiSkin.ButtonBlue,
                                          Color.white, 20, OnAutoCraftAd);
            UiBuild.Anchor((RectTransform)_autoCraftAdBtn.transform,
                           new Vector2(0.10f, 0.445f), new Vector2(0.90f, 0.515f));
            PillFit.Wrap(_autoCraftAdBtn.GetComponent<Image>());
            // The longest string on the screen ("WATCH AD · 15 MIN AUTO-CRAFT") in the shortest pill.
            _autoCraftAdLabel = AtolyeKit.Label(_autoCraftAdBtn, 10, 20);

            // The stop's own strip. It does NOT replace the button — crafting carries on while the
            // bench retools; only the level waits, which is exactly what the strip says. The kit's
            // deep blue capsule, not the flat navy rectangle it used to be: the strip sits on a white
            // card among capsules, and a bare rectangle was the one square corner on the screen.
            //
            // WIDER AND LOWER THAN THE RECTANGLE WAS. The capsule is 4:1 art, and in the old box —
            // barely wider than it was tall — the two end caps met and it drew as a blue egg. Full
            // card width and about half the height is what makes it read as a strip; the three lines
            // it carries still fit, kept inside the caps.
            _gateCard = Art(c, "Durak", _gatePill, new Vector2(0.04f, 0.318f), new Vector2(0.96f, 0.430f));
            var gateImage = _gateCard.GetComponent<Image>();
            gateImage.type = Image.Type.Sliced;
            gateImage.preserveAspect = false;
            if (_gatePill != null) PillFit.Wrap(gateImage);
            else gateImage.color = new Color(0.15f, 0.21f, 0.33f, 0.96f);
            _gateLabel = UiBuild.Label(Zone(_gateCard, "Baslik", new Vector2(0.14f, 0.60f), new Vector2(0.86f, 0.94f)),
                                       "Text", Loc.T("atolye.yenileniyor"), 26, TextAnchor.MiddleCenter);
            _gateClockLabel = UiBuild.Label(Zone(_gateCard, "Saat", new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.60f)),
                                            "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            _bankLabel = UiBuild.Label(Zone(_gateCard, "Not", new Vector2(0.14f, 0.06f), new Vector2(0.86f, 0.30f)),
                                       "Text", Loc.T("atolye.birikiyor"), 18, TextAnchor.MiddleCenter);
            _bankLabel.color = new Color(0.75f, 0.81f, 0.92f, 1f);

            _sourceLabel = UiBuild.Label(Zone(c, "Nereden", new Vector2(0.07f, 0.040f), new Vector2(0.93f, 0.300f)),
                                         "Text", Loc.T("atolye.nereden"), 20, TextAnchor.LowerCenter);
            _sourceLabel.color = InkFaint;
            Fit(_sourceLabel, 12, 20);
        }

        /// <summary>The odds table: one row per grade, straight off <see cref="Crafting.LevelOdds"/>.</summary>
        private void BuildOdds()
        {
            RectTransform c = Art(_root, "Oranlar", _panel, new Vector2(0.505f, 0.030f), new Vector2(0.965f, 0.815f));

            _oddsTitleLabel = UiBuild.Label(Zone(c, "Baslik", new Vector2(0.07f, 0.890f), new Vector2(0.93f, 0.970f)),
                                            "Text", Loc.T("atolye.oranlar"), 30, TextAnchor.MiddleCenter);
            _oddsTitleLabel.color = Ink;

            _captainLabel = UiBuild.Label(Zone(c, "ZanaatKaptani", new Vector2(0.19f, 0.795f), new Vector2(0.81f, 0.875f)),
                                          "Text", string.Empty, 19, TextAnchor.MiddleCenter);
            _captainLabel.color = Ink;
            Fit(_captainLabel, 10, 19);

            // FLATTER THAN THE ROW THEY SIT IN. The kit's buttons are all about 2:1, and the boxes
            // these used to have were taller than they were wide, which draws a capsule as a standing
            // oval. Short and wide, they come out as the stubby pills the rest of the screen is made
            // of instead of the two grey blocks they were.
            _captainPrevBtn = UiBuild.Btn(c, "OncekiKaptan", "‹", _btnPale != null ? _btnPale : UiSkin.ButtonGrey,
                                           Color.white, 24, OnPreviousCaptain);
            UiBuild.Anchor((RectTransform)_captainPrevBtn.transform,
                           new Vector2(0.040f, 0.812f), new Vector2(0.205f, 0.858f));
            PillFit.Wrap(_captainPrevBtn.GetComponent<Image>());
            _captainNextBtn = UiBuild.Btn(c, "SonrakiKaptan", "›", _btnPale != null ? _btnPale : UiSkin.ButtonGrey,
                                           Color.white, 24, OnNextCaptain);
            UiBuild.Anchor((RectTransform)_captainNextBtn.transform,
                           new Vector2(0.795f, 0.812f), new Vector2(0.960f, 0.858f));
            PillFit.Wrap(_captainNextBtn.GetComponent<Image>());

            _unlockLabel = UiBuild.Label(Zone(c, "SonrakiAcilis", new Vector2(0.07f, 0.715f), new Vector2(0.93f, 0.790f)),
                                         "Text", string.Empty, 17, TextAnchor.MiddleCenter);
            _unlockLabel.color = InkSoft;
            Fit(_unlockLabel, 10, 17);

            const float top = 0.705f, bottom = 0.040f;
            float rh = (top - bottom) / Captains.GradeCount;
            for (int g = 0; g < Captains.GradeCount; g++)
            {
                RectTransform row = Zone(c, "Sira" + g, new Vector2(0.06f, top - (g + 1) * rh + 0.012f),
                                                        new Vector2(0.94f, top - g * rh - 0.012f));
                _oddsStripe[g] = Stripe(row, new Vector2(0f, 0.10f), new Vector2(0.035f, 0.90f));
                _oddsStripe[g].color = GradeTint[g];
                _oddsName[g] = UiBuild.Label(Zone(row, "Ad", new Vector2(0.09f, 0f), new Vector2(0.62f, 1f)),
                                             "Text", string.Empty, 26, TextAnchor.MiddleLeft);
                Fit(_oddsName[g], 14, 26);
                _oddsValue[g] = UiBuild.Label(Zone(row, "Deger", new Vector2(0.40f, 0f), new Vector2(1f, 1f)),
                                              "Text", string.Empty, 26, TextAnchor.MiddleRight);
                Fit(_oddsValue[g], 13, 26);
            }
        }

        /// <summary>The decision card: the fresh craft against what the slot wears. Built last so it
        /// draws over both columns; only ever visible while a craft is pending.</summary>
        private void BuildDecideCard()
        {
            _decideCard = Art(_root, "Karar", _panel, new Vector2(0.130f, 0.140f), new Vector2(0.870f, 0.790f));
            var image = _decideCard.GetComponent<Image>();
            image.raycastTarget = true;
            var eat = _decideCard.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;

            _decideTitle = UiBuild.Label(Zone(_decideCard, "Baslik", new Vector2(0.06f, 0.880f), new Vector2(0.94f, 0.970f)),
                                         "Text", string.Empty, 34, TextAnchor.MiddleCenter);
            // UiBuild.Label writes white, and the kit's card is white paper — the title was invisible
            // on it until this line. The rows and the worn line below already carry their own ink.
            _decideTitle.color = Ink;
            Fit(_decideTitle, 16, 34);

            _decideScore = UiBuild.Label(Zone(_decideCard, "Guc", new Vector2(0.06f, 0.800f), new Vector2(0.94f, 0.875f)),
                                         "Text", string.Empty, 28, TextAnchor.MiddleCenter);

            _decideRows = UiBuild.Label(Zone(_decideCard, "Satirlar", new Vector2(0.10f, 0.360f), new Vector2(0.90f, 0.790f)),
                                        "Text", string.Empty, 24, TextAnchor.UpperLeft);
            _decideRows.color = Ink;

            _decideWorn = UiBuild.Label(Zone(_decideCard, "Mevcut", new Vector2(0.10f, 0.280f), new Vector2(0.90f, 0.355f)),
                                        "Text", string.Empty, 22, TextAnchor.MiddleLeft);
            _decideWorn.color = InkSoft;
            Fit(_decideWorn, 12, 22);

            // Three answers now, not two. KEEP is the one the bench never had: a Legendary charm
            // rolled before the charm slot is worth filling used to be a coin toss between wearing
            // the wrong thing and scrapping the right one.
            Button equip = UiBuild.Btn(_decideCard, "Giydir", string.Empty,
                                       _btnGreen != null ? _btnGreen : UiSkin.ButtonGreen,
                                       Color.white, 24, OnEquip);
            UiBuild.Anchor((RectTransform)equip.transform, new Vector2(0.05f, 0.070f), new Vector2(0.35f, 0.170f));
            PillFit.Wrap(equip.GetComponent<Image>());
            _equipLabel = AtolyeKit.Label(equip, 10, 22);

            _stowBtn = UiBuild.Btn(_decideCard, "Depoya", string.Empty,
                                   _btnBlue != null ? _btnBlue : UiSkin.ButtonBlue,
                                   Color.white, 24, OnStow);
            UiBuild.Anchor((RectTransform)_stowBtn.transform, new Vector2(0.35f, 0.070f), new Vector2(0.65f, 0.170f));
            PillFit.Wrap(_stowBtn.GetComponent<Image>());
            _stowLabel = AtolyeKit.Label(_stowBtn, 10, 22);

            Button scrap = UiBuild.Btn(_decideCard, "Sok", string.Empty,
                                       _btnPale != null ? _btnPale : UiSkin.ButtonYellow,
                                       Color.white, 24, OnSalvage);
            UiBuild.Anchor((RectTransform)scrap.transform, new Vector2(0.65f, 0.070f), new Vector2(0.95f, 0.170f));
            PillFit.Wrap(scrap.GetComponent<Image>());
            _salvageLabel = AtolyeKit.Label(scrap, 10, 22);
        }

        /// <summary>
        /// The depo screen, made here rather than authored in the scene.
        ///
        /// It cannot be a seventh opener in the HUD's bottom row — that row re-centres itself on
        /// every attach, so a seventh button starts pushing the ends of it off a narrow screen — and
        /// the workshop is where a shelf of half-decided gear belongs anyway. A scene that authors
        /// one properly is found and used instead; either way the sprites this screen was wired with
        /// are handed over, so the depo wears the workshop's art without a scene edit.
        /// </summary>
        private void BuildDepo()
        {
            _depo = FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (_depo == null) _depo = new GameObject("DepoPaneli").AddComponent<InventoryUI>();
            _depo.Adopt(_panel, _ribbon, _btnBlue, _closeIcon, _chip);
        }

        // ---------------------------------------------------------------- opener
        /// <summary>Order 4 in the HUD's bottom row, after the captains. The chip is the point
        /// balance — the thing that makes the button worth pressing.</summary>
        private void BuildOpener()
        {
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Include);
            if (hud == null) return;

            Sprite icon = AtolyeKit.Get("atolye_ikon") ?? Resources.Load<Sprite>(OpenerIconResource);
            Button open = hud.AttachBottomButton(4, "BtnAtolye",
                                                 icon != null ? icon : UiSkin.ButtonYellow, Show);
            if (open == null) return;

            _openerChip = hud.AttachCounterChip(open);
            if (_openerChip != null) _openerCount = _openerChip.GetComponentInChildren<TMP_Text>(true);
        }

        private void RefreshOpener()
        {
            if (_openerChip == null || _crafting == null) return;
            long points = _crafting.Points;
            bool show = points > 0L;
            if (_openerChip.activeSelf != show) _openerChip.SetActive(show);
            if (show && _openerCount != null)
            {
                string text = points > 99L ? "99+" : points.ToString();
                if (_openerCount.text != text) _openerCount.text = text;
            }
        }

        // --------------------------------------------------------------- actions
        private void OnCraft()
        {
            if (_crafting == null) return;
            _crafting.TryCraft(out _);   // refresh rides the Changed event; refusal changes nothing
        }

        private void OnAutoCraftAd()
        {
            if (_crafting == null || _crafting.AutoCraftActive || !AutoCraftAdReady) return;
            if (_freeRewards != null && _freeRewards.AdsRemoved) { StartAutoCraft(); return; }
            _ad?.ShowRewarded(StartAutoCraft);
        }

        private bool AutoCraftAdReady => (_freeRewards != null && _freeRewards.AdsRemoved)
                                          || (_ad != null && _ad.Available);

        private void StartAutoCraft()
        {
            _crafting?.StartRewardedAutoCraft(rewardedAutoCraftSeconds);
        }

        private void OnEquip()
        {
            if (_crafting == null) return;
            _crafting.EquipPending();
        }

        private void OnSalvage()
        {
            if (_crafting == null) return;
            _crafting.SalvagePending(out _);
        }

        /// <summary>Keep it instead. Refused by the service on a full shelf, and the card stays up
        /// with the item still on the bench — nothing to undo.</summary>
        private void OnStow()
        {
            if (_crafting == null) return;
            _crafting.StowPending();
        }

        private void OnPreviousCaptain() => CycleCaptain(-1);

        private void OnNextCaptain() => CycleCaptain(1);

        private void CycleCaptain(int direction)
        {
            if (_crafting == null || _captains == null || _captains.OwnedCount <= 0) return;
            int current = _crafting.AssignedCaptain;
            int candidate = current;
            for (int i = 0; i <= Captains.Count; i++)
            {
                candidate += direction;
                if (candidate >= Captains.Count) candidate = -1;
                if (candidate < -1) candidate = Captains.Count - 1;
                if (candidate < 0 || _captains.Owned(candidate))
                {
                    _crafting.TryAssignCaptain(candidate);
                    return;
                }
            }
        }

        private void OnDepo()
        {
            if (_depo != null) _depo.Show();
        }

        // --------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_crafting == null) return;

            _pointsLabel.text = CurrencyText.Amount(CurrencyId.CraftPoints, _crafting.Points);

            if (_depoLabel != null)
                _depoLabel.text = Loc.T("depo.baslik") + "  "
                                + (_sea != null ? _sea.StashCount : 0) + "/"
                                + (_sea != null ? _sea.StashCapacity : 0);

            int level = _crafting.Level;
            _levelLabel.text = string.Format(Loc.T("atolye.seviye"), level);
            _tierLabel.text = string.Format(Loc.T("atolye.kademe"), _crafting.CurrentTier + 1);

            RefreshXpBar(level);
            RefreshGate();
            RefreshCaptainAssignment();
            RefreshNextUnlock();

            _craftLabel.text = Loc.T("atolye.uret") + "\n"
                             + CurrencyText.Amount(CurrencyId.CraftPoints, _crafting.Tuning.CraftCost);
            bool canCraft = !_crafting.HasPending && _crafting.Points >= _crafting.Tuning.CraftCost;
            _craftBtn.interactable = canCraft;
            Dress(_craftBtn, _btnGreen, canCraft);

            if (_autoCraftAdBtn != null)
            {
                bool active = _crafting.AutoCraftActive;
                _autoCraftAdBtn.interactable = !active && AutoCraftAdReady;
                Dress(_autoCraftAdBtn, _btnBlue, _autoCraftAdBtn.interactable);
                _autoCraftAdLabel.text = active
                    ? "OTO ÜRETİM  " + UiBuild.Clock(_crafting.AutoCraftSecondsLeft)
                    : "REKLAM İZLE · " + Mathf.CeilToInt(rewardedAutoCraftSeconds / 60f)
                      + " DK OTO ÜRETİM";
            }

            for (int g = 0; g < Captains.GradeCount; g++)
            {
                _oddsName[g].text = Loc.T("kaptan.derece." + g);
                double baseOdds = _crafting.BaseOddsOf(g);
                double odds = _crafting.OddsOf(g);
                if (odds > 0d)
                {
                    _oddsName[g].color = Ink;
                    _oddsStripe[g].color = GradeTint[g];
                    _oddsValue[g].text = PctOdds(baseOdds) + " → " + PctOdds(odds);
                    _oddsValue[g].color = GradeTint[g];
                }
                else
                {
                    int unlock = Crafting.UnlockLevelOf(g);
                    _oddsName[g].color = InkFaint;
                    _oddsStripe[g].color = new Color(GradeTint[g].r, GradeTint[g].g, GradeTint[g].b, 0.25f);
                    _oddsValue[g].text = unlock > 0 ? string.Format(Loc.T("atolye.acilir"), unlock) : "—";
                    _oddsValue[g].color = InkFaint;
                }
            }

            RefreshDecideCard();
        }

        private void RefreshCaptainAssignment()
        {
            if (_captainLabel == null || _crafting == null) return;
            int captain = _crafting.AssignedCaptain;
            if (captain < 0)
                _captainLabel.text = Loc.T("atolye.kaptan") + "\n" + Loc.T("atolye.kaptan_yok");
            else
                _captainLabel.text = Loc.T("atolye.kaptan") + "  "
                                   + Loc.T("kaptan.ad." + Captains.IdOf(captain))
                                   + "  ·  " + string.Format(Loc.T("atolye.seviye"),
                                                               _crafting.AssignedCaptainLevel);

            bool canSelect = _captains != null && _captains.OwnedCount > 0;
            if (_captainPrevBtn != null) _captainPrevBtn.interactable = canSelect;
            if (_captainNextBtn != null) _captainNextBtn.interactable = canSelect;
        }

        private void RefreshNextUnlock()
        {
            if (_unlockLabel == null || _crafting == null) return;
            int grade = _crafting.NextUnlockGrade;
            _unlockLabel.text = grade >= 0
                ? string.Format(Loc.T("atolye.sonraki_acilis"), Loc.T("kaptan.derece." + grade),
                                _crafting.NextUnlockLevel)
                : Loc.T("atolye.tum_acik");
        }

        private void RefreshXpBar(int level)
        {
            long xp = _crafting.Xp;
            long need = Crafting.XpToNext(level);
            long into = Crafting.XpIntoLevel(xp, level);
            if (level >= Crafting.MaxLevel || need <= 0L)
            {
                _xpFill.anchorMax = new Vector2(1f, 1f);
                _xpLabel.text = Loc.T("atolye.maks");
                return;
            }
            long shown = into < need ? into : need;
            _xpFill.anchorMax = new Vector2(need > 0L ? (float)shown / need : 0f, 1f);
            string text = Loc.T("atolye.xp") + "  " + shown + "/" + need;
            if (into > need) text += "  (+" + (into - need) + ")";   // banked behind a stop
            _xpLabel.text = text;
        }

        private void RefreshGate()
        {
            bool gated = _crafting.IsGated;
            if (_gateCard.gameObject.activeSelf != gated) _gateCard.gameObject.SetActive(gated);
            _writtenClock = null;
            if (gated) RefreshGateClock();
        }

        /// <summary>The only per-second write, and only while the strip is up.</summary>
        private void RefreshGateClock()
        {
            if (_gateCard == null || !_gateCard.gameObject.activeSelf || _crafting == null) return;
            double left = _crafting.GateSecondsLeft;
            int hours = (int)(left / 3600d);
            string text = hours > 0
                ? hours + ":" + (((int)left % 3600) / 60).ToString("00") + ":" + ((int)left % 60).ToString("00")
                : UiBuild.Clock((float)left);
            if (text == _writtenClock) return;
            _writtenClock = text;
            _gateClockLabel.text = text;
        }

        private void RefreshDecideCard()
        {
            bool pending = _crafting.HasPending;
            if (_decideCard.gameObject.activeSelf != pending) _decideCard.gameObject.SetActive(pending);
            if (!pending) return;

            SeaCombat.Item item = _crafting.PendingItem();
            SeaCombat.Item cur = _sea != null ? _sea.GearItem(item.Slot) : new SeaCombat.Item { Grade = -1 };
            SeaCombat.Tuning t = _sea != null ? _sea.Combat : SeaCombat.Tuning.Default;
            Color tint = GradeTint[Mathf.Clamp(item.Grade, 0, GradeTint.Length - 1)];

            _decideTitle.text = Loc.T("kaptan.derece." + item.Grade) + "  ·  " + Loc.T("deniz.slot." + item.Slot);
            _decideTitle.color = tint;

            int score = SeaCombat.ItemScore(item, t);
            int delta = score - (_sea != null ? _sea.GearScore(item.Slot) : 0);
            _decideScore.text = Loc.T("deniz.guc") + "  " + score + "   (" + (delta >= 0 ? "+" : "") + delta + ")";
            _decideScore.color = delta >= 0 ? Good : Bad;

            _decideRows.text = ItemRows(item, cur, cur.Grade >= 0);

            _decideWorn.text = cur.Grade < 0
                ? Loc.T("deniz.mevcut") + ":  " + Loc.T("deniz.bos")
                : Loc.T("deniz.mevcut") + ":  " + Loc.T("kaptan.derece." + cur.Grade)
                  + "  ·  " + Loc.T("deniz.guc") + " " + (_sea != null ? _sea.GearScore(item.Slot) : 0);

            _equipLabel.text = Loc.T("deniz.giydir");
            _stowLabel.text = _sea != null && !_sea.StashHasRoom
                ? Loc.T("depo.dolu") : Loc.T("depo.depoya");
            _stowBtn.interactable = _sea != null && _sea.StashHasRoom;
            _salvageLabel.text = string.Format(Loc.T("atolye.sok"),
                                               SeaCombat.ScrapFor(item.Grade),
                                               Crafting.SalvageXpFor(item.Grade));
        }

        /// <summary>An item's five rows, tinted by the compare — the sea's loot card, in this ink.</summary>
        private string ItemRows(in SeaCombat.Item item, in SeaCombat.Item against, bool compare)
        {
            string up = "<color=#1E9E4A>", down = "<color=#C43B32>", end = "</color>";
            string hull = Loc.T("deniz.cesaret") + "  +" + N(item.Hull);
            string shot = Loc.T("deniz.slot.0") + "  +" + N(item.Shot);
            string def = Loc.T("deniz.st.savunma") + "  +" + D(item.Def);
            string spd = Loc.T("deniz.st.surat") + "  +" + D(item.Spd);
            string sec = item.Sec == SeaCombat.SecNone
                ? "—" : Loc.T(SecKey(item.Sec)) + "  +" + Pct(item.SecAmt);
            if (compare)
            {
                hull = (item.Hull >= against.Hull ? up : down) + hull + end;
                shot = (item.Shot >= against.Shot ? up : down) + shot + end;
                def = (item.Def >= against.Def ? up : down) + def + end;
                spd = (item.Spd >= against.Spd ? up : down) + spd + end;
                double curSec = against.Grade < 0 ? 0d : against.SecAmt;
                if (item.Sec != SeaCombat.SecNone || curSec > 0d)
                    sec = ((item.Sec == SeaCombat.SecNone ? 0d : item.SecAmt) >= curSec ? up : down) + sec + end;
            }
            return hull + "\n" + shot + "\n" + def + "\n" + spd + "\n" + sec;
        }

        // ---------------------------------------------------------------- pieces
        private static RectTransform Zone(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
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
            img.preserveAspect = img.type == Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private RectTransform Chip(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _chip != null ? _chip : UiSkin.Pill;
            img.type = Image.Type.Sliced;
            img.color = img.sprite != null ? Color.white : new Color(0.16f, 0.20f, 0.28f, 0.95f);
            img.raycastTarget = false;
            RectTransform rt = UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            // The kit chip is a capsule; keep its caps round however short the box, the way the
            // chapter and goal screens already do with the same sprite.
            if (_chip != null) PillFit.Wrap(img);
            return rt;
        }

        private static Image Stripe(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject("Cizgi", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        private RectTransform Bar(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
                                  out RectTransform fill)
        {
            RectTransform track;
            if (_barTrack != null)
            {
                track = Art(parent, name, _barTrack, aMin, aMax);
                var go = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(track, false);
                var img = go.GetComponent<Image>();
                img.sprite = _barFill;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
                fill = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, new Vector2(0f, 1f));
            }
            else
            {
                track = UiBuild.Bar(parent, name, new Color(0.20f, 0.25f, 0.34f, 1f),
                                    new Color(0.30f, 0.72f, 0.40f, 1f), aMin, aMax, out fill);
            }
            return track;
        }

        /// <summary>The kit's live/dead capsule pair — see <see cref="AtolyeKit.Face"/>.</summary>
        private void Dress(Button button, Sprite live, bool canPress)
            => AtolyeKit.Face(button, live, _btnPale, canPress);

        /// <summary>Shrink-to-fit so a long translation stays on its row.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static string SecKey(int sec)
        {
            switch (sec)
            {
                case SeaCombat.SecCrit:    return "deniz.st.kritik";
                case SeaCombat.SecDodge:   return "deniz.st.manevra";
                case SeaCombat.SecStun:    return "deniz.st.sersem";
                case SeaCombat.SecMend:    return "deniz.st.onarim";
                case SeaCombat.SecBurn:    return "deniz.st.yangin";
                case SeaCombat.SecPlunder: return "deniz.st.yagma";
                case SeaCombat.SecSalvo:   return "deniz.st.salvo";
                case SeaCombat.SecSteal:   return "deniz.st.cancalma";
                case SeaCombat.SecPoison:  return "deniz.st.zehir";
                default:                   return "deniz.bos";
            }
        }

        /// <summary>82% for the big shares, 0.5% never rounded to nothing for the rare ones.</summary>
        private static string PctOdds(double v)
        {
            double pct = v * 100d;
            if (pct >= 10d) return Mathf.RoundToInt((float)pct) + "%";
            double one = System.Math.Round(pct * 10d) / 10d;
            return one.ToString("0.#") + "%";
        }

        private static string Pct(double v) => Mathf.RoundToInt((float)(v * 100d)) + "%";

        private static string N(double v)
        {
            if (v < 999.5d) return Mathf.RoundToInt((float)v).ToString();
            double k = v / 1000d;
            return k < 99.95d ? k.ToString("0.0") + "k" : Mathf.RoundToInt((float)k) + "k";
        }

        private static string D(double v) => (System.Math.Round(v * 10d) / 10d).ToString("0.#");
    }
}
