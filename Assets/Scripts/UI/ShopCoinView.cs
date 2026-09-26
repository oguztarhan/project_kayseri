using System.Collections.Generic;
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
    /// The collectible coin in the shop's customer area: pops in with a sparkle over one of the
    /// <c>Shop_Coin_Spot_0N</c> anchors, bobs and flips, blinks through its last seconds and shrinks away if nobody
    /// taps it. Tapped, it pays (<see cref="ShopCoinService.TryCollect"/> pays and saves first), shows
    /// "+$X · 7/12" where it was, and flies into the HUD counter its reward went to; a jackpot adds confetti and the
    /// reward card. The rules and the clock are the service's; this feeds it eligible time and draws what it says.
    ///
    /// A SCREEN-SIZED ICON, NOT A WORLD OBJECT. The default camera shows the whole island, where the shop's islet is
    /// about 140 canvas units across and a customer about 25: a coin of world size would be a few pixels. So the coin
    /// is a UI icon of fixed size on its own canvas, pinned every frame to its anchor's screen position. The roof and
    /// the awning cannot hide it, and zooming in only makes the shop grow around it.
    ///
    /// THE TAP IS UI, like the flier's: the coin is a button, so the bench card's world tap (which ignores touches
    /// over UI) never opens a bench under a collected coin. The anchors sit on the south lip of the plaza, in front of
    /// the first customers in the queue: at the default camera it is the only strip where the coin's tap area clears
    /// every bench, so the coin never swallows a tap meant for one. It takes taps only while it is up; popping in
    /// counts as up, shrinking and flying do not.
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
        private const int Sparks = 6;

        private enum State
        {
            Hidden,
            Up,
            Shrinking,
            Flying
        }

        [Header("Görünüş")]
        [Tooltip("Sikke görseli. Boşsa arayüz temasının sikkesi kullanılır.")]
        [SerializeField] private Sprite coinSprite;
        [Tooltip("Sikkenin boyu, kanvas birimi (1080×1920 referans).")]
        [SerializeField, Min(16f)] private float coinSize = 60f;
        [Tooltip("Dokunma alanının boyu, kanvas birimi. Sikkeden biraz büyük; tezgâhlara taşmasın diye çok büyük değil.")]
        [SerializeField, Min(16f)] private float tapSize = 68f;
        [SerializeField, Min(0f)] private float bobUnits = 5f;
        [SerializeField, Min(0.1f)] private float bobSeconds = 1.4f;
        [Tooltip("Saniyede kaç kez döner (yüz, kenar, yüz).")]
        [SerializeField, Min(0f)] private float flipsPerSecond = 0.6f;
        [Tooltip("Döner sikkenin en ince hali, genişliğin payı olarak.")]
        [SerializeField, Range(0.05f, 1f)] private float edgeWidth = 0.2f;
        [Tooltip("Sikkenin kanvası: HUD'un (100) üstünde, tezgâh kartının (103) altında.")]
        [SerializeField] private int coinSortingOrder = 101;
        [Tooltip("Büyük ikramiye kartının kanvası; tam ekranların (104+) arasında.")]
        [SerializeField] private int revealSortingOrder = 112;

        [Header("Canlandırma")]
        [SerializeField, Min(0.05f)] private float popSeconds = 0.3f;
        [SerializeField, Min(1f)] private float popOvershoot = 1.15f;
        [SerializeField, Min(0.05f)] private float shrinkSeconds = 0.25f;
        [Tooltip("Sikke son kaç saniyesinde yanıp söner.")]
        [SerializeField, Min(0f)] private float blinkSeconds = 5f;
        [SerializeField, Min(0.5f)] private float blinksPerSecond = 4f;
        [Tooltip("Belirirken saçılan pırıltıların gittiği uzaklık ve süre.")]
        [SerializeField, Min(0f)] private float sparkDistance = 55f;
        [SerializeField, Min(0.1f)] private float sparkSeconds = 0.45f;
        [SerializeField, Min(4f)] private float sparkSize = 18f;
        [Tooltip("Toplanan sikkenin HUD sayacına uçuşu (sn).")]
        [SerializeField, Min(0.1f)] private float flightSeconds = 0.7f;
        [Tooltip("\"+$X · 7/12\" yazısının yükselişi ve ekranda kalışı.")]
        [SerializeField] private float gainRise = 90f;
        [SerializeField, Min(0.3f)] private float gainSeconds = 1.2f;
        [SerializeField] private Color gainColor = new Color(1f, 0.9f, 0.3f);

        [Header("Uygunluk")]
        [Tooltip("Bu sıralamanın üstündeki bir arayüz ekranın ortasını kaplıyorsa açık bir ekran var sayılır.")]
        [SerializeField] private int fullScreenSortingOrder = 104;
        [Tooltip("Dükkân bölgesi ekranın bu kenar payı içinde kalmalı (0-0.5).")]
        [SerializeField, Range(0f, 0.4f)] private float viewportMargin = 0.05f;
        [Tooltip("Bir karede sayılan en uzun süre (sn); takılma ya da uygulamaya dönüş zamanlayıcıyı atlatmasın.")]
        [SerializeField, Min(0.02f)] private float maxStepSeconds = 0.25f;

        private ShopCoinService _coins;
        private MiningShopUpgradeUI _card;
        private HudUI _hud;
        private Camera _camera;
        private Transform[] _spots;
        private Vector3 _areaCentre;

        private Canvas _canvas;
        private RectTransform _canvasRect, _coin, _face, _sparkRoot, _gainRect;
        private readonly RectTransform[] _sparks = new RectTransform[Sparks];
        private CanvasGroup _group, _sparkGroup, _gainGroup;
        private Text _gain;
        private ConfettiBurst _confetti;
        private RewardRevealUI _reveal;

        private State _state = State.Hidden;
        private int _spot = -1;
        private float _popClock, _shrinkClock, _clock, _checkClock, _sparkClock = -1f, _gainClock = -1f, _flyClock;
        private Vector2 _flyFrom, _gainAt;
        private RectTransform _flyTarget;
        private bool _eligible;

        private readonly List<RaycastResult> _hits = new List<RaycastResult>(8);
        private PointerEventData _probe;
        private EventSystem _probeSystem;

        /// <summary>The coin is drawn (popping in, up, blinking, shrinking or flying).</summary>
        public bool CoinVisible => _state != State.Hidden;

        /// <summary>The coin can be tapped right now.</summary>
        public bool CoinTappable => _state == State.Up;

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
            _hud = FindAnyObjectByType<HudUI>();
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

            if (_coins.HasLiveCoin && _state == State.Hidden) Show();
            else if (!_coins.HasLiveCoin && _state == State.Up) SetState(State.Shrinking);

            if (_state != State.Hidden) Animate(dt);
            if (_sparkClock >= 0f) AnimateSparks(dt);
            if (_gainClock >= 0f) AnimateGain(dt);
        }

        // ------------------------------------------------------------------ eligibility
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

        // ------------------------------------------------------------------ the coin
        private void Show()
        {
            // Any spot but the last one, so two coins in a row never sit in the same place.
            int pick = Random.Range(0, _spots.Length);
            if (_spots.Length > 1 && pick == _spot) pick = (pick + 1 + Random.Range(0, _spots.Length - 1)) % _spots.Length;
            _spot = pick;
            _coin.localScale = Vector3.zero;
            _group.alpha = 1f;
            _coin.gameObject.SetActive(true);
            _popClock = 0f;
            _clock = 0f;
            SetState(State.Up);
            Place();
            StartSparks();
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Tick);
        }

        private void SetState(State state)
        {
            _state = state;
            if (state == State.Shrinking) _shrinkClock = shrinkSeconds;
            _group.blocksRaycasts = state == State.Up;
            if (state == State.Hidden) _coin.gameObject.SetActive(false);
        }

        private void Animate(float dt)
        {
            _clock += dt;
            if (_state == State.Flying) { Fly(dt); return; }
            Place();

            // A coin turning on its vertical axis: full face, edge, full face.
            float turn = Mathf.Abs(Mathf.Cos(_clock * flipsPerSecond * Mathf.PI));
            _face.localScale = new Vector3(Mathf.Max(edgeWidth, turn), 1f, 1f);

            if (_state == State.Shrinking)
            {
                _shrinkClock -= dt;
                _group.alpha = 1f;
                float s = Mathf.Clamp01(_shrinkClock / shrinkSeconds);
                _coin.localScale = Vector3.one * (s * s);
                if (_shrinkClock <= 0f) SetState(State.Hidden);
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

        // ------------------------------------------------------------------ collecting
        private void OnTap()
        {
            if (_state != State.Up) return;
            double income = _hud != null ? _hud.CurrentUnboostedIncomePerMinute : 0d;
            // Paid and saved before anything moves: whatever happens to the animation, the reward is already home.
            if (!_coins.TryCollect(income, out ShopCoinService.Receipt receipt)) return;

            _flyFrom = _coin.position;
            _flyTarget = TargetFor(receipt.Kind);
            _flyClock = 0f;
            _face.localScale = Vector3.one;
            _group.alpha = 1f;
            SetState(_flyTarget != null ? State.Flying : State.Shrinking);
            StartGain(GainText(receipt), _flyFrom);

            if (receipt.Kind == ShopCoins.RewardKind.Jackpot)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _flyFrom, null, out Vector2 local);
                _confetti.PlayAt(local);
                // The card brings its own sound and haptic.
                _reveal.Present("+$" + NumberFormatter.Format(new BigDouble(receipt.Cash)));
                return;
            }
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Coin);
            ServiceLocator.Get<HapticService>()?.Light();
        }

        private RectTransform TargetFor(ShopCoins.RewardKind kind)
        {
            if (_hud == null) return null;
            switch (kind)
            {
                case ShopCoins.RewardKind.Gems: return _hud.GemsCounter;
                case ShopCoins.RewardKind.Boost: return _hud.BoostCounter != null ? _hud.BoostCounter : _hud.CashCounter;
                default: return _hud.CashCounter;
            }
        }

        private static string GainText(in ShopCoinService.Receipt receipt)
        {
            string count = " · " + receipt.Collected + "/" + receipt.Cap;
            switch (receipt.Kind)
            {
                case ShopCoins.RewardKind.Gems:
                    return string.Format(Loc.T("sikke.elmas"), receipt.Gems) + count;
                case ShopCoins.RewardKind.Boost:
                    return string.Format(Loc.T("sikke.hiz"), receipt.BoostMultiplier.ToString("0.#"),
                                         Mathf.RoundToInt((float)receipt.BoostSeconds)) + count;
                default:
                    return "+$" + NumberFormatter.Format(new BigDouble(receipt.Cash)) + count;
            }
        }

        /// <summary>From where it was tapped into the counter its reward went to, on a curve, shrinking as it goes.</summary>
        private void Fly(float dt)
        {
            _flyClock += dt;
            float t = Mathf.Clamp01(_flyClock / flightSeconds);
            if (_flyTarget == null || t >= 1f) { SetState(State.Hidden); return; }
            Vector2 to = _flyTarget.TransformPoint(_flyTarget.rect.center);
            // Up and over: the control point rises from the start, so the coin arcs rather than sliding.
            Vector2 ctrl = new Vector2(_flyFrom.x, Mathf.Max(_flyFrom.y, to.y)) + Vector2.up * (120f * _canvas.scaleFactor);
            float e = t * t * (3f - 2f * t);
            float u = 1f - e;
            _coin.position = u * u * _flyFrom + 2f * u * e * ctrl + e * e * to;
            _coin.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, e);
            _face.localScale = new Vector3(Mathf.Max(edgeWidth, Mathf.Abs(Mathf.Cos(_clock * 3f * Mathf.PI))), 1f, 1f);
        }

        // ------------------------------------------------------------------ the sparkle and the gain line
        private void StartSparks()
        {
            _sparkClock = 0f;
            _sparkRoot.gameObject.SetActive(true);
            AnimateSparks(0f);
        }

        private void AnimateSparks(float dt)
        {
            _sparkClock += dt;
            float t = _sparkClock / sparkSeconds;
            if (t >= 1f)
            {
                _sparkClock = -1f;
                _sparkRoot.gameObject.SetActive(false);
                return;
            }
            _sparkRoot.position = _coin.position;
            float ease = 1f - (1f - t) * (1f - t);
            for (int i = 0; i < Sparks; i++)
            {
                float a = (i * 360f / Sparks + 30f) * Mathf.Deg2Rad;
                _sparks[i].anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (sparkDistance * ease);
                _sparks[i].localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, t);
                _sparks[i].localRotation = Quaternion.Euler(0f, 0f, 180f * t);
            }
            _sparkGroup.alpha = 1f - t * t;
        }

        private void StartGain(string text, Vector2 screen)
        {
            _gain.text = text;
            _gainAt = screen / _canvas.scaleFactor;
            _gainClock = 0f;
            _gainRect.gameObject.SetActive(true);
            AnimateGain(0f);
        }

        private void AnimateGain(float dt)
        {
            _gainClock += dt;
            float t = _gainClock / gainSeconds;
            if (t >= 1f)
            {
                _gainClock = -1f;
                _gainRect.gameObject.SetActive(false);
                return;
            }
            _gainRect.anchoredPosition = _gainAt + new Vector2(0f, coinSize * 0.6f + gainRise * (1f - (1f - t) * (1f - t)));
            _gainGroup.alpha = t > 0.7f ? (1f - t) / 0.3f : 1f;
        }

        // ------------------------------------------------------------------ building
        private void BuildCoin()
        {
            _canvasRect = UiBuild.Canvas(transform, "DukkanSikkesi", coinSortingOrder);
            _canvas = _canvasRect.GetComponent<Canvas>();
            Sprite sprite = coinSprite != null ? coinSprite : UiSkin.Coin;

            // The sparkle first, so the coin draws over it.
            _sparkRoot = Group(_canvasRect, "Pirilti", Vector2.zero);
            _sparkGroup = _sparkRoot.gameObject.AddComponent<CanvasGroup>();
            _sparkGroup.blocksRaycasts = false;
            Sprite star = Resources.Load<Sprite>("UI/Sea/yildiz");
            for (int i = 0; i < Sparks; i++)
                _sparks[i] = Picture(_sparkRoot, "Kivilcim" + i, star != null ? star : sprite, new Vector2(sparkSize, sparkSize));
            _sparkRoot.gameObject.SetActive(false);

            _coin = Group(_canvasRect, "Sikke", new Vector2(coinSize, coinSize));
            _group = _coin.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _face = Picture(_coin, "Yuz", sprite, Vector2.zero);
            _face.anchorMin = Vector2.zero;
            _face.anchorMax = Vector2.one;

            // The tap area: invisible, a little larger than the coin, and the coin's only raycast target.
            var hit = new GameObject("Dokun", typeof(RectTransform), typeof(Image), typeof(Button));
            hit.transform.SetParent(_coin, false);
            var hitRect = (RectTransform)hit.transform;
            hitRect.sizeDelta = new Vector2(tapSize, tapSize);
            hit.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            Button button = hit.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnTap);
            _coin.gameObject.SetActive(false);

            _gain = UiBuild.Label(_canvasRect, "Kazanc", string.Empty, 40, TextAnchor.MiddleCenter);
            _gain.color = gainColor;
            _gain.raycastTarget = false;
            _gain.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            _gainRect = _gain.rectTransform;
            _gainRect.anchorMin = _gainRect.anchorMax = Vector2.zero;
            _gainRect.pivot = new Vector2(0.5f, 0f);
            _gainRect.sizeDelta = new Vector2(600f, 60f);
            _gainGroup = _gain.gameObject.AddComponent<CanvasGroup>();
            _gainGroup.blocksRaycasts = false;
            _gain.gameObject.SetActive(false);

            _confetti = _canvasRect.gameObject.AddComponent<ConfettiBurst>();
            // The confetti builds its pieces as the canvas's last children; the gain line must draw over them.
            _gainRect.SetAsLastSibling();
            RectTransform revealCanvas = UiBuild.Canvas(transform, "DukkanSikkesiOdul", revealSortingOrder);
            _reveal = RewardRevealUI.Create(revealCanvas, UiSkin.Panel, sprite);
        }

        /// <summary>An empty, centred-pivot rect anchored to the canvas's bottom-left, so its position is in screen space.</summary>
        private static RectTransform Group(RectTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Picture(RectTransform parent, string name, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rt;
        }
    }
}
