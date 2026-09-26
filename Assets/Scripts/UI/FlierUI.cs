using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The flying reward character on the shop screen: a gull carrying a coin pouch crosses the view now and then;
    /// tapping it offers a few minutes of income for an ad, and every fifth catch in a row also pays a master card.
    /// The rules are in <see cref="Flier"/> and the state in <see cref="FlierService"/>; this draws the bird, takes
    /// the tap and shows the offer.
    ///
    /// ONE BIRD, BUILT ONCE. It is a 3D body flown along the camera's view so it is always on screen and lit with the
    /// island, never instantiated per crossing. Until real art is wired into <see cref="flierPrefab"/> it is a
    /// placeholder of primitives: two flapping wings and a gold pouch.
    ///
    /// THE TAP IS UI. An invisible button follows the bird's screen position on its own canvas, so the bench card's
    /// world tap (which ignores touches over UI) never opens a bench under a caught bird.
    /// </summary>
    [RequireComponent(typeof(MiningShopView))]
    public sealed class FlierUI : MonoBehaviour
    {
        [Header("Zamanlama (Game.Core.Flier)")]
        [Tooltip("İki geçiş arasındaki en kısa ön plan süresi (sn).")]
        [SerializeField, Min(1f)] private float minGapSeconds = 240f;
        [Tooltip("İki geçiş arasındaki en uzun ön plan süresi (sn).")]
        [SerializeField, Min(1f)] private float maxGapSeconds = 420f;
        [Tooltip("Bir geçişin süresi (sn).")]
        [SerializeField, Min(1f)] private float flightSeconds = 12f;
        [Tooltip("Günde en fazla kaç yakalama (UTC gece yarısı sıfırlanır).")]
        [SerializeField, Min(0)] private int chargesPerDay = 8;
        [Tooltip("Bir yakalamanın ödediği gelir dakikası.")]
        [SerializeField, Min(0f)] private float cashMinutes = 4f;
        [SerializeField, Min(0f)] private float cashFloor = 100f;
        [Tooltip("Kaç yakalamada bir usta kartı.")]
        [SerializeField, Min(1)] private int streakLength = 5;
        [Tooltip("Başka bir ödülden sonra kuşun gelmeden beklediği süre (sn).")]
        [SerializeField, Min(0f)] private float quietSeconds = 90f;

        [Header("Görünüş")]
        [Tooltip("Gerçek kuş modeli. Boşsa ilkel parçalardan bir yer tutucu kurulur.")]
        [SerializeField] private GameObject flierPrefab;
        [Tooltip("Kuşun ekran genişliğine oranla boyu.")]
        [SerializeField, Range(0.03f, 0.4f)] private float screenWidthShare = 0.14f;
        [Tooltip("Kuşun kameraya uzaklığı, kameranın dükkâna uzaklığının payı olarak.")]
        [SerializeField, Range(0.1f, 1f)] private float depthShare = 0.45f;
        [Tooltip("Geçişin ekrandaki yüksekliği (0 alt, 1 üst): başlangıç ve bitiş. Adanın üstündeki boş gökyüzü " +
                 "şeridi; daha aşağısı bina işaretlerinin altında kalır.")]
        [SerializeField] private Vector2 flightHeight = new Vector2(0.80f, 0.84f);
        [SerializeField, Range(0f, 0.1f)] private float bobHeight = 0.015f;
        [SerializeField, Min(0.1f)] private float flapsPerSecond = 3f;
        [SerializeField, Range(0f, 80f)] private float flapDegrees = 35f;
        [SerializeField] private Color wingColor = new Color(0.96f, 0.96f, 0.94f);
        [SerializeField] private Color pouchColor = new Color(0.95f, 0.72f, 0.2f);
        [SerializeField] private Color wingTipColor = new Color(0.35f, 0.38f, 0.42f);
        [SerializeField] private Color beakColor = new Color(0.98f, 0.62f, 0.15f);
        [Tooltip("Dokunma alanının çapı, ekran genişliğinin payı olarak.")]
        [SerializeField, Range(0.05f, 0.5f)] private float tapShare = 0.2f;

        [Header("Teklif")]
        [Tooltip("Uçanın dokunma kanvası: HUD'un (100) üstünde, tezgâh kartının (103) altında.")]
        [SerializeField] private int flierSortingOrder = 101;
        [Tooltip("Teklif penceresi; tam ekranların (104+) üstünde.")]
        [SerializeField] private int popupSortingOrder = 110;
        [Tooltip("Bu sıralamanın üstündeki bir arayüz ekranın ortasını kaplıyorsa açık bir ekran var sayılır.")]
        [SerializeField] private int fullScreenSortingOrder = 104;
        [Tooltip("\"Hayır, teşekkürler\" düğmesinin belirmeden önceki bekleyişi (sn).")]
        [SerializeField, Min(0f)] private float noThanksDelay = 1.5f;
        [Tooltip("Seri usta kartı verince yazının ekranda kalma süresi (sn).")]
        [SerializeField, Min(0.3f)] private float cardToastSeconds = 2.5f;
        [SerializeField] private Color dimColor = new Color(0.02f, 0.03f, 0.06f, 0.78f);
        [SerializeField] private Color amountInk = new Color(0.12f, 0.38f, 0.70f, 1f);

        private FlierService _service;
        private MarketService _market;
        private FreeRewardService _free;
        private BalloonRewardService _balloon;
        private IAdService _ad;
        private MiningShopView _view;
        private MiningShopUpgradeUI _card;
        private Camera _camera;

        private Transform _bird, _wingLeft, _wingRight;
        private float _birdSize = 1f;
        private float _flightClock = -1f, _direction = 1f, _depth;
        private RectTransform _hit, _popup, _noThanks;
        private Text _amount, _streak, _watchText, _toast, _title, _noThanksText;
        private Image _streakFill;
        private float _popupClock, _toastClock, _checkClock;
        private double _offer;

        private readonly System.Collections.Generic.List<RaycastResult> _hits = new System.Collections.Generic.List<RaycastResult>(8);
        private PointerEventData _probe;
        private EventSystem _probeSystem;

        /// <summary>Whether the bird is crossing right now.</summary>
        public bool Flying => _flightClock >= 0f;
        /// <summary>Whether the offer is open.</summary>
        public bool OfferOpen => _popup != null && _popup.gameObject.activeSelf;

        private void Awake()
        {
            _view = GetComponent<MiningShopView>();
            _card = GetComponent<MiningShopUpgradeUI>();
        }

        private void Start()
        {
            _market = ServiceLocator.Get<MarketService>();
            _free = ServiceLocator.Get<FreeRewardService>();
            if (_market == null || _market.MiningShopBusiness == null || _free == null) { enabled = false; return; }
            _balloon = ServiceLocator.Get<BalloonRewardService>();
            _ad = ServiceLocator.Get<IAdService>();
            _camera = Camera.main;
            var tuning = new Flier.Tuning
            {
                MinGapSeconds = minGapSeconds, MaxGapSeconds = Mathf.Max(minGapSeconds, maxGapSeconds),
                FlightSeconds = flightSeconds, ChargesPerDay = chargesPerDay, CashMinutes = cashMinutes,
                CashFloor = cashFloor, StreakLength = streakLength, QuietSeconds = quietSeconds
            };
            _service = new FlierService(_free, ServiceLocator.Get<WalletService>(), ServiceLocator.Get<ForemanService>(),
                ServiceLocator.Get<SaveService>(), ServiceLocator.Get<SaveData>(), tuning, Random.value);
            BuildBird();
            BuildHitCanvas();
            BuildPopup();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_toastClock > 0f)
            {
                _toastClock -= dt;
                if (_toastClock <= 0f) _toast.gameObject.SetActive(false);
            }
            if (OfferOpen)
            {
                if (_popupClock > 0f)
                {
                    _popupClock -= dt;
                    if (_popupClock <= 0f) _noThanks.gameObject.SetActive(true);
                }
                return;
            }
            if (Flying) { Fly(dt); return; }

            // Only while a crossing is far off is the clock all there is; once it is due, the screen is checked
            // twice a second rather than every frame.
            if (_service.SecondsToNext > dt) { _service.Tick(dt, default(Flier.Conditions), 0d); return; }
            _checkClock -= dt;
            if (_checkClock > 0f) return;
            _checkClock = 0.5f;
            if (_service.Tick(dt, Now(), Random.value)) StartFlight();
        }

        private Flier.Conditions Now() => new Flier.Conditions
        {
            ChargesLeft = _service.ChargesLeft,
            BalloonReady = _balloon != null && _balloon.Ready,
            AdReady = _free.AdsRemoved || (_ad != null && _ad.Available),
            TutorialBlocking = TutorialUI.Blocking,
            PanelOpen = (_card != null && _card.TutorialPanelOpen) || ScreenCovered(),
            SecondsSinceReward = _free.SecondsSinceAnyWatch()
        };

        /// <summary>A full screen is open when UI at its sorting order covers the middle of the screen.</summary>
        private bool ScreenCovered()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (_probe == null || _probeSystem != events)
            {
                _probe = new PointerEventData(events);
                _probeSystem = events;
            }
            _probe.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _hits.Clear();
            events.RaycastAll(_probe, _hits);
            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].sortingOrder >= fullScreenSortingOrder) return true;
            return false;
        }

        // ------------------------------------------------------------------ the bird
        private void BuildBird()
        {
            if (flierPrefab != null)
            {
                _bird = Instantiate(flierPrefab, transform).transform;
                _bird.name = "Ucan";
            }
            else
            {
                _bird = new GameObject("Ucan").transform;
                _bird.SetParent(transform, false);
                Renderer source = _view.GetComponentInChildren<Renderer>(true);
                Material baseMat = source != null ? source.sharedMaterial : null;
                Material wing = Tinted(baseMat, wingColor), pouch = Tinted(baseMat, pouchColor);
                Material tip = Tinted(baseMat, wingTipColor), beak = Tinted(baseMat, beakColor);
                // Seen from above, as the camera sees it: a body along the heading with a head and beak in front,
                // two swept wings hinged at the shoulders, and the pouch it carries slung on its back.
                Part(PrimitiveType.Capsule, _bird, wing, Vector3.zero, new Vector3(0.2f, 0.36f, 0.2f), Quaternion.Euler(90f, 0f, 0f));
                Part(PrimitiveType.Sphere, _bird, wing, new Vector3(0f, 0.02f, 0.38f), Vector3.one * 0.2f, Quaternion.identity);
                Part(PrimitiveType.Cube, _bird, beak, new Vector3(0f, 0.02f, 0.53f), new Vector3(0.06f, 0.05f, 0.14f), Quaternion.identity);
                _wingLeft = Hinge(_bird, wing, tip, -1f);
                _wingRight = Hinge(_bird, wing, tip, 1f);
                Part(PrimitiveType.Sphere, _bird, pouch, new Vector3(0f, 0.16f, -0.12f), new Vector3(0.3f, 0.2f, 0.3f), Quaternion.identity);
                _materials = new[] { wing, pouch, tip, beak };
            }
            // Measured once at unit scale, so any art sizes the same way.
            _birdSize = 1f;
            Renderer[] parts = _bird.GetComponentsInChildren<Renderer>(true);
            if (parts.Length > 0)
            {
                Bounds b = parts[0].bounds;
                for (int i = 1; i < parts.Length; i++) b.Encapsulate(parts[i].bounds);
                _birdSize = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z) / Mathf.Max(0.0001f, _bird.lossyScale.x));
            }
            _bird.gameObject.SetActive(false);
        }

        private Material[] _materials;

        private void OnDestroy()
        {
            if (_materials == null) return;
            for (int i = 0; i < _materials.Length; i++) if (_materials[i] != null) Destroy(_materials[i]);
        }

        private static Transform Hinge(Transform body, Material m, Material tip, float side)
        {
            var hinge = new GameObject(side < 0f ? "KanatSol" : "KanatSag").transform;
            hinge.SetParent(body, false);
            hinge.localPosition = new Vector3(0.08f * side, 0.04f, 0.04f);
            // Swept back from the shoulder, darker at the tip, as a gull's are.
            var sweep = new GameObject("Kanat").transform;
            sweep.SetParent(hinge, false);
            sweep.localRotation = Quaternion.Euler(0f, 18f * side, 0f);
            Part(PrimitiveType.Cube, sweep, m, new Vector3(0.3f * side, 0f, 0f), new Vector3(0.6f, 0.03f, 0.3f), Quaternion.identity);
            Part(PrimitiveType.Cube, sweep, tip, new Vector3(0.72f * side, 0f, -0.03f), new Vector3(0.26f, 0.03f, 0.2f), Quaternion.identity);
            return hinge;
        }

        private static void Part(PrimitiveType type, Transform parent, Material m, Vector3 at, Vector3 scale, Quaternion rot)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material Tinted(Material source, Color color)
        {
            Material m = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private void StartFlight()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            Vector3 shop = _view.ShopBounds.center;
            _depth = Mathf.Max(_camera.nearClipPlane * 4f, Vector3.Distance(_camera.transform.position, shop) * depthShare);
            float viewWidth = 2f * _depth * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _camera.aspect;
            _bird.localScale = Vector3.one * (viewWidth * screenWidthShare / _birdSize) / Mathf.Max(0.0001f, transform.lossyScale.x);
            _direction = Random.value < 0.5f ? 1f : -1f;
            _flightClock = 0f;
            // Sized here rather than at build: the canvas scaler has settled by the first crossing.
            float tap = Screen.width * tapShare / Mathf.Max(0.01f, _hit.lossyScale.x / Mathf.Max(0.0001f, _hit.localScale.x));
            _hit.sizeDelta = new Vector2(tap, tap);
            _bird.gameObject.SetActive(true);
            _hit.gameObject.SetActive(true);
            Fly(0f);
        }

        private void EndFlight()
        {
            _flightClock = -1f;
            _bird.gameObject.SetActive(false);
            _hit.gameObject.SetActive(false);
        }

        private void Fly(float dt)
        {
            if (TutorialUI.Blocking || _camera == null) { EndFlight(); return; }
            _flightClock += dt;
            float t = _flightClock / flightSeconds;
            if (t >= 1f) { EndFlight(); return; }

            // Edge to edge along the view, a little beyond both sides so it enters and leaves off screen.
            float x = Mathf.Lerp(-0.12f, 1.12f, _direction > 0f ? t : 1f - t);
            float y = Mathf.Lerp(flightHeight.x, flightHeight.y, t) + Mathf.Sin(_flightClock * 2.2f) * bobHeight;
            Vector3 at = _camera.ViewportToWorldPoint(new Vector3(x, y, _depth));
            _bird.position = at;
            // Heading across the screen with its back to the camera, so the wingspan faces the player.
            _bird.rotation = Quaternion.LookRotation(_camera.transform.right * _direction, -_camera.transform.forward);
            if (_wingLeft != null)
            {
                float flap = Mathf.Sin(_flightClock * flapsPerSecond * Mathf.PI * 2f) * flapDegrees;
                _wingLeft.localRotation = Quaternion.Euler(0f, 0f, -flap);
                _wingRight.localRotation = Quaternion.Euler(0f, 0f, flap);
            }
            Vector3 screen = _camera.WorldToScreenPoint(at);
            _hit.position = new Vector3(screen.x, screen.y, 0f);
        }

        // ------------------------------------------------------------------ tap and offer
        private void BuildHitCanvas()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UcanOdulDokunma", flierSortingOrder);
            var hitGo = new GameObject("Dokun", typeof(RectTransform), typeof(Image), typeof(Button));
            hitGo.transform.SetParent(canvas, false);
            _hit = (RectTransform)hitGo.transform;
            _hit.anchorMin = _hit.anchorMax = Vector2.zero;
            Image img = hitGo.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            hitGo.GetComponent<Button>().onClick.AddListener(OpenOffer);
            hitGo.SetActive(false);

            _toast = UiBuild.Label(canvas, "Kart", string.Empty, 40, TextAnchor.MiddleCenter);
            // Between the HUD's button rails, shrinking or wrapping rather than running over them.
            UiBuild.Anchor(_toast.rectTransform, new Vector2(0.15f, 0.56f), new Vector2(0.84f, 0.62f));
            Fit(_toast, 20, 40);
            _toast.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            _toast.gameObject.SetActive(false);
        }

        private void BuildPopup()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "UcanOdulTeklif", popupSortingOrder);
            _popup = UiBuild.Flat(canvas, "Karartma", dimColor, Vector2.zero, Vector2.one);
            _popup.GetComponent<Image>().raycastTarget = true;
            // The contract celebration's crowned card: the same reward-moment family, one size smaller.
            RectTransform box = EkranKit.Sliced(_popup, "Kart", LigKit.Get("odul_pano"),
                                                new Vector2(0.12f, 0.31f), new Vector2(0.88f, 0.67f), false).rectTransform;

            _title = EtkinlikKit.Label(box, "Baslik", new Vector2(0.12f, 0.655f), new Vector2(0.88f, 0.765f),
                                       string.Empty, 36, TextAnchor.MiddleCenter, EkranKit.Ink, 20);
            EkranKit.Icon(box, "Para", UiSkin.Coin, new Vector2(0.41f, 0.545f), new Vector2(0.59f, 0.665f));
            _amount = EtkinlikKit.Label(box, "Miktar", new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.545f),
                                        string.Empty, 56, TextAnchor.MiddleCenter, amountInk, 24);
            _streak = EtkinlikKit.Label(box, "Seri", new Vector2(0.08f, 0.375f), new Vector2(0.92f, 0.44f),
                                        string.Empty, 26, TextAnchor.MiddleCenter, EtkinlikKit.InkSoft, 14);
            _streakFill = EtkinlikKit.Bar(box, "SeriCubuk", new Vector2(0.22f, 0.33f), new Vector2(0.78f, 0.365f), "cubuk_altin");

            Button watch = EtkinlikKit.Capsule(box, "Izle", new Vector2(0.22f, 0.195f), new Vector2(0.78f, 0.305f),
                                               Watch, out _watchText);
            EtkinlikKit.SetFace(watch, _watchText, EtkinlikKit.Face.Claim, true);
            EtkinlikKit.Fit(_watchText, 12, 34);

            // Inside the card, under the claim: over the island it read as a stray label.
            Button no = EtkinlikKit.Capsule(box, "Hayir", new Vector2(0.30f, 0.095f), new Vector2(0.70f, 0.175f),
                                            CloseOffer, out _noThanksText);
            EtkinlikKit.SetFace(no, _noThanksText, EtkinlikKit.Face.Dead, true);
            _noThanks = (RectTransform)no.transform;

            _popup.gameObject.SetActive(false);
            UiBuild.InsetContent(canvas);
        }

        private static void Fit(Text text, int min, int max)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = max;
        }

        private double IncomePerMinute() => _market.MiningShopIncomePerSec * 60d;

        private void OpenOffer()
        {
            if (!Flying) return;
            EndFlight();
            _offer = Flier.Payout(IncomePerMinute(), _service.Tuning);
            _amount.text = "+$" + NumberFormatter.Format(new BigDouble(_offer));
            // Rewritten on every open, so a language changed since the build shows here too.
            _title.text = Loc.T("ucan_odul.baslik");
            _noThanksText.text = Loc.T("ucan_odul.hayir");
            int streak = _service.Streak, length = _service.Tuning.StreakLength;
            _streak.text = string.Format(Loc.T("ucan_odul.seri"), streak, length);
            EtkinlikKit.Progress(_streakFill, (float)streak / length);
            _watchText.text = _free.AdsRemoved ? Loc.T("ortak.topla") : Loc.T("ucan_odul.izle");
            _noThanks.gameObject.SetActive(noThanksDelay <= 0f);
            _popupClock = noThanksDelay;
            _popup.gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.PanelOpen);
        }

        private void CloseOffer() => _popup.gameObject.SetActive(false);

        private void Watch()
        {
            if (_free.AdsRemoved) { Claim(); return; }
            if (_ad == null || !_ad.Available) { CloseOffer(); return; }
            _ad.ShowRewarded(Claim, CloseOffer);
        }

        private void Claim()
        {
            CloseOffer();
            FlierService.Receipt receipt = _service.TryClaim(IncomePerMinute());
            if (!receipt.Paid) return;
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
            if (receipt.CardMaster < 0) return;
            _toast.text = string.Format(Loc.T("ucan_odul.kart"), ForemanRosterUI.DisplayName(receipt.CardMaster));
            _toast.gameObject.SetActive(true);
            _toastClock = cardToastSeconds;
        }
    }
}
