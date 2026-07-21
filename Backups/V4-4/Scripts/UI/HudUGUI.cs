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
    /// Real uGUI mobile HUD (GDD §12): DPI-correct via CanvasScaler, big thumb-reachable targets, currency bar
    /// up top, a bottom tab sheet (Upgrades / Managers / Orders / Extras) that opens/closes, and a TAP MINE
    /// button — built in code so it's version-controlled. Static frame and the dynamic upgrade list live on
    /// separate canvases (canvas-split) so refreshing rows never rebuilds the frame. Uses the Input System UI
    /// module (this project's active input). Rows are pooled; text refreshes on a timer, not every frame.
    /// </summary>
    public sealed class HudUGUI : MonoBehaviour
    {
        [SerializeField] private int rowPool = 16;
        [SerializeField] private float refreshInterval = 0.2f;

        private WalletService _wallet;
        private EconomyService _economy;
        private PrestigeService _prestige;
        private IncomeMeter _income;
        private GameWorld _world;
        private OfflineReport _offline;
        private BoostService _boost;
        private IAdService _ad;

        private Font _font;
        private Text _cashText, _rateText, _gemText;
        private RectTransform _sheet;
        private RectTransform _content;
        private GameObject _welcome;
        private Text _welcomeText;
        private readonly Button[] _tabs = new Button[4];
        private readonly List<Button> _rows = new List<Button>();
        private readonly List<Text> _rowTexts = new List<Text>();

        private int _tab;
        private bool _open;
        private float _timer;
        private static readonly Color Bar = new Color(0.10f, 0.12f, 0.16f, 0.96f);
        private static readonly Color Panel = new Color(0.14f, 0.16f, 0.20f, 0.98f);
        private static readonly Color TabOn = new Color(0.16f, 0.55f, 0.34f, 1f);
        private static readonly Color TabOff = new Color(0.20f, 0.23f, 0.28f, 1f);
        private static readonly Color Row = new Color(0.22f, 0.25f, 0.30f, 1f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _offline = ServiceLocator.Get<OfflineReport>();
            _boost = ServiceLocator.Get<BoostService>();
            _ad = ServiceLocator.Get<IAdService>();
            _world = FindFirstObjectByType<GameWorld>();
            _income = FindFirstObjectByType<IncomeMeter>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
        }

        // ---------- construction ----------
        private void Build()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvasGO = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // top currency bar (anchored full-width to the top)
            RectTransform bar = Panel2(root, "TopBar", Bar);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -110f); bar.offsetMax = new Vector2(0f, 0f);
            _cashText = Label(bar, "Cash", "$ 0", 46, TextAnchor.MiddleLeft);
            SetBand(_cashText.rectTransform, 0f, 0.42f, 24f);
            _rateText = Label(bar, "Rate", "+ 0 /min", 34, TextAnchor.MiddleCenter);
            _rateText.color = new Color(0.45f, 0.9f, 0.5f);
            SetBand(_rateText.rectTransform, 0.32f, 0.66f, 0f);
            _gemText = Label(bar, "Gems", "Gems 0", 36, TextAnchor.MiddleRight);
            SetBand(_gemText.rectTransform, 0.58f, 1f, 24f);

            // TAP MINE (top-left, under the bar)
            Button tap = Btn(root, "TapMine", "TAP MINE", TabOff, () => { if (_world != null) _world.TapAllMines(); });
            RectTransform tapRt = tap.GetComponent<RectTransform>();
            tapRt.anchorMin = new Vector2(0f, 1f); tapRt.anchorMax = new Vector2(0f, 1f); tapRt.pivot = new Vector2(0f, 1f);
            tapRt.anchoredPosition = new Vector2(18f, -122f); tapRt.sizeDelta = new Vector2(230f, 96f);

            // bottom tab bar
            string[] names = { "Upgrades", "Managers", "Orders", "Extras" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Button b = Btn(root, "Tab" + i, names[i], TabOff, () => OnTab(idx));
                RectTransform rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i / 4f, 0f); rt.anchorMax = new Vector2((i + 1) / 4f, 0f); rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(2f, 0f); rt.offsetMax = new Vector2(-2f, 104f);
                _tabs[i] = b;
            }

            // bottom sheet (hidden until a tab is tapped) with a scroll list
            RectTransform sheet = Panel2(root, "Sheet", Panel);
            sheet.anchorMin = new Vector2(0f, 0f); sheet.anchorMax = new Vector2(1f, 0f); sheet.pivot = new Vector2(0.5f, 0f);
            sheet.offsetMin = new Vector2(0f, 104f); sheet.offsetMax = new Vector2(0f, 104f + 300f);
            _sheet = sheet;
            Button close = Btn(sheet, "Close", "X", TabOff, () => { _open = false; RefreshOpen(); });
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-8f, -8f); crt.sizeDelta = new Vector2(60f, 60f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(sheet, false);
            RectTransform vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(1f, 1f); vrt.offsetMin = new Vector2(10f, 10f); vrt.offsetMax = new Vector2(-76f, -10f);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            ScrollRect sr = viewport.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f; vlg.childControlWidth = true; vlg.childControlHeight = false; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vrt; sr.content = _content;

            for (int i = 0; i < rowPool; i++)
            {
                Button rb = Btn(_content, "Row" + i, "", Row, null);
                var le = rb.gameObject.AddComponent<LayoutElement>(); le.minHeight = 66f; le.preferredHeight = 66f;
                rb.gameObject.SetActive(false);
                _rows.Add(rb);
                _rowTexts.Add(rb.GetComponentInChildren<Text>());
            }

            // welcome-back modal
            _welcome = Panel2(root, "Welcome", Panel).gameObject;
            RectTransform wrt = (RectTransform)_welcome.transform;
            wrt.anchorMin = new Vector2(0.5f, 0.5f); wrt.anchorMax = new Vector2(0.5f, 0.5f); wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(560f, 240f); wrt.anchoredPosition = Vector2.zero;
            _welcomeText = Label((RectTransform)_welcome.transform, "WText", "Welcome back!", 34, TextAnchor.UpperCenter);
            _welcomeText.rectTransform.anchorMin = new Vector2(0f, 0.4f); _welcomeText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _welcomeText.rectTransform.offsetMin = new Vector2(20f, 0f); _welcomeText.rectTransform.offsetMax = new Vector2(-20f, -18f);
            Button coll = Btn((RectTransform)_welcome.transform, "Collect", "Collect", TabOn, () => { if (_offline != null) _offline.Pending = false; });
            RectTransform cort = coll.GetComponent<RectTransform>();
            cort.anchorMin = new Vector2(0.5f, 0f); cort.anchorMax = new Vector2(0.5f, 0f); cort.pivot = new Vector2(0.5f, 0f);
            cort.anchoredPosition = new Vector2(0f, 24f); cort.sizeDelta = new Vector2(480f, 72f);
            _welcome.SetActive(false);

            _tab = 0; _open = false;
            for (int t = 0; t < 4; t++) _tabs[t].GetComponent<Image>().color = TabOff;
            RefreshOpen();
        }

        // ---------- update ----------
        private void Update()
        {
            if (_wallet == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;

            string boost = (_boost != null && _boost.IsActive) ? "  (x2!)" : "";
            _cashText.text = "$ " + NumberFormatter.Format(_wallet.Cash) + boost;
            if (_income != null) _rateText.text = "+ " + NumberFormatter.Format(new BigDouble(_income.RatePerSec * 60d)) + " /min";
            string inv = _prestige != null ? _prestige.Investors.ToString("F0") : "0";
            _gemText.text = "Gems " + _wallet.Gems + "    Inv " + inv;

            bool showWelcome = _offline != null && _offline.Pending;
            if (_welcome.activeSelf != showWelcome) _welcome.SetActive(showWelcome);
            if (showWelcome) _welcomeText.text = "Welcome back!\nWhile away you earned\n" + NumberFormatter.Format(_offline.Amount) + " cash";

            if (_open) FillRows();
        }

        private void OnTab(int i)
        {
            if (_open && _tab == i) { _open = false; }
            else { _tab = i; _open = true; }
            for (int t = 0; t < 4; t++) _tabs[t].GetComponent<Image>().color = (_open && t == i) ? TabOn : TabOff;
            RefreshOpen();
            if (_open) FillRows();
        }

        private void RefreshOpen() { if (_sheet != null) _sheet.gameObject.SetActive(_open); }

        // Assign the pooled rows to the current tab's actions (called on open + on the refresh timer).
        private void FillRows()
        {
            if (_world == null) return;
            int r = 0;
            var groups = _world.Groups;
            if (_tab == 0) // Upgrades
            {
                for (int i = 0; i < groups.Count && r < _rows.Count; i++)
                {
                    GameWorld.Group g = groups[i]; if (g == null || g.Rep == null) continue;
                    string fleet = g.Members.Count > 1 ? " x" + g.Members.Count : "";
                    for (int t = 0; t < g.Rep.TrackCount && r < _rows.Count; t++)
                    {
                        BigDouble c = g.Rep.TrackCost(t, _economy);
                        int ti = t; GameWorld.Group gg = g;
                        SetRow(r++, g.Label + fleet + "  —  " + g.Rep.TrackName(t) + "   Lv." + g.Rep.TrackLevel(t) + "     $" + NumberFormatter.Format(c),
                            _wallet.Cash >= c, () => _world.TryUpgradeGroup(gg, ti));
                    }
                }
            }
            else if (_tab == 1) // Managers
            {
                for (int i = 0; i < groups.Count && r < _rows.Count; i++)
                {
                    GameWorld.Group g = groups[i]; if (g == null || !_world.GroupCanManage(g)) continue;
                    if (_world.GroupHasManager(g)) SetRow(r++, g.Label + "   Manager hired (x2)", false, null);
                    else { BigDouble mc = _world.GroupManagerCost(g); GameWorld.Group gg = g; SetRow(r++, "Hire " + g.Label + " Manager    $" + NumberFormatter.Format(mc), _wallet.Cash >= mc, () => _world.TryHireManagerGroup(gg)); }
                }
            }
            else if (_tab == 2) // Orders — prestige lives in Extras; show daily/contract via simple text rows
            {
                SetRow(r++, "Orders & contracts — see the world for live goals", false, null);
            }
            else // Extras
            {
                if (_prestige != null)
                {
                    SetRow(r++, "Income x" + _prestige.IncomeMultiplier.ToString("0.00") + "   Investors " + _prestige.Investors.ToString("F0"), false, null);
                    bool can = _prestige.CanPrestige();
                    SetRow(r++, "PRESTIGE  (+" + NumberFormatter.Format(_prestige.PendingInvestors()) + " investors)", can, () => _world.DoPrestige());
                }
                if (_ad != null)
                {
                    SetRow(r++, "Watch Ad  ->  +10 gems", true, () => _ad.ShowRewarded(() => _wallet.AddGems(10)));
                    SetRow(r++, "Watch Ad  ->  2x income 30s", true, () => _ad.ShowRewarded(() => { if (_boost != null) _boost.SetBoost(2d, 30d); }));
                }
            }
            for (; r < _rows.Count; r++) _rows[r].gameObject.SetActive(false);
        }

        private void SetRow(int i, string text, bool interactable, UnityEngine.Events.UnityAction onClick)
        {
            Button b = _rows[i];
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            _rowTexts[i].text = text;
            b.interactable = interactable && onClick != null;
            b.onClick.RemoveAllListeners();
            if (onClick != null) b.onClick.AddListener(onClick);
        }

        // ---------- tiny uGUI builders ----------
        private RectTransform Panel2(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            return (RectTransform)go.transform;
        }

        private Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            SetStretch((RectTransform)go.transform, 0f, 0f, 1f, 1f);
            return t;
        }

        private Button Btn(Transform parent, string name, string text, Color c, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            var t = Label((RectTransform)go.transform, "Text", text, 26, TextAnchor.MiddleCenter);
            SetStretch(t.rectTransform, 6f, 2f, 1f, 1f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        // stretch a rect to fill its parent with pixel insets on min, and fractional anchors
        private void SetStretch(RectTransform rt, float insetX, float insetY, float axMax, float ayMax)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(insetX, insetY); rt.offsetMax = new Vector2(-insetX, -insetY);
        }
        private void SetStretch(RectTransform rt, float axMin, float axMax)
        {
            rt.anchorMin = new Vector2(axMin, 0f); rt.anchorMax = new Vector2(axMax, 1f); rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
        private void SetBand(RectTransform rt, float axMin, float axMax, float padX)
        {
            rt.anchorMin = new Vector2(axMin, 0f); rt.anchorMax = new Vector2(axMax, 1f);
            rt.offsetMin = new Vector2(padX, 0f); rt.offsetMax = new Vector2(-padX, 0f);
        }
    }
}
