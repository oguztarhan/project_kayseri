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
    /// The in-island mobile HUD (portrait, toony): a rounded currency pill up top, a gems pill, a TAP button,
    /// and a bottom tab sheet (Upgrades / Managers / Orders / Extras) of pooled rows. Built in code, DPI-scaled
    /// via CanvasScaler at 1080×1920 to match the World Map. Shown only while inside an island (the map hides
    /// it). Rows read the live economy through <see cref="GameWorld.Groups"/>.
    /// </summary>
    public sealed class HudUGUI : MonoBehaviour
    {
        [SerializeField] private int rowPool = 18;
        [SerializeField] private float refreshInterval = 0.2f;

        private WalletService _wallet;
        private EconomyService _economy;
        private PrestigeService _prestige;
        private IncomeMeter _income;
        private GameWorld _world;
        private IslandManager _islands;
        private Island _current;          // the island the player is currently inside (its tab is shown); null on the map
        private OfflineReport _offline;
        private BoostService _boost;
        private IAdService _ad;

        private Font _font;
        private Sprite _round;
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
        private static readonly Color Bar = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        private static readonly Color Pill = new Color(0.14f, 0.17f, 0.23f, 0.98f);
        private static readonly Color Panel = new Color(0.12f, 0.15f, 0.20f, 0.99f);
        private static readonly Color TabOn = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color TabOff = new Color(0.22f, 0.25f, 0.31f, 1f);
        private static readonly Color Row = new Color(0.24f, 0.27f, 0.34f, 1f);
        private static readonly Color CashGold = new Color(1f, 0.90f, 0.45f);
        private static readonly Color RateGreen = new Color(0.52f, 0.92f, 0.57f);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _economy = ServiceLocator.Get<EconomyService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _offline = ServiceLocator.Get<OfflineReport>();
            _boost = ServiceLocator.Get<BoostService>();
            _ad = ServiceLocator.Get<IAdService>();
            _world = FindAnyObjectByType<GameWorld>();
            _islands = FindAnyObjectByType<IslandManager>();
            _income = FindAnyObjectByType<IncomeMeter>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _round = MakeRound();
            Build();
        }

        /// <summary>Called by the World Map when the player enters/leaves an island — the Upgrades tab then shows
        /// THAT island's own tracks (or the real home-chain stations for the Coal home island). Null on the map.</summary>
        public void SetCurrentIsland(Island i) { _current = i; }

        // ---------- construction ----------
        private void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
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
            scaler.referenceResolution = new Vector2(1080f, 1920f);   // portrait, matches the World Map
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // ---- top: cash pill (centred so it clears the map's top-left MAP button) ----
            RectTransform cashPill = RoundPanel(root, "CashPill", Pill);
            cashPill.anchorMin = new Vector2(0.5f, 1f); cashPill.anchorMax = new Vector2(0.5f, 1f); cashPill.pivot = new Vector2(0.5f, 1f);
            cashPill.sizeDelta = new Vector2(600f, 132f); cashPill.anchoredPosition = new Vector2(30f, -20f);
            _cashText = Label(cashPill, "Cash", "$ 0", 52, TextAnchor.MiddleCenter); _cashText.color = CashGold;
            _cashText.rectTransform.anchorMin = new Vector2(0f, 0.42f); _cashText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _cashText.rectTransform.offsetMin = new Vector2(20f, 0f); _cashText.rectTransform.offsetMax = new Vector2(-20f, -6f);
            _rateText = Label(cashPill, "Rate", "+ 0 /min", 30, TextAnchor.MiddleCenter); _rateText.color = RateGreen;
            _rateText.rectTransform.anchorMin = new Vector2(0f, 0f); _rateText.rectTransform.anchorMax = new Vector2(1f, 0.42f);
            _rateText.rectTransform.offsetMin = new Vector2(20f, 6f); _rateText.rectTransform.offsetMax = new Vector2(-20f, 0f);

            // ---- gems pill top-right ----
            RectTransform gemPill = RoundPanel(root, "GemPill", Pill);
            gemPill.anchorMin = new Vector2(1f, 1f); gemPill.anchorMax = new Vector2(1f, 1f); gemPill.pivot = new Vector2(1f, 1f);
            gemPill.sizeDelta = new Vector2(300f, 132f); gemPill.anchoredPosition = new Vector2(-20f, -20f);
            _gemText = Label(gemPill, "Gems", "Gems 0\nInv 0", 30, TextAnchor.MiddleCenter);

            // ---- TAP button (bottom-right, above the tabs) ----
            Button tap = Btn(root, "TapMine", "TAP", TabOn, 34, () => { if (_world != null) _world.TapAllMines(); });
            RectTransform tapRt = tap.GetComponent<RectTransform>();
            tapRt.anchorMin = new Vector2(1f, 0f); tapRt.anchorMax = new Vector2(1f, 0f); tapRt.pivot = new Vector2(1f, 0f);
            tapRt.anchoredPosition = new Vector2(-26f, 150f); tapRt.sizeDelta = new Vector2(180f, 120f);

            // ---- bottom tab bar ----
            string[] names = { "Upgrades", "Managers", "Orders", "Extras" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                Button b = Btn(root, "Tab" + i, names[i], TabOff, 30, () => OnTab(idx));
                RectTransform rt = b.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i / 4f, 0f); rt.anchorMax = new Vector2((i + 1) / 4f, 0f); rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(6f, 8f); rt.offsetMax = new Vector2(-6f, 130f);
                _tabs[i] = b;
            }

            // ---- bottom sheet (hidden until a tab is tapped) ----
            RectTransform sheet = RoundPanel(root, "Sheet", Panel);
            sheet.anchorMin = new Vector2(0f, 0f); sheet.anchorMax = new Vector2(1f, 0f); sheet.pivot = new Vector2(0.5f, 0f);
            sheet.offsetMin = new Vector2(12f, 138f); sheet.offsetMax = new Vector2(-12f, 138f + 720f);
            _sheet = sheet;
            Text sheetTitle = Label(sheet, "SheetTitle", "", 30, TextAnchor.UpperLeft); sheetTitle.color = new Color(0.7f, 0.78f, 0.88f);
            sheetTitle.rectTransform.anchorMin = new Vector2(0f, 1f); sheetTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            sheetTitle.rectTransform.pivot = new Vector2(0.5f, 1f); sheetTitle.rectTransform.offsetMin = new Vector2(26f, -60f); sheetTitle.rectTransform.offsetMax = new Vector2(-96f, -14f);
            _sheetTitle = sheetTitle;
            Button close = Btn(sheet, "Close", "✕", TabOff, 32, () => { _open = false; RefreshOpen(); });
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-12f, -12f); crt.sizeDelta = new Vector2(70f, 70f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(sheet, false);
            RectTransform vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = new Vector2(0f, 0f); vrt.anchorMax = new Vector2(1f, 1f); vrt.offsetMin = new Vector2(16f, 16f); vrt.offsetMax = new Vector2(-16f, -74f);
            ScrollRect sr = viewport.GetComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 34f;

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f); _content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f; vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vrt; sr.content = _content;

            for (int i = 0; i < rowPool; i++)
            {
                Button rb = Btn(_content, "Row" + i, "", Row, 28, null);
                var le = rb.gameObject.AddComponent<LayoutElement>(); le.minHeight = 84f; le.preferredHeight = 84f;
                rb.gameObject.SetActive(false);
                _rows.Add(rb);
                _rowTexts.Add(rb.GetComponentInChildren<Text>());
            }

            // ---- welcome-back modal ----
            _welcome = RoundPanel(root, "Welcome", Panel).gameObject;
            RectTransform wrt = (RectTransform)_welcome.transform;
            wrt.anchorMin = new Vector2(0.5f, 0.5f); wrt.anchorMax = new Vector2(0.5f, 0.5f); wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(760f, 420f); wrt.anchoredPosition = Vector2.zero;
            _welcomeText = Label((RectTransform)_welcome.transform, "WText", "Welcome back!", 40, TextAnchor.UpperCenter);
            _welcomeText.rectTransform.anchorMin = new Vector2(0f, 0.35f); _welcomeText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _welcomeText.rectTransform.offsetMin = new Vector2(30f, 0f); _welcomeText.rectTransform.offsetMax = new Vector2(-30f, -34f);
            Button coll = Btn((RectTransform)_welcome.transform, "Collect", "Collect", TabOn, 36, () => { if (_offline != null) _offline.Pending = false; });
            RectTransform cort = coll.GetComponent<RectTransform>();
            cort.anchorMin = new Vector2(0.5f, 0f); cort.anchorMax = new Vector2(0.5f, 0f); cort.pivot = new Vector2(0.5f, 0f);
            cort.anchoredPosition = new Vector2(0f, 40f); cort.sizeDelta = new Vector2(560f, 110f);
            _welcome.SetActive(false);

            _tab = 0; _open = false;
            for (int t = 0; t < 4; t++) _tabs[t].GetComponent<Image>().color = TabOff;
            RefreshOpen();
        }

        private Text _sheetTitle;
        private static readonly string[] TabTitles = { "STATION UPGRADES", "MANAGERS", "ORDERS", "EXTRAS" };

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
            _gemText.text = "Gems " + _wallet.Gems + "\nInv " + inv;

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

        private void RefreshOpen()
        {
            if (_sheet != null) _sheet.gameObject.SetActive(_open);
            if (_open && _sheetTitle != null) _sheetTitle.text = TabTitles[_tab];
        }

        // Assign the pooled rows to the current tab's actions (called on open + on the refresh timer).
        private void FillRows()
        {
            if (_wallet == null)
            {
                for (int k = 0; k < _rows.Count; k++) if (_rows[k].gameObject.activeSelf) _rows[k].gameObject.SetActive(false);
                return;
            }
            int r = 0;
            bool home = _current != null && _current.Def != null && _current.Def.HomeIsland;

            if (_tab == 0) // Upgrades — THIS island's own tracks (or the real home-chain stations for Coal)
            {
                if (home && _world != null && _economy != null)
                {
                    var groups = _world.Groups;
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
                else if (_current != null && _islands != null)
                {
                    for (int t = 0; t < _current.TrackCount && r < _rows.Count; t++)
                    {
                        int lvl = _current.TrackLevel(t);
                        bool trackMax = lvl >= _current.MaxLevelPerTrack;
                        BigDouble c = _current.TrackCost(t);
                        int ti = t; Island cap = _current;
                        string txt = _current.TrackName(t) + "   Lv." + lvl + (trackMax ? "   ★ MAX" : "     $" + NumberFormatter.Format(c));
                        SetRow(r++, txt, !trackMax && _wallet.Cash >= c, () => _islands.TryUpgradeTrack(cap, ti));
                    }
                }
            }
            else if (_tab == 1) // Managers — home real chain only
            {
                if (home && _world != null)
                {
                    var groups = _world.Groups;
                    for (int i = 0; i < groups.Count && r < _rows.Count; i++)
                    {
                        GameWorld.Group g = groups[i]; if (g == null || !_world.GroupCanManage(g)) continue;
                        if (_world.GroupHasManager(g)) SetRow(r++, g.Label + "   Manager hired (x2)", false, null);
                        else { BigDouble mc = _world.GroupManagerCost(g); GameWorld.Group gg = g; SetRow(r++, "Hire " + g.Label + " Manager    $" + NumberFormatter.Format(mc), _wallet.Cash >= mc, () => _world.TryHireManagerGroup(gg)); }
                    }
                }
                else SetRow(r++, "Managers — home island only", false, null);
            }
            else if (_tab == 2) // Orders
            {
                SetRow(r++, "Orders & contracts — see the world map for live goals", false, null);
            }
            else // Extras
            {
                if (_prestige != null && _world != null)
                {
                    SetRow(r++, "Income x" + _prestige.IncomeMultiplier.ToString("0.00") + "   Investors " + _prestige.Investors.ToString("F0"), false, null);
                    bool can = _prestige.CanPrestige();
                    SetRow(r++, "PRESTIGE  (+" + NumberFormatter.Format(_prestige.PendingInvestors()) + " investors)", can, () => _world.DoPrestige());
                }
                if (_ad != null)
                {
                    SetRow(r++, "Watch Ad  →  +10 gems", true, () => _ad.ShowRewarded(() => _wallet.AddGems(10)));
                    SetRow(r++, "Watch Ad  →  2x income 30s", true, () => _ad.ShowRewarded(() => { if (_boost != null) _boost.SetBoost(2d, 30d); }));
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
        private RectTransform RoundPanel(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = _round; img.type = Image.Type.Sliced; img.color = c;
            return (RectTransform)go.transform;
        }

        private Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.text = text; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            SetStretch((RectTransform)go.transform, 0f, 0f);
            return t;
        }

        private Button Btn(Transform parent, string name, string text, Color c, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = _round; img.type = Image.Type.Sliced; img.color = c;
            var t = Label((RectTransform)go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            SetStretch(t.rectTransform, 16f, 2f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            var colors = b.colors; colors.disabledColor = new Color(c.r, c.g, c.b, 0.4f); b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        private void SetStretch(RectTransform rt, float insetX, float insetY)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(insetX, insetY); rt.offsetMax = new Vector2(-insetX, -insetY);
        }

        // rounded-rect 9-slice sprite for the toony panels/buttons
        private Sprite MakeRound()
        {
            const int S = 48; const float rad = 16f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Max(rad - x, 0f, x - (S - 1 - rad));
                    float dy = Mathf.Max(rad - y, 0f, y - (S - 1 - rad));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rad - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(rad, rad, rad, rad));
        }
    }
}
