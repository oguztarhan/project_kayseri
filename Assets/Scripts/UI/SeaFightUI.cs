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
    /// The adventure screen, in the reference game's own portrait shape: the 2D sea STAGE as a band
    /// across the top, and a persistent SHEET PANEL filling the bottom half — the POWER headline, the
    /// whole stat block, the four worn items with their grade stars, who captains, the energy pill, and
    /// the SEARCH and AUTO buttons. The sheet is on from the moment the player is aboard; fights
    /// come and go on the stage above it. The thumb reaches the sheet; the stage is only watched.
    ///
    /// THE FLOW MIRRORS THE REFERENCE: search → the find slides in → a DETAILS CARD (name,
    /// signature chip, TEHLİKELİ when it outguns us, its sheet, the rewards) with SAVAŞ!/VAZGEÇ →
    /// the exchange, every roll narrated as floating text (KRİTİK!, ISKA, burn ticks, mends,
    /// plunder) → a win parks on a COMPARE CARD: current item beside the drop, row by row, the
    /// score delta on top, wear it or scrap it.
    ///
    /// THE THEATER IS NOT THE TRUTH — but they are synchronised by construction: the controller
    /// applies damage on the ball's landing frame and narrates through its event ring; this file
    /// only draws what the ring says happened.
    ///
    /// The chrome — backdrop, plates, panels, slots, bars, buttons, icons — is the design kit
    /// (<see cref="SeaKit"/>); the theater's older pieces (balls, bursts, the non-raider threats)
    /// still come from Resources/UI/Sea (Tools/ui/deniz_savas_seti.py). Every slot falls back to the
    /// flat quad. Balls, flashes and floating texts are pooled — fixed arrays, zero allocation
    /// after Build.
    /// </summary>
    public sealed class SeaFightUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 102;   // above SeaHudUI's 100

        [Tooltip("Boyalı deniz arka planının kanvası. SeaHudUI'nin (100) ALTINDA durmalı: resim " +
                 "ekranın tepesine kadar, rota levhasının arkasına uzanıyor.")]
        [SerializeField] private int backdropSortingOrder = 99;

        [Header("Enerji reklamı — ekstra arama hakkı")]
        [Tooltip("Bir reklamın doldurduğu enerji, günlük hak ve iki izleme arasındaki bekleme. " +
                 "Havuz doluyken düğme kapalıdır — dolu bir havuza akıtılan hak yanar.")]
        [SerializeField, Min(1)] private int energyAdReward = 10;
        [SerializeField, Min(1)] private int energyAdChargesPerDay = 3;
        [SerializeField, Min(0f)] private float energyAdCooldownSeconds = 300f;

        /// <summary>The rewarded-ad slot this button spends. It shares FreeRewardService's day roll
        /// and cooldown book with every other slot in the game, so the sea has no second set of
        /// rules and cannot be farmed past its daily cap.</summary>
        private const string EnergyAdId = "deniz_enerji";

        private static readonly Color Chrome = new Color(0.06f, 0.10f, 0.16f, 0.88f);
        private static readonly Color SkyTint = new Color(0.55f, 0.73f, 0.86f, 1f);
        private static readonly Color HullFillTint = new Color(0.92f, 0.30f, 0.26f, 0.95f);
        private static readonly Color NerveFillTint = new Color(0.36f, 0.74f, 0.99f, 0.95f);

        // The kit's art is pre-coloured, so these MULTIPLY it: white is the art as drawn.
        private static readonly Color Deck = new Color(0.04f, 0.11f, 0.20f, 1f);         // under the sheet
        private static readonly Color StatTint = new Color(0.60f, 0.68f, 0.80f, 1f);     // core stat cells
        private static readonly Color InsetTint = new Color(0.36f, 0.44f, 0.56f, 1f);    // dark wells on a card
        private static readonly Color InsetEmpty = new Color(0.28f, 0.31f, 0.36f, 1f);
        private static readonly Color GridTint = new Color(0.30f, 0.38f, 0.50f, 0.55f); // behind the procs
        private static readonly Color SlotEmpty = new Color(0.62f, 0.66f, 0.72f, 1f);
        private static readonly Color SlotLocked = new Color(0.40f, 0.44f, 0.50f, 1f);
        private static readonly Color RouteOpen = new Color(0.70f, 0.76f, 0.84f, 1f);
        private static readonly Color RouteLocked = new Color(0.45f, 0.50f, 0.58f, 1f);
        private static readonly Color AutoOn = new Color(0.55f, 0.95f, 0.55f, 1f);
        private static readonly Color Outline = new Color(0.02f, 0.06f, 0.14f, 0.94f);

        /// <summary>How far a grade or rarity tint leans the teal slot frame. Full strength drowns the
        /// art in one colour; this keeps the frame the kit's and still reads the grade at a glance.</summary>
        private const float FrameTintWeight = 0.5f;

        /// <summary>The HP frame's trough, as anchors on can_cerceve — printed by the kit script.</summary>
        private static readonly Vector2 TroughMin = new Vector2(0.153f, 0.261f);
        private static readonly Vector2 TroughMax = new Vector2(0.966f, 0.712f);

        /// <summary>How much of a 06 button's width its compass medallion takes, by where it is used.
        /// A slice border is in canvas units, so this depends on the box's own aspect.</summary>
        private const float CompassSearch = 0.17f, CompassCard = 0.20f, CompassWide = 0.25f;

        /// <summary>The route numeral's size: a whole tab when open, beside the padlock when locked.</summary>
        private const float RouteMarkOpen = 30f, RouteMarkLocked = 24f;
        private static readonly Color EnergyTint = new Color(0.99f, 0.82f, 0.28f, 1f);
        private static readonly Color Win = new Color(0.55f, 0.95f, 0.55f, 1f);
        private static readonly Color Loss = new Color(0.95f, 0.75f, 0.45f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Faded = new Color(0.72f, 0.76f, 0.84f, 1f);
        private static readonly Color Danger = new Color(0.98f, 0.36f, 0.32f, 1f);
        private static readonly Color Easy = new Color(0.55f, 0.92f, 0.55f, 1f);
        private static readonly Color CritTint = new Color(1f, 0.85f, 0.25f, 1f);
        private static readonly Color BurnTint = new Color(1f, 0.55f, 0.20f, 1f);
        private static readonly Color MendTint = new Color(0.45f, 0.95f, 0.55f, 1f);
        private static readonly Color StunTint = new Color(0.80f, 0.55f, 1f, 1f);
        private static readonly Color PlunderTint = new Color(0.40f, 0.95f, 0.90f, 1f);
        private static readonly Color PoisonTint = new Color(0.55f, 0.88f, 0.25f, 1f);
        private static readonly Color StealTint = new Color(1f, 0.50f, 0.65f, 1f);

        /// <summary>Grade tints — the same ladder the captain screen wears.</summary>
        private static readonly Color[] GradeTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),
            new Color(0.26f, 0.60f, 0.92f, 1f),
            new Color(0.62f, 0.38f, 0.92f, 1f),
            new Color(0.96f, 0.66f, 0.18f, 1f),
            new Color(0.94f, 0.28f, 0.42f, 1f),
        };

        private const int BallPool = 8;
        private const int FlashPool = 6;
        private const int FloatPool = 14;
        private const int CoreStatCount = 4;
        private const int StatCount = 13;

        /// <summary>The route strip's numerals — a name would not survive the pill's width, and the
        /// chosen route's full name is printed under the strip anyway.</summary>
        private static readonly string[] TierMark = { "I", "II", "III", "IV" };

        private EncounterController _fights;
        private ExpeditionService _sea;
        private CaptainService _captains;
        private FreeRewardService _free;
        private IAdService _ad;
        private SaveService _save;
        private SaveData _data;
        private PetService _pets;
        private PetConfig _petConfig;

        private CanvasGroup _rootGroup;
        private RectTransform _root, _stage, _panel;
        private GameObject _scrim, _bannerBack;
        private Material _outlined;   // one shared copy, so every outlined stage text still batches

        private RectTransform _shipRoot, _threatRoot;
        private CanvasGroup _threatGroup;
        private Image _threatImage;

        private RectTransform _hullTrack, _hullFill, _nerveTrack, _nerveFill;
        private float _barAspect = 6.6f;
        private TMP_Text _threatName, _banner;

        // The sheet panel.
        private TMP_Text _powerLabel, _captainLabel, _energyLabel;
        private Image _captainPortrait;
        private readonly TMP_Text[] _statValue = new TMP_Text[StatCount];
        private readonly Image[] _gearFrame = new Image[SeaCombat.SlotCount];
        private readonly Image[] _gearIcon = new Image[SeaCombat.SlotCount];
        private readonly Sprite[] _slotIcon = new Sprite[SeaCombat.SlotCount];
        private readonly Image[][] _gearStars = new Image[SeaCombat.SlotCount][];

        // Three pet-equip slots sharing the captain's band — see BuildPetSlots.
        private readonly Image[] _petFrame = new Image[Pets.SlotCount];
        private readonly Image[] _petPortrait = new Image[Pets.SlotCount];
        private readonly Image[][] _petStars = new Image[Pets.SlotCount][];
        private readonly TMP_Text[] _petBadge = new TMP_Text[Pets.SlotCount];
        private readonly TMP_Text[] _petBonus = new TMP_Text[Pets.SlotCount];
        private readonly Button[] _petButton = new Button[Pets.SlotCount];

        private Button _search, _autoBtn;
        private TMP_Text _searchLabel, _autoLabel;
        private Image _autoImage;

        // The route strip and what it promises: which waters the fights are priced for, what a
        // locked route still wants, the threat band out there and the drop table it rolls.
        private readonly Button[] _routeBtn = new Button[Voyages.TierCount];
        private readonly Image[] _routeLeft = new Image[Voyages.TierCount];
        private readonly Image[] _routeRight = new Image[Voyages.TierCount];
        private readonly Image[] _routeLock = new Image[Voyages.TierCount];
        private readonly TMP_Text[] _routeMark = new TMP_Text[Voyages.TierCount];
        private readonly TMP_Text[] _routeSub = new TMP_Text[Voyages.TierCount];
        private TMP_Text _threatLine, _lootLine;
        private readonly double[] _odds = new double[SeaCombat.GradeMult.Length];
        private readonly string[] _gradeHex = new string[SeaCombat.GradeMult.Length];
        private readonly System.Text.StringBuilder _lootText = new System.Text.StringBuilder(128);
        private int _routeSeenTier = -1, _routeSeenMax = -1;

        // The rewarded-ad top-up beside the energy pill.
        private Button _energyAd;
        private TMP_Text _energyAdLabel;

        // The details card (Found).
        private RectTransform _foundCard;
        private TMP_Text _foundTitle, _foundTag, _foundDanger, _foundPower, _foundStats, _foundReward;
        private Image _foundTagPill, _foundDangerIcon;

        // The loot compare card.
        private RectTransform _lootCard;
        private TMP_Text _lootTitle, _lootDelta;
        private Image _curFrame, _newFrame;
        private TMP_Text _curHead, _curGrade, _curRows, _newHead, _newGrade, _newRows;
        private TMP_Text _scrapLabel;

        // The worn-gear popup.
        private RectTransform _gearCard;
        private Image _gearCardIcon;
        private TMP_Text _gearTitle, _gearRows, _gearScrapLabel;
        private Button _gearScrap;
        private int _gearShown = -1;

        // Pools — parallel arrays, the per-frame path.
        private readonly RectTransform[] _ball = new RectTransform[BallPool];
        private readonly float[] _ballT = new float[BallPool];
        private readonly float[] _ballDur = new float[BallPool];
        private readonly Vector2[] _ballFrom = new Vector2[BallPool];
        private readonly Vector2[] _ballTo = new Vector2[BallPool];
        private readonly float[] _ballArc = new float[BallPool];
        private readonly RectTransform[] _flash = new RectTransform[FlashPool];
        private readonly CanvasGroup[] _flashGroup = new CanvasGroup[FlashPool];
        private readonly float[] _flashT = new float[FlashPool];
        private readonly float[] _flashScale = new float[FlashPool];
        private readonly TMP_Text[] _float = new TMP_Text[FloatPool];
        private readonly float[] _floatT = new float[FloatPool];
        private readonly Vector2[] _floatFrom = new Vector2[FloatPool];

        private int _seenStamp, _seenEvent, _seenBall, _seenKind = -1;
        private EncounterController.Phase _seenPhase = EncounterController.Phase.Idle;
        private float _toast, _shipWobble, _threatWobble, _energyTick, _sheetTick;
        private double _sheetPowerSeen = -1d;
        private string _lastEnergy, _lastSearch, _lastPower, _lastCaptain, _lastThreat, _lastLoot,
                       _lastEnergyAd;
        private bool _lastAuto;

        private static Sprite S(string name) => Resources.Load<Sprite>("UI/Sea/" + name);

        /// <summary>The theater's threat art by kind. The raider is the kit's pirate ship
        /// (<see cref="ThreatArt"/>), so it has no entry here.</summary>
        private static readonly string[] KindSprite = { null, "canavar", "enkaz", "alev", "hayalet" };

        public void Build(EncounterController fights)
        {
            _fights = fights;
            _sea = ServiceLocator.Get<ExpeditionService>();
            _captains = ServiceLocator.Get<CaptainService>();
            // The energy top-up's four: the daily book, the ad, and the pair a paid claim is
            // written through. Every one of them may be absent — a scene opened without a
            // bootstrap still builds, the button simply never lights.
            _free = ServiceLocator.Get<FreeRewardService>();
            _ad = ServiceLocator.Get<IAdService>();
            _save = ServiceLocator.Get<SaveService>();
            _data = ServiceLocator.Get<SaveData>();
            _pets = ServiceLocator.Get<PetService>();
            GameBootstrap bootstrap = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            _petConfig = bootstrap != null ? bootstrap.PetConfig : null;
            if (_pets != null) _pets.Changed += OnPetsChanged;
            for (int g = 0; g < _gradeHex.Length; g++)
                _gradeHex[g] = ColorUtility.ToHtmlStringRGB(GradeTint[g]);
            RectTransform canvas = UiBuild.Canvas(transform, "CarpismaKanvas", sortingOrder);

            // One group over everything: the whole adventure fades in when the player is aboard.
            var rootGo = new GameObject("Kart", typeof(RectTransform));
            rootGo.transform.SetParent(canvas, false);
            _root = UiBuild.Anchor((RectTransform)rootGo.transform, Vector2.zero, Vector2.one);
            _rootGroup = rootGo.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 0f;
            _rootGroup.blocksRaycasts = false;

            BuildBackdrop();

            // A full-width band between the sheet's top edge (0.545) and SeaHudUI's strip (0.885). It
            // draws nothing itself — the painted sea behind it is the backdrop.
            var stageGo = new GameObject("Sahne", typeof(RectTransform));
            stageGo.transform.SetParent(_root, false);
            _stage = UiBuild.Anchor((RectTransform)stageGo.transform, new Vector2(0f, 0.545f), new Vector2(1f, 0.885f));

            _shipRoot = Vessel("Gemi", SeaKit.Get("oyuncu_gemisi"), false);
            _threatRoot = Vessel("Tehdit", null, true);
            _threatGroup = _threatRoot.gameObject.AddComponent<CanvasGroup>();
            _threatGroup.alpha = 0f;
            _threatImage = _threatRoot.GetChild(0).GetComponent<Image>();
            BuildBars();
            BuildPools();

            // The result toast sits on a dark plate: the painted sky behind it is too light for the
            // win green to read on its own.
            Image back = SeaKit.Sliced(_stage, "SonucLevhasi", "stat", new Vector2(0.08f, 0.70f),
                                       new Vector2(0.92f, 0.84f), true);
            back.color = Outline;
            _bannerBack = back.gameObject;
            _banner = Line((RectTransform)back.transform, "Sonuc", 44f, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
            _banner.fontStyle = FontStyles.Bold;
            _bannerBack.SetActive(false);

            BuildPanel();

            // Behind whichever card is open: dims the whole screen so the decision reads first. It
            // catches no taps — the cards eat their own, exactly as before it existed — so the HUD's
            // way ashore still answers under it: leaving is always allowed (SeaSceneBoot).
            RectTransform scrim = UiBuild.Flat(_root, "Karartma", new Color(0f, 0f, 0f, 0.5f),
                                               Vector2.zero, Vector2.one);
            scrim.GetComponent<Image>().raycastTarget = false;
            _scrim = scrim.gameObject;
            _scrim.SetActive(false);

            BuildFoundCard();
            BuildLootCard();
            BuildGearCard();
            SetChrome(EncounterController.Phase.Idle);
            RefreshPetSlots();
        }

        private void OnDestroy()
        {
            if (_pets != null) _pets.Changed -= OnPetsChanged;
            if (_outlined != null) Destroy(_outlined);
        }

        /// <summary>The strip and the sheet together: the half-second probe only re-inks when POWER
        /// moves by a quarter, and a Common pet can move it by less than that, which would leave the
        /// stat grid disagreeing with the fight it is about to start.</summary>
        private void OnPetsChanged()
        {
            RefreshPetSlots();
            RefreshSheet();
        }

        // ------------------------------------------------------------------ build
        /// <summary>
        /// The painted sea, on its own canvas one step UNDER SeaHudUI's: the picture runs from the
        /// sheet's top edge to the top of the screen, behind the route plate, so there is no seam where
        /// the stage used to meet the 3D camera's flat sky. The fight canvas itself sits above the HUD,
        /// which is why this cannot simply be a child image of the stage. The root group still fades it.
        ///
        /// ENVELOPED, NOT STRETCHED: it covers its region at the art's own aspect and lets the sides fall
        /// off-screen on a tall phone. The region starts a little below the sheet's edge (the sheet is
        /// opaque and hides the overlap) so the horizon lands just above the ships' waterline.
        /// </summary>
        private void BuildBackdrop()
        {
            var layer = new GameObject("ArkaPlan", typeof(RectTransform), typeof(Canvas));
            layer.transform.SetParent(_root, false);
            var canvas = layer.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = backdropSortingOrder;
            RectTransform region = UiBuild.Anchor((RectTransform)layer.transform, new Vector2(0f, 0.50f), Vector2.one);

            var go = new GameObject("Resim", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            go.transform.SetParent(region, false);
            var img = go.GetComponent<Image>();
            Sprite art = SeaKit.Backdrop;
            img.sprite = art != null ? art : UiSkin.Flat;
            img.color = art != null ? Color.white : SkyTint;
            img.raycastTarget = false;
            var fit = go.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = art != null ? art.rect.width / art.rect.height : 1.5f;
        }

        private RectTransform Vessel(string name, Sprite sprite, bool mirrored)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(_stage, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.2f);

            var go = new GameObject("Resim", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root.transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Flat;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var irt = (RectTransform)go.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.2f);
            if (mirrored) irt.localScale = new Vector3(-1f, 1f, 1f);
            return rt;
        }

        private void BuildBars()
        {
            _hullTrack = BarTrack("TehditCani", out _hullFill, HullFillTint);
            _threatName = Line(_stage, "TehditAdi", 30f, Vector2.zero, Vector2.one);
            _threatName.rectTransform.anchorMin = _threatName.rectTransform.anchorMax = Vector2.zero;
            _threatName.rectTransform.sizeDelta = new Vector2(560f, 60f);
            Outlined(_threatName);

            _nerveTrack = BarTrack("Cesaret", out _nerveFill, NerveFillTint);
        }

        /// <summary>
        /// The kit's heart bar: the frame with its trough emptied, and the red keyed out of it as a
        /// separate fill that runs inside the trough. Sized by the frame's own aspect in DriveBars, so the
        /// heart never squashes. Without the kit it is the old dark track and a flat tinted fill.
        /// </summary>
        private RectTransform BarTrack(string name, out RectTransform fill, Color flatTint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_stage, false);
            var frame = go.GetComponent<Image>();
            Sprite art = SeaKit.Get("can_cerceve");
            frame.sprite = art != null ? art : UiSkin.Flat;
            frame.color = art != null ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            frame.raycastTarget = false;
            if (art != null) _barAspect = art.rect.width / art.rect.height;
            var track = (RectTransform)go.transform;
            track.anchorMin = track.anchorMax = Vector2.zero;

            var trough = new GameObject("Oluk", typeof(RectTransform));
            trough.transform.SetParent(track, false);
            RectTransform troughRt = art != null
                ? UiBuild.Anchor((RectTransform)trough.transform, TroughMin, TroughMax)
                : UiBuild.Anchor((RectTransform)trough.transform, new Vector2(0.02f, 0.1f), new Vector2(0.98f, 0.9f));

            Image bar = SeaKit.Sliced(troughRt, "Dolgu", "can_dolgu", Vector2.zero, Vector2.one, true);
            if (bar.sprite == UiSkin.Flat) bar.color = flatTint;
            fill = bar.rectTransform;
            return track;
        }

        /// <summary>
        /// Gives a stage text the navy outline the kit's stickers wear — the painted sky is light, and
        /// a white or pale number on it disappears. One material copy is shared by every text that
        /// asks, so they still batch as one; it is destroyed with the screen.
        /// </summary>
        private void Outlined(TMP_Text text)
        {
            if (text.fontSharedMaterial == null) return;
            if (_outlined == null)
            {
                ShaderUtilities.GetShaderPropertyIDs();
                _outlined = new Material(text.fontSharedMaterial) { name = "DenizYazi (Kontur)" };
                _outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
                _outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.24f);
                _outlined.SetColor(ShaderUtilities.ID_OutlineColor, Outline);
            }
            text.fontSharedMaterial = _outlined;
        }

        private void BuildPools()
        {
            Sprite ball = S("gulle"), burst = S("patlama");
            for (int i = 0; i < BallPool; i++)
            {
                _ball[i] = PoolItem("Gulle" + i, ball);
                _ballT[i] = -1f;
            }
            for (int i = 0; i < FlashPool; i++)
            {
                _flash[i] = PoolItem("Patlama" + i, burst);
                _flashGroup[i] = _flash[i].gameObject.AddComponent<CanvasGroup>();
                _flashT[i] = -1f;
            }
            for (int i = 0; i < FloatPool; i++)
            {
                TMP_Text txt = Line(_stage, "Yazi" + i, 34f, Vector2.zero, Vector2.zero);
                txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = Vector2.zero;
                txt.rectTransform.sizeDelta = new Vector2(460f, 80f);
                txt.enableAutoSizing = false;
                txt.fontStyle = FontStyles.Bold;
                Outlined(txt);
                txt.gameObject.SetActive(false);
                _float[i] = txt;
                _floatT[i] = -1f;
            }
        }

        private RectTransform PoolItem(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_stage, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Flat;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            go.SetActive(false);
            return rt;
        }

        // ------------------------------------------------------------- the sheet
        /// <summary>
        /// The bottom sheet: POWER, the four core stats big, the nine procs under them, the worn
        /// items, the captain, and the two buttons that drive the whole loop. Its inner fractions are
        /// unchanged from the landscape build — the sheet kept its height and only grew wider.
        /// </summary>
        private void BuildPanel()
        {
            // Full-bleed deck under the sheet: the sheet's rounded frame no longer leaves the 3D sea
            // showing at its corners and along the foot of the screen.
            RectTransform deck = UiBuild.Flat(_root, "Guverte", Deck, Vector2.zero, new Vector2(1f, 0.545f));
            deck.GetComponent<Image>().raycastTarget = true;

            Image img = SeaKit.Sliced(_root, "Levha", "panel", new Vector2(0.015f, 0.020f),
                                      new Vector2(0.985f, 0.535f), false);
            if (img.sprite == UiSkin.Flat) img.color = Chrome;
            img.raycastTarget = true;   // eats taps so the 3D scene never hears the sheet
            _panel = img.rectTransform;

            // POWER on the kit's title plate, straddling the sheet's top edge — the plate's medallion
            // covers the panel's own top ornament, which a sheet this wide would otherwise stretch.
            RectTransform powerPlate = SeaKit.Plate(_panel, "GucLevhasi", "plaka", new Vector2(0.23f, 0.922f),
                                                    new Vector2(0.77f, 1.03f));
            _powerLabel = Line(powerPlate, "Guc", 40f, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.58f));
            _powerLabel.fontStyle = FontStyles.Bold;
            _powerLabel.color = EnergyTint;

            BuildRoutes();

            // The four CORE stats read big across the top; the nine PROCS grid under them. One
            // array, core first — RefreshSheet fills them in the same order.
            string[] labels =
            {
                Loc.T("deniz.cesaret"), Loc.T("deniz.slot.0"),
                Loc.T("deniz.st.savunma"), Loc.T("deniz.st.surat"),
                Loc.T("deniz.st.kritik"), Loc.T("deniz.st.manevra"), Loc.T("deniz.st.salvo"),
                Loc.T("deniz.st.sersem"), Loc.T("deniz.st.onarim"), Loc.T("deniz.st.cancalma"),
                Loc.T("deniz.st.yagma"), Loc.T("deniz.st.yangin"), Loc.T("deniz.st.zehir"),
            };
            // The four core stats each on a kit stat card; the nine procs share one darker card
            // behind their grid, so the two tiers read as two tiers.
            for (int i = 0; i < CoreStatCount; i++)
            {
                float x0 = 0.035f + i * 0.2375f, x1 = x0 + 0.22f;
                Image cell = SeaKit.Sliced(_panel, "Hucre" + i, "stat", new Vector2(x0, 0.696f),
                                           new Vector2(x1, 0.780f), false);
                cell.color = StatTint;
                TMP_Text label = Line(cell.rectTransform, "Ist" + i, 15f, new Vector2(0.06f, 0.54f), new Vector2(0.94f, 0.90f));
                label.text = labels[i];
                label.color = Faded;
                _statValue[i] = Line(cell.rectTransform, "Deger" + i, 24f, new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.56f));
                _statValue[i].fontStyle = FontStyles.Bold;
            }
            Image grid = SeaKit.Sliced(_panel, "IkincilIzgara", "stat", new Vector2(0.035f, 0.508f),
                                       new Vector2(0.965f, 0.690f), false);
            grid.color = GridTint;
            for (int i = CoreStatCount; i < StatCount; i++)
            {
                int col = (i - CoreStatCount) % 3, row = (i - CoreStatCount) / 3;
                float x0 = 0.04f + col * 0.315f, x1 = x0 + 0.30f;
                float y1 = 0.684f - row * 0.058f, y0 = y1 - 0.056f;
                TMP_Text label = Line(_panel, "Ist" + i, 14f, new Vector2(x0, y0 + 0.029f), new Vector2(x1, y1));
                label.text = labels[i];
                label.color = Faded;
                _statValue[i] = Line(_panel, "Deger" + i, 18f, new Vector2(x0, y0), new Vector2(x1, y0 + 0.031f));
                _statValue[i].fontStyle = FontStyles.Bold;
            }

            // One icon per slot, in SeaCombat's own order — cannon, plating, spyglass, charm,
            // rigging. It was four entries against a five-slot loop, which threw IndexOutOfRange out
            // of BuildPanel and left the panel half-built, so every Update() after it raised a
            // NullReference. The kit finally gives rigging art of its own.
            _slotIcon[SeaCombat.SlotCannon] = SeaKit.Get("top");
            _slotIcon[SeaCombat.SlotPlating] = SeaKit.Get("zirh");
            _slotIcon[SeaCombat.SlotSpyglass] = SeaKit.Get("durbun");
            _slotIcon[SeaCombat.SlotCharm] = SeaKit.Get("tilsim");
            _slotIcon[SeaCombat.SlotRigging] = SeaKit.Get("riging");

            // The row is laid out from the slot count rather than from a pitch measured against four,
            // or the fifth frame starts at 0.985 and hangs off the panel's right edge.
            const float rowLeft = 0.035f, rowRight = 0.965f, gap = 0.018f;
            float slotPitch = (rowRight - rowLeft) / SeaCombat.SlotCount;

            Sprite star = SeaKit.Get("yildiz");
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                int captured = slot;
                float x0 = rowLeft + slot * slotPitch;
                // The kit's equipment slot: the icon in the well, the grade's stars in the strip along
                // its foot. PillFit keeps the strip the same share of the slot at every size.
                Image frame = SeaKit.Sliced(_panel, "Yuva" + slot, "slot", new Vector2(x0, 0.344f),
                                            new Vector2(x0 + slotPitch - gap, 0.500f), true);
                frame.raycastTarget = true;
                _gearFrame[slot] = frame;

                var icon = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(frame.transform, false);
                var ii = icon.GetComponent<Image>();
                ii.sprite = _slotIcon[slot] != null ? _slotIcon[slot] : UiSkin.Flat;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                UiBuild.Anchor((RectTransform)icon.transform, new Vector2(0.18f, 0.29f), new Vector2(0.82f, 0.84f));
                _gearIcon[slot] = ii;

                _gearStars[slot] = new Image[SeaCombat.GradeMult.Length];
                Stars(frame.transform, star, _gearStars[slot]);

                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() => OnGearSlot(captured));
            }

            // The captain line shares its band with the pet-equip strip: the kit's captain portrait and
            // the label keep the left half (two lines if a long name needs them), three pet slots take
            // the right. The band is taller than the captain line alone needed because the slots are
            // real tap targets with a portrait, a bonus and stars — the height came out of SEARCH/AUTO.
            _captainPortrait = SeaKit.Square(_panel, "KaptanPortre", "kaptan", new Vector2(0.035f, 0.035f),
                                             new Vector2(0.246f, 0.334f), 0f);
            _captainLabel = Line(_panel, "Kaptan", 22f, new Vector2(0.145f, 0.244f), new Vector2(0.50f, 0.336f));
            BuildPetSlots();

            // The kit's energy pill: the bolt rides its left cap, the count reads after it.
            Image pill = SeaKit.Sliced(_panel, "EnerjiHapi", "enerji_pili", new Vector2(0.035f, 0.166f),
                                       new Vector2(0.835f, 0.236f), true);
            if (pill.sprite == UiSkin.Flat) pill.color = Chrome;
            _energyLabel = Line(pill.rectTransform, "Yazi", 24f, new Vector2(0.13f, 0.12f), new Vector2(0.96f, 0.88f));
            _energyLabel.color = EnergyTint;

            // The reference game's "extra stamina" grab, sat where the wait is read rather than
            // behind a popup: the pill says how long the pool takes, the kit's "+" beside it says what
            // an ad would skip — its caption on a small plate across the button's foot.
            Image plus = SeaKit.Square(_panel, "EnerjiReklam", "enerji_ekle", new Vector2(0.952f, 0.952f),
                                       new Vector2(0.160f, 0.242f), 1f);
            plus.raycastTarget = true;
            _energyAd = plus.gameObject.AddComponent<Button>();
            _energyAd.targetGraphic = plus;
            _energyAd.colors = Opaque(_energyAd.colors);
            _energyAd.onClick.AddListener(OnEnergyAd);
            Image tag = SeaKit.Sliced(plus.rectTransform, "Etiket", "stat", new Vector2(-0.26f, -0.10f),
                                      new Vector2(1.14f, 0.28f), true);
            tag.color = Outline;
            _energyAdLabel = Line(tag.rectTransform, "Yazi", 17f, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f));
            _energyAdLabel.fontStyle = FontStyles.Bold;

            _search = PanelButton("Ara", "ana_buton", new Vector2(0.035f, 0.030f),
                                  new Vector2(0.645f, 0.148f), OnSearch, out _searchLabel, 28f, CompassSearch);
            _autoBtn = PanelButton("Oto", "oto_buton", new Vector2(0.685f, 0.030f),
                                   new Vector2(0.965f, 0.148f), OnAuto, out _autoLabel, 26f, 0f);
            _autoImage = _autoBtn.GetComponent<Image>();
            _autoLabel.text = Loc.T("deniz.oto");
        }

        /// <summary>A slot's star pips, laid across the kit slot's foot strip and hidden until a
        /// refresh says how many to show.</summary>
        private static void Stars(Transform slot, Sprite star, Image[] into)
        {
            float pitch = 0.74f / into.Length;
            for (int g = 0; g < into.Length; g++)
            {
                var st = new GameObject("Yildiz" + g, typeof(RectTransform), typeof(Image));
                st.transform.SetParent(slot, false);
                var si = st.GetComponent<Image>();
                si.sprite = star != null ? star : UiSkin.Flat;
                si.preserveAspect = true;
                si.raycastTarget = false;
                float sx = 0.13f + g * pitch;
                UiBuild.Anchor((RectTransform)st.transform, new Vector2(sx, 0.085f), new Vector2(sx + pitch * 0.92f, 0.225f));
                st.SetActive(false);
                into[g] = si;
            }
        }

        /// <summary>
        /// Three pet-equip slots, sharing the captain's band. Same visual language as the gear
        /// slots above (tinted frame, star pips, tap-to-act): portrait on top, that pet's own bonus
        /// under it, stars along the foot. A locked slot says LOCKED and how many more wins open
        /// it; an unlocked slot cycles through owned, not-already-equipped species on tap — empty,
        /// species, species, ..., empty again — straight through PetService.Equip/Unequip. No
        /// popup: the same tap the slot answers with is the whole picker.
        /// </summary>
        private void BuildPetSlots()
        {
            const float left = 0.52f, right = 0.965f, gap = 0.016f;
            float pitch = (right - left) / Pets.SlotCount;
            Sprite star = SeaKit.Get("yildiz");

            for (int slot = 0; slot < Pets.SlotCount; slot++)
            {
                int captured = slot;
                float x0 = left + slot * pitch;
                Image frame = SeaKit.Sliced(_panel, "EvcilYuva" + slot, "slot", new Vector2(x0, 0.244f),
                                            new Vector2(x0 + pitch - gap, 0.336f), true);
                frame.raycastTarget = true;
                var go = frame.gameObject;
                _petFrame[slot] = frame;

                var portrait = new GameObject("Portre", typeof(RectTransform), typeof(Image));
                portrait.transform.SetParent(go.transform, false);
                var pi = portrait.GetComponent<Image>();
                pi.preserveAspect = true;
                pi.raycastTarget = false;
                pi.enabled = false;
                UiBuild.Anchor((RectTransform)portrait.transform, new Vector2(0.12f, 0.44f), new Vector2(0.88f, 0.86f));
                _petPortrait[slot] = pi;

                TMP_Text bonus = Line((RectTransform)go.transform, "Bonus", 20f,
                                      new Vector2(0.06f, 0.25f), new Vector2(0.94f, 0.46f));
                bonus.fontStyle = FontStyles.Bold;
                bonus.color = Paper;
                _petBonus[slot] = bonus;

                _petStars[slot] = new Image[Pets.MaxStars];
                Stars(go.transform, star, _petStars[slot]);

                // Sized for the lone "+" of an empty slot; LOCKED over its wins auto-sizes down.
                TMP_Text badge = Line((RectTransform)go.transform, "Rozet", 44f,
                                      new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.86f));
                badge.fontStyle = FontStyles.Bold;
                badge.color = Faded;
                // Two lines, never three: LOCKED over "N WINS", shrunk to fit like the route pills'
                // own caption rather than broken mid-phrase.
                badge.textWrappingMode = TextWrappingModes.NoWrap;
                _petBadge[slot] = badge;

                var button = go.AddComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() => OnPetSlot(captured));
                _petButton[slot] = button;
            }
        }

        /// <summary>
        /// A locked slot answers a tap with the same toast a locked route uses; an unlocked slot
        /// cycles straight to the next valid state and writes it through the service at once,
        /// then names what the new pet adds — in the sheet's own stat word, so the toast points at
        /// the row that just moved.
        /// </summary>
        private void OnPetSlot(int slot)
        {
            if (_pets == null) return;
            if (slot >= _pets.SlotsUnlocked)
            {
                _toast = 2.2f;
                _banner.color = Faded;
                _banner.text = Loc.T("dost.baslik") + "  ·  "
                             + string.Format(Loc.T("deniz.rotaKilit"), _pets.FightsUntilSlot(slot));
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Denied);
                ServiceLocator.Get<HapticService>()?.Light();
                return;
            }

            int current = _pets.EquippedAt(slot);
            int next = NextCandidateSpecies(slot, current);
            bool ok = next == -1 ? _pets.Unequip(slot) : _pets.Equip(slot, next);
            if (!ok) return;
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Tap);
            ServiceLocator.Get<HapticService>()?.Light();

            if (next != -1 && _pets.TryBestOwned(next, out var rarity, out int star))
            {
                Pets.EffectKind kind = Pets.EffectKindOf(next);
                _toast = 2.2f;
                _banner.color = _pets.RarityTint(rarity);
                _banner.text = Loc.T(EffectLabelKey(kind)) + "  "
                             + PetBonusText(kind, Pets.Bonus(kind, rarity, star, _pets.Tuning));
            }
        }

        /// <summary>
        /// Walks the ring [empty, species0, species1, ..., speciesN-1] one step past
        /// <paramref name="current"/>, skipping a species nobody owns or one already worn in
        /// another slot. Empty is always a valid landing spot, so the walk always terminates
        /// within one lap of the ring.
        /// </summary>
        private int NextCandidateSpecies(int slot, int current)
        {
            int ringLen = Pets.SpeciesCount + 1;
            int currentIdx = current < 0 ? 0 : current + 1;
            for (int i = 1; i <= ringLen; i++)
            {
                int idx = (currentIdx + i) % ringLen;
                if (idx == 0) return -1;
                int species = idx - 1;
                if (!_pets.Owned(species)) continue;

                bool wornElsewhere = false;
                for (int other = 0; other < Pets.SlotCount; other++)
                    if (other != slot && _pets.EquippedAt(other) == species) { wornElsewhere = true; break; }
                if (!wornElsewhere) return species;
            }
            return -1;
        }

        /// <summary>Every pet slot's frame, portrait, bonus, stars and badge, re-read from the
        /// service. Driven by PetService.Changed alone: equip, fusion, a chest and a sea win (whose
        /// pearls raise it, and whose count is what opens a slot) are the only things that move
        /// any of it, so there is no tick to allocate on.</summary>
        private void RefreshPetSlots()
        {
            if (_pets == null || _petFrame[0] == null) return;
            int unlocked = _pets.SlotsUnlocked;

            for (int slot = 0; slot < Pets.SlotCount; slot++)
            {
                bool isUnlocked = slot < unlocked;
                int species = isUnlocked ? _pets.EquippedAt(slot) : -1;
                bool filled = false;
                RosterCardState.Rarity rarity = RosterCardState.Rarity.Common;
                int star = 0;
                if (isUnlocked && Pets.Exists(species))
                    filled = _pets.TryBestOwned(species, out rarity, out star);

                _petFrame[slot].color = filled ? Color.Lerp(Color.white, _pets.RarityTint(rarity), FrameTintWeight)
                                       : isUnlocked ? SlotEmpty : SlotLocked;

                Sprite portrait = filled && _petConfig != null ? _petConfig.PortraitOf(Pets.IdOf(species)) : null;
                _petPortrait[slot].enabled = portrait != null;
                _petPortrait[slot].sprite = portrait;

                Image[] stars = _petStars[slot];
                for (int g = 0; g < stars.Length; g++)
                    stars[g].gameObject.SetActive(filled && g < star);

                if (filled)
                {
                    Pets.EffectKind kind = Pets.EffectKindOf(species);
                    _petBonus[slot].text = PetBonusText(kind, Pets.Bonus(kind, rarity, star, _pets.Tuning));
                }
                else _petBonus[slot].text = string.Empty;

                _petBadge[slot].text = !isUnlocked
                    ? Loc.T("ortak.kilitli") + "\n"
                      + string.Format(Loc.T("deniz.rotaKilit"), _pets.FightsUntilSlot(slot))
                    : !filled ? "+" : string.Empty;
            }
        }

        /// <summary>The sheet's own label for the stat a pet feeds — the same key the stat grid
        /// prints above the number that pet moves. PetRosterUI reads it too, so the pet panel names
        /// each bonus with the very word the fight sheet uses.</summary>
        internal static string EffectLabelKey(Pets.EffectKind kind)
        {
            switch (kind)
            {
                case Pets.EffectKind.Dodge: return "deniz.st.manevra";
                case Pets.EffectKind.Salvo: return "deniz.st.salvo";
                case Pets.EffectKind.Stun:  return "deniz.st.sersem";
                case Pets.EffectKind.Steal: return "deniz.st.cancalma";
                case Pets.EffectKind.Def:   return "deniz.st.savunma";
                default:                    return "deniz.cesaret";
            }
        }

        /// <summary>A pet's bonus in the ink the sheet already uses for that stat: a percentage for
        /// the four procs, one decimal for the flat defence and hull.</summary>
        private static string PetBonusText(Pets.EffectKind kind, double value)
        {
            bool percent = kind == Pets.EffectKind.Dodge || kind == Pets.EffectKind.Salvo
                        || kind == Pets.EffectKind.Stun || kind == Pets.EffectKind.Steal;
            return percent ? "+" + (value * 100d).ToString("0.#") + "%" : "+" + value.ToString("0.#");
        }

        /// <summary>
        /// The route strip, and the two lines that say what picking a route BUYS: the threat band
        /// out there and the drop table it rolls against.
        ///
        /// WHY THE STRIP EXISTS AT ALL. The waters used to be whatever the dock had opened, with no
        /// say in it — which meant a player whose gear had fallen behind the fleet had exactly one
        /// move left, which was to stop. Four pills turn that into a decision: hunt shallower and
        /// build the sheet back up, or take the danger for the drop table. Locked routes are SHOWN,
        /// captioned with what still opens them, because the ladder ahead is half the reason to
        /// keep sailing the dock.
        /// </summary>
        private void BuildRoutes()
        {
            Sprite lockArt = SeaKit.Get("kilit");
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                int captured = tier;
                float x0 = 0.035f + tier * 0.2375f;
                // The kit's route tab, drawn as its two halves so its star never stretches. The star
                // rides the top edge and dips into the body, so the numeral sits below it.
                RectTransform tab = SeaKit.Plate(_panel, "Rota" + tier, "rota", new Vector2(x0, 0.840f),
                                                 new Vector2(x0 + 0.22f, 0.912f),
                                                 out _routeLeft[tier], out _routeRight[tier]);
                _routeLeft[tier].raycastTarget = true;
                _routeRight[tier].raycastTarget = true;

                // A locked route wears the kit's padlock beside what still opens it.
                var lk = new GameObject("Kilit", typeof(RectTransform), typeof(Image));
                lk.transform.SetParent(tab, false);
                var li = lk.GetComponent<Image>();
                li.sprite = lockArt != null ? lockArt : UiSkin.Flat;
                li.preserveAspect = true;
                li.raycastTarget = false;
                UiBuild.Anchor((RectTransform)lk.transform, new Vector2(0.09f, 0.10f), new Vector2(0.30f, 0.64f));
                lk.SetActive(false);
                _routeLock[tier] = li;

                // A fixed size, not auto-sized: Baloo2's tall line box made auto-size shrink a lone
                // numeral to half the tab. RefreshRoutes sets it per state; the glyph may use the box's
                // slack above and below.
                _routeMark[tier] = Line(tab, "Kademe", RouteMarkOpen, new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.64f));
                _routeMark[tier].fontStyle = FontStyles.Bold;
                _routeMark[tier].enableAutoSizing = false;
                _routeMark[tier].overflowMode = TextOverflowModes.Overflow;
                _routeMark[tier].text = TierMark[tier];
                _routeSub[tier] = Line(tab, "Sart", 17f, new Vector2(0.30f, 0.08f), new Vector2(0.94f, 0.38f));
                _routeSub[tier].fontStyle = FontStyles.Bold;
                _routeSub[tier].color = Faded;

                // The halves tint by state, so the press is shown by the pick itself, not a tint.
                var button = tab.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnRoute(captured));
                _routeBtn[tier] = button;
            }

            _threatLine = Line(_panel, "TehditBandi", 17f,
                               new Vector2(0.035f, 0.808f), new Vector2(0.965f, 0.836f));
            _lootLine = Line(_panel, "GanimetSinifi", 15f,
                             new Vector2(0.035f, 0.784f), new Vector2(0.965f, 0.808f));
            _lootLine.richText = true;
        }

        /// <summary>
        /// A kit capsule button — <c>ana_buton</c> (orange, the one move that matters) or
        /// <c>oto_buton</c> (teal, everything else). The orange one carries a compass medallion in its
        /// left cap, so its label starts after <paramref name="labelLeft"/> of the width.
        /// </summary>
        private static Button KitButton(RectTransform parent, string name, string art, Vector2 aMin, Vector2 aMax,
                                        UnityEngine.Events.UnityAction onClick, out TMP_Text label, float size,
                                        float labelLeft)
        {
            Image image = SeaKit.Sliced(parent, name, art, aMin, aMax, true);
            image.raycastTarget = true;
            if (image.sprite == UiSkin.Flat) image.color = Chrome;
            label = Line(image.rectTransform, "Yazi", size,
                         new Vector2(Mathf.Max(0.06f, labelLeft), 0.12f), new Vector2(0.94f, 0.88f));
            label.fontStyle = FontStyles.Bold;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.colors = Opaque(b.colors);
            b.onClick.AddListener(onClick);
            return b;
        }

        /// <summary>
        /// Keeps a disabled kit button solid. The default disabled tint is half transparent, which on
        /// a pre-coloured button lets the panel read through and looks like a rendering fault rather
        /// than "not now"; callers grey the art themselves when they switch it off.
        /// </summary>
        private static ColorBlock Opaque(ColorBlock colors)
        {
            colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            return colors;
        }

        private Button PanelButton(string name, string art, Vector2 aMin, Vector2 aMax,
                                   UnityEngine.Events.UnityAction onClick, out TMP_Text label, float size,
                                   float labelLeft)
            => KitButton(_panel, name, art, aMin, aMax, onClick, out label, size, labelLeft);

        // ------------------------------------------------------------ the cards
        /// <summary>
        /// A card on the kit's info panel — opaque, so the sheet under it no longer reads through — with
        /// the kit's title plate straddling its top edge, carrying the title. Returns the card; the
        /// title label comes back through <paramref name="title"/>.
        /// </summary>
        private RectTransform Card(string name, Vector2 aMin, Vector2 aMax, out TMP_Text title)
        {
            Image img = SeaKit.Sliced(_root, name, "panel", aMin, aMax, false);
            if (img.sprite == UiSkin.Flat) img.color = Chrome;
            img.raycastTarget = true;
            RectTransform card = img.rectTransform;
            // The card eats taps so nothing behind it can fire while the decision is open.
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            RectTransform plate = SeaKit.Plate(card, "BaslikLevhasi", "plaka", new Vector2(0.16f, 1f), new Vector2(0.84f, 1f));
            plate.offsetMin = new Vector2(0f, -64f);
            plate.offsetMax = new Vector2(0f, 50f);
            title = Line(plate, "Baslik", 34f, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.58f));
            title.fontStyle = FontStyles.Bold;

            card.gameObject.SetActive(false);
            return card;
        }

        private static Button CardButton(RectTransform card, string name, string art, Vector2 aMin, Vector2 aMax,
                                         UnityEngine.Events.UnityAction onClick, out TMP_Text label, float labelLeft)
            => KitButton(card, name, art, aMin, aMax, onClick, out label, 26f, labelLeft);

        /// <summary>A dark well on a card — the kit's stat card, tinted down.</summary>
        private static Image Inset(RectTransform card, string name, Vector2 aMin, Vector2 aMax)
        {
            Image well = SeaKit.Sliced(card, name, "stat", aMin, aMax, false);
            well.color = InsetTint;
            return well;
        }

        /// <summary>The details card — the reference game's Monster Details: who this is, what it
        /// does, whether it outguns us, and the one decision: SAVAŞ! or VAZGEÇ.</summary>
        private void BuildFoundCard()
        {
            _foundCard = Card("DetayKarti", new Vector2(0.06f, 0.31f), new Vector2(0.94f, 0.69f), out _foundTitle);

            // The signature chip: a dark kit stat card with the signature written in its own colour —
            // tinting the slate card itself turned CRIT's yellow into olive.
            Image tag = SeaKit.Sliced(_foundCard, "Imza", "stat", new Vector2(0.28f, 0.795f),
                                      new Vector2(0.72f, 0.880f), true);
            tag.color = Outline;
            _foundTagPill = tag;
            _foundTag = Line(tag.rectTransform, "Yazi", 22f, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.90f));
            _foundTag.fontStyle = FontStyles.Bold;

            // DANGEROUS, with the kit's warning sign beside the word. The word is short and set
            // at a fixed size, so FillFoundCard can place the sign against its measured width.
            _foundDanger = Line(_foundCard, "Uyari", 30f, new Vector2(0.05f, 0.700f), new Vector2(0.95f, 0.785f));
            _foundDanger.fontStyle = FontStyles.Bold;
            _foundDanger.enableAutoSizing = false;
            var sign = new GameObject("UyariIkon", typeof(RectTransform), typeof(Image));
            sign.transform.SetParent(_foundDanger.transform, false);
            _foundDangerIcon = sign.GetComponent<Image>();
            Sprite danger = SeaKit.Get("tehlike");
            _foundDangerIcon.sprite = danger != null ? danger : UiSkin.Flat;
            _foundDangerIcon.preserveAspect = true;
            _foundDangerIcon.raycastTarget = false;
            var srt = (RectTransform)sign.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(56f, 56f);

            _foundPower = Line(_foundCard, "Guc", 27f, new Vector2(0.05f, 0.610f), new Vector2(0.95f, 0.695f));
            Image block = Inset(_foundCard, "BlokKuyusu", new Vector2(0.07f, 0.380f), new Vector2(0.93f, 0.600f));
            _foundStats = Line(block.rectTransform, "Blok", 23f, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f));

            // What a win pays, as the kit's three loot icons over the words that name them — gear
            // (the cannon stands for the whole loadout), charts, salvage — in the words' own order.
            string[] loot = { "top", "harita", "hurda" };
            for (int i = 0; i < loot.Length; i++)
            {
                var go = new GameObject("OdulIkon" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_foundCard, false);
                var icon = go.GetComponent<Image>();
                Sprite art = SeaKit.Get(loot[i]);
                icon.sprite = art != null ? art : UiSkin.Flat;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                float cx = 0.34f + i * 0.16f;
                UiBuild.Anchor((RectTransform)go.transform, new Vector2(cx - 0.06f, 0.262f), new Vector2(cx + 0.06f, 0.364f));
            }
            _foundReward = Line(_foundCard, "Odul", 21f, new Vector2(0.05f, 0.208f), new Vector2(0.95f, 0.260f));
            _foundReward.color = Faded;

            TMP_Text fightLabel, passLabel;
            CardButton(_foundCard, "Savas", "ana_buton", new Vector2(0.07f, 0.04f),
                       new Vector2(0.60f, 0.18f), OnConfirm, out fightLabel, CompassCard);
            fightLabel.text = Loc.T("deniz.savas");
            fightLabel.fontSize = 30f;
            CardButton(_foundCard, "Vazgec", "oto_buton", new Vector2(0.64f, 0.04f),
                       new Vector2(0.93f, 0.18f), OnDecline, out passLabel, 0f);
            passLabel.text = Loc.T("deniz.vazgec");
        }

        /// <summary>The compare card — the reference game's Current Items / Drop Items, one card:
        /// the worn thing beside the dropped thing, row by row, delta on top.</summary>
        private void BuildLootCard()
        {
            _lootCard = Card("GanimetKarti", new Vector2(0.05f, 0.32f), new Vector2(0.95f, 0.68f), out _lootTitle);

            _lootDelta = Line(_lootCard, "Fark", 27f, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.86f));
            _lootDelta.fontStyle = FontStyles.Bold;

            _curFrame = Column(new Vector2(0.05f, 0.25f), new Vector2(0.485f, 0.765f),
                               out _curHead, out _curGrade, out _curRows);
            _newFrame = Column(new Vector2(0.515f, 0.25f), new Vector2(0.95f, 0.765f),
                               out _newHead, out _newGrade, out _newRows);
            _curHead.text = Loc.T("deniz.mevcut");
            _newHead.text = Loc.T("deniz.yeni");

            TMP_Text equipLabel;
            CardButton(_lootCard, "Giydir", "ana_buton", new Vector2(0.07f, 0.05f),
                       new Vector2(0.48f, 0.20f), OnEquip, out equipLabel, CompassWide);
            equipLabel.text = Loc.T("deniz.giydir");
            CardButton(_lootCard, "Sok", "oto_buton", new Vector2(0.52f, 0.05f),
                       new Vector2(0.93f, 0.20f), OnScrap, out _scrapLabel, 0f);
        }

        private Image Column(Vector2 aMin, Vector2 aMax, out TMP_Text head, out TMP_Text grade, out TMP_Text rows)
        {
            Image img = Inset(_lootCard, "Sutun", aMin, aMax);
            RectTransform col = img.rectTransform;
            head = Line(col, "Bas", 22f, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.98f));
            head.color = Faded;
            grade = Line(col, "Derece", 24f, new Vector2(0.05f, 0.70f), new Vector2(0.95f, 0.85f));
            grade.fontStyle = FontStyles.Bold;
            rows = Line(col, "Satirlar", 22f, new Vector2(0.07f, 0.06f), new Vector2(0.93f, 0.68f));
            rows.alignment = TextAlignmentOptions.Top;
            return img;
        }

        /// <summary>The worn-item popup off the sheet's slots: what it does, SÖK for salvage.</summary>
        private void BuildGearCard()
        {
            _gearCard = Card("TakiKarti", new Vector2(0.14f, 0.32f), new Vector2(0.86f, 0.68f), out _gearTitle);

            // The slot's own kit icon, so the card says which slot it is before a word is read.
            var icon = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(_gearCard, false);
            _gearCardIcon = icon.GetComponent<Image>();
            _gearCardIcon.preserveAspect = true;
            _gearCardIcon.raycastTarget = false;
            UiBuild.Anchor((RectTransform)icon.transform, new Vector2(0.38f, 0.70f), new Vector2(0.62f, 0.88f));

            _gearRows = Line(_gearCard, "Satirlar", 24f, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.68f));
            _gearRows.alignment = TextAlignmentOptions.Top;

            TMP_Text closeLabel;
            _gearScrap = CardButton(_gearCard, "Sok", "oto_buton", new Vector2(0.07f, 0.06f),
                                    new Vector2(0.52f, 0.21f), OnGearScrap, out _gearScrapLabel, 0f);
            CardButton(_gearCard, "Kapat", "oto_buton", new Vector2(0.56f, 0.06f),
                       new Vector2(0.93f, 0.21f), OnGearClose, out closeLabel, 0f);
            closeLabel.text = Loc.T("deniz.kapat");
        }

        // ---------------------------------------------------------------- actions
        private void OnSearch()
        {
            if (_fights == null || !_fights.TrySearch()) return;
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void OnAuto()
        {
            if (_fights == null) return;
            _fights.SetAuto(!_fights.Auto);
            ServiceLocator.Get<HapticService>()?.Light();
        }

        /// <summary>
        /// Pick the waters. A locked pill is not a dead button: it answers with what still opens
        /// the route, on the same banner a fight's result uses, because "you cannot" without "yet"
        /// is the one message that reads as a bug.
        /// </summary>
        private void OnRoute(int tier)
        {
            if (_sea == null) return;
            if (!_sea.TrySetTier(tier))
            {
                _toast = 2.2f;
                _banner.color = Faded;
                _banner.text = string.Format(Loc.T("deniz.rotaKapali"), _sea.FightsToUnlock(tier));
                ServiceLocator.Get<HapticService>()?.Light();
                return;
            }
            RefreshRoutes();
            ServiceLocator.Get<HapticService>()?.Light();
        }

        /// <summary>
        /// The energy top-up. Every gate is checked BEFORE the ad plays — a full pool, a spent day,
        /// a running cooldown, no ad loaded — so the player is never shown thirty seconds of video
        /// for nothing.
        /// </summary>
        private void OnEnergyAd()
        {
            if (_sea == null || _free == null) return;
            if (_sea.Energy >= _sea.EnergyMax) return;
            if (!_free.CanWatch(EnergyAdId, energyAdChargesPerDay, energyAdCooldownSeconds)) return;

            // The remove-ads pack is the only thing that stands in for the ad; the daily cap and the
            // cooldown still apply, or the button would print energy.
            if (_free.AdsRemoved) { PayEnergy(); return; }
            if (_ad == null || !_ad.Available) return;
            _ad.ShowRewarded(PayEnergy);
        }

        /// <summary>
        /// What the ad bought. The grant is capped by the pool, so it reports what actually landed:
        /// a pool that filled itself while the video played keeps the player's charge rather than
        /// burning it on nothing. A charge that IS spent reaches the disk before the pill says so.
        /// </summary>
        private void PayEnergy()
        {
            if (_sea == null || _free == null) return;
            int given = _sea.GrantEnergy(energyAdReward);
            if (given <= 0) return;

            _free.Consume(EnergyAdId);
            if (_save != null && _data != null) _save.Save(_data);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
            _energyTick = 0f;   // repaint the pill and the button on the next tick
        }

        private void OnConfirm()
        {
            if (_fights == null || !_fights.Confirm()) return;
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void OnDecline()
        {
            if (_fights == null || !_fights.Decline()) return;
            ServiceLocator.Get<HapticService>()?.Light();
        }

        private void OnEquip()
        {
            if (_fights == null || !_fights.EquipDrop()) return;
            ServiceLocator.Get<HapticService>()?.Medium();
            RefreshSheet();
        }

        private void OnScrap()
        {
            if (_fights == null || !_fights.ScrapDrop()) return;
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void OnGearSlot(int slot)
        {
            if (_sea == null) return;
            _gearShown = slot;
            FillGearCard();
            _gearCard.gameObject.SetActive(true);
            RefreshScrim();
            ServiceLocator.Get<HapticService>()?.Light();
        }

        private void OnGearScrap()
        {
            if (_sea == null || _gearShown < 0) return;
            if (_sea.ScrapWorn(_gearShown) > 0L) ServiceLocator.Get<HapticService>()?.Medium();
            OnGearClose();
            RefreshSheet();
        }

        private void OnGearClose()
        {
            _gearShown = -1;
            _gearCard.gameObject.SetActive(false);
            RefreshScrim();
        }

        private void RefreshScrim()
        {
            bool open = _foundCard.gameObject.activeSelf || _lootCard.gameObject.activeSelf
                     || _gearCard.gameObject.activeSelf;
            if (_scrim.activeSelf != open) _scrim.SetActive(open);
        }

        // ----------------------------------------------------------------- drive
        private void Update()
        {
            if (_fights == null || _stage == null) return;
            float dt = Time.deltaTime;
            EncounterController.Phase phase = _fights.State;

            if (_fights.Stamp != _seenStamp)
            {
                _seenStamp = _fights.Stamp;
                _toast = 2.2f;
                _banner.color = _fights.LastWon ? Win : Loss;
                _banner.text = _fights.LastWon
                    ? Loc.T("deniz.batti") + "  " + string.Format(Loc.T("deniz.ganimet"),
                                                                  _fights.LastCharts, _fights.LastSalvage)
                    : Loc.T("deniz.yenildik");
            }
            if (_toast > 0f) { _toast -= dt; if (_toast <= 0f) _banner.text = string.Empty; }
            bool toasting = _toast > 0f;
            if (_bannerBack.activeSelf != toasting) _bannerBack.SetActive(toasting);

            if (phase != _seenPhase)
            {
                _seenPhase = phase;
                SetChrome(phase);
                if (phase == EncounterController.Phase.Approach)
                {
                    _seenKind = -1;
                    _threatGroup.alpha = 1f;
                    _threatRoot.localRotation = Quaternion.identity;
                }
                if (phase == EncounterController.Phase.Found) FillFoundCard();
                if (phase == EncounterController.Phase.Loot) FillLootCard();
            }

            // The sheet is on from the moment the player is aboard — the sea IS the screen.
            bool aboard = _sea != null && _sea.Active;
            _rootGroup.alpha = Mathf.MoveTowards(_rootGroup.alpha, aboard || _toast > 0f ? 1f : 0f, dt * 5f);
            _rootGroup.blocksRaycasts = aboard;
            if (_rootGroup.alpha <= 0.001f) return;

            float w = _stage.rect.width, h = _stage.rect.height;
            float t = Time.time;

            DriveShip(w, h, t, dt);
            DriveThreat(phase, w, h, t);
            DriveBars(phase, w, h);
            DriveBallLaunches(phase, w, h);
            DriveEvents(w, h);
            DriveBalls(dt);
            DriveFlashes(dt);
            DriveFloats(dt, h);
            DriveSheet(phase, dt);
        }

        private void DriveShip(float w, float h, float t, float dt)
        {
            _shipWobble = Mathf.MoveTowards(_shipWobble, 0f, dt * 26f);
            SizeVessel(_shipRoot, h * 0.36f);
            _shipRoot.anchoredPosition = new Vector2(w * 0.26f,
                h * 0.335f + Mathf.Sin(t * Mathf.PI * 2f / 3.1f) * h * 0.012f);
            _shipRoot.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(t * Mathf.PI * 2f / 4.7f) * 3f + _shipWobble);
        }

        private void DriveThreat(EncounterController.Phase phase, float w, float h, float t)
        {
            int kind = _fights.ThreatKind;
            bool visible = phase == EncounterController.Phase.Approach
                        || phase == EncounterController.Phase.Found
                        || phase == EncounterController.Phase.Fight
                        || phase == EncounterController.Phase.Sunk
                        || phase == EncounterController.Phase.Driven;
            if (visible && kind != _seenKind)
            {
                _seenKind = kind;
                bool raider;
                Sprite art = ThreatArt(kind, out raider);
                if (art != null) _threatImage.sprite = art;
                // Every older threat sprite faces right and is mirrored to face us; the kit's pirate
                // ship is drawn already facing left, so mirroring it would turn its stern on us.
                _threatImage.rectTransform.localScale = new Vector3(raider ? 1f : -1f, 1f, 1f);
                _threatName.text = Loc.T("deniz.tehdit." + kind);
            }

            _threatWobble = Mathf.MoveTowards(_threatWobble, 0f, Time.deltaTime * 26f);
            SizeVessel(_threatRoot, h * (kind == SeaCombat.Beast ? 0.40f : 0.34f));
            float xHome = w * 0.74f;
            float y = h * 0.335f + Mathf.Sin(t * Mathf.PI * 2f / 3.6f + 1.7f) * h * 0.012f;

            switch (phase)
            {
                case EncounterController.Phase.Approach:
                {
                    float a = Mathf.Clamp01(_fights.PhaseTime / (float)_fights.Combat.ApproachSeconds);
                    a = 1f - (1f - a) * (1f - a);
                    _threatRoot.anchoredPosition = new Vector2(Mathf.Lerp(w * 1.14f, xHome, a), y);
                    _threatRoot.localRotation = Quaternion.identity;
                    break;
                }
                case EncounterController.Phase.Found:
                    _threatRoot.anchoredPosition = new Vector2(xHome, y);
                    break;

                case EncounterController.Phase.Fight:
                    _threatRoot.anchoredPosition = new Vector2(xHome, y);
                    _threatRoot.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Sin(t * Mathf.PI * 2f / 4.1f + 0.9f) * 2.6f + _threatWobble);
                    break;

                case EncounterController.Phase.Sunk:
                {
                    float a = Mathf.Clamp01(_fights.PhaseTime / _fights.ResolveSeconds);
                    _threatRoot.anchoredPosition = new Vector2(xHome, y - h * 0.34f * a * a);
                    _threatRoot.localRotation = Quaternion.Euler(0f, 0f, 34f * a);
                    _threatGroup.alpha = 1f - a * a;
                    break;
                }
                case EncounterController.Phase.Driven:
                {
                    // They drove US off: the victor holds station and jeers; our ship takes the list.
                    float a = Mathf.Clamp01(_fights.PhaseTime / _fights.ResolveSeconds);
                    _threatRoot.anchoredPosition = new Vector2(xHome - w * 0.04f * a, y);
                    _threatGroup.alpha = 1f;
                    break;
                }
                default:
                    _threatGroup.alpha = Mathf.MoveTowards(_threatGroup.alpha, 0f, Time.deltaTime * 4f);
                    break;
            }
        }

        /// <summary>The raider is the kit's pirate ship; the other kinds keep the theater set's art.
        /// <paramref name="raider"/> says whether the kit ship was used, which decides the facing.</summary>
        private static Sprite ThreatArt(int kind, out bool raider)
        {
            if (kind == SeaCombat.Raider)
            {
                Sprite ship = SeaKit.Get("korsan_gemisi");
                raider = ship != null;
                if (raider) return ship;
            }
            raider = false;
            string name = KindSprite[Mathf.Clamp(kind, 0, KindSprite.Length - 1)];
            return name != null ? S(name) : null;
        }

        private static void SizeVessel(RectTransform root, float height)
        {
            var img = (RectTransform)root.GetChild(0);
            if (img.sizeDelta.y != height) img.sizeDelta = new Vector2(height * 1.45f, height);
        }

        private void DriveBars(EncounterController.Phase phase, float w, float h)
        {
            bool naming = phase == EncounterController.Phase.Approach
                       || phase == EncounterController.Phase.Found
                       || phase == EncounterController.Phase.Fight;
            bool fighting = phase == EncounterController.Phase.Fight;
            _hullTrack.gameObject.SetActive(naming);
            _threatName.gameObject.SetActive(naming);
            _nerveTrack.gameObject.SetActive(fighting);
            if (!naming) return;

            SeaCombat.Fight f = _fights.Current;
            float hull = fighting && f.Them.HullMax > 0d ? (float)(f.Them.Hull / f.Them.HullMax) : 1f;
            // Sized by the heart frame's own aspect, so the heart stays round on any screen.
            _hullTrack.sizeDelta = new Vector2(w * 0.30f, w * 0.30f / _barAspect);
            // Just clear of the masthead: the kit ships are tall, and at 0.66 the bar sat on the flag.
            _hullTrack.anchoredPosition = new Vector2(w * 0.74f, h * 0.70f);
            _hullFill.anchorMax = new Vector2(Mathf.Clamp01(hull), 1f);
            _threatName.rectTransform.anchoredPosition = new Vector2(w * 0.74f, h * 0.70f + w * 0.075f);

            if (!fighting) return;
            _nerveTrack.sizeDelta = new Vector2(w * 0.26f, w * 0.26f / _barAspect);
            _nerveTrack.anchoredPosition = new Vector2(w * 0.26f, h * 0.70f);
            _nerveFill.anchorMax = new Vector2(
                Mathf.Clamp01(f.Us.HullMax > 0d ? (float)(f.Us.Hull / f.Us.HullMax) : 0f), 1f);
        }

        // ---------------------------------------------------------------- theater
        /// <summary>A ball goes up whenever the controller opens a ball step — BallSerial is the
        /// cue, so a SALVO's second ball launches exactly like the first.</summary>
        private void DriveBallLaunches(EncounterController.Phase phase, float w, float h)
        {
            if (phase != EncounterController.Phase.Fight) { _seenBall = _fights.BallSerial; return; }
            if (_fights.BallSerial == _seenBall) return;
            _seenBall = _fights.BallSerial;

            float flight = (float)_fights.Combat.TurnFlightSeconds;
            Vector2 ours = _shipRoot.anchoredPosition + new Vector2(w * 0.075f, h * 0.055f);
            Vector2 theirs = _threatRoot.anchoredPosition + new Vector2(-w * 0.02f, h * 0.05f);

            if (_fights.TurnStep == EncounterController.Step.OurBall)
            {
                Fire(ours, theirs, flight, h * 0.10f);
            }
            else if (_fights.TurnStep == EncounterController.Step.TheirBall)
            {
                Fire(theirs, ours + new Vector2(w * 0.01f, 0f), flight,
                     h * (_fights.Current.Kind == SeaCombat.Beast ? 0.16f : 0.11f));
            }
        }

        /// <summary>
        /// The ring's narration into pictures: numbers that float, flashes on the hit frame, a
        /// wobble for an unbraced hit. This is the only reader of the fight's events.
        /// </summary>
        private void DriveEvents(float w, float h)
        {
            int count = _fights.EventCount;
            for (; _seenEvent < count; _seenEvent++)
            {
                EncounterController.FightEvent ev = _fights.EventAt(_seenEvent);
                Vector2 at = (ev.OnUs ? _shipRoot.anchoredPosition : _threatRoot.anchoredPosition)
                           + new Vector2(0f, h * 0.30f);
                switch (ev.Kind)
                {
                    case EncounterController.EvHit:
                        Impact(ev.OnUs, w, h);
                        Float("-" + N(ev.Amount), ev.OnUs ? Danger : Paper, at, 1f);
                        if (!ev.OnUs) _threatWobble = Random.Range(4.5f, 7f) * Sign();
                        else _shipWobble = Random.Range(3.5f, 6f) * Sign();
                        break;
                    case EncounterController.EvCrit:
                        Impact(ev.OnUs, w, h);
                        Float(Loc.T("deniz.kritikvur") + " -" + N(ev.Amount), CritTint, at, 1.35f);
                        if (!ev.OnUs) _threatWobble = Random.Range(6f, 9f) * Sign();
                        else _shipWobble = Random.Range(5f, 8f) * Sign();
                        break;
                    case EncounterController.EvDodge:
                        Float(Loc.T("deniz.iska"), Faded, at, 1f);
                        break;
                    case EncounterController.EvBurnTick:
                        Float("-" + N(ev.Amount), BurnTint, at + new Vector2(w * 0.03f, 0f), 0.85f);
                        break;
                    case EncounterController.EvPoisonTick:
                        Float("-" + N(ev.Amount), PoisonTint, at + new Vector2(w * 0.05f, 0f), 0.85f);
                        break;
                    case EncounterController.EvMend:
                        Float("+" + N(ev.Amount), MendTint, at + new Vector2(-w * 0.03f, 0f), 0.85f);
                        break;
                    case EncounterController.EvSteal:
                        // The vampiric ball: the heal floats over whoever fired it.
                        Float("+" + N(ev.Amount), StealTint,
                              (ev.OnUs ? _shipRoot.anchoredPosition : _threatRoot.anchoredPosition)
                              + new Vector2(-w * 0.02f, h * 0.36f), 0.9f);
                        break;
                    case EncounterController.EvStunProc:
                        Float(Loc.T("deniz.sersem"), StunTint, at + new Vector2(0f, h * 0.07f), 1.1f);
                        break;
                    case EncounterController.EvBurnProc:
                        Float(Loc.T("deniz.yanginvur"), BurnTint, at + new Vector2(0f, h * 0.07f), 1.1f);
                        break;
                    case EncounterController.EvPoisonProc:
                        Float(Loc.T("deniz.zehirvur"), PoisonTint, at + new Vector2(0f, h * 0.07f), 1.1f);
                        break;
                    case EncounterController.EvPlunder:
                        Float("+" + (long)ev.Amount + " " + Loc.T("sefer.hurda"), PlunderTint,
                              _threatRoot.anchoredPosition + new Vector2(0f, h * 0.38f), 0.9f);
                        break;
                    case EncounterController.EvSalvo:
                        Float(Loc.T("deniz.salvovur"), CritTint,
                              _shipRoot.anchoredPosition + new Vector2(0f, h * 0.38f), 1f);
                        break;
                    case EncounterController.EvHeld:
                        Float(Loc.T("deniz.sersem"), StunTint, at, 1.1f);
                        break;
                }
            }
        }

        private static float Sign() => Random.value < 0.5f ? -1f : 1f;

        private void Fire(Vector2 from, Vector2 to, float dur, float arc)
        {
            for (int i = 0; i < BallPool; i++)
            {
                if (_ballT[i] >= 0f) continue;
                _ballT[i] = 0f;
                _ballDur[i] = dur;
                _ballFrom[i] = from;
                _ballTo[i] = to;
                _ballArc[i] = arc;
                _ball[i].sizeDelta = new Vector2(26f, 26f);
                _ball[i].anchoredPosition = from;
                _ball[i].gameObject.SetActive(true);
                return;
            }
        }

        private void Impact(bool onUs, float w, float h)
        {
            Vector2 at = (onUs ? _shipRoot.anchoredPosition : _threatRoot.anchoredPosition)
                       + new Vector2(onUs ? w * 0.01f : -w * 0.02f, h * 0.05f);
            for (int i = 0; i < FlashPool; i++)
            {
                if (_flashT[i] >= 0f) continue;
                _flashT[i] = 0f;
                _flashScale[i] = onUs ? 0.8f : 1f;
                _flash[i].anchoredPosition = at;
                _flash[i].gameObject.SetActive(true);
                return;
            }
        }

        private void Float(string text, Color color, Vector2 at, float scale)
        {
            for (int i = 0; i < FloatPool; i++)
            {
                if (_floatT[i] >= 0f) continue;
                _floatT[i] = 0f;
                _float[i].text = text;
                _float[i].color = color;
                _float[i].fontSize = 34f * scale;
                _floatFrom[i] = at + new Vector2(Random.Range(-20f, 20f), 0f);
                _float[i].rectTransform.anchoredPosition = _floatFrom[i];
                _float[i].gameObject.SetActive(true);
                return;
            }
        }

        private void DriveBalls(float dt)
        {
            for (int i = 0; i < BallPool; i++)
            {
                if (_ballT[i] < 0f) continue;
                _ballT[i] += dt;
                float a = _ballT[i] / _ballDur[i];
                if (a >= 1f)
                {
                    _ballT[i] = -1f;
                    _ball[i].gameObject.SetActive(false);
                    continue;
                }
                Vector2 at = Vector2.Lerp(_ballFrom[i], _ballTo[i], a);
                at.y += _ballArc[i] * Mathf.Sin(a * Mathf.PI);
                _ball[i].anchoredPosition = at;
            }
        }

        private void DriveFlashes(float dt)
        {
            float h = _stage.rect.height;
            for (int i = 0; i < FlashPool; i++)
            {
                if (_flashT[i] < 0f) continue;
                _flashT[i] += dt;
                float a = _flashT[i] / 0.32f;
                if (a >= 1f)
                {
                    _flashT[i] = -1f;
                    _flash[i].gameObject.SetActive(false);
                    continue;
                }
                float s = h * 0.085f * _flashScale[i] * (0.6f + 0.9f * a);
                _flash[i].sizeDelta = new Vector2(s, s);
                _flashGroup[i].alpha = 1f - a * a;
            }
        }

        private void DriveFloats(float dt, float h)
        {
            for (int i = 0; i < FloatPool; i++)
            {
                if (_floatT[i] < 0f) continue;
                _floatT[i] += dt;
                float a = _floatT[i] / 0.95f;
                if (a >= 1f)
                {
                    _floatT[i] = -1f;
                    _float[i].gameObject.SetActive(false);
                    continue;
                }
                _float[i].rectTransform.anchoredPosition =
                    _floatFrom[i] + new Vector2(0f, h * 0.11f * a);
                _float[i].alpha = a < 0.7f ? 1f : 1f - (a - 0.7f) / 0.3f;
            }
        }

        // -------------------------------------------------------------- the sheet
        /// <summary>The sheet's slow tick: the pill counts down every quarter second, the whole
        /// sheet re-derives every half — cheap, and never inside the ball maths.</summary>
        private void DriveSheet(EncounterController.Phase phase, float dt)
        {
            if (_sea == null || !_sea.Active) return;

            _energyTick -= dt;
            if (_energyTick <= 0f)
            {
                _energyTick = 0.25f;
                int have = _sea.Energy;
                string pill = have >= _sea.EnergyMax
                    ? Loc.T("deniz.enerji") + "  " + have + "/" + _sea.EnergyMax
                    : Loc.T("deniz.enerji") + "  " + have + "/" + _sea.EnergyMax
                      + "   +1: " + UiBuild.Clock((float)_sea.SecondsToNextEnergy);
                Push(_energyLabel, pill, ref _lastEnergy);

                bool idle = phase == EncounterController.Phase.Idle;
                Push(_searchLabel, Loc.T("deniz.ara") + "  (1)", ref _lastSearch);
                _search.interactable = idle && have > 0;
                _search.targetGraphic.color = idle && have > 0 ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);

                bool auto = _fights.Auto;
                if (auto != _lastAuto)
                {
                    _lastAuto = auto;
                    _autoImage.color = auto ? AutoOn : Color.white;
                    _autoLabel.color = auto ? CritTint : Color.white;
                }

                RefreshEnergyAd(have);
                // Cheap on every tick but the ones that matter: the strip re-inks only when the
                // pick or the fleet's furthest route has actually moved.
                RefreshRoutes();
            }

            _sheetTick -= dt;
            if (_sheetTick > 0f) return;
            _sheetTick = 0.5f;
            // The sheet only re-inks when its headline moved — the tick itself is just a probe.
            double power = SeaCombat.PowerFor(_sea.ShipStats(), _sea.Combat);
            if (System.Math.Abs(power - _sheetPowerSeen) < 0.25d) return;
            _sheetPowerSeen = power;
            RefreshSheet();
        }

        /// <summary>Every derived number on the panel, re-read. Called on the half-second and after
        /// anything that changes the loadout.</summary>
        private void RefreshSheet()
        {
            if (_sea == null || !_sea.Active) return;

            SeaCombat.Stats s = _sea.ShipStats();
            SeaCombat.Tuning t = _sea.Combat;
            Push(_powerLabel, Loc.T("deniz.guc") + "  " + N(SeaCombat.PowerFor(s, t)), ref _lastPower);

            _statValue[0].text = N(s.Hull);
            _statValue[1].text = N(s.Shot);
            _statValue[2].text = D(s.Def);
            _statValue[3].text = D(s.Spd);
            _statValue[4].text = Pct(s.Crit);
            _statValue[5].text = Pct(s.Dodge);
            _statValue[6].text = Pct(s.Salvo);
            _statValue[7].text = Pct(s.Stun);
            _statValue[8].text = Pct(s.Mend);
            _statValue[9].text = Pct(s.Steal);
            _statValue[10].text = Pct(s.Plunder);
            _statValue[11].text = Pct(s.Burn);
            _statValue[12].text = Pct(s.Poison);

            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                int grade = _sea.GearGrade(slot);
                _gearFrame[slot].color = grade < 0
                    ? SlotEmpty
                    : Color.Lerp(Color.white, GradeTint[Mathf.Clamp(grade, 0, GradeTint.Length - 1)], FrameTintWeight);
                // An empty slot keeps its icon as a ghost, so the row still says what goes where.
                _gearIcon[slot].color = grade < 0 ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                Image[] stars = _gearStars[slot];
                for (int g = 0; g < stars.Length; g++)
                    stars[g].gameObject.SetActive(grade >= 0 && g <= grade);
            }

            int aboard = _sea.CaptainAboard;
            string captain;
            if (aboard >= 0 && _captains != null && _captains.Owned(aboard))
                captain = Loc.T("sefer.kaptan") + " · " + Loc.T("kaptan.ad." + Captains.IdOf(aboard))
                        + " (" + Loc.T("kaptan.rol." + Captains.RoleOf(aboard)) + ")";
            else captain = Loc.T("deniz.kaptanyok");
            Push(_captainLabel, captain, ref _lastCaptain);
            _captainLabel.color = aboard >= 0 ? Paper : Faded;
            // One painted captain stands for whoever is aboard; with nobody at the wheel he greys out.
            _captainPortrait.color = aboard >= 0 ? Color.white : new Color(0.45f, 0.48f, 0.54f, 0.7f);

            // The threat reading is OUR power against theirs and the drop odds lean on the worn
            // spyglass, so both move with the sheet and re-derive here rather than on their own clock.
            RefreshPreview();
        }

        /// <summary>
        /// The route strip. Locked routes stay on the strip wearing what still opens them — the
        /// ladder ahead is half the reason to keep sailing the dock — and only the pick and the
        /// fleet's furthest route can change what is drawn, so the tick guards on both.
        /// </summary>
        private void RefreshRoutes()
        {
            if (_sea == null || _routeLeft[0] == null) return;
            int tier = _sea.Tier, max = _sea.MaxTier;
            if (tier == _routeSeenTier && max == _routeSeenMax) return;
            _routeSeenTier = tier;
            _routeSeenMax = max;

            for (int t = 0; t < Voyages.TierCount; t++)
            {
                bool open = t <= max;
                bool picked = open && t == tier;
                // The pick is the tab as drawn, full size; an open route dims; a locked one darkens,
                // shows the padlock, and moves its numeral over to make room for it.
                Color tab = picked ? Color.white : (open ? RouteOpen : RouteLocked);
                _routeLeft[t].color = tab;
                _routeRight[t].color = tab;
                _routeBtn[t].transform.localScale = picked ? Vector3.one : new Vector3(0.93f, 0.93f, 1f);
                _routeLock[t].gameObject.SetActive(!open);

                RectTransform mark = _routeMark[t].rectTransform;
                mark.anchorMin = new Vector2(open ? 0.06f : 0.30f, open ? 0.10f : 0.36f);
                mark.anchorMax = new Vector2(open ? 0.94f : 0.92f, 0.64f);
                _routeMark[t].fontSize = open ? RouteMarkOpen : RouteMarkLocked;
                _routeMark[t].color = picked ? EnergyTint : (open ? Paper : Faded);
                _routeSub[t].text = open
                    ? string.Empty
                    : string.Format(Loc.T("deniz.rotaKilit"), _sea.FightsToUnlock(t));
            }

            RefreshPreview();
        }

        /// <summary>
        /// What the picked route promises, before an energy is spent on finding out: the threat
        /// BAND (the derelict at one end, the beast at the other — one number would be a lie either
        /// way), how the worst of it reads against our sheet, and the drop table it rolls.
        /// </summary>
        private void RefreshPreview()
        {
            if (_sea == null || _threatLine == null) return;
            int tier = _sea.Tier;
            SeaCombat.Tuning t = _sea.Combat;

            double lo = double.MaxValue, hi = 0d;
            for (int kind = 0; kind < SeaCombat.KindCount; kind++)
            {
                double p = SeaCombat.PowerFor(SeaCombat.ThreatStats(tier, kind, t), t);
                if (p < lo) lo = p;
                if (p > hi) hi = p;
            }

            // Priced against the WORST of them: the reading that decides whether to sail is the one
            // about the fight that can go wrong, not the one about the derelict.
            int menace = SeaCombat.Menace(SeaCombat.PowerFor(_sea.ShipStats(), t), hi, t);
            string line = Loc.T("sefer.rota" + tier) + "   ·   "
                        + string.Format(Loc.T("deniz.tehditBant"), N(lo), N(hi));
            if (menace == 2) line += "   ·   " + Loc.T("deniz.tehlikeli");
            else if (menace == 0) line += "   ·   " + Loc.T("deniz.kolay");
            Push(_threatLine, line, ref _lastThreat);
            _threatLine.color = menace == 2 ? Danger : (menace == 0 ? Easy : Paper);

            SeaCombat.GradeOdds(tier, SeaCombat.SpyglassLuck(_sea.GearGrade(SeaCombat.SlotSpyglass)),
                                t, _odds);
            _lootText.Clear();
            _lootText.Append(Loc.T("deniz.ganimetSinif"));
            for (int g = 0; g < _odds.Length; g++)
                _lootText.Append(g == 0 ? "  " : " · ")
                         .Append("<color=#").Append(_gradeHex[g]).Append('>')
                         .Append(Odd(_odds[g])).Append("</color>");
            _lootText.Append('%');
            Push(_lootLine, _lootText.ToString(), ref _lastLoot);
        }

        /// <summary>
        /// The energy top-up's four states, in the order they gate the tap: a full pool, a spent
        /// day, a running cooldown, and only then the offer. The label always says WHY it is dark.
        /// </summary>
        private void RefreshEnergyAd(int have)
        {
            if (_energyAd == null) return;

            string label;
            bool ready;
            if (_free == null) { label = string.Empty; ready = false; }
            else if (have >= _sea.EnergyMax) { label = Loc.T("deniz.enerjiDolu"); ready = false; }
            else
            {
                int left = _free.ChargesLeft(EnergyAdId, energyAdChargesPerDay);
                float cooldown = _free.CooldownLeft(EnergyAdId, energyAdCooldownSeconds);
                if (left <= 0) { label = Loc.T("deniz.enerjiYarin"); ready = false; }
                else if (cooldown > 0f) { label = UiBuild.Clock(cooldown); ready = false; }
                else
                {
                    label = string.Format(Loc.T("deniz.enerjiEkle"), energyAdReward);
                    ready = _free.AdsRemoved || (_ad != null && _ad.Available);
                }
            }

            Push(_energyAdLabel, label, ref _lastEnergyAd);
            _energyAd.interactable = ready;
            _energyAd.targetGraphic.color = ready ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
        }

        /// <summary>A drop-table share as a percent. A share too small to round to a whole percent
        /// reads as "about none" rather than as a flat zero it is not.</summary>
        private static string Odd(double share)
        {
            int percent = Mathf.RoundToInt((float)(share * 100d));
            return percent <= 0 && share > 0d ? "~0" : percent.ToString();
        }

        // -------------------------------------------------------------- the cards
        /// <summary>The details card, filled from the sighting: the reference's Monster Details.</summary>
        private void FillFoundCard()
        {
            int kind = _fights.ThreatKind;
            SeaCombat.Stats them = _fights.ThreatSheet;

            _foundTitle.text = Loc.T("deniz.tehdit." + kind);

            int sig = SeaCombat.SignatureOf(kind);
            _foundTag.text = sig == SeaCombat.SecNone
                ? Loc.T("deniz.savunmasiz") : Loc.T(SecKey(sig)) + "  " + Pct(SigAmount(them, sig));
            _foundTag.color = sig == SeaCombat.SecNone ? Easy : SecTint(sig);

            int menace = _fights.MenaceLevel;
            _foundDanger.gameObject.SetActive(menace != 1);
            _foundDanger.text = menace == 2 ? Loc.T("deniz.tehlikeli") : Loc.T("deniz.kolay");
            _foundDanger.color = menace == 2 ? Danger : Easy;
            // An even match has no verdict line, so the power line moves up into its place rather
            // than leaving a hole between the chip and the stat block.
            RectTransform power = _foundPower.rectTransform;
            power.anchorMin = new Vector2(0.05f, menace != 1 ? 0.610f : 0.655f);
            power.anchorMax = new Vector2(0.95f, menace != 1 ? 0.695f : 0.760f);

            // The warning sign stands just left of the word, whatever the language made it.
            _foundDangerIcon.gameObject.SetActive(menace == 2);
            if (menace == 2)
            {
                float word = _foundDanger.GetPreferredValues(_foundDanger.text).x;
                var sign = _foundDangerIcon.rectTransform;
                sign.anchoredPosition = new Vector2(-(word * 0.5f + 14f + sign.sizeDelta.x * 0.5f), 0f);
            }

            _foundPower.text = Loc.T("deniz.guc") + " " + N(_fights.ThreatPower)
                             + "   ·   " + Loc.T("deniz.biz") + " " + N(_fights.OurPower);
            _foundPower.color = menace == 2 ? Danger : Paper;

            // The whole core block, and SÜRAT's verdict: whose ball goes up first.
            bool usFirst = _fights.UsOpensFirst;
            string opener = usFirst
                ? "<color=#8CEB8C>" + Loc.T("deniz.biz") + "</color>"
                : "<color=#FA5C52>" + Loc.T("deniz.onlar") + "</color>";
            _foundStats.text = Loc.T("deniz.govde") + "  " + N(them.Hull)
                             + "      " + Loc.T("deniz.slot.0") + "  " + N(them.Shot) + "\n"
                             + Loc.T("deniz.st.savunma") + "  " + D(them.Def)
                             + "      " + Loc.T("deniz.st.surat") + "  " + D(them.Spd) + "\n"
                             + Loc.T("deniz.ilkatis") + ": " + opener;
            _foundReward.text = Loc.T("deniz.odul") + ": " + Loc.T("deniz.odulsatir");
        }

        /// <summary>The compare card: what is worn beside what fell, row by row, delta on top.</summary>
        private void FillLootCard()
        {
            SeaCombat.Item drop = _fights.DropItem;
            SeaCombat.Item cur = _sea != null ? _sea.GearItem(drop.Slot) : new SeaCombat.Item { Grade = -1 };
            SeaCombat.Tuning t = _fights.Combat;
            Color tint = GradeTint[Mathf.Clamp(drop.Grade, 0, GradeTint.Length - 1)];

            // White on the teal title plate — a blue or purple grade would sink into it; the grade's
            // colour is carried by the NEW column's grade line below.
            _lootTitle.text = Loc.T("kaptan.derece." + drop.Grade) + "  ·  " + Loc.T("deniz.slot." + drop.Slot);

            int delta = SeaCombat.ItemScore(drop, t) - (_sea != null ? _sea.GearScore(drop.Slot) : 0);
            _lootDelta.text = Loc.T("deniz.guc") + "  " + (delta >= 0 ? "+" : "") + delta;
            _lootDelta.color = delta >= 0 ? Easy : Danger;

            if (cur.Grade < 0)
            {
                _curGrade.text = Loc.T("deniz.bos");
                _curGrade.color = Faded;
                _curRows.text = string.Empty;
                _curFrame.color = InsetEmpty;
            }
            else
            {
                _curGrade.text = Loc.T("kaptan.derece." + cur.Grade);
                _curGrade.color = GradeTint[Mathf.Clamp(cur.Grade, 0, GradeTint.Length - 1)];
                _curRows.text = ItemRows(cur, cur, false);
                _curFrame.color = InsetTint;
            }

            _newGrade.text = Loc.T("kaptan.derece." + drop.Grade);
            _newGrade.color = tint;
            _newRows.text = ItemRows(drop, cur, true);
            _newFrame.color = InsetTint;

            _scrapLabel.text = string.Format(Loc.T("deniz.sok"), SeaCombat.ScrapFor(drop.Grade));
        }

        /// <summary>An item's five rows — the whole core block and the secondary. On the NEW side
        /// each row is tinted by how it compares — the reference game's red and green arrows,
        /// done in ink.</summary>
        private string ItemRows(in SeaCombat.Item item, in SeaCombat.Item against, bool compare)
        {
            string up = "<color=#8CEB8C>", down = "<color=#FA5C52>", end = "</color>";
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
                    sec = (SecWorth(item) >= curSec ? up : down) + sec + end;
            }
            return hull + "\n" + shot + "\n" + def + "\n" + spd + "\n" + sec;
        }

        private static double SecWorth(in SeaCombat.Item item)
            => item.Sec == SeaCombat.SecNone ? 0d : item.SecAmt;

        /// <summary>The worn-item popup: the sheet slot's own card.</summary>
        private void FillGearCard()
        {
            SeaCombat.Item item = _sea.GearItem(_gearShown);
            Sprite icon = _slotIcon[_gearShown];
            _gearCardIcon.sprite = icon != null ? icon : UiSkin.Flat;
            _gearCardIcon.color = item.Grade < 0 ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            if (item.Grade < 0)
            {
                _gearTitle.text = Loc.T("deniz.slot." + _gearShown);
                _gearTitle.color = Faded;
                _gearRows.text = Loc.T("deniz.bos") + "\n\n" + Loc.T("deniz.dusmandanduser");
                _gearScrap.gameObject.SetActive(false);
                return;
            }
            // White on the plate, as on the compare card; the grade shows in the rows' own tint.
            _gearTitle.text = Loc.T("kaptan.derece." + item.Grade) + "  ·  " + Loc.T("deniz.slot." + _gearShown);
            _gearTitle.color = Paper;
            _gearRows.text = Loc.T("deniz.guc") + "  " + _sea.GearScore(_gearShown) + "\n"
                           + ItemRows(item, item, false);
            _gearScrap.gameObject.SetActive(true);
            _gearScrapLabel.text = string.Format(Loc.T("deniz.sok"), SeaCombat.ScrapFor(item.Grade));
        }

        /// <summary>Which chrome belongs to which phase — never two decisions at once. The sheet
        /// panel stays; only the stage's cards trade places.</summary>
        private void SetChrome(EncounterController.Phase phase)
        {
            _foundCard.gameObject.SetActive(phase == EncounterController.Phase.Found);
            _lootCard.gameObject.SetActive(phase == EncounterController.Phase.Loot);
            if (phase != EncounterController.Phase.Idle) OnGearClose();
            RefreshScrim();
        }

        // ---------------------------------------------------------------- helpers
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

        private static Color SecTint(int sec)
        {
            switch (sec)
            {
                case SeaCombat.SecCrit:    return CritTint;
                case SeaCombat.SecDodge:   return Faded;
                case SeaCombat.SecStun:    return StunTint;
                case SeaCombat.SecMend:    return MendTint;
                case SeaCombat.SecBurn:    return BurnTint;
                case SeaCombat.SecPlunder: return PlunderTint;
                case SeaCombat.SecSteal:   return StealTint;
                case SeaCombat.SecPoison:  return PoisonTint;
                default:                   return CritTint;
            }
        }

        private static double SigAmount(in SeaCombat.Stats s, int sec)
        {
            switch (sec)
            {
                case SeaCombat.SecCrit:  return s.Crit;
                case SeaCombat.SecDodge: return s.Dodge;
                case SeaCombat.SecStun:  return s.Stun;
                case SeaCombat.SecBurn:  return s.Burn;
                default:                 return 0d;
            }
        }

        /// <summary>Short number ink: whole below 1000, one-decimal k above.</summary>
        private static string N(double v)
        {
            if (v < 999.5d) return Mathf.RoundToInt((float)v).ToString();
            double k = v / 1000d;
            return k < 99.95d ? k.ToString("0.0") + "k" : Mathf.RoundToInt((float)k) + "k";
        }

        private static string Pct(double v) => Mathf.RoundToInt((float)(v * 100d)) + "%";

        /// <summary>Defence and speed ink: one decimal, because the items carry one.</summary>
        private static string D(double v) => (System.Math.Round(v * 10d) / 10d).ToString("0.#");

        private static void Push(TMP_Text label, string value, ref string last)
        {
            if (label == null || value == last) return;
            label.text = value;
            last = value;
        }

        private static TMP_Text Line(RectTransform parent, string name, float size, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * 0.45f;
            text.fontSizeMax = size;
            text.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return text;
        }
    }
}
