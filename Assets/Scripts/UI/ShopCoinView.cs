using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The collectible coin in the shop's customer area: pops in over one of the <c>Shop_Coin_Spot_0N</c> anchors,
    /// bobs and flips, blinks through its last seconds and shrinks away if nobody taps it. The rules and the clock are
    /// <see cref="ShopCoinService"/>'s; this only feeds it eligible time and draws what it says.
    ///
    /// A SCREEN-SIZED ICON, NOT A WORLD OBJECT. The default camera shows the whole island, where the shop's islet is
    /// about 140 canvas units across and a customer about 25: a coin of world size would be a few pixels. So the coin
    /// is a UI icon of fixed size on its own canvas, pinned every frame to its anchor's screen position. The roof and
    /// the awning cannot hide it, and zooming in only makes the shop grow around it.
    ///
    /// The anchors sit on the south lip of the plaza, in front of the first customers in the queue: at the default
    /// camera it is the only strip where the coin's tap area clears every bench, and a tap over UI never reaches the
    /// world, so a coin over a bench would swallow that bench's taps.
    ///
    /// ELIGIBLE TIME is the only time the coins see: the shop on camera, the opening tutorial and its tips out of the
    /// way, no bench card and no full screen covering it. The check is the flier's (<see cref="FlierUI"/>), done twice
    /// a second rather than every frame.
    ///
    /// The view follows the service rather than the Tick result, so a coin spawned or dropped from anywhere — the
    /// clock set back, a test — is drawn the same way.
    /// </summary>
    [RequireComponent(typeof(MiningShopView))]
    public sealed class ShopCoinView : MonoBehaviour
    {
        private const string SpotPrefix = "Shop_Coin_Spot_0";

        [Header("Görünüş")]
        [Tooltip("Sikke görseli. Boşsa arayüz temasının sikkesi kullanılır.")]
        [SerializeField] private Sprite coinSprite;
        [Tooltip("Sikkenin boyu, kanvas birimi (1080×1920 referans).")]
        [SerializeField, Min(16f)] private float coinSize = 60f;
        [SerializeField, Min(0f)] private float bobUnits = 5f;
        [SerializeField, Min(0.1f)] private float bobSeconds = 1.4f;
        [Tooltip("Saniyede kaç kez döner (yüz, kenar, yüz).")]
        [SerializeField, Min(0f)] private float flipsPerSecond = 0.6f;
        [Tooltip("Döner sikkenin en ince hali, genişliğin payı olarak.")]
        [SerializeField, Range(0.05f, 1f)] private float edgeWidth = 0.2f;
        [Tooltip("Sikkenin kanvası: HUD'un (100) üstünde, tezgâh kartının (103) altında.")]
        [SerializeField] private int coinSortingOrder = 101;

        [Header("Canlandırma")]
        [SerializeField, Min(0.05f)] private float popSeconds = 0.3f;
        [SerializeField, Min(1f)] private float popOvershoot = 1.15f;
        [SerializeField, Min(0.05f)] private float shrinkSeconds = 0.25f;
        [Tooltip("Sikke son kaç saniyesinde yanıp söner.")]
        [SerializeField, Min(0f)] private float blinkSeconds = 5f;
        [SerializeField, Min(0.5f)] private float blinksPerSecond = 4f;

        [Header("Uygunluk")]
        [Tooltip("Bu sıralamanın üstündeki bir arayüz ekranın ortasını kaplıyorsa açık bir ekran var sayılır.")]
        [SerializeField] private int fullScreenSortingOrder = 104;
        [Tooltip("Dükkân bölgesi ekranın bu kenar payı içinde kalmalı (0-0.5).")]
        [SerializeField, Range(0f, 0.4f)] private float viewportMargin = 0.05f;
        [Tooltip("Bir karede sayılan en uzun süre (sn); takılma ya da uygulamaya dönüş zamanlayıcıyı atlatmasın.")]
        [SerializeField, Min(0.02f)] private float maxStepSeconds = 0.25f;

        private ShopCoinService _coins;
        private MiningShopUpgradeUI _card;
        private Camera _camera;
        private Transform[] _spots;
        private Vector3 _areaCentre;

        private Canvas _canvas;
        private RectTransform _coin, _face;
        private CanvasGroup _group;
        private int _spot = -1;
        private bool _shown;
        private float _popClock, _shrinkClock = -1f, _clock, _checkClock;
        private bool _eligible;

        private readonly List<RaycastResult> _hits = new List<RaycastResult>(8);
        private PointerEventData _probe;
        private EventSystem _probeSystem;

        /// <summary>The coin is drawn (popping in, idle, blinking or shrinking).</summary>
        public bool CoinVisible => _shown;

        /// <summary>The coin's screen position in pixels, while it is drawn.</summary>
        public Vector2 CoinScreenPosition => _coin != null ? (Vector2)_coin.position : Vector2.zero;

        /// <summary>Whether the coins' clock is running right now.</summary>
        public bool Eligible => _eligible;

        private void Awake()
        {
            _card = GetComponent<MiningShopUpgradeUI>();
            int count = 0;
            while (transform.Find(SpotPrefix + (count + 1)) != null) count++;
            _spots = new Transform[count];
            for (int i = 0; i < count; i++) _spots[i] = transform.Find(SpotPrefix + (i + 1));
        }

        private void Start()
        {
            _coins = ServiceLocator.Get<ShopCoinService>();
            // No shop this launch, or the scene has no coin spots: no coins.
            if (_coins == null || _spots.Length == 0 || !GetComponent<MiningShopView>().enabled) { enabled = false; return; }
            _camera = Camera.main;
            _areaCentre = Vector3.zero;
            for (int i = 0; i < _spots.Length; i++) _areaCentre += _spots[i].position;
            _areaCentre /= _spots.Length;
            BuildCoin();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxStepSeconds);

            _checkClock -= dt;
            if (_checkClock <= 0f)
            {
                _checkClock = 0.5f;
                _eligible = CheckEligible();
            }
            if (_eligible) _coins.Tick(dt);

            if (_coins.HasLiveCoin && !_shown) Show();
            else if (!_coins.HasLiveCoin && _shown && _shrinkClock < 0f) _shrinkClock = shrinkSeconds;

            if (_shown) Animate(dt);
        }

        private bool CheckEligible()
        {
            if (!_coins.Unlocked || TutorialUI.Blocking) return false;
            if (_card != null && _card.TutorialPanelOpen) return false;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;
            Vector3 v = _camera.WorldToViewportPoint(_areaCentre);
            if (v.z <= 0f || v.x < viewportMargin || v.x > 1f - viewportMargin ||
                v.y < viewportMargin || v.y > 1f - viewportMargin) return false;
            return !ScreenCovered();
        }

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

        private void Show()
        {
            // Any spot but the last one, so two coins in a row never sit in the same place.
            int pick = Random.Range(0, _spots.Length);
            if (_spots.Length > 1 && pick == _spot) pick = (pick + 1 + Random.Range(0, _spots.Length - 1)) % _spots.Length;
            _spot = pick;
            _coin.localScale = Vector3.zero;
            _group.alpha = 1f;
            _coin.gameObject.SetActive(true);
            _shown = true;
            _popClock = 0f;
            _shrinkClock = -1f;
            _clock = 0f;
            Place();
        }

        private void Animate(float dt)
        {
            _clock += dt;
            Place();

            // A coin turning on its vertical axis: full face, edge, full face.
            float turn = Mathf.Abs(Mathf.Cos(_clock * flipsPerSecond * Mathf.PI));
            _face.localScale = new Vector3(Mathf.Max(edgeWidth, turn), 1f, 1f);

            if (_shrinkClock >= 0f)
            {
                _shrinkClock -= dt;
                _group.alpha = 1f;
                float s = Mathf.Clamp01(_shrinkClock / shrinkSeconds);
                _coin.localScale = Vector3.one * (s * s);
                if (_shrinkClock <= 0f)
                {
                    _coin.gameObject.SetActive(false);
                    _shown = false;
                    _shrinkClock = -1f;
                }
                return;
            }

            if (_popClock < popSeconds)
            {
                _popClock += dt;
                _coin.localScale = Vector3.one * Pop(Mathf.Clamp01(_popClock / popSeconds));
            }

            bool on = _coins.LiveSecondsLeft > blinkSeconds || Mathf.Repeat(_clock * blinksPerSecond, 1f) < 0.6f;
            _group.alpha = on ? 1f : 0.25f;
        }

        /// <summary>Pins the coin to its anchor's screen position, bobbing. Hidden while the anchor is behind the camera.</summary>
        private void Place()
        {
            if (_camera == null) return;
            Vector3 screen = _camera.WorldToScreenPoint(_spots[_spot].position);
            if (screen.z <= 0f) { _group.alpha = 0f; return; }
            float bob = Mathf.Sin(_clock * Mathf.PI * 2f / bobSeconds) * bobUnits * _canvas.scaleFactor;
            _coin.position = new Vector3(screen.x, screen.y + bob, 0f);
        }

        /// <summary>0 → overshoot → 1: up to the overshoot over the first 60%, settling back over the rest.</summary>
        private float Pop(float t)
        {
            if (t < 0.6f)
            {
                float u = t / 0.6f;
                return popOvershoot * (1f - (1f - u) * (1f - u));
            }
            return Mathf.Lerp(popOvershoot, 1f, (t - 0.6f) / 0.4f);
        }

        private void BuildCoin()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DukkanSikkesi", coinSortingOrder);
            _canvas = canvas.GetComponent<Canvas>();
            Sprite sprite = coinSprite != null ? coinSprite : UiSkin.Coin;

            var root = new GameObject("Sikke", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(canvas, false);
            _coin = (RectTransform)root.transform;
            _coin.anchorMin = _coin.anchorMax = Vector2.zero;
            _coin.sizeDelta = new Vector2(coinSize, coinSize);
            _group = root.GetComponent<CanvasGroup>();
            // Step 3 makes the coin the tap target; until then it must not swallow taps meant for the world.
            _group.blocksRaycasts = false;

            var face = new GameObject("Yuz", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(_coin, false);
            _face = (RectTransform)face.transform;
            _face.anchorMin = Vector2.zero;
            _face.anchorMax = Vector2.one;
            _face.sizeDelta = Vector2.zero;
            Image img = face.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            root.SetActive(false);
        }
    }
}
