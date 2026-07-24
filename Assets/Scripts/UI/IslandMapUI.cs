using System.Collections;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// World-map screen for the 8-ore archipelago (GDD §2 meta): a MAP button opens a full-screen grid of
    /// island cards — tap an owned island to sail there (loading overlay, then the camera re-frames and the
    /// HUD retargets), tap a locked one to buy it with cash and sail immediately. Cards show each island's
    /// live/background $/min against its cap, so "max this island, buy the next" reads at a glance.
    /// Purchases and switching go through <see cref="WorldIslands"/>. Self-contained runtime uGUI.
    /// </summary>
    public sealed class IslandMapUI : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.5f;
        [SerializeField] private float sailSeconds = 1.3f;   // loading-overlay length (the "loading screen")

        private WorldIslands _world;
        private WalletService _wallet;
        private Font _font;
        private Sprite _flat;
        private GameObject _map, _loading;
        private Text _loadTitle;
        private RectTransform _loadFill;
        private float _timer;
        private bool _sailing;

        private sealed class Card { public int index; public Text name, status; public Image bg, icon; public Button btn; }
        private Card[] _cards;

        private static readonly Color Ocean = new Color(0.07f, 0.16f, 0.24f, 0.98f);
        private static readonly Color CardBg = new Color(0.12f, 0.22f, 0.31f, 1f);
        private static readonly Color CardActive = new Color(0.16f, 0.34f, 0.24f, 1f);
        private static readonly Color CardLocked = new Color(0.10f, 0.13f, 0.17f, 1f);
        private static readonly Color Buy = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color Amber = new Color(0.90f, 0.62f, 0.16f, 1f);

        private void Start()
        {
            _world = FindAnyObjectByType<WorldIslands>();
            _wallet = ServiceLocator.Get<WalletService>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _flat = MakeFlat();
            if (_world != null) Build();
        }

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_map == null || !_map.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        private void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvasGO = new GameObject("IslandMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 150;   // above the island HUD
            var sc = canvasGO.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1080f, 1920f); sc.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // MAP button (bottom-left, always visible — mirrors the HUD's UPGRADES button bottom-right)
            Button mapBtn = Btn(root, "MapBtn", "🌍  MAP", Amber, 34, () => { if (_sailing) return; bool on = !_map.activeSelf; _map.SetActive(on); if (on) Refresh(); });
            RectTransform mrt = mapBtn.GetComponent<RectTransform>();
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.zero; mrt.pivot = Vector2.zero;
            mrt.anchoredPosition = new Vector2(24f, 24f); mrt.sizeDelta = new Vector2(300f, 100f);

            // full-screen map
            RectTransform map = Panel(root, "WorldMap", Ocean);
            _map = map.gameObject;
            Text title = Label(map, "Title", "ORE  EMPIRE  —  WORLD  MAP", 44, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 0.925f); title.rectTransform.anchorMax = new Vector2(1f, 0.985f);
            title.rectTransform.offsetMin = Vector2.zero; title.rectTransform.offsetMax = Vector2.zero;
            title.color = new Color(1f, 0.92f, 0.5f);
            Text hint = Label(map, "Hint", "each island is capped — max it out, then buy the next. owned islands keep earning while you're away.", 22, TextAnchor.MiddleCenter);
            hint.rectTransform.anchorMin = new Vector2(0f, 0.885f); hint.rectTransform.anchorMax = new Vector2(1f, 0.925f);
            hint.rectTransform.offsetMin = Vector2.zero; hint.rectTransform.offsetMax = Vector2.zero;
            hint.color = new Color(0.6f, 0.72f, 0.82f);

            int n = _world.Count;
            _cards = new Card[n];
            for (int i = 0; i < n; i++)
            {
                int col = i % 2, row = i / 2;
                var card = new Card { index = i };
                Button b = Btn(map, "Card_" + _world.IslandKey(i), "", CardBg, 30, null);
                int ci = i;
                b.onClick.AddListener(() => OnCard(ci));
                RectTransform rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.04f + col * 0.48f, 0.875f - 0.208f * (row + 1) + 0.02f);
                rt.anchorMax = new Vector2(0.48f + col * 0.48f, 0.875f - 0.208f * row);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                card.btn = b; card.bg = b.GetComponent<Image>();
                b.GetComponentInChildren<Text>().text = "";

                var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGO.transform.SetParent(b.transform, false);
                var irt = (RectTransform)iconGO.transform;
                irt.anchorMin = new Vector2(0.06f, 0.56f); irt.anchorMax = new Vector2(0.22f, 0.92f);
                irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
                card.icon = iconGO.GetComponent<Image>();
                card.icon.sprite = _flat; card.icon.color = _world.OreColor(i);

                card.name = Label((RectTransform)b.transform, "Name", _world.IslandName(i), 32, TextAnchor.MiddleLeft);
                card.name.rectTransform.anchorMin = new Vector2(0.27f, 0.56f); card.name.rectTransform.anchorMax = new Vector2(1f, 0.92f);
                card.name.rectTransform.offsetMin = Vector2.zero; card.name.rectTransform.offsetMax = Vector2.zero;

                card.status = Label((RectTransform)b.transform, "Status", "", 25, TextAnchor.UpperLeft);
                card.status.rectTransform.anchorMin = new Vector2(0.08f, 0.06f); card.status.rectTransform.anchorMax = new Vector2(0.97f, 0.52f);
                card.status.rectTransform.offsetMin = Vector2.zero; card.status.rectTransform.offsetMax = Vector2.zero;
                _cards[i] = card;
            }

            Button close = Btn(map, "CloseBtn", "▼  CLOSE MAP", Amber, 32, () => { if (!_sailing) _map.SetActive(false); });
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.3f, 0.005f); crt.anchorMax = new Vector2(0.7f, 0.045f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            _map.SetActive(false);

            // loading overlay ("sailing" screen) — topmost
            RectTransform load = Panel(root, "Loading", new Color(0.02f, 0.05f, 0.09f, 1f));
            _loading = load.gameObject;
            _loadTitle = Label(load, "Title", "SAILING …", 46, TextAnchor.MiddleCenter);
            _loadTitle.rectTransform.anchorMin = new Vector2(0f, 0.5f); _loadTitle.rectTransform.anchorMax = new Vector2(1f, 0.62f);
            _loadTitle.rectTransform.offsetMin = Vector2.zero; _loadTitle.rectTransform.offsetMax = Vector2.zero;
            RectTransform barBg = Panel(load, "BarBg", new Color(0.15f, 0.2f, 0.28f, 1f));
            barBg.anchorMin = new Vector2(0.2f, 0.44f); barBg.anchorMax = new Vector2(0.8f, 0.47f);
            barBg.offsetMin = Vector2.zero; barBg.offsetMax = Vector2.zero;
            RectTransform fill = Panel(barBg, "Fill", Amber);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            _loadFill = fill;
            _loading.SetActive(false);
        }

        private void OnCard(int i)
        {
            if (_sailing || _world == null) return;
            if (_world.IsOwned(i))
            {
                if (i != _world.ActiveIndex) StartCoroutine(Sail(i));
                return;
            }
            if (_world.TryBuy(i)) StartCoroutine(Sail(i));   // buy → sail straight there
            else Refresh();
        }

        /// <summary>The tap-to-enter loading screen: overlay + progress bar; the actual switch (island roots,
        /// operation, camera, HUD) happens hidden behind it at the halfway mark.</summary>
        private IEnumerator Sail(int i)
        {
            _sailing = true;
            _loading.SetActive(true);
            _loadTitle.text = "SAILING  TO  " + _world.IslandName(i) + " …";
            bool switched = false;
            float t = 0f;
            while (t < sailSeconds)
            {
                t += Time.unscaledDeltaTime;
                float f = Mathf.Clamp01(t / sailSeconds);
                _loadFill.anchorMax = new Vector2(f, 1f);
                if (!switched && f >= 0.45f)
                {
                    switched = true;
                    CoalOperation op = _world.Travel(i);
                    if (op != null)
                    {
                        var boot = FindAnyObjectByType<OperationCameraBoot>();
                        if (boot != null) boot.FrameOn(_world.RootName(i));
                        var hud = FindAnyObjectByType<CoalHud>();
                        if (hud != null) hud.SetOperation(op);
                    }
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.25f);
            _loading.SetActive(false);
            _map.SetActive(false);
            _sailing = false;
        }

        private void Refresh()
        {
            if (_world == null || _cards == null) return;
            for (int c = 0; c < _cards.Length; c++)
            {
                Card card = _cards[c];
                int i = card.index;
                bool owned = _world.IsOwned(i);
                bool active = i == _world.ActiveIndex;
                string cap = NumberFormatter.Format(new BigDouble(_world.CapPerMin(i)));
                if (owned)
                {
                    string rate = NumberFormatter.Format(new BigDouble(_world.RatePerMin(i)));
                    string tag = active ? "◉ ACTIVE" : "⚓ TAP TO SAIL";
                    string max = _world.IsMaxed(i) ? "   ★ MAXED" : "";
                    card.status.text = tag + max + "\n$" + rate + " / min   (cap $" + cap + ")";
                    card.bg.color = active ? CardActive : CardBg;
                    card.btn.interactable = !active;
                }
                else
                {
                    BigDouble cost = new BigDouble(_world.UnlockCost(i));
                    bool afford = _wallet != null && _wallet.CanAfford(cost);
                    card.status.text = "🔒 UNLOCK   $" + NumberFormatter.Format(cost) + "\nearns up to $" + cap + " / min";
                    card.bg.color = afford ? Buy : CardLocked;
                    card.btn.interactable = afford;
                }
            }
        }

        // ---- tiny builders (same style as the HUD) ----
        private RectTransform Panel(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return t;
        }

        private Button Btn(Transform parent, string name, string text, Color c, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = _flat; img.type = Image.Type.Sliced; img.color = c;
            Text t = Label(go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        private Sprite MakeFlat()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        }
    }
}
