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
    /// HUD for the single Coal island. Top bar: cash + trailing $/min (the number the player is trying to
    /// push up). A toggleable, scrollable upgrade tab lists every station with all its upgrade axes
    /// (GDD §3 — e.g. Ore Trucks: Trucks / Speed / Capacity) plus the one-time ghost-building unlocks.
    /// Buying goes through <see cref="CoalOperation.TryUpgrade"/> / <see cref="CoalOperation.TryUnlock"/>,
    /// which spend via <see cref="WalletService"/>. Self-contained — builds its own uGUI at runtime.
    /// </summary>
    public sealed class CoalHud : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.25f;
        [SerializeField] private float panelHeight = 1150f;

        private WalletService _wallet;
        private CoalOperation _op;
        private Font _font;
        private Sprite _flat;
        private Text _cashText, _rateText;
        private GameObject _panel;
        private float _timer;

        private sealed class Row
        {
            public int station, axis;     // axis row when unlock < 0
            public int unlock = -1;       // unlock row when >= 0
            public Text label; public Image bg; public Button btn;
        }
        private readonly List<Row> _rows = new List<Row>();

        private static readonly Color Bg = new Color(0.11f, 0.14f, 0.19f, 0.96f);
        private static readonly Color Buy = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color Cant = new Color(0.32f, 0.35f, 0.40f, 1f);
        private static readonly Color Done = new Color(0.16f, 0.22f, 0.30f, 1f);
        private static readonly Color Amber = new Color(0.90f, 0.62f, 0.16f, 1f);
        private static readonly Color Ghost = new Color(0.45f, 0.55f, 0.75f, 1f);

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

            // top bar: cash + $/min
            RectTransform bar = Panel(root, "TopBar", Bg);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -170f); bar.offsetMax = Vector2.zero;
            _cashText = Label(bar, "Cash", "$ 0", 54, TextAnchor.MiddleCenter); _cashText.color = new Color(1f, 0.92f, 0.5f);
            _cashText.rectTransform.anchorMin = new Vector2(0f, 0.38f); _cashText.rectTransform.anchorMax = Vector2.one;
            _cashText.rectTransform.offsetMin = Vector2.zero; _cashText.rectTransform.offsetMax = Vector2.zero;
            _rateText = Label(bar, "Rate", "▲ $0 / min", 32, TextAnchor.MiddleCenter); _rateText.color = new Color(0.55f, 0.9f, 0.55f);
            _rateText.rectTransform.anchorMin = Vector2.zero; _rateText.rectTransform.anchorMax = new Vector2(1f, 0.38f);
            _rateText.rectTransform.offsetMin = Vector2.zero; _rateText.rectTransform.offsetMax = Vector2.zero;

            // upgrade panel (toggled) with a scroll view — too many axes to fit on screen
            RectTransform panel = Panel(root, "Upgrades", Bg);
            panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(1f, 0f); panel.pivot = new Vector2(0.5f, 0f);
            panel.offsetMin = new Vector2(16f, 150f); panel.offsetMax = new Vector2(-16f, 150f + panelHeight);
            _panel = panel.gameObject;

            float titleH = 64f;
            Text title = Label(panel, "Title", "UPGRADES  —  COAL ISLAND", 34, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f); title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(0f, -titleH); title.rectTransform.offsetMax = Vector2.zero;

            // scroll view
            var viewGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewGO.transform.SetParent(panel, false);
            var viewRT = (RectTransform)viewGO.transform;
            viewRT.anchorMin = Vector2.zero; viewRT.anchorMax = Vector2.one;
            viewRT.offsetMin = new Vector2(10f, 10f); viewRT.offsetMax = new Vector2(-10f, -titleH);
            viewGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(viewRT, false);
            var contentRT = (RectTransform)contentGO.transform;
            contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f); contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero; contentRT.offsetMax = Vector2.zero;
            var lay = contentGO.GetComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(8, 8, 8, 8); lay.spacing = 10f;
            lay.childControlWidth = true; lay.childControlHeight = true;
            lay.childForceExpandWidth = true; lay.childForceExpandHeight = false;
            contentGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollGO = _panel;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.viewport = viewRT; scroll.content = contentRT;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // station groups: header + one buy-row per axis
            int stations = _op != null ? _op.StationCount : 0;
            for (int s = 0; s < stations; s++)
            {
                Header(contentRT, _op.StationName(s));
                for (int a = 0; a < _op.AxisCount(s); a++)
                {
                    int cs = s, ca = a;
                    Row row = BuyRow(contentRT, 86f, () => { if (_op != null && _op.TryUpgrade(cs, ca)) Refresh(); });
                    row.station = s; row.axis = a;
                    _rows.Add(row);
                }
            }

            // ghost-building unlock section
            if (_op != null && _op.UnlockCount > 0)
            {
                Header(contentRT, "EXPANSIONS  (ghost buildings)");
                for (int u = 0; u < _op.UnlockCount; u++)
                {
                    int cu = u;
                    Row row = BuyRow(contentRT, 92f, () => { if (_op != null && _op.TryUnlock(cu)) Refresh(); });
                    row.unlock = u;
                    _rows.Add(row);
                }
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

        private void Header(Transform parent, string text)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 48f;
            Text t = Label((RectTransform)go.transform, "Text", text, 30, TextAnchor.MiddleLeft);
            t.color = new Color(0.75f, 0.82f, 0.92f);
            Stretch(t.rectTransform, 10f, 0f, 10f, 0f);
        }

        private Row BuyRow(Transform parent, float height, UnityEngine.Events.UnityAction onClick)
        {
            Button b = Btn(parent, "Buy", "", Buy, 30, onClick);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            Text lbl = b.GetComponentInChildren<Text>();
            lbl.alignment = TextAnchor.MiddleLeft;
            Stretch(lbl.rectTransform, 26f, 0f, 26f, 0f);
            return new Row { label = lbl, bg = b.GetComponent<Image>(), btn = b };
        }

        private void Refresh()
        {
            if (_cashText != null && _wallet != null) _cashText.text = "$ " + NumberFormatter.Format(_wallet.Cash);
            if (_rateText != null && _op != null) _rateText.text = "▲ $" + NumberFormatter.Format(new BigDouble(_op.CashPerMinute)) + " / min";
            if (_op == null || _panel == null || !_panel.activeSelf) return;
            BigDouble cash = _wallet != null ? _wallet.Cash : new BigDouble(0d);
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.unlock >= 0)
                {
                    if (_op.IsUnlocked(r.unlock))
                    {
                        r.label.text = _op.UnlockName(r.unlock) + "      BUILT ✓";
                        r.bg.color = Done; r.btn.interactable = false;
                    }
                    else
                    {
                        BigDouble ucost = _op.UnlockCost(r.unlock);
                        bool uafford = cash >= ucost;
                        r.label.text = _op.UnlockName(r.unlock) + "      $" + NumberFormatter.Format(ucost);
                        r.bg.color = uafford ? Ghost : Cant; r.btn.interactable = uafford;
                    }
                    continue;
                }
                if (_op.AxisMaxed(r.station, r.axis))
                {
                    r.label.text = _op.AxisName(r.station, r.axis) + "    Lv " + _op.AxisLevel(r.station, r.axis) + "      MAX";
                    r.bg.color = Done; r.btn.interactable = false;
                    continue;
                }
                BigDouble cost = _op.AxisCost(r.station, r.axis);
                bool afford = cash >= cost;
                r.label.text = _op.AxisName(r.station, r.axis) + "    Lv " + _op.AxisLevel(r.station, r.axis) + "      $" + NumberFormatter.Format(cost);
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
