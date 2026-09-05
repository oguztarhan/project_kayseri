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
    /// Sprites from Resources/UI/Sea (Tools/ui/deniz_savas_seti.py); every slot falls back to the
    /// flat quad. Balls, flashes and floating texts are pooled — fixed arrays, zero allocation
    /// after Build.
    /// </summary>
    public sealed class SeaFightUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 102;   // above SeaHudUI's 100

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
        private static readonly Color SeaTint = new Color(0.09f, 0.30f, 0.46f, 1f);
        private static readonly Color HullFillTint = new Color(0.92f, 0.30f, 0.26f, 0.95f);
        private static readonly Color NerveFillTint = new Color(0.36f, 0.74f, 0.99f, 0.95f);
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

        private CanvasGroup _rootGroup;
        private RectTransform _root, _stage, _panel;
        private RawImage _horizonWaves, _frontWaves;
        private RectTransform[] _clouds;

        private RectTransform _shipRoot, _threatRoot;
        private CanvasGroup _threatGroup;
        private Image _threatImage;

        private RectTransform _hullTrack, _hullFill, _nerveTrack, _nerveFill;
        private TMP_Text _threatName, _banner;

        // The sheet panel.
        private TMP_Text _powerLabel, _captainLabel, _energyLabel;
        private readonly TMP_Text[] _statValue = new TMP_Text[StatCount];
        private readonly Image[] _gearFrame = new Image[SeaCombat.SlotCount];
        private readonly Image[][] _gearStars = new Image[SeaCombat.SlotCount][];
        private Button _search, _autoBtn;
        private TMP_Text _searchLabel, _autoLabel;
        private Image _autoImage;

        // The route strip and what it promises: which waters the fights are priced for, what a
        // locked route still wants, the threat band out there and the drop table it rolls.
        private readonly Button[] _routeBtn = new Button[Voyages.TierCount];
        private readonly Image[] _routeFrame = new Image[Voyages.TierCount];
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
        private Image _foundTagPill;

        // The loot compare card.
        private RectTransform _lootCard;
        private TMP_Text _lootTitle, _lootDelta;
        private Image _curFrame, _newFrame;
        private TMP_Text _curHead, _curGrade, _curRows, _newHead, _newGrade, _newRows;
        private TMP_Text _scrapLabel;

        // The worn-gear popup.
        private RectTransform _gearCard;
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

        private static readonly string[] KindSprite = { "korsan", "canavar", "enkaz", "alev", "hayalet" };

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

            // A full-width band between the sheet's top edge (0.545) and SeaHudUI's strip (0.885).
            _stage = UiBuild.Flat(_root, "Sahne", SkyTint, new Vector2(0f, 0.545f), new Vector2(1f, 0.885f));
            _stage.GetComponent<Image>().raycastTarget = false;

            BuildBackdrop();
            _shipRoot = Vessel("Gemi", S("gemi"), false);
            _threatRoot = Vessel("Tehdit", null, true);
            _threatGroup = _threatRoot.gameObject.AddComponent<CanvasGroup>();
            _threatGroup.alpha = 0f;
            _threatImage = _threatRoot.GetChild(0).GetComponent<Image>();
            BuildFrontWaves();
            BuildBars();
            BuildPools();

            _banner = Line(_stage, "Sonuc", 52f, new Vector2(0.10f, 0.68f), new Vector2(0.90f, 0.84f));
            _banner.fontStyle = FontStyles.Bold;

            BuildPanel();
            BuildFoundCard();
            BuildLootCard();
            BuildGearCard();
            SetChrome(EncounterController.Phase.Idle);
        }

        // ------------------------------------------------------------------ build
        private void BuildBackdrop()
        {
            RectTransform sea = UiBuild.Flat(_stage, "Deniz", SeaTint, Vector2.zero, new Vector2(1f, 0.46f));
            sea.GetComponent<Image>().raycastTarget = false;

            Sprite cloud = S("bulut");
            _clouds = new RectTransform[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Bulut" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_stage, false);
                var img = go.GetComponent<Image>();
                img.sprite = cloud != null ? cloud : UiSkin.Flat;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0.85f);
                _clouds[i] = UiBuild.Anchor((RectTransform)go.transform,
                    new Vector2(i == 0 ? 0.08f : 0.62f, i == 0 ? 0.80f : 0.87f),
                    new Vector2(i == 0 ? 0.24f : 0.80f, i == 0 ? 0.92f : 0.97f));
            }
            _horizonWaves = Waves("UfukDalga", new Vector2(0f, 0.42f), new Vector2(1f, 0.50f),
                                  new Color(1f, 1f, 1f, 0.9f));
        }

        private void BuildFrontWaves()
            => _frontWaves = Waves("OnDalga", new Vector2(0f, 0f), new Vector2(1f, 0.13f),
                                   new Color(0.75f, 0.85f, 0.95f, 1f));

        private RawImage Waves(string name, Vector2 aMin, Vector2 aMax, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(_stage, false);
            var raw = go.GetComponent<RawImage>();
            Sprite strip = S("dalga");
            raw.texture = strip != null ? strip.texture : null;
            raw.color = strip != null ? tint : new Color(SeaTint.r, SeaTint.g, SeaTint.b, 0.6f);
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 3f, 1f);
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return raw;
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

            _nerveTrack = BarTrack("Cesaret", out _nerveFill, NerveFillTint);
        }

        private RectTransform BarTrack(string name, out RectTransform fill, Color tint)
        {
            RectTransform track = UiBuild.Flat(_stage, name, new Color(0f, 0f, 0f, 0.55f),
                                               Vector2.zero, Vector2.zero);
            track.anchorMin = track.anchorMax = Vector2.zero;
            track.GetComponent<Image>().raycastTarget = false;
            fill = UiBuild.Flat(track, "Dolgu", tint, Vector2.zero, Vector2.one);
            fill.GetComponent<Image>().raycastTarget = false;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            return track;
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
            _panel = UiBuild.Flat(_root, "Levha", Chrome, new Vector2(0.015f, 0.020f), new Vector2(0.985f, 0.535f));
            var img = _panel.GetComponent<Image>();
            img.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;   // eats taps so the 3D scene never hears the sheet

            _powerLabel = Line(_panel, "Guc", 40f, new Vector2(0.05f, 0.930f), new Vector2(0.95f, 0.990f));
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
            for (int i = 0; i < CoreStatCount; i++)
            {
                float x0 = 0.035f + i * 0.2375f, x1 = x0 + 0.22f;
                TMP_Text label = Line(_panel, "Ist" + i, 15f, new Vector2(x0, 0.756f), new Vector2(x1, 0.792f));
                label.text = labels[i];
                label.color = Faded;
                _statValue[i] = Line(_panel, "Deger" + i, 24f, new Vector2(x0, 0.706f), new Vector2(x1, 0.754f));
                _statValue[i].fontStyle = FontStyles.Bold;
            }
            for (int i = CoreStatCount; i < StatCount; i++)
            {
                int col = (i - CoreStatCount) % 3, row = (i - CoreStatCount) / 3;
                float x0 = 0.04f + col * 0.315f, x1 = x0 + 0.30f;
                float y1 = 0.694f - row * 0.060f, y0 = y1 - 0.058f;
                TMP_Text label = Line(_panel, "Ist" + i, 14f, new Vector2(x0, y0 + 0.030f), new Vector2(x1, y1));
                label.text = labels[i];
                label.color = Faded;
                _statValue[i] = Line(_panel, "Deger" + i, 18f, new Vector2(x0, y0), new Vector2(x1, y0 + 0.032f));
                _statValue[i].fontStyle = FontStyles.Bold;
            }

            Sprite[] icons = { S("ikon_top"), S("ikon_zirh"), S("ikon_durbun"), S("ikon_tilsim") };
            Sprite star = S("yildiz");
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                int captured = slot;
                var go = new GameObject("Yuva" + slot, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_panel, false);
                var frame = go.GetComponent<Image>();
                frame.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
                frame.type = Image.Type.Sliced;
                float x0 = 0.035f + slot * 0.2375f;
                UiBuild.Anchor((RectTransform)go.transform, new Vector2(x0, 0.348f), new Vector2(x0 + 0.22f, 0.505f));
                _gearFrame[slot] = frame;

                var icon = new GameObject("Ikon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                var ii = icon.GetComponent<Image>();
                ii.sprite = icons[slot] != null ? icons[slot] : UiSkin.Flat;
                ii.preserveAspect = true;
                ii.raycastTarget = false;
                UiBuild.Anchor((RectTransform)icon.transform, new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.96f));

                _gearStars[slot] = new Image[SeaCombat.GradeMult.Length];
                for (int g = 0; g < _gearStars[slot].Length; g++)
                {
                    var st = new GameObject("Yildiz" + g, typeof(RectTransform), typeof(Image));
                    st.transform.SetParent(go.transform, false);
                    var si = st.GetComponent<Image>();
                    si.sprite = star != null ? star : UiSkin.Flat;
                    si.preserveAspect = true;
                    si.raycastTarget = false;
                    float sx = 0.10f + g * 0.165f;
                    UiBuild.Anchor((RectTransform)st.transform, new Vector2(sx, 0.05f), new Vector2(sx + 0.15f, 0.28f));
                    st.SetActive(false);
                    _gearStars[slot][g] = si;
                }

                var button = go.GetComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() => OnGearSlot(captured));
            }

            _captainLabel = Line(_panel, "Kaptan", 22f, new Vector2(0.04f, 0.284f), new Vector2(0.96f, 0.344f));

            RectTransform pill = UiBuild.Flat(_panel, "EnerjiHapi", Chrome,
                                              new Vector2(0.035f, 0.196f), new Vector2(0.70f, 0.276f));
            var pi = pill.GetComponent<Image>();
            pi.sprite = UiSkin.Pill != null ? UiSkin.Pill : UiSkin.Flat;
            pi.type = Image.Type.Sliced;
            pi.raycastTarget = false;
            PillFit.Wrap(pi);
            _energyLabel = Line(pill, "Yazi", 24f, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f));
            _energyLabel.color = EnergyTint;

            // The reference game's "extra stamina" grab, sat where the wait is read rather than
            // behind a popup: the pill says how long the pool takes, the button beside it says
            // what an ad would skip.
            _energyAd = PanelButton("EnerjiReklam", UiSkin.ButtonGreen, new Vector2(0.72f, 0.196f),
                                    new Vector2(0.965f, 0.276f), OnEnergyAd, out _energyAdLabel, 20f);

            _search = PanelButton("Ara", UiSkin.ButtonYellow, new Vector2(0.035f, 0.030f),
                                  new Vector2(0.645f, 0.180f), OnSearch, out _searchLabel, 28f);
            _autoBtn = PanelButton("Oto", UiSkin.ButtonGrey, new Vector2(0.685f, 0.030f),
                                   new Vector2(0.965f, 0.180f), OnAuto, out _autoLabel, 26f);
            _autoImage = _autoBtn.GetComponent<Image>();
            _autoLabel.text = Loc.T("deniz.oto");
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
            Sprite art = UiSkin.Pill != null ? UiSkin.Pill : UiSkin.Flat;
            for (int tier = 0; tier < Voyages.TierCount; tier++)
            {
                int captured = tier;
                var go = new GameObject("Rota" + tier, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_panel, false);
                var frame = go.GetComponent<Image>();
                frame.sprite = art;
                frame.type = Image.Type.Sliced;
                float x0 = 0.035f + tier * 0.2375f;
                UiBuild.Anchor((RectTransform)go.transform, new Vector2(x0, 0.858f),
                               new Vector2(x0 + 0.22f, 0.922f));
                PillFit.Wrap(frame);
                _routeFrame[tier] = frame;

                _routeMark[tier] = Line((RectTransform)go.transform, "Kademe", 22f,
                                        new Vector2(0.04f, 0.40f), new Vector2(0.96f, 0.97f));
                _routeMark[tier].fontStyle = FontStyles.Bold;
                _routeMark[tier].text = TierMark[tier];
                _routeSub[tier] = Line((RectTransform)go.transform, "Sart", 12f,
                                       new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.40f));
                _routeSub[tier].color = Faded;

                var button = go.GetComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() => OnRoute(captured));
                _routeBtn[tier] = button;
            }

            _threatLine = Line(_panel, "TehditBandi", 17f,
                               new Vector2(0.035f, 0.826f), new Vector2(0.965f, 0.856f));
            _lootLine = Line(_panel, "GanimetSinifi", 15f,
                             new Vector2(0.035f, 0.796f), new Vector2(0.965f, 0.824f));
            _lootLine.richText = true;
        }

        private Button PanelButton(string name, Sprite art, Vector2 aMin, Vector2 aMax,
                                   UnityEngine.Events.UnityAction onClick, out TMP_Text label, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel, false);
            var image = go.GetComponent<Image>();
            image.sprite = art != null ? art : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = UiSkin.HasArt ? Color.white : Chrome;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            PillFit.Wrap(image);
            label = Line((RectTransform)go.transform, "Yazi", size,
                         new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));
            var b = go.GetComponent<Button>();
            b.targetGraphic = image;
            b.onClick.AddListener(onClick);
            return b;
        }

        // ------------------------------------------------------------ the cards
        private RectTransform Card(string name, Vector2 aMin, Vector2 aMax)
        {
            RectTransform card = UiBuild.Flat(_root, name, Chrome, aMin, aMax);
            var img = card.GetComponent<Image>();
            img.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            // The card eats taps so nothing behind it can fire while the decision is open.
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            card.gameObject.SetActive(false);
            return card;
        }

        private Button CardButton(RectTransform card, string name, Sprite art, Vector2 aMin, Vector2 aMax,
                                  UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(card, false);
            var image = go.GetComponent<Image>();
            image.sprite = art != null ? art : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = UiSkin.HasArt ? Color.white : Chrome;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            PillFit.Wrap(image);
            label = Line((RectTransform)go.transform, "Yazi", 26f,
                         new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));
            var b = go.GetComponent<Button>();
            b.targetGraphic = image;
            b.onClick.AddListener(onClick);
            return b;
        }

        /// <summary>The details card — the reference game's Monster Details: who this is, what it
        /// does, whether it outguns us, and the one decision: SAVAŞ! or VAZGEÇ.</summary>
        private void BuildFoundCard()
        {
            _foundCard = Card("DetayKarti", new Vector2(0.06f, 0.31f), new Vector2(0.94f, 0.69f));

            _foundTitle = Line(_foundCard, "Baslik", 36f, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f));
            _foundTitle.fontStyle = FontStyles.Bold;

            RectTransform tag = UiBuild.Flat(_foundCard, "Imza", Chrome,
                                             new Vector2(0.28f, 0.755f), new Vector2(0.72f, 0.845f));
            _foundTagPill = tag.GetComponent<Image>();
            _foundTagPill.sprite = UiSkin.Pill != null ? UiSkin.Pill : UiSkin.Flat;
            _foundTagPill.type = Image.Type.Sliced;
            _foundTagPill.raycastTarget = false;
            PillFit.Wrap(_foundTagPill);
            _foundTag = Line(tag, "Yazi", 22f, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f));

            _foundDanger = Line(_foundCard, "Uyari", 30f, new Vector2(0.05f, 0.655f), new Vector2(0.95f, 0.745f));
            _foundDanger.fontStyle = FontStyles.Bold;

            _foundPower = Line(_foundCard, "Guc", 27f, new Vector2(0.05f, 0.545f), new Vector2(0.95f, 0.645f));
            _foundStats = Line(_foundCard, "Blok", 23f, new Vector2(0.07f, 0.315f), new Vector2(0.93f, 0.535f));
            _foundReward = Line(_foundCard, "Odul", 21f, new Vector2(0.05f, 0.225f), new Vector2(0.95f, 0.305f));
            _foundReward.color = Faded;

            TMP_Text fightLabel, passLabel;
            CardButton(_foundCard, "Savas", UiSkin.ButtonGreen, new Vector2(0.07f, 0.05f),
                       new Vector2(0.60f, 0.19f), OnConfirm, out fightLabel);
            fightLabel.text = Loc.T("deniz.savas");
            fightLabel.fontSize = 30f;
            CardButton(_foundCard, "Vazgec", UiSkin.ButtonGrey, new Vector2(0.64f, 0.05f),
                       new Vector2(0.93f, 0.19f), OnDecline, out passLabel);
            passLabel.text = Loc.T("deniz.vazgec");
        }

        /// <summary>The compare card — the reference game's Current Items / Drop Items, one card:
        /// the worn thing beside the dropped thing, row by row, delta on top.</summary>
        private void BuildLootCard()
        {
            _lootCard = Card("GanimetKarti", new Vector2(0.05f, 0.29f), new Vector2(0.95f, 0.71f));

            _lootTitle = Line(_lootCard, "Baslik", 32f, new Vector2(0.05f, 0.885f), new Vector2(0.95f, 0.975f));
            _lootTitle.fontStyle = FontStyles.Bold;
            _lootDelta = Line(_lootCard, "Fark", 27f, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.875f));
            _lootDelta.fontStyle = FontStyles.Bold;

            _curFrame = Column(new Vector2(0.05f, 0.24f), new Vector2(0.485f, 0.79f),
                               out _curHead, out _curGrade, out _curRows);
            _newFrame = Column(new Vector2(0.515f, 0.24f), new Vector2(0.95f, 0.79f),
                               out _newHead, out _newGrade, out _newRows);
            _curHead.text = Loc.T("deniz.mevcut");
            _newHead.text = Loc.T("deniz.yeni");

            TMP_Text equipLabel;
            CardButton(_lootCard, "Giydir", UiSkin.ButtonGreen, new Vector2(0.07f, 0.05f),
                       new Vector2(0.48f, 0.19f), OnEquip, out equipLabel);
            equipLabel.text = Loc.T("deniz.giydir");
            CardButton(_lootCard, "Sok", UiSkin.ButtonGrey, new Vector2(0.52f, 0.05f),
                       new Vector2(0.93f, 0.19f), OnScrap, out _scrapLabel);
        }

        private Image Column(Vector2 aMin, Vector2 aMax, out TMP_Text head, out TMP_Text grade, out TMP_Text rows)
        {
            RectTransform col = UiBuild.Flat(_lootCard, "Sutun", new Color(0f, 0f, 0f, 0.30f), aMin, aMax);
            var img = col.GetComponent<Image>();
            img.sprite = UiSkin.Panel != null ? UiSkin.Panel : UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
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
            _gearCard = Card("TakiKarti", new Vector2(0.14f, 0.32f), new Vector2(0.86f, 0.68f));
            _gearTitle = Line(_gearCard, "Baslik", 30f, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.96f));
            _gearTitle.fontStyle = FontStyles.Bold;
            _gearRows = Line(_gearCard, "Satirlar", 24f, new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.82f));
            _gearRows.alignment = TextAlignmentOptions.Top;

            TMP_Text closeLabel;
            _gearScrap = CardButton(_gearCard, "Sok", UiSkin.ButtonGrey, new Vector2(0.07f, 0.06f),
                                    new Vector2(0.52f, 0.22f), OnGearScrap, out _gearScrapLabel);
            CardButton(_gearCard, "Kapat", UiSkin.ButtonBlue, new Vector2(0.56f, 0.06f),
                       new Vector2(0.93f, 0.22f), OnGearClose, out closeLabel);
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
                _banner.text = string.Format(Loc.T("deniz.rotaKapali"), _sea.VoyagesToUnlock(tier));
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

            if (_horizonWaves.texture != null)
                _horizonWaves.uvRect = new Rect(t * 0.020f, 0f, 3f, 1f);
            if (_frontWaves.texture != null)
                _frontWaves.uvRect = new Rect(-t * 0.045f, 0f, 3.6f, 1f);
            for (int i = 0; i < _clouds.Length; i++)
                _clouds[i].anchoredPosition = new Vector2(Mathf.Sin(t * 0.05f + i * 2.4f) * 26f, 0f);

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
                Sprite art = S(KindSprite[Mathf.Clamp(kind, 0, KindSprite.Length - 1)]);
                if (art != null) _threatImage.sprite = art;
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
            _hullTrack.sizeDelta = new Vector2(w * 0.24f, h * 0.034f);
            _hullTrack.anchoredPosition = new Vector2(w * 0.74f, h * 0.66f);
            _hullFill.anchorMax = new Vector2(Mathf.Clamp01(hull), 1f);
            _threatName.rectTransform.anchoredPosition = new Vector2(w * 0.74f, h * 0.715f);

            if (!fighting) return;
            _nerveTrack.sizeDelta = new Vector2(w * 0.20f, h * 0.030f);
            _nerveTrack.anchoredPosition = new Vector2(w * 0.26f, h * 0.66f);
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
                    _autoImage.color = auto ? Easy : Color.white;
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
                _gearFrame[slot].color = grade < 0 ? new Color(0.35f, 0.40f, 0.48f, 0.9f)
                                                   : GradeTint[Mathf.Clamp(grade, 0, GradeTint.Length - 1)];
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
            if (_sea == null || _routeFrame[0] == null) return;
            int tier = _sea.Tier, max = _sea.MaxTier;
            if (tier == _routeSeenTier && max == _routeSeenMax) return;
            _routeSeenTier = tier;
            _routeSeenMax = max;

            for (int t = 0; t < Voyages.TierCount; t++)
            {
                bool open = t <= max;
                bool picked = open && t == tier;
                _routeFrame[t].color = picked ? EnergyTint
                                     : open ? new Color(0.42f, 0.48f, 0.58f, 1f)
                                            : new Color(0.22f, 0.26f, 0.33f, 1f);
                _routeMark[t].color = picked ? Chrome : (open ? Paper : Faded);
                _routeSub[t].text = open
                    ? string.Empty
                    : string.Format(Loc.T("deniz.rotaKilit"), _sea.VoyagesToUnlock(t));
                _routeSub[t].color = picked ? Chrome : Faded;
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
            _foundTagPill.color = sig == SeaCombat.SecNone ? Easy : SecTint(sig);

            int menace = _fights.MenaceLevel;
            _foundDanger.gameObject.SetActive(menace != 1);
            _foundDanger.text = menace == 2 ? Loc.T("deniz.tehlikeli") : Loc.T("deniz.kolay");
            _foundDanger.color = menace == 2 ? Danger : Easy;

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

            _lootTitle.text = Loc.T("kaptan.derece." + drop.Grade) + "  ·  " + Loc.T("deniz.slot." + drop.Slot);
            _lootTitle.color = tint;

            int delta = SeaCombat.ItemScore(drop, t) - (_sea != null ? _sea.GearScore(drop.Slot) : 0);
            _lootDelta.text = Loc.T("deniz.guc") + "  " + (delta >= 0 ? "+" : "") + delta;
            _lootDelta.color = delta >= 0 ? Easy : Danger;

            if (cur.Grade < 0)
            {
                _curGrade.text = Loc.T("deniz.bos");
                _curGrade.color = Faded;
                _curRows.text = string.Empty;
                _curFrame.color = new Color(0.30f, 0.34f, 0.42f, 0.85f);
            }
            else
            {
                _curGrade.text = Loc.T("kaptan.derece." + cur.Grade);
                _curGrade.color = GradeTint[Mathf.Clamp(cur.Grade, 0, GradeTint.Length - 1)];
                _curRows.text = ItemRows(cur, cur, false);
                _curFrame.color = new Color(0.16f, 0.20f, 0.28f, 0.95f);
            }

            _newGrade.text = Loc.T("kaptan.derece." + drop.Grade);
            _newGrade.color = tint;
            _newRows.text = ItemRows(drop, cur, true);
            _newFrame.color = new Color(0.16f, 0.20f, 0.28f, 0.95f);

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
            if (item.Grade < 0)
            {
                _gearTitle.text = Loc.T("deniz.slot." + _gearShown);
                _gearTitle.color = Faded;
                _gearRows.text = Loc.T("deniz.bos") + "\n\n" + Loc.T("deniz.dusmandanduser");
                _gearScrap.gameObject.SetActive(false);
                return;
            }
            _gearTitle.text = Loc.T("kaptan.derece." + item.Grade) + "  ·  " + Loc.T("deniz.slot." + _gearShown);
            _gearTitle.color = GradeTint[Mathf.Clamp(item.Grade, 0, GradeTint.Length - 1)];
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
