using Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The chain of islands along the bottom of the world map: eight nodes on a sea route, the owned
    /// ones lit in their ore colour and the rest dark.
    ///
    /// WHY. The screen is called the world map and was a single medallion on a flat fill. A carousel
    /// tells you what island you are looking at; it does not tell you where that island is, how far you
    /// have come, or how much is left — which is the one thing a map is for, and the reason the screen
    /// photographed as half empty.
    ///
    /// The carousel is deliberately untouched. It works, the buy/confirm/sail flow around it is well
    /// tested, and replacing it would put all of that at risk to solve a problem that is really about
    /// the empty space beside it. This is a layer underneath, not a rewrite.
    ///
    /// Built in code and parented under the map's own panel, so it needs nothing wired in the
    /// UI_Harita prefab — the same trade ForemanRosterUI and GoalsUI make.
    /// </summary>
    public sealed class MapArchipelago
    {
        private readonly WorldIslands _world;
        private RectTransform _root;
        private Image[] _node;
        private Image[] _ring;
        private Image[] _legDot;      // flattened: (count-1) legs x DotsPerLeg
        private Sprite[] _icons;      // ada başına cevher rozeti; boşsa düğüm düz disk kalır
        private Sprite _glowSprite;   // bakılan adanın arkasındaki hale; boşsa UiSkin.Pill

        private const int DotsPerLeg = 5;

        /// <summary>The colour of the spotlight on the island being looked at.</summary>
        private static readonly Color Isik = new Color(1f, 0.95f, 0.80f, 0.95f);

        private int _highlight = -1;
        private float _pulse;

        // NO MARKER FOR THE ISLAND YOU ARE ON. There was one — a small rect parked over that node —
        // and it never had any boat art to wear, so it fell back to UiSkin.Pill and drew as a plain
        // blue rectangle sitting on top of the coal badge. The screen already says where you are
        // twice over: the halo burns behind the island the carousel is showing, and the call to
        // action reads YOU ARE HERE. A third telling, drawn as a stray square, was worth less than
        // the pixels it covered.

        public MapArchipelago(WorldIslands world) { _world = world; }

        public bool Ready => _root != null;

        /// <summary>
        /// Lays the chain out inside <paramref name="parent"/>. Anchors are fractions so the ribbon
        /// keeps its place whatever the canvas is; it belongs in the band below the medallion and the
        /// call-to-action, which on the landscape layout is everything under about -0.40.
        /// </summary>
        public void Build(RectTransform parent, int siblingIndex, Vector2 aMin, Vector2 aMax,
                          Color routeColor, Color lockedColor, float nodeSize,
                          Sprite[] oreIcons, Sprite glowSprite)
        {
            if (_world == null || parent == null) return;
            int n = _world.Count;
            if (n <= 0) return;
            _icons = oreIcons;
            _glowSprite = glowSprite;

            _root = UiBuild.Anchor(NewRect(parent, "Takimadalari"), aMin, aMax);
            _root.SetSiblingIndex(siblingIndex);

            _node = new Image[n];
            _ring = new Image[n];
            _legDot = new Image[Mathf.Max(0, n - 1) * DotsPerLeg];

            // Nodes sit on an even spread with a gentle sine lift, so the route reads as a chain
            // crossing open water rather than as a progress bar with circles on it.
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) : 0.5f;

                // Legs first, so every dot sits behind the nodes it joins.
                if (i > 0)
                {
                    float tPrev = (i - 1) / (float)(n - 1);
                    for (int d = 0; d < DotsPerLeg; d++)
                    {
                        float td = Mathf.Lerp(tPrev, t, (d + 1f) / (DotsPerLeg + 1f));
                        var dot = UiBuild.Flat(_root, "Yol", routeColor, Vector2.zero, Vector2.zero);
                        Place(dot, td, nodeSize * 0.16f);
                        _legDot[(i - 1) * DotsPerLeg + d] = dot.GetComponent<Image>();
                    }
                }

                Sprite icon = Icon(i);

                // Halka artık rozetin arkasındaki madalyon değil, sadece BAKILAN adanın arkasında
                // yanan yumuşak hale. Her rozetin altına disk koymak sekiz ada arasında hangisinde
                // olduğunu söylemiyordu — hepsi aynı görünüyordu; ayrıca cevher resmini boğuyordu.
                RectTransform ring = MakeDisc(_root, "Hale_" + i, routeColor, t,
                                              nodeSize * (icon != null ? 3.00f : 1.42f));
                RectTransform node = MakeDisc(_root, "Ada_" + i, lockedColor, t,
                                              nodeSize * (icon != null ? 1.55f : 1f));
                _ring[i] = ring.GetComponent<Image>();
                _node[i] = node.GetComponent<Image>();
                if (icon != null)
                {
                    _node[i].sprite = icon;
                    _node[i].preserveAspect = true;
                }
                if (_glowSprite != null)
                {
                    _ring[i].sprite = _glowSprite;
                    _ring[i].preserveAspect = true;
                }
                _ring[i].gameObject.SetActive(false);
            }
        }

        private Sprite Icon(int i) =>
            _icons != null && i >= 0 && i < _icons.Length ? _icons[i] : null;

        private static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private RectTransform MakeDisc(RectTransform parent, string name, Color c, float t, float size)
        {
            RectTransform rt = UiBuild.Flat(parent, name, c, Vector2.zero, Vector2.zero);
            rt.GetComponent<Image>().sprite = UiSkin.Pill;   // rounded, so a small square reads as a disc
            Place(rt, t, size);
            return rt;
        }

        /// <summary>
        /// Puts a rect on the route at <paramref name="t"/>, 0 at the left end and 1 at the right.
        ///
        /// The pivot stays in the middle. It used to be set to the anchor fraction along with the
        /// anchors, which reads like a tidy one-liner and is not: a rect pinned by a point 6% from
        /// its own left edge hangs almost entirely to the right of the place it was put. Two rects
        /// of different sizes then hang by different amounts, so the halo and the badge it belongs
        /// behind drifted apart — 17px at the coal end, 19px the other way at diamond, and nothing
        /// in the middle, which is exactly the signature of a pivot expressed as a fraction.
        /// </summary>
        private static void Place(RectTransform rt, float t, float size)
        {
            float y = 0.5f + Mathf.Sin(t * Mathf.PI) * 0.16f;   // the lift
            rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Lerp(0.06f, 0.94f, t), y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
        }

        // ---------------------------------------------------------------- refresh
        /// <summary>Recolours the chain. Cheap enough to call on every page of the carousel.</summary>
        public void Refresh(int highlight, Color lockedColor, Color routeColor)
        {
            if (!Ready) return;
            _highlight = highlight;

            int n = _world.Count;
            int nextLocked = -1;
            for (int i = 0; i < n; i++) if (!_world.IsOwned(i)) { nextLocked = i; break; }

            for (int i = 0; i < n; i++)
            {
                bool owned = _world.IsOwned(i);
                bool here = i == _world.ActiveIndex;
                Color ore = _world.BrandColor(i);

                // Owned islands carry their brand at full strength. Locked ones keep it too, darkened
                // toward the sea rather than replaced by a shared grey — eight identical grey dots told
                // the player nothing about where they were going. The one that can be bought NEXT sits
                // between the two, so the chain answers "where do I go now" at a glance.
                Color c;
                if (owned) c = Color.Lerp(ore, Color.white, here ? 0.35f : 0.10f);
                else if (i == nextLocked) c = Color.Lerp(ore, lockedColor, 0.25f);
                else
                {
                    Color dim = Color.Lerp(ore, lockedColor, 0.62f);
                    c = new Color(dim.r, dim.g, dim.b, 0.72f);
                }

                bool looked = i == _highlight;
                if (Icon(i) != null)
                {
                    // Cevher resmi kendi rengini taşıyor; boyamak onu bulandırıyor. Sahip olunan
                    // ada tam renkte, kilitli olan sönük — bakılan ada her hâlükârda tam parlak.
                    _node[i].color = owned || looked
                        ? Color.white
                        : new Color(0.62f, 0.66f, 0.72f, 0.8f);
                }
                else _node[i].color = c;

                // Hale yalnızca kartoteksin gösterdiği adada yanıyor ve rengi cevherden GELMİYOR:
                // ekranın zemini zaten o adanın cevher rengine boyanıyor, dolayısıyla cevher renkli
                // bir hale kömür adasında koyu kırmızının üstünde koyu kırmızı kalıyordu. Sıcak beyaz
                // bir ışık sekiz adanın hepsinde aynı şeyi söylüyor: bakılan ada bu.
                _ring[i].gameObject.SetActive(looked);
                if (looked) _ring[i].color = Isik;
                else _ring[i].rectTransform.localScale = Vector3.one;
                if (!looked) _node[i].rectTransform.localScale = Vector3.one;
            }

            // A leg is lit once the island it arrives at is owned — the wake you have already sailed.
            for (int i = 1; i < n; i++)
            {
                bool sailed = _world.IsOwned(i);
                for (int d = 0; d < DotsPerLeg; d++)
                {
                    Image dot = _legDot[(i - 1) * DotsPerLeg + d];
                    if (dot == null) continue;
                    dot.color = sailed
                        ? new Color(routeColor.r, routeColor.g, routeColor.b, 0.85f)
                        : new Color(routeColor.r, routeColor.g, routeColor.b, 0.28f);
                }
            }
        }

        /// <summary>
        /// Per-frame motion: the island being looked at breathes, and nothing else. The halo swings
        /// wider than the badge and its alpha rides with it, so the glow reads as a pulse rather than
        /// as a disc that changed size.
        /// </summary>
        public void Tick(float dt)
        {
            if (!Ready) return;
            if (_highlight < 0 || _highlight >= _ring.Length) return;

            _pulse += dt * 2.4f;
            float wave = Mathf.Sin(_pulse);

            Image halo = _ring[_highlight];
            if (halo != null && halo.gameObject.activeSelf)
            {
                float s = 1f + wave * 0.14f;
                halo.rectTransform.localScale = new Vector3(s, s, 1f);
                halo.color = new Color(Isik.r, Isik.g, Isik.b, 0.82f + wave * 0.18f);
            }

            Image badge = _node[_highlight];
            if (badge != null)
            {
                float s = 1.12f + wave * 0.05f;
                badge.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
