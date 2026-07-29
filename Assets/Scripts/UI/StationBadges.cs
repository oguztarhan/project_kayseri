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
        // Sized so all eight stations can be de-overlapped inside the safe area of a portrait screen:
        // 8 × (94 + 12) = 848 against roughly 1540 usable reference pixels.
        [SerializeField] private float cardWidth = 176f;
        [SerializeField] private float cardHeight = 94f;
        [SerializeField] private float worldYOffset = 5f;      // lift above the building's roof
        [SerializeField] private float pulseSpeed = 3.2f;      // affordable cards breathe
        [SerializeField] private float pulseAmount = 0.045f;
        [SerializeField] private float safeTopPx = 210f;       // keep cards clear of the cash bar
        [SerializeField] private float safeBottomPx = 170f;    // and of the MAP / UPGRADES row
        // A card centred on its building covers the building. Offsetting it sideways keeps the chain the
        // player is actually watching — mine, rails, trucks, piles — visible down the middle of the screen.
        [SerializeField] private float sideOffsetPx = 132f;
        [SerializeField] private float stackGapPx = 12f;       // clear space between de-overlapped cards
        // Which side the column of cards sits on. The yards sit to the right of the chain on the authored
        // islands, so the left is the open side — flip this if you mirror a layout.
        [SerializeField] private bool badgesOnLeft = true;

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

            c.title = Label(c.rt, "Title", "", 21, TextAnchor.MiddleCenter);
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

            c.cost = Label(brt, "Cost", "", 23, TextAnchor.MiddleCenter);
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

        // Scratch buffers for the layout pass, allocated once — Position runs every frame.
        private int[] _order;
        private float[] _wantX, _wantY;

        /// <summary>
        /// Places every visible card, in three steps: project its building to screen space and push the
        /// card to one side of it, resolve cards that would sit on top of each other, then apply.
        ///
        /// The de-overlap matters more than it sounds. Eight stations on a chain that runs down a portrait
        /// screen put every card in the same narrow column, so without this they stack into one unreadable
        /// pile that hides the mine, the rails and the tunnel completely.
        /// </summary>
        private void Position()
        {
            float sf = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
            float refW = Screen.width / sf, refH = Screen.height / sf;
            float halfW = cardWidth * 0.5f;
            float ceilY = refH - cardHeight - safeTopPx;

            if (_order == null || _order.Length != _cards.Length)
            {
                _order = new int[_cards.Length];
                _wantX = new float[_cards.Length];
                _wantY = new float[_cards.Length];
            }

            int live = 0;
            for (int i = 0; i < _cards.Length; i++)
            {
                Card c = _cards[i];
                if (!c.hasAnchor) { SetShown(c, false); continue; }

                Vector3 sp = _cam.WorldToScreenPoint(c.anchor);
                bool visible = sp.z > 0f
                               && sp.x > -cardWidth * sf && sp.x < Screen.width + cardWidth * sf
                               && sp.y > -cardHeight * sf && sp.y < Screen.height + cardHeight * sf;
                SetShown(c, visible);
                if (!visible) continue;

                // Push the card to one consistent side of its building. Picking the side by screen centre
                // instead made the column zigzag across the chain, which put cards straight onto the ore
                // and bar yards — the two things a player most wants to watch. Only swing to the far side
                // when the preferred one would run off the edge.
                float x = sp.x / sf;
                float lean = badgesOnLeft ? -sideOffsetPx : sideOffsetPx;
                float want = x + lean;
                if (want - halfW < 8f || want + halfW > refW - 8f) want = x - lean;
                _wantX[i] = Mathf.Clamp(want, halfW + 8f, refW - halfW - 8f);
                _wantY[i] = Mathf.Clamp(sp.y / sf, safeBottomPx, ceilY);
                _order[live++] = i;
            }

            // Sort bottom-to-top. Insertion sort: live is at most 8, and it allocates nothing.
            for (int a = 1; a < live; a++)
            {
                int key = _order[a], b = a - 1;
                while (b >= 0 && _wantY[_order[b]] > _wantY[key]) { _order[b + 1] = _order[b]; b--; }
                _order[b + 1] = key;
            }

            // Lift any card that would land on the one below it. Cards far enough apart horizontally are
            // left alone — two stations side by side on screen don't need separating.
            float minGap = cardHeight + stackGapPx;
            for (int a = 1; a < live; a++)
            {
                int cur = _order[a], below = _order[a - 1];
                if (Mathf.Abs(_wantX[cur] - _wantX[below]) > cardWidth) continue;
                float floorY = _wantY[below] + minGap;
                if (_wantY[cur] < floorY) _wantY[cur] = floorY;
            }

            // Lifting can push the top card under the cash bar; slide the whole stack back down if so.
            if (live > 0)
            {
                float overshoot = _wantY[_order[live - 1]] - ceilY;
                if (overshoot > 0f)
                    for (int a = 0; a < live; a++)
                        _wantY[_order[a]] = Mathf.Max(safeBottomPx, _wantY[_order[a]] - overshoot);
            }

            for (int a = 0; a < live; a++)
            {
                int i = _order[a];
                Card c = _cards[i];
                c.rt.anchoredPosition = new Vector2(_wantX[i], _wantY[i]);
                float s = c.affordable ? pulse : 1f;
                if (c.punch > 0f)
                {
                    c.punch = Mathf.Max(0f, c.punch - Time.unscaledDeltaTime * 3.5f);
                    s *= 1f + 0.28f * c.punch * c.punch;   // fast pop, soft settle
                }
                c.rt.localScale = new Vector3(s, s, 1f);
            }
        }

        private static void SetShown(Card c, bool on)
        {
            if (c.rt.gameObject.activeSelf != on) c.rt.gameObject.SetActive(on);
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
