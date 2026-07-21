using System.Collections.Generic;
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
    /// Minimal HUD for the single Coal island: a cash bar on top and a toggleable upgrade tab listing the
    /// coal operation's tracks (Mountain / Train / Ore Truck / Smelter / Cargo Truck / Storage / Market).
    /// Buying a level spends cash through <see cref="WalletService"/> and scales the live cycle via
    /// <see cref="CoalOperation.TryUpgrade"/>. Self-contained — no GameWorld/Island dependency.
    /// </summary>
    public sealed class CoalHud : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.25f;

        private WalletService _wallet;
        private CoalOperation _op;
        private Font _font;
        private Sprite _flat;
        private Text _cashText;
        private GameObject _panel;
        private float _timer;

        private sealed class Row { public int track; public Text label; public Image bg; public Button btn; }
        private readonly List<Row> _rows = new List<Row>();

        private static readonly Color Bg = new Color(0.11f, 0.14f, 0.19f, 0.96f);
        private static readonly Color Buy = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color Cant = new Color(0.32f, 0.35f, 0.40f, 1f);
        private static readonly Color Amber = new Color(0.90f, 0.62f, 0.16f, 1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _op = FindAnyObjectByType<CoalOperation>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _flat = MakeFlat();
            Build();
            Refresh();
        }

        private void Update()
        {
            if (_wallet == null) _wallet = ServiceLocator.Get<WalletService>();
            if (_op == null) _op = FindAnyObjectByType<CoalOperation>();
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

            var canvasGO = new GameObject("CoalHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var sc = canvasGO.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1080f, 1920f); sc.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // top cash bar
            RectTransform bar = Panel(root, "TopBar", Bg);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -140f); bar.offsetMax = Vector2.zero;
            _cashText = Label(bar, "Cash", "$ 0", 54, TextAnchor.MiddleCenter); _cashText.color = new Color(1f, 0.92f, 0.5f);

            int n = _op != null ? _op.TrackCount : 0;
            float rowH = 96f, gap = 14f, titleH = 66f;
            float h = titleH + n * (rowH + gap) + gap;

            // upgrade panel (toggled)
            RectTransform panel = Panel(root, "Upgrades", Bg);
            panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(1f, 0f); panel.pivot = new Vector2(0.5f, 0f);
            panel.offsetMin = new Vector2(16f, 150f); panel.offsetMax = new Vector2(-16f, 150f + h);   // sit above the toggle button
            _panel = panel.gameObject;

            Text title = Label(panel, "Title", "UPGRADES  —  COAL ISLAND", 34, TextAnchor.UpperCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f); title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(0f, -titleH); title.rectTransform.offsetMax = new Vector2(0f, -6f);

            for (int i = 0; i < n; i++)
            {
                int t = i;
                Button rowBtn = Btn(panel, "Row" + i, "", Buy, 32, null);
                RectTransform rt = rowBtn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                float y = -titleH - i * (rowH + gap);
                rt.offsetMin = new Vector2(14f, y - rowH); rt.offsetMax = new Vector2(-14f, y);
                Text lbl = rowBtn.GetComponentInChildren<Text>(); lbl.alignment = TextAnchor.MiddleLeft;
                Stretch(lbl.rectTransform, 28f, 0f, 28f, 0f);
                rowBtn.onClick.AddListener(() => { if (_op != null && _op.TryUpgrade(t)) Refresh(); });
                _rows.Add(new Row { track = t, label = lbl, bg = rowBtn.GetComponent<Image>(), btn = rowBtn });
            }

            _panel.SetActive(false);

            // toggle button (bottom-right, always visible)
            Button toggle = Btn(root, "ToggleBtn", "▲  UPGRADES", Amber, 34, null);
            RectTransform trt = toggle.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(1f, 0f); trt.anchorMax = new Vector2(1f, 0f); trt.pivot = new Vector2(1f, 0f);
            trt.anchoredPosition = new Vector2(-24f, 24f); trt.sizeDelta = new Vector2(360f, 100f);
            Text ttxt = toggle.GetComponentInChildren<Text>();
            toggle.onClick.AddListener(() => { bool on = !_panel.activeSelf; _panel.SetActive(on); ttxt.text = on ? "▼  CLOSE" : "▲  UPGRADES"; if (on) Refresh(); });
        }

        private void Refresh()
        {
            if (_cashText != null && _wallet != null) _cashText.text = "$ " + NumberFormatter.Format(_wallet.Cash);
            if (_op == null || _panel == null || !_panel.activeSelf) return;
            BigDouble cash = _wallet != null ? _wallet.Cash : new BigDouble(0d);
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i]; int t = r.track;
                BigDouble cost = _op.Cost(t);
                bool afford = cash >= cost;
                r.label.text = _op.TrackName(t) + "    Lv " + _op.Level(t) + "          $" + NumberFormatter.Format(cost);
                r.bg.color = afford ? Buy : Cant;
                r.btn.interactable = afford;
            }
        }

        // ---- tiny builders ----
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
            Stretch((RectTransform)go.transform, 0f, 0f, 0f, 0f);
            return t;
        }

        private Button Btn(Transform parent, string name, string text, Color c, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = _flat; img.type = Image.Type.Sliced; img.color = c;
            var t = Label((RectTransform)go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 6f, 2f, 6f, 2f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        private static void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        private Sprite MakeFlat()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
        }
    }
}
