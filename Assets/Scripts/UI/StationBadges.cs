using Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Small floating level chips over each station — "REFINERY 23/50". Purely informative: buying
    /// happens in the upgrade panel, so the chips carry no button and no price, just how far along
    /// each building is.
    ///
    /// Screen-space canvas anchored to world points via <see cref="Camera.WorldToScreenPoint"/> rather than
    /// a world-space canvas: the chips stay a constant, readable pixel size at every zoom level, and there
    /// is no depth sorting to fight with the buildings.
    /// </summary>
    public sealed class StationBadges : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.2f;
        // A chip is a label, not a panel: small enough that eight of them stacked leave the island
        // readable. 8 × (40 + 10) = 400 against roughly 1540 usable reference pixels.
        [SerializeField] private float cardWidth = 178f;
        [SerializeField] private float cardHeight = 40f;
        [SerializeField] private float worldYOffset = 5f;      // lift above the building's roof
        [SerializeField] private float safeTopPx = 210f;       // keep chips clear of the cash bar
        [SerializeField] private float safeBottomPx = 170f;    // and of the MAP / UPGRADES row
        // A chip centred on its building covers the building. Offsetting it sideways keeps the chain the
        // player is actually watching — mine, rails, trucks, piles — visible down the middle of the screen.
        [SerializeField] private float sideOffsetPx = 132f;
        [SerializeField] private float stackGapPx = 10f;       // clear space between de-overlapped chips
        // Which side the column of chips sits on. The yards sit to the right of the chain on the authored
        // islands, so the left is the open side — flip this if you mirror a layout.
        [SerializeField] private bool badgesOnLeft = true;

        private CoalOperation _op;
        private Camera _cam;
        private Canvas _canvas;
        private Font _font;
        private float _timer;

        private sealed class Card
        {
            public int station;
            public RectTransform rt;
            public Button button;
            public Text title;
            public Vector3 anchor;
            public bool hasAnchor;
            public float punch;           // decays after a level-up, drives the confirm pop
            public int lastTotal = -1;
            public string lastTitle;
        }
        private Card[] _cards;

        private static readonly Color CardBg = new Color(0.10f, 0.13f, 0.18f, 0.88f);

        private void Start()
        {
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
            // The raycaster is what makes the chips tappable at all — a Canvas on its own receives no
            // pointer events, so without it the buttons below would look pressable and do nothing.
            var go = new GameObject("StationBadgeCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;                     // under the HUD (100) so panels cover the chips
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

            var root = new GameObject("Badge_" + station, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            c.rt = (RectTransform)root.transform;
            c.rt.anchorMin = Vector2.zero; c.rt.anchorMax = Vector2.zero; c.rt.pivot = new Vector2(0.5f, 0f);
            c.rt.sizeDelta = new Vector2(cardWidth, cardHeight);
            var bg = root.GetComponent<Image>();
            bg.sprite = UiSkin.Panel; bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.2f;   // keep the 9-slice corners from swallowing a small chip
            // The chip is the one place the kit art *is* tinted: it's a light sprite, and the station
            // name sits on it in white. Tinting keeps the rounded shape and outline but darkens the fill.
            bg.color = CardBg;

            // The chip is the building's handle. It used to be inert — every purchase meant opening one
            // long list and finding the row for the building you were already looking at, which is why the
            // map read as something to watch rather than something to play. Tapping it now opens the panel
            // already scrolled to that station's upgrades.
            bg.raycastTarget = true;
            c.button = root.GetComponent<Button>();
            c.button.targetGraphic = bg;
            var colors = c.button.colors;
            colors.highlightedColor = colors.pressedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.fadeDuration = 0.08f;
            c.button.colors = colors;
            int cs = station;
            c.button.onClick.AddListener(() => OpenStation(cs));

            c.title = Label(c.rt, "Title", "", 15, TextAnchor.MiddleCenter);
            // "POWER PLANT 100/100" is half again the width of "MINE 4/100", so a fixed size either spills
            // the long names outside the chip or shrinks the short ones to nothing. Best-fit sizes each
            // chip's own text to its own pill instead, inside a range that stays legible on a phone. The
            // ceiling is low enough that even "POWER PLANT LOCKED" stays on one line: best-fit wraps
            // before it shrinks, and a chip on two lines stops reading as a label and starts reading
            // as a sign.
            c.title.resizeTextForBestFit = true;
            c.title.resizeTextMinSize = 10;
            c.title.resizeTextMaxSize = 13;
            c.title.horizontalOverflow = HorizontalWrapMode.Wrap;
            c.title.verticalOverflow = VerticalWrapMode.Truncate;
            c.title.rectTransform.offsetMin = new Vector2(9f, 0f);
            c.title.rectTransform.offsetMax = new Vector2(-9f, 0f);
            return c;
        }

        /// <summary>
        /// Opens the upgrade panel on this station's rows. Looked up rather than wired in the Inspector
        /// because the chips are built at runtime and the panel lives on its own scene root.
        /// </summary>
        private void OpenStation(int station)
        {
            if (_panel == null) _panel = FindAnyObjectByType<UpgradePanelUI>(FindObjectsInactive.Include);
            if (_panel != null) _panel.OpenAtStation(station);
        }

        private UpgradePanelUI _panel;

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
                Vector3 w = Vector3.zero;   // the fleet stations short-circuit before StationAnchor writes it
                int st = _cards[i].station;
                _cards[i].hasAnchor = _op.StationHasBody(st) && _op.StationAnchor(st, out w);
                _cards[i].anchor = w + Vector3.up * worldYOffset;
                if (_cards[i].hasAnchor) any = true;
            }
            return any;
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
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
        /// Places every visible chip, in three steps: project its building to screen space and push the
        /// chip to one side of it, resolve chips that would sit on top of each other, then apply.
        ///
        /// The de-overlap matters more than it sounds. Eight stations on a chain that runs down a portrait
        /// screen put every chip in the same narrow column, so without this they stack into one unreadable
        /// pile that hides the mine, the rails and the tunnel completely.
        /// </summary>
        private void Position()
        {
            float sf = _canvas != null && _canvas.scaleFactor > 0.0001f ? _canvas.scaleFactor : 1f;
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

                // Push the chip to one consistent side of its building. Picking the side by screen centre
                // instead made the column zigzag across the chain, which put chips straight onto the ore
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

            // Lift any chip that would land on the one below it. Chips far enough apart horizontally are
            // left alone — two stations side by side on screen don't need separating.
            float minGap = cardHeight + stackGapPx;
            for (int a = 1; a < live; a++)
            {
                int cur = _order[a], below = _order[a - 1];
                if (Mathf.Abs(_wantX[cur] - _wantX[below]) > cardWidth) continue;
                float floorY = _wantY[below] + minGap;
                if (_wantY[cur] < floorY) _wantY[cur] = floorY;
            }

            // Lifting can push the top chip under the cash bar; slide the whole stack back down if so.
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
                float s = 1f;
                if (c.punch > 0f)
                {
                    c.punch = Mathf.Max(0f, c.punch - Time.unscaledDeltaTime * 3.5f);
                    s += 0.28f * c.punch * c.punch;   // fast pop, soft settle
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

                // Plain station name, not PowerPlantName: "COPPER POWER PLANT LOCKED" is half again as
                // long as any other chip and wrapped onto a second line. Which island's ore it is is not
                // in question while you are standing on it.
                string name = _op.StationName(c.station);
                int total = _op.StationLevelTotal(c.station);

                string title = _op.AxisLocked(c.station, 0)
                    ? name + "  LOCKED"
                    : name + "  " + total + "/" + _op.StationLevelCap(c.station);

                // pop when the level ticks up — the purchase itself happens over in the upgrade panel,
                // so this is the only in-world confirmation that something got better here
                if (c.lastTotal >= 0 && total > c.lastTotal) c.punch = 1f;
                c.lastTotal = total;

                if (c.lastTitle != title) { c.title.text = title; c.lastTitle = title; }
                // scale is owned by Position() every frame (punch) — don't stomp it here
            }
        }

        private const int CoalOperationPowerStation = 7;

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
