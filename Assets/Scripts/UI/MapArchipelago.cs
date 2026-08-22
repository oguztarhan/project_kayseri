using Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The chain of islands along the bottom of the world map: eight nodes on a sea route, the owned
    /// ones lit in their ore colour, the rest dark, and a boat marking the one you are standing on.
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
        private RectTransform _boat;
        private Image _boatImage;

        private const int DotsPerLeg = 5;

        private int _highlight = -1;
        private float _pulse;

        // NO SAIL ANIMATION, deliberately. The obvious idea is to glide the boat along the leg during
        // the 1.7s travel hold — but Travel() deactivates panelRoot before that hold begins, and
        // IslandMapUI.Update returns early while the panel is inactive, so nothing on this layer draws
        // a single frame of it. Making it visible would mean reordering the travel sequence, and that
        // sequence carries the curtain, the sorting-order raise and the scene load. The boat stays a
        // marker: it says where you are, which is what it was worth having for.

        public MapArchipelago(WorldIslands world) { _world = world; }

        public bool Ready => _root != null;

        /// <summary>
        /// Lays the chain out inside <paramref name="parent"/>. Anchors are fractions so the ribbon
        /// keeps its place whatever the canvas is; it belongs in the band below the medallion and the
        /// call-to-action, which on the landscape layout is everything under about -0.40.
        /// </summary>
        public void Build(RectTransform parent, int siblingIndex, Vector2 aMin, Vector2 aMax,
                          Color routeColor, Color lockedColor, float nodeSize)
        {
            if (_world == null || parent == null) return;
            int n = _world.Count;
            if (n <= 0) return;

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

                RectTransform ring = MakeDisc(_root, "Halka_" + i, routeColor, t, nodeSize * 1.42f);
                RectTransform node = MakeDisc(_root, "Ada_" + i, lockedColor, t, nodeSize);
                _ring[i] = ring.GetComponent<Image>();
                _node[i] = node.GetComponent<Image>();
                _ring[i].gameObject.SetActive(false);
            }

            RectTransform boat = MakeDisc(_root, "Gemi", Color.white, 0f, nodeSize * 0.52f);
            _boat = boat;
            _boatImage = boat.GetComponent<Image>();
            _boat.gameObject.SetActive(false);
        }

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

        /// <summary>Puts a rect on the route at <paramref name="t"/>, 0 at the left end and 1 at the right.</summary>
        private static void Place(RectTransform rt, float t, float size)
        {
            float y = 0.5f + Mathf.Sin(t * Mathf.PI) * 0.16f;   // the lift
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(Mathf.Lerp(0.06f, 0.94f, t), y);
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
                _node[i].color = c;

                bool ringed = i == _highlight || here;
                _ring[i].gameObject.SetActive(ringed);
                if (ringed)
                    _ring[i].color = here ? new Color(1f, 0.86f, 0.42f, 0.55f)
                                          : new Color(1f, 1f, 1f, 0.28f);
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

            ParkBoat();
        }

        /// <summary>Parks the boat on the island the player is actually standing on.</summary>
        private void ParkBoat()
        {
            if (!Ready || _boat == null) return;
            int at = _world.ActiveIndex;
            int n = _world.Count;
            if (at < 0 || n <= 1) { _boat.gameObject.SetActive(false); return; }
            _boat.gameObject.SetActive(true);
            _boatImage.color = new Color(1f, 0.96f, 0.86f, 0.9f);
            Place(_boat, at / (float)(n - 1), _boat.sizeDelta.x);
            _boat.anchoredPosition = new Vector2(0f, _boat.sizeDelta.y * 0.9f);
        }

        /// <summary>Per-frame motion: the ring around the island being looked at breathes, and nothing else.</summary>
        public void Tick(float dt)
        {
            if (!Ready) return;

            _pulse += dt * 2.4f;
            if (_highlight >= 0 && _highlight < _ring.Length && _ring[_highlight] != null
                && _ring[_highlight].gameObject.activeSelf)
            {
                float s = 1f + Mathf.Sin(_pulse) * 0.06f;
                _ring[_highlight].rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
