using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The bench card: tap a bench to open it. A built bench shows its stars, the way to the next star, and a button
    /// that buys one level, ten, or up to the next star (the toggle beside it); holding the button repeats, faster the
    /// longer it is held, and stops at every star. An offered, unbuilt bench shows its build once the one before it is
    /// levelled enough. Spends only through <see cref="MiningShopBusinessService"/>, and a hold is saved on release.
    /// Also turns the opening island shot onto the shop once the island camera has framed itself.
    /// </summary>
    [RequireComponent(typeof(MiningShopView))]
    public sealed class MiningShopUpgradeUI : MonoBehaviour
    {
        [Tooltip("Above the badges (92), building signs (99), HUD (100) and floating buttons (102); below every full " +
                 "screen (104 and up).")]
        [SerializeField] private int sortingOrder = 103;
        [Tooltip("Slack around the shop when the opening camera fits it to the screen. 1 = edge to edge.")]
        [SerializeField, Min(1f)] private float shopFitMargin = 1.15f;
        [Tooltip("Shop raised on screen by this share of the view height, clear of the bench panel below it.")]
        [SerializeField] private float shopScreenLift = 0.08f;
        [Tooltip("Downward tilt of the shop shot. Steeper than the island shot so the market roof does not hide the loop.")]
        [SerializeField, Range(40f, 89f)] private float shopPitch = 72f;
        [Tooltip("Açılışta ada kamerasını dükkân yakın planıyla değiştirir. Ana ekranda adanın tamamı " +
                 "görünsün diye varsayılan olarak kapalıdır.")]
        [SerializeField] private bool focusShopOnOpen;
        [Tooltip("Screen width the HUD's left button rail covers. The shop is fitted into the width between the rails.")]
        [SerializeField, Range(0f, 0.4f)] private float hudLeftFraction = 0.15f;
        [Tooltip("Screen width the HUD's right-hand buttons cover.")]
        [SerializeField, Range(0f, 0.4f)] private float hudRightFraction = 0.12f;
        [Tooltip("World size of the small Craft / Stock / Sell signs. Kept secondary to the people and goods.")]
        [SerializeField] private float signScale = 0.22f;
        [SerializeField] private Color signColor = Color.white;
        [SerializeField] private float tapSlopPixels = 24f;
        [SerializeField] private float refreshSeconds = 0.25f;
        [SerializeField] private Vector2 panelMin = new Vector2(0.06f, 0.17f);
        [SerializeField] private Vector2 panelMax = new Vector2(0.94f, 0.34f);
        [SerializeField] private Color panelColor = new Color(0.1f, 0.12f, 0.16f, 0.92f);
        [SerializeField] private Color buyColor = new Color(0.22f, 0.62f, 0.3f);
        [SerializeField] private Color closeColor = new Color(0.35f, 0.35f, 0.38f);
        [Header("Bench card")]
        [Tooltip("Basılı tutunca tekrar başlamadan önceki bekleme (sn).")]
        [SerializeField, Min(0f)] private float holdDelay = 0.35f;
        [Tooltip("Basılı tutmanın ilk hızı, saniyedeki alım.")]
        [SerializeField, Min(0.1f)] private float holdStartRate = 6f;
        [Tooltip("Basılı tutmanın en yüksek hızı, saniyedeki alım.")]
        [SerializeField, Min(0.1f)] private float holdMaxRate = 20f;
        [Tooltip("İlk hızdan en yüksek hıza çıkma süresi (sn).")]
        [SerializeField, Min(0.01f)] private float holdRampSeconds = 1.5f;
        [SerializeField] private Color starEarnedColor = Color.white;
        [SerializeField] private Color starMissingColor = new Color(0.22f, 0.24f, 0.3f, 0.85f);
        [SerializeField] private Color starBarTrack = new Color(0f, 0f, 0f, 0.35f);
        [SerializeField] private Color starBarFill = new Color(1f, 0.78f, 0.2f);
        [Tooltip("Kartın açık renkli kit panelindeki yazı rengi.")]
        [SerializeField] private Color cardTextColor = new Color(0.16f, 0.2f, 0.27f);
        [Header("Star moment")]
        [Tooltip("Yıldız afişinin ekranda kalma süresi (sn).")]
        [SerializeField, Min(0.3f)] private float starBannerSeconds = 1.8f;
        [Tooltip("Kazanılan yıldızın büyüyüp oturma süresi (sn).")]
        [SerializeField, Min(0.05f)] private float starPopSeconds = 0.5f;
        [Tooltip("Bir elmasın yıldızdan elmas sayacına uçuş süresi (sn).")]
        [SerializeField, Min(0.1f)] private float gemFlightSeconds = 0.7f;
        [SerializeField, Min(8f)] private float gemSize = 56f;
        [Tooltip("\"+N ELMAS\" yazısının yükselme mesafesi (px).")]
        [SerializeField] private float gainRise = 90f;
        [SerializeField] private Color gainColor = new Color(0.55f, 0.88f, 1f);
        [Header("Worker")]
        [Tooltip("Usta seçicinin ekrandaki yeri; kartın hemen üstü.")]
        [SerializeField] private Vector2 pickerMin = new Vector2(0.06f, 0.35f);
        [SerializeField] private Vector2 pickerMax = new Vector2(0.94f, 0.72f);
        [Tooltip("Seçicide yan yana kaç usta.")]
        [SerializeField, Min(1)] private int pickerColumns = 5;
        [SerializeField] private Color workerChipColor = new Color(0.3f, 0.34f, 0.42f);
        [Header("Perfect sale")]
        [Tooltip("\"MÜKEMMEL!\" yazısının ekranda kalma süresi (sn).")]
        [SerializeField, Min(0.3f)] private float perfectSeconds = 1.2f;
        [Tooltip("Yazının müşterinin başından yükselme mesafesi (px).")]
        [SerializeField] private float perfectRise = 120f;
        [Tooltip("Her mükemmel satışta fırlayan sikke sayısı (en fazla 10).")]
        [SerializeField, Range(0, 10)] private int perfectCoins = 6;
        [SerializeField, Min(8f)] private float perfectCoinSize = 44f;
        [Tooltip("Sikkelerin ilk hızı (px/sn).")]
        [SerializeField] private float perfectCoinSpeed = 520f;
        [Tooltip("Sikkeleri aşağı çeken yerçekimi (px/sn²); negatif = aşağı.")]
        [SerializeField] private float perfectCoinGravity = -1400f;
        [SerializeField] private Color perfectTitleColor = new Color(1f, 0.84f, 0.22f);
        [SerializeField] private Color perfectCashColor = Color.white;
        [Header("Tips")]
        [Tooltip("Bahşiş yazısının ekranda kalma süresi (sn).")]
        [SerializeField, Min(0.3f)] private float tipSeconds = 1.3f;
        [SerializeField] private float tipRise = 110f;
        [Tooltip("Bahşiş, büyük bahşiş, dev bahşiş: yazının boyu.")]
        [SerializeField] private float[] tipScales = { 0.8f, 1f, 1.25f };
        [Tooltip("Bahşiş, büyük bahşiş, dev bahşiş: yazıdan fırlayan sikke sayısı (en fazla 10).")]
        [SerializeField] private int[] tipBurstCoins = { 2, 4, 8 };
        [Tooltip("Bahşiş, büyük bahşiş, dev bahşiş: başlık rengi.")]
        [SerializeField] private Color[] tipTitleColors =
        {
            new Color(0.55f, 0.95f, 0.55f), new Color(1f, 0.84f, 0.22f), new Color(1f, 0.62f, 0.12f)
        };
        [SerializeField] private Color tipCashColor = Color.white;
        [Tooltip("Mükemmel satışta bahşiş yazısı, mükemmel yazısından bu kadar sonra çıkar (sn) ve aşağıdaki kadar altında durur.")]
        [SerializeField, Min(0f)] private float tipDelayAfterPerfect = 0.45f;
        [SerializeField] private Vector2 tipOffsetAfterPerfect = new Vector2(0f, -110f);
        [Tooltip("Bahşiş, büyük bahşiş, dev bahşiş: kasadan para sayacına uçan sikke sayısı.")]
        [SerializeField] private int[] tipFlightCoins = { 2, 3, 5 };
        [SerializeField, Min(8f)] private float tipCoinSize = 40f;
        [Tooltip("Bir sikkenin para sayacına uçuş süresi (sn).")]
        [SerializeField, Min(0.1f)] private float tipFlightSeconds = 0.7f;
        [Tooltip("Dev bahşişte kameranın sarsılması; hareket azaltma açıkken hiç sarsılmaz.")]
        [SerializeField, Min(0f)] private float tipShakeAmplitude = 0.12f;
        [SerializeField, Min(0f)] private float tipShakeSeconds = 0.25f;
        [Header("Goal card")]
        [Tooltip("Sonraki tezgâh yıldızı kartının yatay yeri (ekran genişliğinin payı).")]
        [SerializeField] private float goalLeft = 0.025f;
        [SerializeField] private float goalRight = 0.56f;
        [Tooltip("Kartın üstten uzaklığı ve yüksekliği (tuval birimi). Ekran payı değil: SATILDI hapı da üstten sabit " +
                 "uzaklıkta durur, 3:4'te payla konan kart onun üstüne biniyordu.")]
        [SerializeField, Min(0f)] private float goalTop = 196f;
        [SerializeField, Min(40f)] private float goalHeight = 124f;
        [Tooltip("Kartın yeni hedefe bakma aralığı (sn).")]
        [SerializeField, Min(0.25f)] private float goalRefreshSeconds = 1f;

        /// <summary>Panel titles in MiningShopCampaign.ProductIdAt order.</summary>
        private static readonly string[] TitleKeys =
        {
            "maden_dukkani.kazma_tezgahi", "maden_dukkani.kask_tezgahi",
            "maden_dukkani.fener_tezgahi", "maden_dukkani.canta_tezgahi"
        };

        /// <summary>Tip pop titles in ShopTips tier order.</summary>
        private static readonly string[] TipTitleKeys =
        {
            "maden_dukkani.bahsis", "maden_dukkani.bahsis_buyuk", "maden_dukkani.bahsis_dev"
        };

        /// <summary>A bench's name key, for screens that say where a master works.</summary>
        public static string BenchTitleKey(int product) => TitleKeys[product];

        private readonly System.Collections.Generic.List<Text> _signLabels = new System.Collections.Generic.List<Text>(3);
        private readonly System.Collections.Generic.List<string> _signKeys = new System.Collections.Generic.List<string>(3);
        private LocalizationService _loc;

        private MiningShopView _view;
        private MiningShopBusinessService _shop;
        private HudUI _hud;
        private BenchStarFx _starFx;
        private PerfectSaleFx _perfectFx;
        private PerfectSaleFx _tipFx;
        private TipCoinFlight _tipFlight;
        private ConfettiBurst _tipConfetti;
        private RectTransform _tipConfettiRect;
        private BenchGoalUI _goal;
        private MarketService _market;
        private int _product;
        private Text _title;
        private WalletService _wallet;
        private OperationCameraBoot _boot;
        private CameraController _cameraController;
        private Camera _camera;
        private bool _framed;
        private RectTransform _panel;
        private Text _summary, _buyText, _nextStar, _modeText;
        private Button _buy, _mode;
        private RectTransform _starBar, _starBarFill;
        private readonly Image[] _stars = new Image[BenchMastery.StarCount];
        private ForemanService _foremen;
        private ForemanRosterUI _roster;
        private Button _worker;
        private Image _workerFace;
        private Text _workerText;
        // The picker: one cell per master, built once and laid out on open with the owned ones only.
        private RectTransform _picker;
        private Text _pickerTitle, _pickerEmpty;
        private readonly Button[] _cell = new Button[Foremen.Count];
        private readonly Image[] _cellFace = new Image[Foremen.Count];
        private readonly Text[] _cellName = new Text[Foremen.Count];
        private readonly Text[] _cellValue = new Text[Foremen.Count];
        private readonly Text[] _cellBench = new Text[Foremen.Count];
        private readonly int[] _order = new int[Foremen.Count];
        private Vector2 _pressAt;
        private float _refresh;

        // How much one press buys: one level, ten, or up to the next star. Every press stops at a star either way.
        private const int ModeOne = 0, ModeTen = 1, ModeStar = 2;
        private static readonly string[] ModeLabels = { "x1", "x10", null };
        private int _modeIndex;

        // What the card last showed. It is rewritten only when one of these moves, so an open card that is only
        // watching sales allocates nothing but the sold count.
        private int _shownProduct = -1, _shownLevel, _shownMode, _shownAffordable;
        private long _shownSold = -1;
        private bool _shownBuilt, _shownBlocked, _shownRequirement;
        private int _shownRequiredLevel = -1;
        private int _shownWorker = -2;
        private double _shownWorkerMultiplier;

        /// <summary>Whether the player has the product upgrade panel open.</summary>
        public bool TutorialPanelOpen => _panel != null && _panel.gameObject.activeSelf;
        /// <summary>Which bench the open panel is for — 0 the pickaxe bench, 1 the second.</summary>
        public int TutorialProduct => _product;
        /// <summary>The panel's one action: the next level on a built bench, the build on an unbuilt one.</summary>
        public RectTransform TutorialBuyRect => _buy != null ? _buy.transform as RectTransform : null;
        public RectTransform TutorialPanelRect => _panel;

        private void Awake()
        {
            _view = GetComponent<MiningShopView>();
        }

        private void Start()
        {
            _market = ServiceLocator.Get<MarketService>();
            _shop = _market != null ? _market.MiningShopBusiness : null;
            _wallet = ServiceLocator.Get<WalletService>();
            if (_shop == null || _wallet == null) { enabled = false; return; }

            _boot = FindAnyObjectByType<OperationCameraBoot>();
            _cameraController = FindAnyObjectByType<CameraController>();
            _camera = Camera.main;
            _hud = FindAnyObjectByType<HudUI>();
            _foremen = ServiceLocator.Get<ForemanService>();
            _roster = FindAnyObjectByType<ForemanRosterUI>(FindObjectsInactive.Include);
            Build();
            _shop.StarPaid += OnStarPaid;
            _shop.WorkersChanged += OnWorkersChanged;
            _market.MiningShopBusinessSold += OnSold;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
            if (_shop != null) _shop.StarPaid -= OnStarPaid;
            if (_shop != null) _shop.WorkersChanged -= OnWorkersChanged;
            if (_market != null) _market.MiningShopBusinessSold -= OnSold;
        }

        /// <summary>
        /// A perfect sale pops over the customer who made it, with what it paid; plain sales show nothing. A tip pops
        /// there too, after the perfect pop when the sale was both, and its coins fly into the cash counter.
        /// </summary>
        private void OnSold(MiningShopBusinessSimulation.Sale sale)
        {
            if (sale.Perfect && _perfectFx != null)
                _perfectFx.Play(_view.SalePoint, "+$" + NumberFormatter.Format(new BigDouble(sale.Cash)) + "  ×" +
                                _shop.PerfectMultiplier.ToString("0.#"), _camera);
            if (sale.TipTier != ShopTips.None) PlayTip(sale);
        }

        /// <summary>The tip's moment. The wallet already holds it; this is feedback only.</summary>
        private void PlayTip(in MiningShopBusinessSimulation.Sale sale)
        {
            int tier = Mathf.Clamp(sale.TipTier, 0, ShopTips.TierCount - 1);
            bool huge = tier == ShopTips.TierCount - 1;
            float delay = sale.Perfect ? tipDelayAfterPerfect : 0f;
            Vector3 at = _view.SalePoint;
            if (_tipFx != null)
                _tipFx.Play(at, Loc.T(TipTitleKeys[tier]), Pick(tipTitleColors, tier, perfectTitleColor),
                    "+$" + NumberFormatter.Format(new BigDouble(sale.Tip)), _camera, delay, Pick(tipScales, tier, 1f),
                    Pick(tipBurstCoins, tier, 0), sale.Perfect ? tipOffsetAfterPerfect : Vector2.zero);
            if (_tipFlight != null)
                _tipFlight.Play(at, _camera, _hud != null ? _hud.CashCounter : null, Pick(tipFlightCoins, tier, 0), delay);

            if (huge)
            {
                AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
                bool still = accessibility != null && accessibility.ReduceMotion;
                if (!still && _tipConfetti != null && _camera != null)
                {
                    Vector3 screen = _camera.WorldToScreenPoint(at);
                    if (screen.z > 0f &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(_tipConfettiRect, screen, null, out Vector2 local))
                        _tipConfetti.PlayAt(local);
                }
                if (!still) CameraShake.Request(tipShakeAmplitude, tipShakeSeconds);
                ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
                ServiceLocator.Get<HapticService>()?.Medium();
                return;
            }
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Coin);
            ServiceLocator.Get<HapticService>()?.Light();
        }

        /// <summary>A tier's entry from an Inspector list, or <paramref name="fallback"/> when the list is short.</summary>
        private static T Pick<T>(T[] values, int tier, T fallback)
            => values != null && tier < values.Length ? values[tier] : fallback;

        /// <summary>A worker moved or got better: the open card and the open picker show it straight away.</summary>
        private void OnWorkersChanged()
        {
            if (_panel.gameObject.activeSelf) Refresh();
            if (_picker.gameObject.activeSelf) LayoutPicker();
        }

        /// <summary>
        /// A star's gems are already in the wallet and on disk; this is the moment itself. The star pops on the card
        /// when the card is showing that bench, and the bench bounces on the island either way.
        /// </summary>
        private void OnStarPaid(int product, int star, long gems)
        {
            _view.PulseBench(product);
            bool showing = _panel.gameObject.activeSelf && product == _product;
            string banner = string.Format(Loc.T("maden_dukkani.yildiz_kazanildi"), star + 1, StarEffectText(star));
            _starFx.Play(showing ? _stars[star].rectTransform : null, banner,
                string.Format(Loc.T("reklam.elmas"), gems), gems, _hud != null ? _hud.GemsCounter : null);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        /// <summary>The signs are drawn once when the camera frames the shop, so they are rewritten here.</summary>
        private void OnLanguageChanged()
        {
            for (int i = 0; i < _signLabels.Count; i++) _signLabels[i].text = Loc.T(_signKeys[i]);
            if (_perfectFx != null) _perfectFx.SetTitle(Loc.T("maden_dukkani.mukemmel"));
            if (_goal != null) _goal.MarkDirty();
            _shownProduct = -1;
            if (_panel != null && _panel.gameObject.activeSelf) Refresh();
            if (_picker == null) return;
            _pickerTitle.text = Loc.T("maden_dukkani.usta_sec");
            _pickerEmpty.text = Loc.T("maden_dukkani.usta_yok");
            if (_picker.gameObject.activeSelf) LayoutPicker();
        }

        private void Update()
        {
            if (!_framed)
            {
                if (focusShopOnOpen) FrameShop();
                else _framed = true;
            }

            Pointer pointer = Pointer.current;
            if (pointer != null)
            {
                Vector2 at = pointer.position.ReadValue();
                if (pointer.press.wasPressedThisFrame) _pressAt = at;
                else if (pointer.press.wasReleasedThisFrame &&
                         (at - _pressAt).sqrMagnitude < tapSlopPixels * tapSlopPixels &&
                         !CameraController.PointerOverUI())
                    TapWorld(at);
            }

            if (!_panel.gameObject.activeSelf) return;
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh > 0f) return;
            _refresh = refreshSeconds;
            Refresh();
        }

        /// <summary>After the island shot is solved, so the two never ease the camera to different places.</summary>
        private void FrameShop()
        {
            if (_view.TableCollider == null || _camera == null) return;
            if (_boot != null && !_boot.Framed) return;
            Bounds shop = _view.ShopBounds;
            Quaternion rot = Quaternion.Euler(shopPitch, _camera.transform.eulerAngles.y, 0f);
            float vTan = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float hTan = vTan * Mathf.Max(0.1f, _camera.aspect);
            // Portrait runs out of width first, and the HUD rails take some of it; ground depth is
            // foreshortened by the pitch.
            float usable = Mathf.Max(0.2f, 1f - hudLeftFraction - hudRightFraction);
            float pitchDepth = Mathf.Abs((rot * Vector3.forward).y);
            float dist = Mathf.Max(shop.extents.x / (hTan * usable), shop.extents.z * pitchDepth / vTan) * shopFitMargin;
            // Camera left moves the shop right: centre it between the rails, then lift it clear of the panel.
            Vector3 pos = shop.center - rot * Vector3.forward * dist
                        - rot * Vector3.right * ((hudLeftFraction - hudRightFraction) * dist * hTan)
                        - rot * Vector3.up * (shopScreenLift * 2f * dist * vTan);
            if (_cameraController != null) _cameraController.FrameTo(pos, rot, dist);
            else _camera.transform.SetPositionAndRotation(pos, rot);
            _framed = true;
            BuildSigns(rot);
        }

        /// <summary>
        /// One static world-space canvas of station names, turned to the shop camera. From a phone's distance a
        /// table, a pallet and a counter are just shapes; the names are what make the loop readable.
        /// </summary>
        private void BuildSigns(Quaternion facing)
        {
            var go = new GameObject("MiningShopSigns", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)go.transform;
            // Craft and Sell sit above heads. Stock stays low (at the shop pitch a tall sign over the rack lands on the
            // table behind it) and on the rack's outer side, away from the carrier's route out of it.
            Sign(canvas, "Shop_Pickaxe_Work", new Vector3(0f, 95f, 0f), "maden_dukkani.uret", facing);
            Sign(canvas, "Shop_Pickaxe_Output", new Vector3(24f, 22f, 0f), "maden_dukkani.stok", facing);
            Sign(canvas, "Shop_Market_Shelf", new Vector3(0f, 55f, 0f), "maden_dukkani.sat", facing);
        }

        private void Sign(RectTransform canvas, string anchor, Vector3 offset, string key, Quaternion facing)
        {
            Transform at = _view.transform.Find(anchor);
            if (at == null) return;
            Text label = UiBuild.Label(canvas, "Sign", Loc.T(key), 48, TextAnchor.MiddleCenter);
            _signLabels.Add(label);
            _signKeys.Add(key);
            label.color = signColor;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(700f, 80f);
            rt.SetPositionAndRotation(at.position + offset, facing);
            rt.localScale = Vector3.one * signScale;
        }

        /// <summary>Opens the card on a bench, as tapping it on the island does; the goal card's tap.</summary>
        public void OpenBench(int product)
        {
            if (product < 0 || product >= MiningShopCampaign.ProductCount) return;
            _product = product;
            _panel.gameObject.SetActive(true);
            _picker.gameObject.SetActive(false);
            Refresh();
        }

        private void TapWorld(Vector2 screen)
        {
            if (_camera == null || _view.TableCollider == null) return;
            if (!Physics.Raycast(_camera.ScreenPointToRay(screen), out RaycastHit hit, _camera.farClipPlane)) return;
            if (!_view.TryGetProduct(hit.collider, out int product, out _)) return;
            // Tapping the bench whose panel is already open closes it; any other bench switches to that one.
            bool open = !(_panel.gameObject.activeSelf && product == _product);
            _product = product;
            _panel.gameObject.SetActive(open);
            _picker.gameObject.SetActive(false);
            if (open) Refresh();
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "MiningShopCanvas", sortingOrder);
            _panel = UiBuild.Box(canvas, "BenchPanel", panelColor, panelMin, panelMax);
            // Everything a purchase changes lives on its own canvas, so a hold rebuilds only these, not the card's frame.
            var dynamicGo = new GameObject("Dinamik", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            dynamicGo.transform.SetParent(_panel, false);
            RectTransform content = UiBuild.Anchor((RectTransform)dynamicGo.transform, Vector2.zero, Vector2.one);

            _title = UiBuild.Label(content, "Title", string.Empty, 40, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_title.rectTransform, new Vector2(0.05f, 0.78f), new Vector2(0.545f, 0.96f));

            // The five stars, earned ones lit. The kit's star is the sea screen's; it reads the same here.
            Sprite star = SeaKit.Get("yildiz");
            const float starLeft = 0.56f, starPitch = 0.058f;
            for (int i = 0; i < _stars.Length; i++)
            {
                var go = new GameObject("Yildiz" + i, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(content, false);
                Image img = go.GetComponent<Image>();
                img.sprite = star != null ? star : UiSkin.Flat;
                img.preserveAspect = true;
                img.raycastTarget = false;
                float x = starLeft + i * starPitch;
                UiBuild.Anchor((RectTransform)go.transform, new Vector2(x, 0.79f), new Vector2(x + starPitch * 0.9f, 0.95f));
                _stars[i] = img;
            }

            // Skinned sprites: with a skin wired, Btn leaves the image white, and a white label on the flat
            // fallback sprite disappears.
            Button close = UiBuild.Btn(content, "Close", "X", UiSkin.ButtonGrey, closeColor, 40, ClosePanel);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.86f, 0.76f), new Vector2(0.97f, 0.96f));

            _nextStar = UiBuild.Label(content, "NextStar", string.Empty, 28, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_nextStar.rectTransform, new Vector2(0.05f, 0.63f), new Vector2(0.95f, 0.77f));
            // The longest lines (Polish star line, Portuguese requirement) reach the card's edge at full size.
            FitText(_nextStar, 16, 28);
            _starBar = UiBuild.Bar(content, "StarBar", starBarTrack, starBarFill, new Vector2(0.05f, 0.54f),
                new Vector2(0.95f, 0.61f), out _starBarFill);

            _summary = UiBuild.Label(content, "Summary", string.Empty, 28, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_summary.rectTransform, new Vector2(0.05f, 0.38f), new Vector2(0.60f, 0.52f));
            FitText(_summary, 16, 28);

            // Who works the bench: his face and what he adds. Tapping it opens the picker.
            _worker = UiBuild.Btn(content, "Usta", string.Empty, UiSkin.ButtonBlue, workerChipColor, 26, OpenPicker);
            UiBuild.Anchor((RectTransform)_worker.transform, new Vector2(0.62f, 0.37f), new Vector2(0.96f, 0.53f));
            _workerText = _worker.GetComponentInChildren<Text>();
            UiBuild.Anchor(_workerText.rectTransform, new Vector2(0.30f, 0f), new Vector2(0.97f, 1f));
            FitText(_workerText, 14, 26);
            _workerFace = Face((RectTransform)_worker.transform, "Portre", new Vector2(0.04f, 0.06f), new Vector2(0.28f, 0.94f));

            _mode = UiBuild.Btn(content, "Mode", ModeLabels[0], UiSkin.ButtonBlue, closeColor, 32, NextMode);
            UiBuild.Anchor((RectTransform)_mode.transform, new Vector2(0.04f, 0.06f), new Vector2(0.26f, 0.35f));
            _modeText = _mode.GetComponentInChildren<Text>();

            // No click listener: HoldRepeat takes the press, so a long hold never also buys once more on release.
            _buy = UiBuild.Btn(content, "Buy", string.Empty, UiSkin.ButtonGreen, buyColor, 30, null);
            UiBuild.Anchor((RectTransform)_buy.transform, new Vector2(0.29f, 0.06f), new Vector2(0.96f, 0.35f));
            _buyText = _buy.GetComponentInChildren<Text>();
            _buy.gameObject.AddComponent<HoldRepeat>().Configure(Step, _shop.FlushSave,
                holdDelay, holdStartRate, holdMaxRate, holdRampSeconds);

            // The kit panel is light; the fallback box is dark and keeps white text.
            if (UiSkin.HasArt) _title.color = _nextStar.color = _summary.color = cardTextColor;

            // After the panel, so the moment draws over the card; before the inset, so it moves with it.
            _starFx = BenchStarFx.Create(canvas, cardTextColor, gainColor, gemSize);
            _starFx.Configure(starBannerSeconds, starPopSeconds, gemFlightSeconds, gainRise);
            _perfectFx = PerfectSaleFx.Create(canvas, Loc.T("maden_dukkani.mukemmel"), perfectTitleColor,
                perfectCashColor, perfectCoinSize);
            _perfectFx.Configure(perfectSeconds, perfectRise, perfectCoins, perfectCoinSpeed, perfectCoinGravity);
            // Under the card: the shop sits behind it in the island shot, and a pop must not cover the card's text.
            _perfectFx.transform.SetSiblingIndex(0);
            _tipFx = PerfectSaleFx.Create(canvas, string.Empty, perfectTitleColor, tipCashColor, perfectCoinSize);
            _tipFx.Configure(tipSeconds, tipRise, 0, perfectCoinSpeed, perfectCoinGravity);
            _tipFx.transform.SetSiblingIndex(1);
            // Over the card: the coins leave the counter for the HUD's cash counter.
            _tipFlight = TipCoinFlight.Create(canvas, tipCoinSize);
            _tipFlight.Configure(tipFlightSeconds, 0.06f, 140f, 30f);
            // Its own canvas: eighty moving pieces would otherwise rebuild the card with them every frame. Under the
            // tip pop, which it bursts from, so the paper never hides the words.
            var confetti = new GameObject("BahsisKonfeti", typeof(RectTransform), typeof(Canvas));
            confetti.transform.SetParent(canvas, false);
            _tipConfettiRect = UiBuild.Anchor((RectTransform)confetti.transform, Vector2.zero, Vector2.one);
            _tipConfetti = confetti.AddComponent<ConfettiBurst>();
            _tipConfetti.transform.SetSiblingIndex(1);
            _goal = BenchGoalUI.Create(canvas, _shop, this, goalLeft, goalRight, goalTop, goalHeight);
            _goal.Configure(goalRefreshSeconds);

            BuildPicker(canvas);

            _panel.gameObject.SetActive(false);
            // The card sits on the bottom edge, so it moves inside the notch and gesture bar with the rest.
            UiBuild.InsetContent(canvas);
        }

        private void ClosePanel()
        {
            _panel.gameObject.SetActive(false);
            _picker.gameObject.SetActive(false);
        }

        /// <summary>
        /// The master picker: a titled box over the card with one cell per master. Every cell exists from the start and
        /// only the owned ones are placed when it opens, so opening it allocates nothing but the labels it rewrites.
        /// </summary>
        private void BuildPicker(RectTransform canvas)
        {
            _picker = UiBuild.Box(canvas, "UstaSecici", panelColor, pickerMin, pickerMax);
            _pickerTitle = UiBuild.Label(_picker, "Baslik", Loc.T("maden_dukkani.usta_sec"), 36, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_pickerTitle.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.8f, 0.97f));
            Button close = UiBuild.Btn(_picker, "Close", "X", UiSkin.ButtonGrey, closeColor, 36,
                () => _picker.gameObject.SetActive(false));
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.86f, 0.86f), new Vector2(0.97f, 0.98f));
            _pickerEmpty = UiBuild.Label(_picker, "Bos", Loc.T("maden_dukkani.usta_yok"), 28, TextAnchor.MiddleCenter);
            UiBuild.Anchor(_pickerEmpty.rectTransform, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.8f));
            _pickerEmpty.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (UiSkin.HasArt) _pickerTitle.color = _pickerEmpty.color = cardTextColor;

            for (int m = 0; m < Foremen.Count; m++)
            {
                int master = m;
                Button cell = UiBuild.Btn(_picker, "Usta" + m, string.Empty, UiSkin.Flat, Color.white, 20, () => PickWorker(master));
                // Flat keeps its colour under a skin too: the cell is his rarity, as his roster card is.
                cell.GetComponent<Image>().color = _foremen != null ? _foremen.RarityTintOf(m) : workerChipColor;
                RectTransform rt = (RectTransform)cell.transform;
                _cellFace[m] = Face(rt, "Portre", new Vector2(0.15f, 0.36f), new Vector2(0.85f, 0.97f));
                _cellName[m] = cell.GetComponentInChildren<Text>();
                UiBuild.Anchor(_cellName[m].rectTransform, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.36f));
                FitText(_cellName[m], 10, 20);
                _cellValue[m] = UiBuild.Label(rt, "Deger", string.Empty, 20, TextAnchor.MiddleCenter);
                UiBuild.Anchor(_cellValue[m].rectTransform, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.18f));
                _cellBench[m] = UiBuild.Label(rt, "Tezgah", string.Empty, 16, TextAnchor.UpperCenter);
                UiBuild.Anchor(_cellBench[m].rectTransform, new Vector2(0.02f, 0.80f), new Vector2(0.98f, 0.98f));
                FitText(_cellBench[m], 8, 16);
                _cellBench[m].gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);
                _cell[m] = cell;
            }
            _picker.gameObject.SetActive(false);
        }

        private void OpenPicker()
        {
            if (_foremen == null || !_shop.View.ProductAt(_product).TableBuilt) return;
            _picker.gameObject.SetActive(true);
            LayoutPicker();
        }

        /// <summary>The owned masters, best first, in a grid; each says what he adds and which bench he works now.</summary>
        private void LayoutPicker()
        {
            int shown = 0;
            for (int m = 0; m < Foremen.Count; m++)
            {
                bool owned = _foremen.IsHired(m);
                _cell[m].gameObject.SetActive(owned);
                if (!owned) continue;
                // Insertion by what he is worth on a bench, so the best is always first.
                int at = shown++;
                while (at > 0 && _shop.MasterMultiplier(_order[at - 1]) < _shop.MasterMultiplier(m))
                {
                    _order[at] = _order[at - 1];
                    at--;
                }
                _order[at] = m;
            }
            _pickerEmpty.gameObject.SetActive(shown == 0);

            int rows = Mathf.Max(1, (Foremen.Count + pickerColumns - 1) / pickerColumns);
            const float left = 0.03f, right = 0.97f, top = 0.84f, bottom = 0.03f, gap = 0.01f;
            float w = (right - left) / pickerColumns, h = (top - bottom) / rows;
            for (int i = 0; i < shown; i++)
            {
                int m = _order[i];
                float x = left + (i % pickerColumns) * w, y = top - (i / pickerColumns + 1) * h;
                UiBuild.Anchor((RectTransform)_cell[m].transform, new Vector2(x + gap, y + gap), new Vector2(x + w - gap, y + h - gap));
                Sprite face = _roster != null ? _roster.PortraitOf(m) : null;
                _cellFace[m].sprite = face;
                _cellFace[m].enabled = face != null;
                _cellName[m].text = ForemanRosterUI.DisplayName(m);
                _cellValue[m].text = WorkerBonus(_shop.MasterMultiplier(m));
                int bench = _shop.BenchOf(m);
                _cellBench[m].text = bench >= 0 ? Loc.T(TitleKeys[bench]) : string.Empty;
            }
        }

        private void PickWorker(int master)
        {
            _shop.TrySetWorker(_product, master);
            _picker.gameObject.SetActive(false);
        }

        /// <summary>"+10%", or "×1" for nobody.</summary>
        private static string WorkerBonus(double multiplier)
            => multiplier > 1d ? "+" + ((multiplier - 1d) * 100d).ToString("0.#") + "%" : "×1";

        /// <summary>A portrait that never takes a tap from the button it sits on.</summary>
        private static Image Face(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, min, max);
            return img;
        }

        /// <summary>
        /// The bench name on one line, shrunk just enough to stop short of the stars: long names (German, Polish,
        /// Portuguese) wrapped or ran under them. Measured only when the name itself changes.
        /// </summary>
        private void SetTitle(string title)
        {
            if (ReferenceEquals(_title.text, title)) return;
            _title.text = title;
            _title.fontSize = TitleSize;
            float room = _title.rectTransform.rect.width, width = _title.preferredWidth;
            if (width > room && room > 0f) _title.fontSize = Mathf.Max(TitleMinSize, (int)(TitleSize * room / width));
        }

        private const int TitleSize = 40, TitleMinSize = 20;

        private static void FitText(Text text, int min, int max)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = max;
        }

        private void NextMode()
        {
            _modeIndex = (_modeIndex + 1) % ModeLabels.Length;
            Refresh();
        }

        /// <summary>The levels one press asks for: the mode's count, never past the next star.</summary>
        private int PressLevels(int level)
        {
            int toStar = BenchMastery.LevelsToNextStar(level);
            int toTop = BenchMastery.MaxLevel - level;
            int wanted = _modeIndex == ModeOne ? 1 : _modeIndex == ModeTen ? 10 : toStar;
            if (toStar > 0 && wanted > toStar) wanted = toStar;
            return wanted < toTop ? wanted : toTop;
        }

        /// <summary>
        /// One press, or one repeat of a held press. A built bench buys up to <see cref="PressLevels"/>; the save waits
        /// for the release. Answers false to stop the repeats: at a star, at the top, or when nothing could be bought.
        /// An unbuilt bench is built once and never repeats.
        /// </summary>
        private bool Step()
        {
            MiningShopBusinessSimulation.ProductSnapshot product = _shop.View.ProductAt(_product);
            if (!product.TableBuilt)
            {
                _shop.TryBuildTable(_product);
                Refresh();
                return false;
            }
            int bought = _shop.TryBuyLevels(_product, PressLevels(product.Level), false);
            Refresh();
            if (bought < 1) return false;
            int level = _shop.View.ProductAt(_product).Level;
            return level < BenchMastery.MaxLevel && BenchMastery.StarsAt(level) == BenchMastery.StarsAt(level - bought);
        }

        private void Refresh()
        {
            MiningShopBusinessSimulation.Snapshot v = _shop.View;
            MiningShopBusinessSimulation.ProductSnapshot product = v.ProductAt(_product);
            int wanted = product.TableBuilt ? PressLevels(product.Level) : 0;
            int affordable = wanted > 0 ? _shop.AffordableLevels(_product, wanted) : 0;
            bool blocked = v.PendingSeconds > 0d;
            bool requirement = !product.TableBuilt && _shop.BuildRequirementMet(_product);
            // How far the bench before it has got towards the build's requirement.
            int requiredLevel = !product.TableBuilt && _product > 0 && v.ProductAt(_product - 1).TableBuilt
                ? v.ProductAt(_product - 1).Level : 0;
            // An unbuilt bench only cares whether its build is affordable.
            if (!product.TableBuilt) affordable = requirement && _wallet.CanAfford(new BigDouble(_shop.TableCost(_product))) ? 1 : 0;
            int worker = _shop.WorkerAt(_product);
            double workerMultiplier = _shop.WorkerMultiplier(_product);
            if (_shownProduct == _product && _shownLevel == product.Level && _shownMode == _modeIndex &&
                _shownAffordable == affordable && _shownSold == product.Sold && _shownBuilt == product.TableBuilt &&
                _shownBlocked == blocked && _shownRequirement == requirement && _shownWorker == worker &&
                _shownRequiredLevel == requiredLevel &&
                _shownWorkerMultiplier == workerMultiplier) return;
            _shownProduct = _product;
            _shownLevel = product.Level;
            _shownMode = _modeIndex;
            _shownAffordable = affordable;
            _shownSold = product.Sold;
            _shownBuilt = product.TableBuilt;
            _shownBlocked = blocked;
            _shownRequirement = requirement;
            _shownRequiredLevel = requiredLevel;
            _shownWorker = worker;
            _shownWorkerMultiplier = workerMultiplier;

            SetTitle(Loc.T(TitleKeys[_product]));
            _summary.text = string.Format(Loc.T("maden_dukkani.ozet"), _shop.CraftSeconds(_product).ToString("0.0"),
                NumberFormatter.Format(new BigDouble(_shop.UnitPrice(_product))), product.Sold);
            // The tip chance follows the bench's stars, which follow its level: redrawn whenever the level is.
            if (product.TableBuilt)
                _summary.text += "\n" + string.Format(Loc.T("maden_dukkani.bahsis_sans"),
                    Mathf.RoundToInt((float)(_shop.TipChance(_product) * 100d)));

            bool built = product.TableBuilt;
            for (int i = 0; i < _stars.Length; i++)
            {
                _stars[i].gameObject.SetActive(built);
                _stars[i].color = i < product.Stars ? starEarnedColor : starMissingColor;
            }
            // A locked build uses the star line and bar for its requirement instead.
            bool locked = !built && !requirement && _product > 0;
            _nextStar.gameObject.SetActive(built || locked);
            _starBar.gameObject.SetActive(built || locked);
            _mode.gameObject.SetActive(built);
            _worker.gameObject.SetActive(built && _foremen != null);
            if (built && _foremen != null)
            {
                Sprite face = worker >= 0 && _roster != null ? _roster.PortraitOf(worker) : null;
                _workerFace.sprite = face;
                _workerFace.enabled = face != null;
                _workerText.text = (worker >= 0 ? ForemanRosterUI.DisplayName(worker) : Loc.T("maden_dukkani.cirak")) +
                                   "  " + WorkerBonus(workerMultiplier);
            }

            if (built)
            {
                ShowStarProgress(product.Level, product.Stars);
                _modeText.text = ModeLabels[_modeIndex] ?? Loc.T("maden_dukkani.mod_yildiz");
                if (wanted <= 0)
                {
                    _buyText.text = string.Format(Loc.T("maden_dukkani.seviye"), product.Level) + "\n" + Loc.T("maden_dukkani.maks");
                    _buy.interactable = false;
                    return;
                }
                // What this press will buy: as many of the wanted levels as the wallet covers, or, when it covers
                // none, the whole press at its price so the player can see what to save for.
                int levels = affordable > 0 ? affordable : wanted;
                _buyText.text = string.Format(Loc.T("maden_dukkani.seviye_ok"), product.Level, product.Level + levels) +
                                "\n$" + NumberFormatter.Format(new BigDouble(_shop.CostOfLevels(_product, levels)));
                _buy.interactable = !blocked && affordable > 0;
                return;
            }

            // An offered bench not yet built: one build button with its price, usable once the bench before it is
            // levelled enough. Until then the line and bar say which bench, which level, and how far it has got.
            _buyText.text = Loc.T("maden_dukkani.tezgah_kur") + "\n$" +
                            NumberFormatter.Format(new BigDouble(_shop.TableCost(_product)));
            if (locked)
            {
                int need = _shop.BuildRequiresLevel;
                _nextStar.text = string.Format(Loc.T("maden_dukkani.gereken"), Loc.T(TitleKeys[_product - 1]), need) +
                                 "  (" + requiredLevel + "/" + need + ")";
                _starBarFill.anchorMax = new Vector2(Mathf.Clamp01((float)requiredLevel / need), 1f);
                _buy.interactable = false;
                return;
            }
            _buy.interactable = !blocked && affordable > 0;
        }

        /// <summary>
        /// The bar fills from the last star's level to the next; the line names the next star, what it doubles and the
        /// gems it pays.
        /// </summary>
        private void ShowStarProgress(int level, int stars)
        {
            int next = BenchMastery.NextStarLevel(level);
            if (next == 0)
            {
                _nextStar.text = Loc.T("maden_dukkani.tum_yildizlar");
                _starBarFill.anchorMax = Vector2.one;
                return;
            }
            int from = stars > 0 ? BenchMastery.StarLevels[stars - 1] : BenchMastery.MinLevel;
            _starBarFill.anchorMax = new Vector2((float)(level - from) / (next - from), 1f);
            _nextStar.text = string.Format(Loc.T("maden_dukkani.sonraki_yildiz"), stars + 1, next, StarEffectText(stars)) +
                             "  ·  " + string.Format(Loc.T("reklam.elmas"), _shop.StarGems(stars));
        }

        /// <summary>What the star at this position doubles, e.g. "VALUE ×2".</summary>
        internal string StarEffectText(int star)
        {
            string effect = BenchMastery.StarEffects[star] == BenchMastery.StarEffect.Value
                ? "maden_dukkani.etki_deger" : "maden_dukkani.etki_hiz";
            return string.Format(Loc.T(effect), _shop.StarMultiplier.ToString("0.##"));
        }
    }
}
