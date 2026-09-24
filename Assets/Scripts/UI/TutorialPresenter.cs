using System;
using System.Collections;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Everything the tutorial draws, and nothing it decides. <see cref="TutorialUI"/> says what to
    /// teach and when; this puts Usta Max, his card and the spotlight on screen and reports the taps.
    ///
    /// THE DOCK. Max stands at one side, large, and his card stands in front of his legs: the head,
    /// the pointing arm and the torso show above the card, the rest is behind it. The card's gold name
    /// tab is always on the side away from him — the panel is mirrored when he stands on the left — so
    /// his body never sits on the tab. Max and card move as one block, docked to the bottom of the
    /// safe area unless the thing being taught is there, in which case the block goes to the top. The
    /// camera is never asked to move; the dock moves instead.
    ///
    /// PROPORTIONS ARE THE ART'S. The panel and the DEVAM button are single painted images, not
    /// sliced ones, so their height always follows their width. Max's size follows the screen's height
    /// and his width follows his sprite. Nothing here is stretched to fill a box.
    ///
    /// THE SPOTLIGHT is four shade quads around a hole, solved every frame against a UI rect or a
    /// world object. The quads are buttons (a tap on the dark reports <see cref="ShadeTapped"/>) and
    /// the hole is empty, so a tap inside it lands on whatever is really there — a HUD button, or a
    /// bench in the world. A separate full-screen blocker can close the hole too when a step should
    /// only be looked at. A world target that has left the screen gets an arrow at the edge instead.
    ///
    /// Built in code, added at runtime to its own overlay canvas, so the prefab only carries the art.
    /// </summary>
    public sealed class TutorialPresenter : MonoBehaviour
    {
        public struct Art
        {
            public Sprite Panel;
            public Sprite Button;
            public Sprite Medallion;
            public Sprite SkipPill;
            public Sprite PipOn;
            public Sprite PipOff;
            public Sprite WorldPin;
            public Sprite TapHand;
            /// <summary>Max pointing to the left in the source art. Used whenever a card has a target.</summary>
            public Sprite PointPose;
            /// <summary>Max when a card has no target and the caller gave no pose.</summary>
            public Sprite DefaultPose;
            public TMP_FontAsset Font;
            public Color Shade;
            public Color Ring;
            public float HandTapSeconds;
        }

        public struct Layout
        {
            /// <summary>Widest the card may be, canvas units. Phones are narrower than this and use their safe width.</summary>
            public float CardMaxWidth;
            /// <summary>Max's height as a share of the canvas height, clamped to the two values below.</summary>
            public float NarratorHeightShare;
            public float NarratorMinHeight;
            public float NarratorMaxHeight;
            /// <summary>How far up the card Max's feet stand, as a share of the card's height.</summary>
            public float NarratorSink;
            /// <summary>Gap between the dock and the safe area's edges.</summary>
            public float EdgeMargin;
            /// <summary>Space kept clear under the top of the safe area when docked at the top — the cash and gem bar.</summary>
            public float TopReserve;
        }

        // ------------------------------------------------------------------ measures
        private const float ReferenceWidth = 1080f;
        private const float ReferenceHeight = 1920f;
        private const float RingPad = 26f;          // hole edge to the ring's outer edge
        private const float TargetGap = 36f;        // ring to the dock
        private const float ContinueHeight = 78f;
        private const float SkipWidth = 190f;
        private const float SkipHeight = 66f;
        private const float MedalSize = 96f;
        private const float PipSize = 16f;
        private const float ArrowSize = 96f;
        private const float ArrowInset = 80f;       // arrow centre from the safe edge, top and bottom
        private const float ArrowInsetSide = 190f;  // and from the sides: inside the HUD's button rails
        private const float PinSize = 128f;
        private const float NarratorInset = 30f;    // Max's outer edge inside the card's end; the waving glove reaches past it
        private const float MinHole = 120f;         // smallest spotlight, either side
        private const float WideTarget = 1.6f;      // width/height past which the hand presses off-centre
        private const float ScreenEdgeGap = 4f;     // the ring stops this far inside the screen

        // Where things are inside tutorial_panel_yeni, as shares of its own width and height, measured
        // off the 1999×787 export. The navy field, the gold tab, and the transparent margin around both.
        private const float NavyLeft = 0.0375f, NavyRight = 0.96f, NavyTop = 0.16f, NavyBottom = 0.11f;
        private const float TabLeft = 0.072f, TabRight = 0.36f, TabTop = 0.064f, TabBottom = 0.26f;
        private const float BodyTop = 0.29f;        // just under the tab
        private const float DefaultPanelAspect = 1999f / 787f;

        // ------------------------------------------------------------------ events
        public event Action Continued;
        public event Action Skipped;
        public event Action ShadeTapped;

        /// <summary>
        /// The overlay was switched off — by <see cref="Clear"/> at the end of a flow, or from outside,
        /// when the island's scene is parked for the sea or the market. In that second case every
        /// flow running on this object has just stopped, and its owner has to let go of it.
        /// </summary>
        public event Action Disabled;

        // ------------------------------------------------------------------ parts
        private Art _art;
        private Layout _layout;
        private RectTransform _root;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private Image[] _shade;
        private Image _blocker;
        private Image _ring, _pulse;
        private RectTransform _hand, _pin, _arrow;
        private RectTransform _dock;
        private CanvasGroup _dockFade;
        private Image _narrator;
        private RectTransform _card;
        private RectTransform _panel;
        private TMP_Text _title, _body;
        private RectTransform _medal;
        private Image _icon;
        private RectTransform _continue, _skip, _pips;
        private Image[] _pip = new Image[0];

        // ------------------------------------------------------------------ state
        private RectTransform _uiTarget;
        private Collider _worldArea;
        private Transform _worldPoint;
        private Vector3 _worldPosition;
        private bool _hasWorldPosition;
        private bool _ringOn, _handOn, _showing;
        private bool _dockTop, _narratorLeft, _hasIcon, _continueOn, _skipOn, _pipsOn;
        private Vector2 _laidOutFor;
        private Rect _laidOutSafe;
        private float _shadeAlpha;
        private Coroutine _cardAnim, _shadeAnim;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _boxCorners = new Vector3[8];

        /// <summary>Whether anything of the tutorial is on screen.</summary>
        public bool Visible => _root != null && _root.gameObject.activeSelf;

        /// <summary>The card, while it is up — for callers that need to keep other UI clear of it.</summary>
        public bool CardShowing => _showing;

        /// <summary>Visible and not stepped aside for another screen (<see cref="SetSuspended"/>).</summary>
        public bool OnScreen => Visible && _canvas != null && _canvas.enabled;

        /// <summary>The overlay canvas and this component on it. Nothing is visible until a card or shade is shown.</summary>
        public static TutorialPresenter Create(Art art, Layout layout, int sortingOrder)
        {
            var go = new GameObject("UI_Egitim", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
            var presenter = go.AddComponent<TutorialPresenter>();
            presenter._art = art;
            presenter._layout = layout;
            presenter._root = (RectTransform)go.transform;
            presenter._canvas = canvas;
            presenter._raycaster = go.GetComponent<GraphicRaycaster>();
            presenter.Build();
            go.SetActive(false);
            return presenter;
        }

        // ================================================================== what callers use

        public void SetVisible(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        /// <summary>
        /// Out of sight and out of the way of taps, with everything kept as it was: another screen is
        /// over the island. Switching the canvas off rather than the object keeps the card's slide and
        /// the shade where they were, so the lesson comes back exactly as the player left it.
        /// </summary>
        public void SetSuspended(bool on)
        {
            if (_canvas != null) _canvas.enabled = !on;
            if (_raycaster != null) _raycaster.enabled = !on;
        }

        /// <summary>
        /// Puts up a card. The dock is chosen now, against the current target, and stays put while
        /// the card is up — a target that moves (the carrier) must not make the card jump.
        /// </summary>
        public void ShowCard(string title, string body, Sprite pose, Sprite icon, bool continueButton, bool skipButton)
        {
            SetVisible(true);
            _title.text = title ?? string.Empty;
            _body.text = body ?? string.Empty;
            _hasIcon = icon != null;
            _icon.sprite = icon;
            _medal.gameObject.SetActive(_hasIcon);
            _continueOn = continueButton;
            _skipOn = skipButton;

            Rect target;
            bool hasTarget = TargetRect(out target);
            bool pointing = pose == null && hasTarget && _art.PointPose != null;
            Sprite sprite = pose != null ? pose : (pointing ? _art.PointPose : _art.DefaultPose);
            _narrator.sprite = sprite;
            _narrator.enabled = sprite != null;
            // Max stands opposite the thing he points at; with nothing to point at, on the right,
            // where the source poses face into the screen.
            _narratorLeft = hasTarget && target.center.x >= CanvasSize().x * 0.5f;
            _dockTop = hasTarget && ChooseTop(target);

            _dock.gameObject.SetActive(true);
            LayoutDock();
            _showing = true;
            if (_cardAnim != null) StopCoroutine(_cardAnim);
            _cardAnim = StartCoroutine(CardIn());
        }

        /// <summary>Rewrites the text of the card already up — a live counter — without sliding it again.</summary>
        public void SetBody(string body)
        {
            _body.text = body ?? string.Empty;
        }

        /// <summary>
        /// Adds or removes DEVAM on the card already up. A lesson that waits on the game offers it
        /// late, so a shop that stalls can never hold the player on one card.
        /// </summary>
        public void ShowContinue(bool on)
        {
            if (_continueOn == on) return;
            _continueOn = on;
            LayoutDock();
        }

        /// <summary>Slides the card out. Yield on it so the next card does not start under this one.</summary>
        public IEnumerator HideCard()
        {
            if (!_showing) yield break;
            _showing = false;
            if (_cardAnim != null) StopCoroutine(_cardAnim);
            _cardAnim = null;
            float dir = _dockTop ? 1f : -1f;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.18f;
                float e = t >= 1f ? 1f : t * t;
                _dockFade.alpha = 1f - e;
                _dock.anchoredPosition = DockHome() + new Vector2(0f, e * 70f * dir);
                yield return null;
            }
            _dock.gameObject.SetActive(false);
            _dock.anchoredPosition = DockHome();
            _dockFade.alpha = 1f;
        }

        /// <summary>A HUD control or any other UI rect. Clears any world target.</summary>
        public void TargetUi(RectTransform target)
        {
            ClearTarget();
            _uiTarget = target;
        }

        /// <summary>Something in the world with a size — a bench. The hole is fitted to its collider's bounds.</summary>
        public void TargetWorldArea(Collider area)
        {
            ClearTarget();
            _worldArea = area;
        }

        /// <summary>A moving point in the world — the carrier. Marked by the pin, not a hole.</summary>
        public void TargetWorldPoint(Transform point)
        {
            ClearTarget();
            _worldPoint = point;
        }

        /// <summary>A fixed point in the world. Marked by the pin, not a hole.</summary>
        public void TargetWorldPoint(Vector3 position)
        {
            ClearTarget();
            _worldPosition = position;
            _hasWorldPosition = true;
        }

        public void ClearTarget()
        {
            _uiTarget = null;
            _worldArea = null;
            _worldPoint = null;
            _hasWorldPosition = false;
            SetHole(new Rect());
        }

        /// <summary>The gold ring around the target. The tapping hand shows with it when <paramref name="hand"/> is set.</summary>
        public void SetRing(bool on, bool hand)
        {
            _ringOn = on;
            _handOn = on && hand;
        }

        /// <summary>
        /// Whether the shade is drawn and catches taps. Off, the tutorial is only watching: the world
        /// and the HUD work normally all around the card.
        /// </summary>
        public void SetShadeActive(bool on)
        {
            for (int i = 0; i < _shade.Length; i++) _shade[i].enabled = on;
        }

        /// <summary>Fades the shade to <paramref name="alpha"/>. Yield on it or fire and forget.</summary>
        public IEnumerator FadeShade(float alpha, float seconds)
        {
            float from = _shadeAlpha;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds);
                ApplyShade(Mathf.Lerp(from, alpha, t >= 1f ? 1f : t));
                yield return null;
            }
        }

        public void SetShadeAlpha(float alpha) => ApplyShade(alpha);

        /// <summary>Closes the spotlight's hole to input too: the target can be seen but not pressed.</summary>
        public void BlockInput(bool on)
        {
            _blocker.enabled = on;
            _blocker.raycastTarget = on;
        }

        /// <summary>A row of step dots under the text. <paramref name="count"/> 0 hides them.</summary>
        public void SetPips(int count, int live)
        {
            _pipsOn = count > 0;
            _pips.gameObject.SetActive(_pipsOn);
            if (!_pipsOn) return;
            if (_pip.Length != count) BuildPips(count);
            for (int i = 0; i < _pip.Length; i++)
            {
                bool on = i <= live;
                _pip[i].sprite = on ? _art.PipOn : _art.PipOff;
                _pip[i].color = _pip[i].sprite != null ? Color.white
                              : (on ? _art.Ring : new Color(1f, 1f, 1f, 0.3f));
            }
        }

        /// <summary>Everything off, the overlay hidden. The next <see cref="ShowCard"/> brings it back.</summary>
        public void Clear()
        {
            ClearTarget();
            SetRing(false, false);
            SetPips(0, 0);
            BlockInput(false);
            if (_cardAnim != null) StopCoroutine(_cardAnim);
            _cardAnim = null;
            _showing = false;
            _dock.gameObject.SetActive(false);
            _pin.gameObject.SetActive(false);
            _arrow.gameObject.SetActive(false);
            SetSuspended(false);
            SetVisible(false);
        }

        // ================================================================== per frame

        private void OnDisable() => Disabled?.Invoke();

        private void LateUpdate()
        {
            Vector2 size = CanvasSize();
            Rect safe = SafeRect();
            if (_showing && (size != _laidOutFor || safe != _laidOutSafe)) LayoutDock();

            Rect hole;
            bool haveHole = HoleRect(out hole);
            SetHole(haveHole ? hole : new Rect());

            bool ring = _ringOn && haveHole;
            _ring.enabled = ring;
            _pulse.enabled = ring;
            if (ring)
            {
                PlaceRing(hole);
                float p = Mathf.PingPong(Time.unscaledTime * 1.25f, 1f);
                float s = 1f + 0.09f * p;
                _pulse.rectTransform.localScale = new Vector3(s, s, 1f);
                Color c = _art.Ring; c.a = 0.55f * (1f - p);
                _pulse.color = c;
            }

            bool hand = _handOn && ring && _hand != null;
            if (_hand != null && _hand.gameObject.activeSelf != hand) _hand.gameObject.SetActive(hand);
            if (hand)
            {
                float p = Mathf.PingPong(Time.unscaledTime * (2f / Mathf.Max(0.1f, _art.HandTapSeconds)), 1f);
                float s = 1f - 0.13f * (p * p * (3f - 2f * p));
                _hand.localScale = new Vector3(s, s, 1f);
            }

            PlaceWorldMarkers(safe);
        }

        /// <summary>The pin over an on-screen world target, or the edge arrow toward an off-screen one.</summary>
        private void PlaceWorldMarkers(Rect safe)
        {
            bool point = _worldPoint != null || _hasWorldPosition;
            bool area = _worldArea != null;
            if (!point && !area)
            {
                if (_pin.gameObject.activeSelf) _pin.gameObject.SetActive(false);
                if (_arrow.gameObject.activeSelf) _arrow.gameObject.SetActive(false);
                return;
            }

            Vector3 world = area ? _worldArea.bounds.center : (_worldPoint != null ? _worldPoint.position : _worldPosition);
            Vector2 local;
            bool front = WorldToCanvas(world, out local);
            Rect visible = new Rect(safe.xMin + 20f, safe.yMin + 20f, safe.width - 40f, safe.height - 40f);
            bool onScreen = front && visible.Contains(local);

            bool pin = point && onScreen && _art.WorldPin != null;
            if (_pin.gameObject.activeSelf != pin) _pin.gameObject.SetActive(pin);
            if (pin)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 2.6f) * 12f;
                _pin.anchoredPosition = local + new Vector2(0f, 96f + bob);
            }

            if (_arrow.gameObject.activeSelf == onScreen) _arrow.gameObject.SetActive(!onScreen);
            if (onScreen) return;

            // The arrow runs around the part of the screen the dock leaves free, so it never lands on the card.
            float lo = safe.yMin, hi = safe.yMax;
            if (_showing)
            {
                Vector2 home = DockHome();
                if (_dockTop) hi = Mathf.Min(hi, home.y);
                else lo = Mathf.Max(lo, home.y + DockSize().y);
            }
            Rect edge = new Rect(safe.xMin + ArrowInsetSide, lo + ArrowInset,
                                 Mathf.Max(0f, safe.width - 2f * ArrowInsetSide), Mathf.Max(0f, hi - lo - 2f * ArrowInset));
            // Behind the camera the projection mirrors; flip it so the arrow still points the right way.
            Vector2 centre = edge.center;
            Vector2 dir = local - centre;
            if (!front) dir = -dir;
            if (dir.sqrMagnitude < 1f) dir = Vector2.down;
            float sx = Mathf.Abs(dir.x) > 0.001f ? (edge.width * 0.5f) / Mathf.Abs(dir.x) : float.MaxValue;
            float sy = Mathf.Abs(dir.y) > 0.001f ? (edge.height * 0.5f) / Mathf.Abs(dir.y) : float.MaxValue;
            float k = Mathf.Min(sx, sy);
            float nudge = Mathf.Sin(Time.unscaledTime * 5f) * 8f;
            Vector2 n = dir.normalized;
            _arrow.anchoredPosition = centre + dir * k + n * nudge;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg);
        }

        // ================================================================== layout

        /// <summary>
        /// The canvas's size in its own units, from the screen and the scaler's formula rather than
        /// the root rect: on the first frame after the canvas is built the scaler has not run yet, and
        /// the rect still reads the reference size — which is how the first card came out too wide.
        /// </summary>
        private static Vector2 CanvasSize()
        {
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            float logW = Mathf.Log(w / ReferenceWidth, 2f), logH = Mathf.Log(h / ReferenceHeight, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, 0.5f));
            return new Vector2(w / scale, h / scale);
        }

        /// <summary>The device safe area in canvas units, bottom-left origin.</summary>
        private static Rect SafeRect()
        {
            Vector2 size = CanvasSize();
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            Rect sa = Screen.safeArea;
            if (sa.width <= 0f || sa.height <= 0f) sa = new Rect(0f, 0f, w, h);
            float kx = size.x / w, ky = size.y / h;
            return new Rect(sa.xMin * kx, sa.yMin * ky, sa.width * kx, sa.height * ky);
        }

        private float PanelAspect()
        {
            Sprite s = _art.Panel;
            return s != null ? s.rect.width / Mathf.Max(1f, s.rect.height) : DefaultPanelAspect;
        }

        private Vector2 CardSize()
        {
            Rect safe = SafeRect();
            float width = Mathf.Min(safe.width - 2f * _layout.EdgeMargin, _layout.CardMaxWidth);
            return new Vector2(width, width / PanelAspect());
        }

        private Vector2 NarratorSize()
        {
            Sprite s = _narrator.sprite;
            if (s == null) return Vector2.zero;
            float height = Mathf.Clamp(CanvasSize().y * _layout.NarratorHeightShare,
                                       _layout.NarratorMinHeight, _layout.NarratorMaxHeight);
            float width = height * s.rect.width / Mathf.Max(1f, s.rect.height);
            // A wide pose on a narrow card is kept to the card's width; its height follows.
            float maxWidth = CardSize().x * 0.62f;
            if (width > maxWidth)
            {
                height *= maxWidth / width;
                width = maxWidth;
            }
            return new Vector2(width, height);
        }

        private Vector2 DockSize()
        {
            Vector2 card = CardSize();
            Vector2 max = NarratorSize();
            return new Vector2(card.x, Mathf.Max(card.y, card.y * _layout.NarratorSink + max.y));
        }

        /// <summary>Where the dock's bottom-left corner sits on the canvas.</summary>
        private Vector2 DockHome()
        {
            Rect safe = SafeRect();
            Vector2 dock = DockSize();
            float x = safe.xMin + (safe.width - dock.x) * 0.5f;
            float bottom = safe.yMin + _layout.EdgeMargin;
            if (!_dockTop) return new Vector2(x, bottom);
            float y = safe.yMax - _layout.TopReserve - dock.y;
            return new Vector2(x, Mathf.Max(bottom, y));
        }

        /// <summary>
        /// Top or bottom, whichever leaves the target clear. The bottom is preferred — the thumb is
        /// there, and a card there covers sea rather than the HUD — so the top is used only when the
        /// bottom block would sit on the target, and when both would, the smaller overlap wins.
        /// </summary>
        private bool ChooseTop(Rect target)
        {
            float lo = target.yMin - RingPad - TargetGap, hi = target.yMax + RingPad + TargetGap;
            bool was = _dockTop;
            _dockTop = false;
            Vector2 bottomHome = DockHome();
            _dockTop = true;
            Vector2 topHome = DockHome();
            _dockTop = was;
            float h = DockSize().y;
            float bottomOver = Mathf.Min(bottomHome.y + h, hi) - Mathf.Max(bottomHome.y, lo);
            if (bottomOver <= 0f) return false;
            float topOver = Mathf.Min(topHome.y + h, hi) - Mathf.Max(topHome.y, lo);
            if (topOver <= 0f) return true;
            return topOver < bottomOver;
        }

        private void LayoutDock()
        {
            _laidOutFor = CanvasSize();
            _laidOutSafe = SafeRect();

            Vector2 card = CardSize();
            Vector2 max = NarratorSize();
            Vector2 dock = DockSize();
            _dock.sizeDelta = dock;
            _dock.anchoredPosition = DockHome();

            _card.sizeDelta = card;
            _card.anchoredPosition = Vector2.zero;

            // Max: feet up the card, outer edge just inside the card's end, facing inward.
            RectTransform m = _narrator.rectTransform;
            m.sizeDelta = max;
            float mx = _narratorLeft ? NarratorInset + max.x * 0.5f : card.x - NarratorInset - max.x * 0.5f;
            m.anchoredPosition = new Vector2(mx, card.y * _layout.NarratorSink);
            m.localScale = new Vector3(_narratorLeft ? -1f : 1f, 1f, 1f);

            // The panel is mirrored when Max is on the left so its tab stays on the far side from him.
            _panel.localScale = new Vector3(_narratorLeft ? -1f : 1f, 1f, 1f);

            float tabL = _narratorLeft ? 1f - TabRight : TabLeft;
            float tabR = _narratorLeft ? 1f - TabLeft : TabRight;
            Place((RectTransform)_title.transform, card, tabL, tabR, 1f - TabBottom, 1f - TabTop, 14f, 6f);

            // DEVAM bottom right, ATLA bottom left, both inside the navy field.
            float navyL = card.x * NavyLeft, navyR = card.x * NavyRight, navyB = card.y * NavyBottom;
            float buttonY = navyB + 12f;
            float continueW = ContinueHeight * ButtonAspect();
            _continue.sizeDelta = new Vector2(continueW, ContinueHeight);
            _continue.anchoredPosition = new Vector2(navyR - 16f - continueW, buttonY);
            _continue.gameObject.SetActive(_continueOn);
            _skip.anchoredPosition = new Vector2(navyL + 20f, buttonY + (ContinueHeight - SkipHeight) * 0.5f);
            _skip.gameObject.SetActive(_skipOn);
            _pips.anchoredPosition = new Vector2(card.x * 0.5f, buttonY + ContinueHeight * 0.5f);
            _pips.gameObject.SetActive(_pipsOn);

            bool buttons = _continueOn || _skipOn || _pipsOn;
            float bodyBottom = buttons ? buttonY + ContinueHeight + 10f : navyB + 18f;
            float bodyTop = card.y * (1f - BodyTop);
            float bodyLeft = navyL + 28f;
            if (_hasIcon)
            {
                _medal.anchoredPosition = new Vector2(bodyLeft + MedalSize * 0.5f, (bodyTop + bodyBottom) * 0.5f);
                bodyLeft += MedalSize + 18f;
            }
            RectTransform b = (RectTransform)_body.transform;
            b.anchorMin = b.anchorMax = Vector2.zero;
            b.pivot = Vector2.zero;
            b.anchoredPosition = new Vector2(bodyLeft, bodyBottom);
            b.sizeDelta = new Vector2(navyR - 24f - bodyLeft, Mathf.Max(40f, bodyTop - bodyBottom));
        }

        private float ButtonAspect()
        {
            Sprite s = _art.Button;
            return s != null ? s.rect.width / Mathf.Max(1f, s.rect.height) : 2.8f;
        }

        private static void Place(RectTransform rt, Vector2 box, float xMin, float xMax, float yMin, float yMax,
                                  float padX, float padY)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(box.x * xMin + padX, box.y * yMin + padY);
            rt.sizeDelta = new Vector2(box.x * (xMax - xMin) - 2f * padX, box.y * (yMax - yMin) - 2f * padY);
        }

        /// <summary>
        /// The slide in. Home is asked for every frame, not remembered: a resolution or safe-area change
        /// while the card is sliding would otherwise put it back where the old screen wanted it.
        /// </summary>
        private IEnumerator CardIn()
        {
            float dir = _dockTop ? 1f : -1f;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.34f;
                float e = t >= 1f ? 1f : 1f - Mathf.Pow(1f - t, 3f);           // ease-out-cubic
                _dockFade.alpha = Mathf.Clamp01(t * 1.6f);
                _dock.anchoredPosition = DockHome() + new Vector2(0f, (1f - e) * 40f * dir);
                yield return null;
            }
            _dock.anchoredPosition = DockHome();
            _dockFade.alpha = 1f;
            _cardAnim = null;
        }

        // ================================================================== targets

        /// <summary>The target in canvas units for choosing a dock: its hole, or a pin-sized box around a point.</summary>
        private bool TargetRect(out Rect rect)
        {
            if (HoleRect(out rect)) return true;
            Vector3 world;
            if (_worldPoint != null) world = _worldPoint.position;
            else if (_hasWorldPosition) world = _worldPosition;
            else return false;
            Vector2 local;
            if (!WorldToCanvas(world, out local)) return false;
            rect = new Rect(local.x - PinSize * 0.5f, local.y - 20f, PinSize, PinSize + 110f);
            return true;
        }

        /// <summary>
        /// The spotlight's hole, never smaller than <see cref="MinHole"/> on either side. In the normal
        /// island view the pickaxe bench is about forty units across; a ring that tight disappears
        /// under the tapping hand, and a finger has to find it.
        /// </summary>
        private bool HoleRect(out Rect rect)
        {
            bool found;
            if (_uiTarget != null) found = UiRect(_uiTarget, out rect);
            else if (_worldArea != null) found = WorldRect(_worldArea.bounds, out rect);
            else { rect = new Rect(); return false; }
            if (!found) return false;
            float w = Mathf.Max(rect.width, MinHole), h = Mathf.Max(rect.height, MinHole);
            rect = new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h);
            return true;
        }

        /// <summary>A UI rect in this canvas's bottom-left-origin units.</summary>
        private bool UiRect(RectTransform target, out Rect rect)
        {
            rect = new Rect();
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            target.GetWorldCorners(_corners);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector2 k = new Vector2(CanvasSize().x / Mathf.Max(1f, Screen.width), CanvasSize().y / Mathf.Max(1f, Screen.height));
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, _corners[i]);
                float x = sp.x * k.x, y = sp.y * k.y;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            return rect.width > 1f && rect.height > 1f;
        }

        /// <summary>A world box's screen footprint in canvas units. False when any of it is behind the camera.</summary>
        private bool WorldRect(Bounds bounds, out Rect rect)
        {
            rect = new Rect();
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 c = bounds.center, e = bounds.extents;
            int n = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        _boxCorners[n++] = c + new Vector3(e.x * x, e.y * y, e.z * z);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            Vector2 k = new Vector2(CanvasSize().x / Mathf.Max(1f, Screen.width), CanvasSize().y / Mathf.Max(1f, Screen.height));
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(_boxCorners[i]);
                if (sp.z <= 0f) return false;
                float px = sp.x * k.x, py = sp.y * k.y;
                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
            }
            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            Vector2 size = CanvasSize();
            bool anyOnScreen = rect.xMax > 0f && rect.yMax > 0f && rect.xMin < size.x && rect.yMin < size.y;
            return anyOnScreen && rect.width > 1f && rect.height > 1f;
        }

        private static bool WorldToCanvas(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(world);
            Vector2 size = CanvasSize();
            local = new Vector2(sp.x * size.x / Mathf.Max(1f, Screen.width), sp.y * size.y / Mathf.Max(1f, Screen.height));
            return sp.z > 0f;
        }

        // ================================================================== shade, ring

        private void SetHole(Rect hole)
        {
            Vector2 size = CanvasSize();
            float w = size.x, h = size.y;
            if (hole.width <= 0f || hole.height <= 0f) hole = new Rect(w * 0.5f, h * 0.5f, 0f, 0f);
            float l = Mathf.Clamp(hole.xMin, 0f, w), r = Mathf.Clamp(hole.xMax, 0f, w);
            float b = Mathf.Clamp(hole.yMin, 0f, h), t = Mathf.Clamp(hole.yMax, 0f, h);
            Quad(_shade[0], 0f, t, w, h - t);        // above
            Quad(_shade[1], 0f, 0f, w, b);           // below
            Quad(_shade[2], 0f, b, l, t - b);        // left
            Quad(_shade[3], r, b, w - r, t - b);     // right
        }

        private static void Quad(Image img, float x, float y, float w, float h)
        {
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        private void ApplyShade(float alpha)
        {
            _shadeAlpha = alpha;
            for (int i = 0; i < _shade.Length; i++)
            {
                Color c = _art.Shade; c.a = alpha;
                _shade[i].color = c;
            }
        }

        /// <summary>
        /// The ring around the hole, pulled in where it would leave the screen: the rails' buttons sit
        /// against the edge, and half a ring there looks like a rendering fault rather than a highlight.
        /// </summary>
        private void PlaceRing(Rect hole)
        {
            Vector2 canvas = CanvasSize();
            float l = Mathf.Max(hole.xMin - RingPad, ScreenEdgeGap);
            float r = Mathf.Min(hole.xMax + RingPad, canvas.x - ScreenEdgeGap);
            float b = Mathf.Max(hole.yMin - RingPad, ScreenEdgeGap);
            float t = Mathf.Min(hole.yMax + RingPad, canvas.y - ScreenEdgeGap);
            var ring = new Rect(l, b, Mathf.Max(0f, r - l), Mathf.Max(0f, t - b));
            _ring.rectTransform.anchoredPosition = ring.center;
            _ring.rectTransform.sizeDelta = ring.size;
            _pulse.rectTransform.anchoredPosition = ring.center;
            _pulse.rectTransform.sizeDelta = ring.size;
            if (_hand == null) return;
            float hh = Mathf.Clamp(hole.height * 0.95f, 110f, 190f);
            Sprite s = _art.TapHand;
            float aspect = s != null ? s.rect.width / Mathf.Max(1f, s.rect.height) : 0.75f;
            _hand.sizeDelta = new Vector2(hh * aspect, hh);
            // On a wide button the middle is its label and price; the finger presses toward the right
            // end instead, where it still lands on the button but covers none of the text.
            _hand.anchoredPosition = hole.width > hole.height * WideTarget
                ? new Vector2(hole.xMin + hole.width * 0.8f, hole.center.y)
                : hole.center;
        }

        // ================================================================== taps

        private void OnShadeTap() => ShadeTapped?.Invoke();
        private void OnContinue() => Continued?.Invoke();
        private void OnSkip() => Skipped?.Invoke();

        // ================================================================== build

        private void Build()
        {
            Sprite ringSprite = MakeRing(112, 44f, 9f);

            _shade = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Golge_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_root, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
                _shade[i] = go.GetComponent<Image>();
                var b = go.GetComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(OnShadeTap);
            }
            ApplyShade(0f);
            SetHole(new Rect());

            // Over every game canvas and over the hole, under the card: when on, the target is only
            // something to look at, and DEVAM is the one control that works.
            var blocker = new GameObject("GirisKilidi", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)blocker.transform;
            brt.SetParent(_root, false);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            _blocker = blocker.GetComponent<Image>();
            _blocker.color = new Color(0f, 0f, 0f, 0.001f);
            BlockInput(false);

            _pulse = RingImage("Nabiz", ringSprite);
            _ring = RingImage("Halka", ringSprite);
            Color rc = _art.Ring; rc.a = 0.95f;
            _ring.color = rc;
            _ring.enabled = _pulse.enabled = false;

            if (_art.TapHand != null)
            {
                _hand = Sprite_("El", _art.TapHand, _root);
                _hand.anchorMin = _hand.anchorMax = Vector2.zero;
                _hand.pivot = new Vector2(0.40f, 0.87f);     // the fingertip, so the press lands where it points
                _hand.gameObject.SetActive(false);
            }

            _pin = Sprite_("Igne", _art.WorldPin, _root);
            _pin.anchorMin = _pin.anchorMax = Vector2.zero;
            _pin.sizeDelta = new Vector2(PinSize, PinSize);
            _pin.gameObject.SetActive(false);

            _arrow = Sprite_("Ok", MakeArrow(96), _root);
            _arrow.anchorMin = _arrow.anchorMax = Vector2.zero;
            _arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            _arrow.GetComponent<Image>().color = _art.Ring;
            _arrow.gameObject.SetActive(false);

            BuildDock();
        }

        private void BuildDock()
        {
            var dock = new GameObject("Rehber", typeof(RectTransform), typeof(CanvasGroup));
            _dock = (RectTransform)dock.transform;
            _dock.SetParent(_root, false);
            _dock.anchorMin = _dock.anchorMax = _dock.pivot = Vector2.zero;
            _dockFade = dock.GetComponent<CanvasGroup>();

            // Max first: the card is drawn over his legs.
            RectTransform max = Sprite_("UstaMax", null, _dock);
            max.anchorMin = max.anchorMax = Vector2.zero;
            max.pivot = new Vector2(0.5f, 0f);
            _narrator = max.GetComponent<Image>();

            var card = new GameObject("Kart", typeof(RectTransform));
            _card = (RectTransform)card.transform;
            _card.SetParent(_dock, false);
            _card.anchorMin = _card.anchorMax = _card.pivot = Vector2.zero;

            // The panel is its own child so it can be mirrored without mirroring the words on it.
            var panel = new GameObject("Zemin", typeof(RectTransform), typeof(Image));
            _panel = (RectTransform)panel.transform;
            _panel.SetParent(_card, false);
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.one;
            _panel.offsetMin = _panel.offsetMax = Vector2.zero;
            var pimg = panel.GetComponent<Image>();
            pimg.sprite = _art.Panel != null ? _art.Panel : UiSkin.Panel;
            pimg.type = _art.Panel != null ? Image.Type.Simple : Image.Type.Sliced;
            pimg.preserveAspect = _art.Panel != null;
            // The card takes the taps that land on it, so a tap on the words never reaches a HUD button underneath.
            pimg.raycastTarget = true;

            _title = Text(_card, "Baslik", 36f, TextAlignmentOptions.Center, new Color32(0x58, 0x32, 0x0B, 0xFF));
            _title.enableAutoSizing = true;
            _title.fontSizeMin = 22f;
            _title.fontSizeMax = 40f;
            _title.textWrappingMode = TextWrappingModes.NoWrap;

            var med = new GameObject("Madalyon", typeof(RectTransform), typeof(Image));
            _medal = (RectTransform)med.transform;
            _medal.SetParent(_card, false);
            _medal.anchorMin = _medal.anchorMax = Vector2.zero;
            _medal.sizeDelta = new Vector2(MedalSize, MedalSize);
            var medImg = med.GetComponent<Image>();
            medImg.sprite = _art.Medallion;
            medImg.enabled = _art.Medallion != null;
            medImg.preserveAspect = true;
            medImg.raycastTarget = false;
            RectTransform icon = Sprite_("Simge", null, _medal);
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(MedalSize * 0.62f, MedalSize * 0.62f);
            _icon = icon.GetComponent<Image>();
            _medal.gameObject.SetActive(false);

            _body = Text(_card, "Metin", 34f, TextAlignmentOptions.MidlineLeft, Color.white);
            _body.enableAutoSizing = true;
            _body.fontSizeMin = 28f;
            _body.fontSizeMax = 38f;

            _continue = CardButton("BtnDevam", _art.Button != null ? _art.Button : UiSkin.ButtonBlue,
                                   _art.Button == null, Loc.T("egitim.devam"), OnContinue);
            _skip = CardButton("BtnAtla", _art.SkipPill != null ? _art.SkipPill : UiSkin.Flat,
                               true, Loc.T("egitim.atla"), OnSkip);
            _skip.sizeDelta = new Vector2(SkipWidth, SkipHeight);
            if (_art.SkipPill == null) _skip.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var pips = new GameObject("Adimlar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _pips = (RectTransform)pips.transform;
            _pips.SetParent(_card, false);
            _pips.anchorMin = _pips.anchorMax = Vector2.zero;
            _pips.pivot = new Vector2(0.5f, 0.5f);
            _pips.sizeDelta = new Vector2(300f, 30f);
            var lay = pips.GetComponent<HorizontalLayoutGroup>();
            lay.spacing = 12f;
            lay.childAlignment = TextAnchor.MiddleCenter;
            lay.childForceExpandWidth = false;
            lay.childForceExpandHeight = false;
            _pips.gameObject.SetActive(false);

            _dock.gameObject.SetActive(false);
        }

        private RectTransform CardButton(string name, Sprite sprite, bool sliced, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_card, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = !sliced;
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            TapBounce.Attach(button);
            TMP_Text text = Text(rt, "Yazi", 32f, TextAlignmentOptions.Center, Color.white);
            text.text = label;
            text.enableAutoSizing = true;
            text.fontSizeMin = 22f;
            text.fontSizeMax = 34f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var trt = (RectTransform)text.transform;
            trt.offsetMin = new Vector2(12f, 6f);
            trt.offsetMax = new Vector2(-12f, -6f);
            go.SetActive(false);
            return rt;
        }

        private void BuildPips(int count)
        {
            for (int i = _pips.childCount - 1; i >= 0; i--) Destroy(_pips.GetChild(i).gameObject);
            _pip = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var p = new GameObject("Pip_" + i, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                p.transform.SetParent(_pips, false);
                var le = p.GetComponent<LayoutElement>();
                le.preferredWidth = PipSize;
                le.preferredHeight = PipSize;
                _pip[i] = p.GetComponent<Image>();
                _pip[i].raycastTarget = false;
                _pip[i].preserveAspect = true;
            }
        }

        private Image RingImage(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = _art.Ring;
            img.raycastTarget = false;
            return img;
        }

        private static RectTransform Sprite_(string name, Sprite sprite, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return rt;
        }

        private TMP_Text Text(RectTransform parent, string name, float size, TextAlignmentOptions align, Color ink)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var t = go.AddComponent<TextMeshProUGUI>();
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (_art.Font != null) t.font = _art.Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = ink;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// The highlight outline, generated rather than authored: it has to fit a 1200-wide button and a
        /// 150-wide pill equally well, which a stretched PNG cannot do. A rounded-rect stroke drawn from
        /// its signed distance field, then 9-sliced past the corner radius so the corners never squash.
        /// </summary>
        private static Sprite MakeRing(int size, float radius, float stroke)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - inner;
                    float dy = Mathf.Abs(y + 0.5f - half) - inner;
                    float qx = Mathf.Max(dx, 0f), qy = Mathf.Max(dy, 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    float a = 1f - Mathf.Clamp01((Mathf.Abs(d) - stroke * 0.5f) / 1.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float border = radius + stroke;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>
        /// The edge arrow, generated for the same reason as the ring: a filled chevron pointing along +X,
        /// with a one-pixel soft edge. There is no arrow in the UI kit, and a rotated pin reads as a pin.
        /// </summary>
        private static Sprite MakeArrow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float s = size;
            // Triangle tip at the right, base on the left, with a notch cut into the base.
            Vector2 tip = new Vector2(s * 0.92f, s * 0.5f);
            Vector2 top = new Vector2(s * 0.12f, s * 0.9f);
            Vector2 bottom = new Vector2(s * 0.12f, s * 0.1f);
            Vector2 notch = new Vector2(s * 0.36f, s * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Max(Mathf.Max(Edge(top, tip, p), Edge(tip, bottom, p)),
                                        Mathf.Min(Edge(bottom, notch, p), Edge(notch, top, p)));
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Signed distance from <paramref name="p"/> to the line a→b, positive on its left-hand side. The
        /// arrow's outline runs clockwise, so left of an edge is outside the shape.
        /// </summary>
        private static float Edge(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 d = (b - a).normalized;
            return (p.y - a.y) * d.x - (p.x - a.x) * d.y;
        }

        private void OnDestroy()
        {
            Continued = null;
            Skipped = null;
            ShadeTapped = null;
        }
    }
}
