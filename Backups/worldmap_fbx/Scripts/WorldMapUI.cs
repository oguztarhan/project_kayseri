using System.Collections.Generic;
using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The archipelago <b>World Map</b> hub. The map is the real 3D toony sea of islands viewed through the
    /// shared <see cref="CameraController"/> pulled up into a top-down "map" framing: roam with your finger,
    /// tap an islet to select it, ENTER to fly in. Locked islands ghost themselves (see <see cref="Island"/>).
    /// This class owns only the framing + the lightweight portrait UI (floating name pills + a bottom action
    /// card); all progression flows through the unchanged <see cref="IslandManager"/>.
    /// </summary>
    public sealed class WorldMapUI : MonoBehaviour
    {
        [SerializeField] private float refreshInterval = 0.25f;

        [Header("Map camera profile")]
        [SerializeField] private Vector3 mapLook = new Vector3(0f, 0f, 600f);
        [SerializeField] private Vector3 mapRotEuler = new Vector3(56f, 32f, 0f);
        [SerializeField] private float mapBack = 500f;
        [SerializeField] private float mapSize = 330f;
        [SerializeField] private Vector2 mapZoom = new Vector2(150f, 460f);
        [SerializeField] private Vector2 mapPan = new Vector2(330f, 340f);   // half-extents around the framing

        [Header("Island camera profile")]
        [SerializeField] private Vector3 islandRotEuler = new Vector3(52f, 32f, 0f);
        [SerializeField] private float islandBack = 200f;
        [SerializeField] private float islandSize = 95f;
        [SerializeField] private Vector2 islandZoom = new Vector2(45f, 160f);
        [SerializeField] private float islandPan = 95f;

        private WalletService _wallet;
        private IslandManager _islands;
        private Camera _cam;
        private CameraController _camCtrl;
        private Canvas _hudCanvas;
        private HudDebug _hudDebug;

        private Font _font;
        private Sprite _circle, _flat;
        private Canvas _canvas;
        private RectTransform _mapUI;
        private RectTransform _pillsParent;
        private GameObject _backBtn;
        private Text _cashText;
        private float _timer;

        private enum Mode { Map, Island }
        private Mode _mode = Mode.Map;

        // tap detection (map mode): a tap = small travel + short hold, not a drag
        private bool _wasPressed;
        private Vector2 _downPos;
        private float _downTime;
        private float _maxTravel;

        // bottom selection card
        private Island _selected;
        private RectTransform _card;
        private Image _cardSwatch;
        private Text _cardName, _cardInfo;
        private Button _cardAct; private Text _cardActTxt; private Image _cardActBg;
        private Button _cardEnter;

        private sealed class Pill { public Island isl; public RectTransform rt; public Text txt; }
        private readonly List<Pill> _pills = new List<Pill>();

        private static readonly Color Sea = new Color(0.16f, 0.44f, 0.62f, 1f);
        private static readonly Color PanelBg = new Color(0.11f, 0.14f, 0.19f, 0.96f);
        private static readonly Color Amber = new Color(0.90f, 0.62f, 0.16f, 1f);
        private static readonly Color Green = new Color(0.20f, 0.62f, 0.36f, 1f);
        private static readonly Color Gold = new Color(0.82f, 0.68f, 0.20f, 1f);
        private static readonly Color GreyBtn = new Color(0.30f, 0.33f, 0.38f, 1f);
        private const string AmberHex = "E69E29", GreenHex = "5FD98A", GoldHex = "E6C94D", GreyHex = "AEB2BA";

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _islands = FindAnyObjectByType<IslandManager>();
            _cam = Camera.main;
            _camCtrl = FindAnyObjectByType<CameraController>();
            _hudDebug = FindAnyObjectByType<HudDebug>();
            var hud = FindAnyObjectByType<HudUGUI>();
            if (hud != null) _hudCanvas = hud.GetComponentInChildren<Canvas>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (_cam != null) _cam.farClipPlane = Mathf.Max(_cam.farClipPlane, 3000f);   // pulling back to map framing must not clip

            _circle = MakeCircle(); _flat = MakeFlat();
            Build();
            OpenMap();
        }

        private void Update()
        {
            if (_wallet == null) return;
            HandlePointer();
            if (_mode == Mode.Map) UpdatePillPositions();   // every frame so pills track the panning camera
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            if (_mode == Mode.Map) RefreshStates();
        }

        // ---------------- tap-to-select ----------------
        private void HandlePointer()
        {
            bool pressed = false; Vector2 pos = Vector2.zero;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) { pressed = true; pos = ts.primaryTouch.position.ReadValue(); }
            else { var m = Mouse.current; if (m != null) { pressed = m.leftButton.isPressed; pos = m.position.ReadValue(); } }

            if (pressed && !_wasPressed) { _downPos = pos; _downTime = Time.unscaledTime; _maxTravel = 0f; }
            if (pressed) _maxTravel = Mathf.Max(_maxTravel, Vector2.Distance(pos, _downPos));
            if (!pressed && _wasPressed)
            {
                bool tap = _maxTravel < 26f && (Time.unscaledTime - _downTime) < 0.4f && !CameraController.PointerOverUI();
                if (tap && _mode == Mode.Map) SelectNearest(_downPos);
            }
            _wasPressed = pressed;
        }

        private void SelectNearest(Vector2 screenPos)
        {
            if (_islands == null || _islands.Islands == null || _cam == null) return;
            Island best = null; float bestD = 0.16f * Screen.height;
            var arr = _islands.Islands;
            for (int i = 0; i < arr.Length; i++)
            {
                Island isl = arr[i]; if (isl == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(isl.LabelAnchor);
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (d < bestD) { bestD = d; best = isl; }
            }
            if (best != null) ShowCard(best); else HideCard();
        }

        // ---------------- build ----------------
        private void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var canvasGO = new GameObject("WorldMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);   // portrait phone
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = (RectTransform)canvasGO.transform;

            // map-only UI layer (top bar + pills + card); hidden inside an island
            _mapUI = Panel(root, "MapUI", new Color(0f, 0f, 0f, 0f));
            Stretch(_mapUI, 0f, 0f, 0f, 0f);
            _mapUI.GetComponent<Image>().raycastTarget = false;

            // top bar
            RectTransform bar = Panel(_mapUI, "TopBar", PanelBg);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -150f); bar.offsetMax = Vector2.zero;
            Text title = Label(bar, "Title", "WORLD MAP", 46, TextAnchor.MiddleLeft);
            Band(title.rectTransform, 0f, 0.6f, 34f);
            _cashText = Label(bar, "Cash", "$ 0", 44, TextAnchor.MiddleRight);
            _cashText.color = new Color(1f, 0.92f, 0.5f);
            Band(_cashText.rectTransform, 0.4f, 1f, 34f);
            Text hint = Label(bar, "Hint", "Tap an island  ·  drag to roam the sea", 26, TextAnchor.LowerLeft);
            hint.color = new Color(0.7f, 0.78f, 0.85f);
            hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.offsetMin = new Vector2(34f, 8f); hint.rectTransform.offsetMax = new Vector2(-34f, 40f);

            // pills parent (floating labels over the 3D islands)
            _pillsParent = Panel(_mapUI, "Pills", new Color(0f, 0f, 0f, 0f));
            Stretch(_pillsParent, 0f, 0f, 0f, 0f);
            _pillsParent.GetComponent<Image>().raycastTarget = false;
            if (_islands != null && _islands.Islands != null)
                for (int i = 0; i < _islands.Islands.Length; i++) _pills.Add(MakePill(_islands.Islands[i]));

            BuildCard(_mapUI);

            // back-to-map button (island mode only)
            Button back = Btn(root, "BackBtn", "◀  MAP", PanelBg, 34, OpenMap);
            RectTransform brt = back.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(24f, -24f); brt.sizeDelta = new Vector2(210f, 88f);
            _backBtn = back.gameObject;
        }

        private Pill MakePill(Island isl)
        {
            var go = new GameObject("Pill", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_pillsParent, false);
            var bg = go.GetComponent<Image>(); bg.sprite = _flat; bg.type = Image.Type.Sliced; bg.color = new Color(0.08f, 0.10f, 0.14f, 0.82f); bg.raycastTarget = false;
            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(250f, 60f); rt.pivot = new Vector2(0.5f, 0f);
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 0f);
            var t = Label(rt, "T", "", 26, TextAnchor.MiddleCenter);
            t.supportRichText = true; Stretch(t.rectTransform, 10f, 2f, 10f, 2f);
            return new Pill { isl = isl, rt = rt, txt = t };
        }

        private void BuildCard(RectTransform parent)
        {
            _card = Panel(parent, "Card", PanelBg);
            _card.anchorMin = new Vector2(0f, 0f); _card.anchorMax = new Vector2(1f, 0f); _card.pivot = new Vector2(0.5f, 0f);
            _card.offsetMin = new Vector2(24f, 24f); _card.offsetMax = new Vector2(-24f, 24f + 380f);

            _cardSwatch = Img(_card, "Swatch", _circle, Color.white, new Vector2(0f, 0f), new Vector2(96f, 96f));
            _cardSwatch.rectTransform.anchorMin = new Vector2(0f, 1f); _cardSwatch.rectTransform.anchorMax = new Vector2(0f, 1f);
            _cardSwatch.rectTransform.pivot = new Vector2(0f, 1f); _cardSwatch.rectTransform.anchoredPosition = new Vector2(28f, -24f);

            _cardName = Label(_card, "Name", "", 44, TextAnchor.UpperLeft);
            _cardName.rectTransform.anchorMin = new Vector2(0f, 1f); _cardName.rectTransform.anchorMax = new Vector2(1f, 1f);
            _cardName.rectTransform.pivot = new Vector2(0f, 1f); _cardName.rectTransform.offsetMin = new Vector2(150f, -84f); _cardName.rectTransform.offsetMax = new Vector2(-90f, -24f);
            _cardInfo = Label(_card, "Info", "", 28, TextAnchor.UpperLeft);
            _cardInfo.color = new Color(0.78f, 0.82f, 0.9f);
            _cardInfo.rectTransform.anchorMin = new Vector2(0f, 1f); _cardInfo.rectTransform.anchorMax = new Vector2(1f, 1f);
            _cardInfo.rectTransform.pivot = new Vector2(0f, 1f); _cardInfo.rectTransform.offsetMin = new Vector2(150f, -140f); _cardInfo.rectTransform.offsetMax = new Vector2(-24f, -86f);

            Button close = Btn(_card, "X", "✕", GreyBtn, 30, HideCard);
            RectTransform crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-16f, -16f); crt.sizeDelta = new Vector2(62f, 62f);

            _cardAct = Btn(_card, "Act", "", Green, 32, null);
            _cardActBg = _cardAct.GetComponent<Image>(); _cardActTxt = _cardAct.GetComponentInChildren<Text>();
            RectTransform art = _cardAct.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 0f); art.anchorMax = new Vector2(0.6f, 0f); art.pivot = new Vector2(0f, 0f);
            art.offsetMin = new Vector2(24f, 22f); art.offsetMax = new Vector2(-8f, 22f + 92f);

            _cardEnter = Btn(_card, "Enter", "ENTER ▶", new Color(0.18f, 0.42f, 0.68f, 1f), 32, () => { if (_selected != null) EnterIsland(_selected); });
            RectTransform ert = _cardEnter.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0.6f, 0f); ert.anchorMax = new Vector2(1f, 0f); ert.pivot = new Vector2(0f, 0f);
            ert.offsetMin = new Vector2(8f, 22f); ert.offsetMax = new Vector2(-24f, 22f + 92f);

            _card.gameObject.SetActive(false);
        }

        // ---------------- per-frame + interval refresh ----------------
        private void UpdatePillPositions()
        {
            if (_cam == null || _canvas == null) return;
            float inv = 1f / Mathf.Max(0.0001f, _canvas.scaleFactor);
            for (int i = 0; i < _pills.Count; i++)
            {
                Pill p = _pills[i]; if (p.isl == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(p.isl.LabelAnchor);
                bool vis = sp.z > 0f;
                if (p.rt.gameObject.activeSelf != vis) p.rt.gameObject.SetActive(vis);
                if (vis) p.rt.anchoredPosition = new Vector2(sp.x * inv, sp.y * inv);
            }
        }

        private void RefreshStates()
        {
            if (_wallet != null && _cashText != null) _cashText.text = "$ " + NumberFormatter.Format(_wallet.Cash);
            if (_islands == null) return;
            for (int i = 0; i < _pills.Count; i++)
            {
                Pill p = _pills[i]; Island isl = p.isl;
                if (isl == null || isl.Def == null) continue;
                p.txt.text = isl.Def.DisplayName + "  " + PillTag(isl);
            }
            if (_selected != null && _card.gameObject.activeSelf) UpdateCard(_selected);
        }

        private string PillTag(Island isl)
        {
            if (isl.Unlocked)
                return isl.IsMaxed ? "<color=#" + GoldHex + ">★</color>"
                                   : "<color=#" + GreenHex + ">Lv " + isl.Level + "/" + isl.MaxLevel + "</color>";
            if (_islands.CanUnlock(isl))
                return "<color=#" + AmberHex + ">$" + NumberFormatter.Format(new BigDouble(isl.Def.UnlockCost)) + "</color>";
            return "<color=#" + GreyHex + ">🔒</color>";
        }

        private void ShowCard(Island isl) { _selected = isl; _card.gameObject.SetActive(true); UpdateCard(isl); }
        private void HideCard() { _selected = null; if (_card != null) _card.gameObject.SetActive(false); }

        private void UpdateCard(Island isl)
        {
            if (isl.Def == null) return;
            bool unlocked = isl.Unlocked, maxed = isl.IsMaxed, can = _islands.CanUnlock(isl);
            BigDouble cash = _wallet != null ? _wallet.Cash : new BigDouble(0d);
            Color oc = isl.Def.Ore != null ? isl.Def.Ore.Color : Color.white;
            _cardSwatch.color = oc;
            _cardName.text = isl.Def.DisplayName;

            if (unlocked)
            {
                string inc = isl.Def.HomeIsland ? "Home operation" : ("+ $" + NumberFormatter.Format(new BigDouble(isl.IncomePerSec)) + "/s");
                _cardInfo.text = inc + "     Lv " + isl.Level + " / " + isl.MaxLevel;
            }
            else _cardInfo.text = can ? "Ready to claim — unlock to start mining" : "Locked — max the previous island first";

            _cardAct.onClick.RemoveAllListeners();
            if (unlocked && maxed) { _cardActTxt.text = "★ MAXED"; _cardActBg.color = Gold; _cardAct.interactable = false; }
            else if (unlocked)
            {
                BigDouble uc = new BigDouble(isl.UpgradeCost);
                _cardActTxt.text = "UPGRADE  $" + NumberFormatter.Format(uc); _cardActBg.color = Green; _cardAct.interactable = cash >= uc;
                Island cap = isl; _cardAct.onClick.AddListener(() => _islands.TryUpgrade(cap));
            }
            else if (can)
            {
                BigDouble c = new BigDouble(isl.Def.UnlockCost);
                _cardActTxt.text = "UNLOCK  $" + NumberFormatter.Format(c); _cardActBg.color = Amber; _cardAct.interactable = cash >= c;
                Island cap = isl; _cardAct.onClick.AddListener(() => _islands.TryUnlock(cap));
            }
            else { _cardActTxt.text = "LOCKED"; _cardActBg.color = GreyBtn; _cardAct.interactable = false; }

            _cardEnter.gameObject.SetActive(unlocked);
        }

        // ---------------- navigation / camera modes ----------------
        private void ResolveHud()
        {
            if (_hudCanvas == null) { var hud = FindAnyObjectByType<HudUGUI>(); if (hud != null) _hudCanvas = hud.GetComponentInChildren<Canvas>(); }
            if (_hudDebug == null) _hudDebug = FindAnyObjectByType<HudDebug>();
        }

        private void OpenMap()
        {
            _mode = Mode.Map;
            ResolveHud();
            if (_mapUI != null) _mapUI.gameObject.SetActive(true);
            if (_backBtn != null) _backBtn.SetActive(false);
            if (_hudCanvas != null) _hudCanvas.enabled = false;
            if (_hudDebug != null) _hudDebug.enabled = false;
            HideCard();

            Quaternion rot = Quaternion.Euler(mapRotEuler);
            Vector3 pos = mapLook - rot * Vector3.forward * mapBack;
            if (_camCtrl != null)
            {
                _camCtrl.enabled = true;
                _camCtrl.SetZoomRange(mapZoom.x, mapZoom.y);
                _camCtrl.SetBounds(new Vector2(pos.x - mapPan.x, pos.x + mapPan.x), new Vector2(pos.z - mapPan.y, pos.z + mapPan.y));
                _camCtrl.FrameTo(pos, rot, mapSize);
            }
            else if (_cam != null) { _cam.transform.SetPositionAndRotation(pos, rot); if (_cam.orthographic) _cam.orthographicSize = mapSize; }
            RefreshStates();
        }

        private void EnterIsland(Island isl)
        {
            if (isl == null || !isl.Unlocked) return;
            _mode = Mode.Island;
            ResolveHud();
            if (_mapUI != null) _mapUI.gameObject.SetActive(false);
            if (_backBtn != null) _backBtn.SetActive(true);
            if (_hudCanvas != null) _hudCanvas.enabled = true;
            if (_hudDebug != null) _hudDebug.enabled = true;

            // every island zooms into its spot on the fbx world map
            Quaternion rot = Quaternion.Euler(islandRotEuler);
            Vector3 pos = isl.transform.position + rot * Vector3.forward * -islandBack;
            float size = islandSize;

            if (_camCtrl != null)
            {
                _camCtrl.enabled = true;
                _camCtrl.SetZoomRange(islandZoom.x, islandZoom.y);
                _camCtrl.SetBounds(new Vector2(pos.x - islandPan, pos.x + islandPan), new Vector2(pos.z - islandPan, pos.z + islandPan));
                _camCtrl.FrameTo(pos, rot, size);
            }
            else if (_cam != null) { _cam.transform.SetPositionAndRotation(pos, rot); if (_cam.orthographic) _cam.orthographicSize = size; }
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
            var img = go.GetComponent<Image>(); img.sprite = _flat; img.type = Image.Type.Sliced; img.color = c;
            var t = Label((RectTransform)go.transform, "Text", text, size, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4f, 2f, 4f, 2f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

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

        // ---------------- procedural sprites ----------------
        private Sprite MakeCircle()
        {
            const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            float c = (S - 1) * 0.5f, rad = c - 1f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - d)));
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
