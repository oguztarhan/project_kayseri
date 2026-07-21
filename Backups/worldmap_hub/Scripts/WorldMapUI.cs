using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The archipelago <b>World Map</b> hub (the Idle-Miner-Tycoon style map): a portrait, finger-pannable
    /// screen showing every ore island. All islands start locked except Coal; you upgrade an island to MAX,
    /// which unlocks the next — "goes on like that". Tap an unlocked island to ENTER its 3D operation; a back
    /// button returns to the map. Built in code so it's version-controlled. Reads/writes state via
    /// <see cref="IslandManager"/>; toggles the game HUD's canvas so map and operation never fight for input.
    /// </summary>
    public sealed class WorldMapUI : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField] private Vector2 contentSize = new Vector2(1300f, 2600f);

        private WalletService _wallet;
        private IslandManager _islands;
        private Camera _cam;
        private CameraController _camCtrl;
        private Canvas _hudCanvas;
        private HudDebug _hudDebug;

        private Font _font;
        private Sprite _circle, _diamond, _flat;
        private RectTransform _mapRoot;
        private GameObject _backBtn;
        private Text _cashText;
        private ScrollRect _scroll;
        private float _timer;

        private Vector3 _homePos; private Quaternion _homeRot; private float _homeSize;
        private int _snap;   // frames left to keep re-applying the initial scroll (after layout settles)

        // one island node's live widgets
        private sealed class Node
        {
            public Island isl;
            public Image land, gem;
            public Text name, status;
            public Button actBtn; public Text actText; public Image actBg;
            public Button enterBtn;
        }
        private readonly List<Node> _nodes = new List<Node>();

        // per-island layout (x, y-from-top) and land colours — each island its own look
        // index order matches IslandManager: Coal, Iron, Copper, Silver, Gold, Emerald, Ruby, Diamond —
        // laid out bottom (start) → top (end tier) so the player pans upward through the progression.
        private static readonly Vector2[] Pos =
        {
            new Vector2(650f, 2450f), new Vector2(970f, 2140f), new Vector2(360f, 1860f), new Vector2(940f, 1560f),
            new Vector2(330f, 1260f), new Vector2(910f, 940f),  new Vector2(370f, 600f),  new Vector2(820f, 220f)
        };
        private static readonly Color[] Land =
        {
            new Color(0.34f,0.40f,0.30f), new Color(0.42f,0.47f,0.52f), new Color(0.52f,0.36f,0.24f), new Color(0.56f,0.59f,0.63f),
            new Color(0.60f,0.52f,0.22f), new Color(0.20f,0.48f,0.32f), new Color(0.52f,0.22f,0.28f), new Color(0.40f,0.56f,0.63f)
        };
        private static readonly Color Sea = new Color(0.16f, 0.44f, 0.62f, 1f);
        private static readonly Color Beach = new Color(0.90f, 0.84f, 0.62f, 1f);
        private static readonly Color Amber = new Color(0.90f, 0.62f, 0.16f, 1f);
        private static readonly Color Green = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color Gold = new Color(0.82f, 0.68f, 0.20f, 1f);
        private static readonly Color GreyBtn = new Color(0.28f, 0.30f, 0.34f, 1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _islands = FindFirstObjectByType<IslandManager>();
            _cam = Camera.main;
            _camCtrl = FindFirstObjectByType<CameraController>();
            var hud = FindFirstObjectByType<HudUGUI>();
            if (hud != null) _hudCanvas = hud.GetComponentInChildren<Canvas>();
            _hudDebug = FindFirstObjectByType<HudDebug>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (_cam != null) { _homePos = _cam.transform.position; _homeRot = _cam.transform.rotation; _homeSize = _cam.orthographic ? _cam.orthographicSize : 26f; }

            _circle = MakeCircle(); _diamond = MakeDiamond(); _flat = MakeFlat();
            Build();
            OpenMap();
        }

        private void Update()
        {
            if (_wallet == null) return;
            // keep re-applying the initial scroll for a few frames until the layout has settled
            if (_snap > 0 && _scroll != null)
            {
                _scroll.horizontalNormalizedPosition = 0.5f;
                _scroll.verticalNormalizedPosition = 0f;   // bottom = Coal (the starting island)
                _snap--;
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            if (_mapRoot != null && _mapRoot.gameObject.activeSelf) Refresh();
        }

        // ---------------- build ----------------
        private void Build()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvasGO = new GameObject("WorldMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;                          // above the HUD (100)
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);   // portrait phone
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // ---- map root (full screen) ----
            _mapRoot = Panel(root, "MapRoot", Sea);
            Stretch(_mapRoot, 0f, 0f, 0f, 0f);

            // scroll area (fills the screen under the top bar) — finger pans in both axes
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect));
            viewport.transform.SetParent(_mapRoot, false);
            viewport.GetComponent<Image>().color = Sea;
            RectTransform vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(0f, 0f); vrt.offsetMax = new Vector2(0f, -150f);   // leave room for the top bar
            _scroll = viewport.GetComponent<ScrollRect>();
            _scroll.horizontal = true; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.08f; _scroll.scrollSensitivity = 24f; _scroll.inertia = true; _scroll.decelerationRate = 0.92f;

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(0f, 1f); crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = contentSize; crt.anchoredPosition = Vector2.zero;
            _scroll.viewport = vrt; _scroll.content = crt;

            int n = _islands != null && _islands.Islands != null ? Mathf.Min(_islands.Islands.Length, Pos.Length) : 0;

            // progression path lines behind the nodes
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = new Vector2(Pos[i].x, -Pos[i].y);
                Vector2 b = new Vector2(Pos[i + 1].x, -Pos[i + 1].y);
                MakeLine(crt, a, b);
            }

            // island nodes
            for (int i = 0; i < n; i++)
                _nodes.Add(MakeNode(crt, i, _islands.Islands[i]));

            // ---- top bar ----
            RectTransform bar = Panel(root, "TopBar", new Color(0.10f, 0.13f, 0.18f, 0.98f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -150f); bar.offsetMax = new Vector2(0f, 0f);
            Text title = Label(bar, "Title", "WORLD MAP", 46, TextAnchor.MiddleLeft);
            Band(title.rectTransform, 0f, 0.6f, 34f);
            _cashText = Label(bar, "Cash", "$ 0", 44, TextAnchor.MiddleRight);
            _cashText.color = new Color(1f, 0.92f, 0.5f);
            Band(_cashText.rectTransform, 0.4f, 1f, 34f);
            Text hint = Label(bar, "Hint", "Max an island to unlock the next  ·  tap an island to enter", 26, TextAnchor.LowerLeft);
            hint.color = new Color(0.7f, 0.78f, 0.85f);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.offsetMin = new Vector2(34f, 8f); hint.rectTransform.offsetMax = new Vector2(-34f, 40f);

            // ---- back-to-map button (only visible inside an island) ----
            Button back = Btn(root, "BackBtn", "◀  MAP", new Color(0.10f, 0.13f, 0.18f, 0.96f), 34, OpenMap);
            RectTransform brt = back.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(24f, -24f); brt.sizeDelta = new Vector2(200f, 84f);
            _backBtn = back.gameObject;
        }

        private Node MakeNode(RectTransform content, int idx, Island isl)
        {
            var node = new Node { isl = isl };
            var go = new GameObject("Node_" + idx, typeof(RectTransform));
            go.transform.SetParent(content, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 380f);
            rt.anchoredPosition = new Vector2(Pos[idx].x, -Pos[idx].y);

            // whole island disc is tap-to-enter (no-ops until unlocked) — big, obvious target
            var tap = new GameObject("Tap", typeof(RectTransform), typeof(Image), typeof(Button));
            tap.transform.SetParent(rt, false);
            var tapImg = tap.GetComponent<Image>(); tapImg.sprite = _circle; tapImg.color = new Color(1f, 1f, 1f, 0f); tapImg.raycastTarget = true;
            Place((RectTransform)tap.transform, new Vector2(0f, 66f), new Vector2(256f, 256f));
            var tapBtn = tap.GetComponent<Button>(); tapBtn.targetGraphic = tapImg;
            tapBtn.onClick.AddListener(() => EnterIsland(node.isl));

            Img(rt, "Beach", _circle, Beach, new Vector2(0f, 66f), new Vector2(256f, 256f));
            node.land = Img(rt, "Land", _circle, Land[idx], new Vector2(0f, 66f), new Vector2(224f, 224f));
            Color oc = (isl != null && isl.Def != null && isl.Def.Ore != null) ? isl.Def.Ore.Color : Color.white;
            node.gem = Img(rt, "Gem", _diamond, oc, new Vector2(0f, 92f), new Vector2(96f, 96f));

            node.name = Label(rt, "Name", isl != null && isl.Def != null ? isl.Def.DisplayName : "Island", 32, TextAnchor.MiddleCenter);
            Place(node.name.rectTransform, new Vector2(0f, 6f), new Vector2(280f, 40f));
            node.status = Label(rt, "Status", "", 24, TextAnchor.MiddleCenter);
            node.status.color = new Color(0.92f, 0.94f, 1f);
            Place(node.status.rectTransform, new Vector2(0f, -26f), new Vector2(280f, 30f));

            node.actBtn = Btn(rt, "Act", "", Green, 26, null);
            node.actBg = node.actBtn.GetComponent<Image>();
            node.actText = node.actBtn.GetComponentInChildren<Text>();
            Place(node.actBtn.GetComponent<RectTransform>(), new Vector2(0f, -70f), new Vector2(260f, 56f));

            node.enterBtn = Btn(rt, "Enter", "ENTER ▶", new Color(0.18f, 0.40f, 0.66f, 1f), 26, () => EnterIsland(node.isl));
            Place(node.enterBtn.GetComponent<RectTransform>(), new Vector2(0f, -134f), new Vector2(260f, 52f));
            return node;
        }

        // ---------------- state ----------------
        private void Refresh()
        {
            if (_wallet != null) _cashText.text = "$ " + NumberFormatter.Format(_wallet.Cash);
            if (_islands == null) return;
            BigDouble cash = _wallet != null ? _wallet.Cash : new BigDouble(0d);

            for (int i = 0; i < _nodes.Count; i++)
            {
                Node nd = _nodes[i]; Island isl = nd.isl;
                if (isl == null || isl.Def == null) continue;
                bool unlocked = isl.Unlocked;
                bool maxed = isl.IsMaxed;
                bool canUnlock = _islands.CanUnlock(isl);

                float a = unlocked ? 1f : 0.42f;
                SetAlpha(nd.land, a); SetAlpha(nd.gem, unlocked ? 1f : 0.5f);
                nd.enterBtn.gameObject.SetActive(unlocked);

                nd.actBtn.onClick.RemoveAllListeners();
                if (unlocked)
                {
                    if (maxed)
                    {
                        nd.status.text = "MAX  (Lv " + isl.MaxLevel + ")";
                        nd.actText.text = "★ MAXED";
                        nd.actBg.color = Gold; nd.actBtn.interactable = false;
                    }
                    else
                    {
                        BigDouble uc = new BigDouble(isl.UpgradeCost);
                        nd.status.text = "Lv " + isl.Level + " / " + isl.MaxLevel;
                        nd.actText.text = "UPGRADE  $" + NumberFormatter.Format(uc);
                        nd.actBg.color = Green; nd.actBtn.interactable = cash >= uc;
                        Island cap = isl; nd.actBtn.onClick.AddListener(() => _islands.TryUpgrade(cap));
                    }
                }
                else if (canUnlock)
                {
                    BigDouble c = new BigDouble(isl.Def.UnlockCost);
                    nd.status.text = "READY TO CLAIM";
                    nd.actText.text = "UNLOCK  $" + NumberFormatter.Format(c);
                    nd.actBg.color = Amber; nd.actBtn.interactable = cash >= c;
                    Island cap = isl; nd.actBtn.onClick.AddListener(() => _islands.TryUnlock(cap));
                }
                else
                {
                    nd.status.text = "LOCKED";
                    nd.actText.text = "LOCKED";
                    nd.actBg.color = GreyBtn; nd.actBtn.interactable = false;
                }
            }
        }

        // ---------------- navigation ----------------
        // The HUD builds its canvas in its own Start(); if ours ran first we may not have found it yet.
        private void ResolveHud()
        {
            if (_hudCanvas == null)
            {
                var hud = FindFirstObjectByType<HudUGUI>();
                if (hud != null) _hudCanvas = hud.GetComponentInChildren<Canvas>();
            }
            if (_hudDebug == null) _hudDebug = FindFirstObjectByType<HudDebug>();
        }

        private void OpenMap()
        {
            ResolveHud();
            if (_mapRoot != null) _mapRoot.gameObject.SetActive(true);
            if (_backBtn != null) _backBtn.SetActive(false);
            if (_hudCanvas != null) _hudCanvas.enabled = false;
            if (_hudDebug != null) _hudDebug.enabled = false;
            if (_camCtrl != null) _camCtrl.enabled = false;   // freeze the 3D camera while the map is up
            _snap = 4;   // re-apply the start-on-Coal scroll over the next few frames (post-layout)
            Refresh();
        }

        private void EnterIsland(Island isl)
        {
            if (isl == null || !isl.Unlocked) return;
            ResolveHud();
            if (_mapRoot != null) _mapRoot.gameObject.SetActive(false);
            if (_backBtn != null) _backBtn.SetActive(true);
            if (_hudCanvas != null) _hudCanvas.enabled = true;
            if (_hudDebug != null) _hudDebug.enabled = true;
            FocusCamera(isl);
            if (_camCtrl != null) _camCtrl.enabled = true;    // free pan/zoom once inside the island
            Refresh();
        }

        private void FocusCamera(Island isl)
        {
            if (_cam == null) return;
            if (isl.Def != null && isl.Def.HomeIsland)
            {
                _cam.transform.SetPositionAndRotation(_homePos, _homeRot);
                if (_cam.orthographic) _cam.orthographicSize = _homeSize;
                return;
            }
            Quaternion rot = Quaternion.Euler(50f, 45f, 0f);
            _cam.transform.SetPositionAndRotation(isl.transform.position + rot * new Vector3(0f, 0f, -72f), rot);
            if (_cam.orthographic) _cam.orthographicSize = 34f;
        }

        // ---------------- tiny builders ----------------
        private RectTransform Panel(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            return (RectTransform)go.transform;
        }

        private Image Img(Transform parent, string name, Sprite sp, Color c, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = sp; img.color = c; img.raycastTarget = false;
            Place((RectTransform)go.transform, pos, size);
            return img;
        }

        private void MakeLine(RectTransform content, Vector2 a, Vector2 b)
        {
            Vector2 mid = (a + b) * 0.5f;
            float len = Vector2.Distance(a, b);
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            var img = Img(content, "Path", _flat, new Color(1f, 1f, 1f, 0.22f), Vector2.zero, new Vector2(len, 12f));
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = mid; rt.localEulerAngles = new Vector3(0f, 0f, ang);
        }

        private Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Stretch((RectTransform)go.transform, 0f, 0f, 0f, 0f);
            return t;
        }

        private Button Btn(Transform parent, string name, string text, Color c, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().sprite = _flat; go.GetComponent<Image>().type = Image.Type.Sliced;
            go.GetComponent<Image>().color = c;
            var t = Label((RectTransform)go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4f, 2f, 0f, 0f);
            var b = go.GetComponent<Button>(); b.targetGraphic = go.GetComponent<Image>();
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        // place a centred rect at anchoredPosition with a fixed size (anchor = parent centre)
        private void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
        private void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }
        private void Band(RectTransform rt, float axMin, float axMax, float padX)
        {
            rt.anchorMin = new Vector2(axMin, 0f); rt.anchorMax = new Vector2(axMax, 1f);
            rt.offsetMin = new Vector2(padX, 0f); rt.offsetMax = new Vector2(-padX, 0f);
        }
        private static void SetAlpha(Graphic g, float a) { Color c = g.color; c.a = a; g.color = c; }

        // ---------------- procedural sprites ----------------
        private Sprite MakeCircle()
        {
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            float c = (S - 1) * 0.5f, rad = c - 1f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(rad - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        private Sprite MakeDiamond()
        {
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float m = (Mathf.Abs(x - c) + Mathf.Abs(y - c)) / c;
                    float a = Mathf.Clamp01((0.94f - m) * 12f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }

        private Sprite MakeFlat()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        }
    }
}
