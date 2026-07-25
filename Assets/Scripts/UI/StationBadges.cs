using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Floating upgrade cards that hover over each station — the thing that makes an idle tycoon read as
    /// one at a glance. Each card shows the station, its level, and the price of its next upgrade, and
    /// buying happens right there in the world instead of only inside a menu.
    ///
    /// Screen-space canvas anchored to world points via <see cref="Camera.WorldToScreenPoint"/> rather than
    /// a world-space canvas: the cards stay a constant, readable pixel size at every zoom level, and there
    /// is no depth sorting to fight with the buildings.
    /// </summary>
    public sealed class StationBadges : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.2f;
        [SerializeField] private float cardWidth = 208f;
        [SerializeField] private float cardHeight = 116f;
        [SerializeField] private float worldYOffset = 5f;      // lift above the building's roof
        [SerializeField] private float pulseSpeed = 3.2f;      // affordable cards breathe
        [SerializeField] private float pulseAmount = 0.045f;
        [SerializeField] private float safeTopPx = 210f;       // keep cards clear of the cash bar
        [SerializeField] private float safeBottomPx = 170f;    // and of the MAP / UPGRADES row

        private WalletService _wallet;
        private CoalOperation _op;
        private Camera _cam;
        private Canvas _canvas;
        private Font _font;
        private float _timer;

        private sealed class Card
        {
            public int station;
            public RectTransform rt;
            public Image bg;
            public Image btnBg;
            public Button btn;
            public Text title;
            public Text cost;
            public Vector3 anchor;
            public bool hasAnchor;
            public bool affordable;
            public float punch;           // decays after a purchase, drives the confirm pop
            public string lastTitle, lastCost;
        }
        private Card[] _cards;

        private static readonly Color CardBg = new Color(0.10f, 0.13f, 0.18f, 0.94f);
        private static readonly Color Buy = new Color(0.24f, 0.72f, 0.35f, 1f);
        private static readonly Color Cant = new Color(0.30f, 0.33f, 0.39f, 1f);
        private static readonly Color Done = new Color(0.17f, 0.24f, 0.33f, 1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _cam = Camera.main;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BindEnabledOp();
            Build();
            Refresh();
        }

        /// <summary>Retarget onto another island's operation (world-map travel).</summary>
        public void SetOperation(CoalOperation op)
        {
            if (op == null) return;
            _op = op;
            _anchorsResolved = CacheAnchors();
            Refresh();
        }

        private bool _anchorsResolved;

        private void BindEnabledOp()
        {
            var ops = FindObjectsByType<CoalOperation>();
            for (int i = 0; i < ops.Length; i++)
                if (ops[i].enabled) { _op = ops[i]; return; }
            if (_op == null && ops.Length > 0) _op = ops[0];
        }

        private void Build()
        {
            var go = new GameObject("StationBadgeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;                     // under the HUD (100) so panels cover the cards
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;

            int n = _op != null ? _op.StationCount : 8;
            _cards = new Card[n];
            for (int s = 0; s < n; s++) _cards[s] = BuildCard((RectTransform)go.transform, s);
            CacheAnchors();
        }

        private Card BuildCard(RectTransform parent, int station)
        {
            var c = new Card { station = station };

            var root = new GameObject("Badge_" + station, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            c.rt = (RectTransform)root.transform;
            c.rt.anchorMin = Vector2.zero; c.rt.anchorMax = Vector2.zero; c.rt.pivot = new Vector2(0.5f, 0f);
            c.rt.sizeDelta = new Vector2(cardWidth, cardHeight);
            c.bg = root.GetComponent<Image>();
            c.bg.sprite = UiSkin.Panel; c.bg.type = Image.Type.Sliced;
            c.bg.pixelsPerUnitMultiplier = 2.2f;   // keep the 9-slice corners from swallowing a small card
            // The card is the one place the kit art *is* tinted: it's a light sprite, and the station
            // name sits on it in white. Tinting keeps the rounded shape and outline but darkens the fill.
            c.bg.color = CardBg;
            c.bg.raycastTarget = false;

            c.title = Label(c.rt, "Title", "", 25, TextAnchor.MiddleCenter);
            c.title.rectTransform.anchorMin = new Vector2(0f, 0.52f);
            c.title.rectTransform.anchorMax = new Vector2(1f, 1f);
            c.title.rectTransform.offsetMin = Vector2.zero; c.title.rectTransform.offsetMax = Vector2.zero;

            int captured = station;
            var btnGO = new GameObject("Buy", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(c.rt, false);
            c.btnBg = btnGO.GetComponent<Image>();
            c.btnBg.sprite = UiSkin.ButtonGreen; c.btnBg.type = Image.Type.Sliced;
            c.btnBg.pixelsPerUnitMultiplier = 2.6f;
            c.btnBg.color = UiSkin.HasArt ? Color.white : Buy;
            var brt = (RectTransform)btnGO.transform;
            brt.anchorMin = new Vector2(0.06f, 0.08f); brt.anchorMax = new Vector2(0.94f, 0.48f);
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            c.btn = btnGO.GetComponent<Button>();
            c.btn.onClick.AddListener(() => OnBuy(captured));

            c.cost = Label(brt, "Cost", "", 26, TextAnchor.MiddleCenter);
            return c;
        }

        /// <summary>
        /// Station anchors are static geometry, so they're resolved once per island rather than per frame.
        /// Returns false while the operation hasn't finished resolving its landmarks — travelling calls
        /// SetOperation in the same frame the component is enabled, which is before its Start() has run,
        /// so a single attempt would cache "no anchor" for every card and never recover.
        /// </summary>
        private bool CacheAnchors()
        {
            if (_cards == null || _op == null) return false;
            bool any = false;
            for (int i = 0; i < _cards.Length; i++)
            {
                Vector3 w;
                _cards[i].hasAnchor = _op.StationAnchor(_cards[i].station, out w);
                _cards[i].anchor = w + Vector3.up * worldYOffset;
                if (_cards[i].hasAnchor) any = true;
            }
            return any;
        }

        private void OnBuy(int station)
        {
            if (_op == null) return;
            int axis = CheapestAxis(station);
            if (axis >= 0 && _op.TryUpgrade(station, axis) && _cards != null)
            {
                for (int i = 0; i < _cards.Length; i++)
                    if (_cards[i].station == station) _cards[i].punch = 1f;
            }
            Refresh();
        }

        /// <summary>The next thing worth buying at this station: cheapest axis that isn't maxed or locked.</summary>
        private int CheapestAxis(int station)
        {
            if (_op == null) return -1;
            int best = -1;
            BigDouble bestCost = default(BigDouble);
            int n = _op.AxisCount(station);
            for (int a = 0; a < n; a++)
            {
                if (_op.AxisLocked(station, a) || _op.AxisMaxed(station, a)) continue;
                BigDouble c = _op.AxisCost(station, a);
                if (best < 0 || c < bestCost) { best = a; bestCost = c; }
            }
            return best;
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null || !_op.enabled) { BindEnabledOp(); _anchorsResolved = CacheAnchors(); }
            if (_cards == null || _cam == null) return;
            // keep retrying until the operation has resolved its landmarks
            if (!_anchorsResolved) _anchorsResolved = CacheAnchors();

            Position();

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        private void Position()
        {
            float sf = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
            for (int i = 0; i < _cards.Length; i++)
            {
                Card c = _cards[i];
                if (!c.hasAnchor) { if (c.rt.gameObject.activeSelf) c.rt.gameObject.SetActive(false); continue; }

                Vector3 sp = _cam.WorldToScreenPoint(c.anchor);
                bool visible = sp.z > 0f
                               && sp.x > -cardWidth * sf && sp.x < Screen.width + cardWidth * sf
                               && sp.y > -cardHeight * sf && sp.y < Screen.height + cardHeight * sf;
                if (c.rt.gameObject.activeSelf != visible) c.rt.gameObject.SetActive(visible);
                if (!visible) continue;

                // Keep the whole card on screen when its building sits near an edge — a half-clipped
                // price is worse than a card that leans in a little.
                float halfW = cardWidth * 0.5f;
                float refW = Screen.width / sf, refH = Screen.height / sf;
                float px = Mathf.Clamp(sp.x / sf, halfW + 8f, refW - halfW - 8f);
                float py = Mathf.Clamp(sp.y / sf, safeBottomPx, refH - cardHeight - safeTopPx);
                c.rt.anchoredPosition = new Vector2(px, py);
                float s = c.affordable ? pulse : 1f;
                if (c.punch > 0f)
                {
                    c.punch = Mathf.Max(0f, c.punch - Time.unscaledDeltaTime * 3.5f);
                    s *= 1f + 0.28f * c.punch * c.punch;   // fast pop, soft settle
                }
                c.rt.localScale = new Vector3(s, s, 1f);
            }
        }

        private void Refresh()
        {
            if (_cards == null || _op == null) return;
            for (int i = 0; i < _cards.Length; i++)
            {
                Card c = _cards[i];
                if (!c.hasAnchor) continue;

                string name = c.station == CoalOperationPowerStation ? _op.PowerPlantName : _op.StationName(c.station);
                int axis = CheapestAxis(c.station);

                string title, cost;
                if (axis < 0)
                {
                    // every axis here is either maxed or gated behind the power plant
                    bool locked = _op.AxisLocked(c.station, 0);
                    title = name;
                    cost = locked ? "LOCKED" : "MAX";
                    c.affordable = false;
                    SetButtonState(c, UiSkin.ButtonBlue, Done);
                    c.btn.interactable = false;
                }
                else
                {
                    BigDouble price = _op.AxisCost(c.station, axis);
                    bool afford = _wallet != null && _wallet.CanAfford(price);
                    title = name + "  Lv " + _op.AxisLevel(c.station, axis);
                    cost = "$" + NumberFormatter.Format(price);
                    c.affordable = afford;
                    SetButtonState(c, afford ? UiSkin.ButtonGreen : UiSkin.ButtonGrey, afford ? Buy : Cant);
                    c.btn.interactable = afford;
                }

                if (c.lastTitle != title) { c.title.text = title; c.lastTitle = title; }
                if (c.lastCost != cost) { c.cost.text = cost; c.lastCost = cost; }
                // scale is owned by Position() every frame (pulse + punch) — don't stomp it here
            }
        }

        private const int CoalOperationPowerStation = 7;

        /// <summary>Pre-coloured kit art selects state by sprite; the flat fallback selects it by tint.</summary>
        private static void SetButtonState(Card c, Sprite art, Color tint)
        {
            if (UiSkin.HasArt) { c.btnBg.sprite = art; c.btnBg.color = Color.white; }
            else c.btnBg.color = tint;
        }

        // ---- tiny builders (same style as CoalHud) ----
        private Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.alignment = anchor;
            t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return t;
        }
    }
}
