using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The player's own fighting ship, lying at anchor off the island's harbour, and the button that
    /// boards her. Tapping it opens the sea scene — the search-and-fight adventure that used to be
    /// entered from the dock panel's board button and now belongs to the port itself.
    ///
    /// THE HULL IS BUILT HERE, from the same primitives and paint as the sea scene's ship, so the
    /// boat the player taps at the pier is recognisably the boat they then stand on at sea. It is
    /// parented under the island root, which is what makes travel and scene-parking free: the hull
    /// sleeps and wakes with the island it belongs to.
    ///
    /// SELF-HOSTING, unlike the authored markers beside it. MarketDoorMarker and PortContractMarker
    /// are objects in Main.unity; this one spawns its own persistent host at boot instead, because
    /// it has to exist at whichever island's port is live and adding it to the scene would mean
    /// editing Main for a component with nothing to wire. Same construction as those markers
    /// otherwise: one screen-space canvas, a slow re-bind, and the badge hides when the harbour
    /// leaves the frame — the ship belongs to the port, not to the HUD.
    /// </summary>
    public sealed class PortShipMarker : MonoBehaviour
    {
        [Header("Hedef")]
        [Tooltip("Dokununca açılacak sahne. Build Settings'te ekli olmalı.")]
        [SerializeField] private string seaSceneName = "Sea";

        [Header("Görsel")]
        [Tooltip("Düğmenin genişliği ve yüksekliği, referans çözünürlükte piksel.")]
        [SerializeField] private Vector2 buttonSize = new Vector2(230f, 78f);
        [Tooltip("Yazı boyu. Uzun dillerde düğmeden taşmayacak kadar küçük olmalı.")]
        [SerializeField, Min(10)] private int labelSize = 28;
        [Tooltip("Geminin direğinden ne kadar yukarıda durduğu, dünya birimi.")]
        [SerializeField] private float worldLift = 9f;
        [Tooltip("HUD 100, yükseltme rozetleri 92. Kapı rozetiyle aynı katta, dokunuş için üstte.")]
        [SerializeField] private int sortingOrder = 92;
        [Tooltip("Kenar payı: gemi ekranın bu kadar yakınında ya da dışındaysa rozet gizlenir — " +
                 "liman kadraj dışıyken düğme HUD gibi peşten gelmesin.")]
        [SerializeField] private float viewportMargin = 24f;
        [SerializeField] private Color tint = new Color(0.16f, 0.18f, 0.24f, 0.94f);

        /// <summary>The sea's accent — the same blue the dock panel and the curtain used for it.</summary>
        private static readonly Color SeaBlue = new Color(0.36f, 0.74f, 0.99f, 1f);

        private Camera _cam;
        private CoalOperation _op;
        private CoalOperation _hullOp;
        private Transform _hull;
        private RectTransform _canvasRect, _rect;
        private GameObject _root;
        private Text _label;
        private Button _hudButton;
        private LocalizationService _loc;
        private float _rebindIn;
        private bool _opening;

        /// <summary>
        /// Boot-time host. The markers this is modelled on are authored in Main.unity; this one has
        /// nothing to wire in an Inspector, so it hosts itself and the scene stays untouched.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Spawn()
        {
            if (FindAnyObjectByType<PortShipMarker>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("LimanGemisi");
            DontDestroyOnLoad(go);
            go.AddComponent<PortShipMarker>();
        }

        private void Awake()
        {
            _cam = Camera.main;

            var go = new GameObject("LimanGemisiKanvas", typeof(Canvas), typeof(CanvasScaler),
                                    typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD in the raycast stack, for the reason MarketDoorMarker gives: a HUD
            // button's transparent edge can lie over this one and quietly eat the tap.
            canvas.sortingOrder = Mathf.Max(sortingOrder, 102);
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)go.transform;

            Button button = UiBuild.Btn(_canvasRect, "DenizeAcil", Loc.T("deniz.acil"),
                                        UiSkin.ButtonBlue, tint, labelSize, Open);
            _rect = (RectTransform)button.transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);
            _rect.sizeDelta = buttonSize;
            _root = button.gameObject;
            _root.SetActive(false);

            _label = button.GetComponentInChildren<Text>();
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (_label != null) _label.text = Loc.T("deniz.acil");
        }

        /// <summary>
        /// Put out to sea. The session is opened BEFORE the curtain so the sea scene wakes already
        /// knowing which island it sailed from; if the curtain refuses (one is already up), the
        /// session is closed again rather than left half-made.
        /// </summary>
        private void Open()
        {
            if (_opening && SceneCurtain.Busy) return;
            _opening = false;

            if (!Application.CanStreamedLevelBeLoaded(seaSceneName))
            {
                Debug.LogError("Deniz sahnesi Build Settings içinde yüklenebilir değil: " + seaSceneName);
                return;
            }
            var sea = ServiceLocator.Get<ExpeditionService>();
            if (sea == null) return;

            string key = ServiceLocator.Get<MarketService>()?.ActiveIsland;
            if (string.IsNullOrEmpty(key)) key = "coal";
            sea.SetSail(key);
            ServiceLocator.Get<HapticService>()?.Medium();

            // parkCurrent: the island stays built underneath the sea, so coming ashore wakes it
            // instead of reconstructing the whole operation — the same trick the market door uses.
            _opening = SceneCurtain.Cover(seaSceneName, SeaBlue, Loc.T("deniz.baslik"));
            if (!_opening) sea.Ashore();
            else if (_root != null) _root.SetActive(false);
        }

        /// <summary>Travelling enables a different operation, so which one is live is re-checked on a timer.</summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            EnsureHudButton();
            // isActiveAndEnabled, not enabled: parking the island for the sea or market scene only
            // deactivates its root, so a component-level enabled flag stays true and the marker
            // would keep tracking — and drawing a "put to sea" button — over a scene it has left.
            if (_op != null && _op.isActiveAndEnabled) return;

            var all = FindObjectsByType<CoalOperation>(FindObjectsInactive.Exclude);
            _op = null;
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) { _op = all[i]; return; }
        }

        /// <summary>
        /// Sea combat is a primary action, so compact HUD mode gives it one permanent rail slot as
        /// well as the contextual button over the moored ship. The host persists across scenes;
        /// Unity's null semantics let this reattach when a newly loaded Main scene creates a new HUD.
        /// </summary>
        private void EnsureHudButton()
        {
            if (_hudButton != null) return;
            HudUI hud = FindAnyObjectByType<HudUI>(FindObjectsInactive.Exclude);
            if (hud == null) return;
            Sprite icon = Resources.Load<Sprite>("UI/Sea/gemi");
            _hudButton = hud.AttachBottomButton(14, HudUI.SailButtonName, icon, Open);
        }

        private void Update()
        {
            if (_opening && !SceneCurtain.Busy) _opening = false;
            _rebindIn -= Time.unscaledDeltaTime;
            if (_rebindIn <= 0f)
            {
                _rebindIn = 1f;
                Rebind();
                PlaceHull();
            }
            if (_op == null || !_op.isActiveAndEnabled || _cam == null || !_cam.isActiveAndEnabled
                || _hull == null) { Hide(); return; }

            Vector3 world = _hull.position;
            world.y += worldLift;
            Vector3 screen = _cam.WorldToScreenPoint(world);

            // The ship belongs to the harbour: when the harbour leaves the camera the button leaves
            // too, the same rule the port contract badge keeps. Clamping it to an edge would make it
            // follow the player around like a HUD control.
            bool onScreen = screen.z > 0f
                            && screen.x >= viewportMargin && screen.x <= Screen.width - viewportMargin
                            && screen.y >= viewportMargin && screen.y <= Screen.height - viewportMargin;
            if (!onScreen || SceneCurtain.Busy) { Hide(); return; }

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, new Vector2(screen.x, screen.y), null, out local);
            _rect.anchoredPosition = local;

            if (!_root.activeSelf) _root.SetActive(true);
        }

        /// <summary>
        /// Keeps the hull at the live island's berth: built when an island first resolves one,
        /// rebuilt when travel swaps the operation, re-moored in place when the port district's
        /// phase moves the berth. The one-second cadence is the rebind's own.
        /// </summary>
        private void PlaceHull()
        {
            if (_op == null) return;

            Vector3 pos, heading; Transform parent;
            if (!_op.OurShipBerth(out pos, out heading, out parent))
            {
                // Travelled to an island with no harbour: the old island's boat must not keep a
                // live button pointing at a berth the camera cannot even see.
                if (_hull != null && _hullOp != _op) { Destroy(_hull.gameObject); _hull = null; }
                return;
            }

            if (_hull == null || _hullOp != _op)
            {
                if (_hull != null) Destroy(_hull.gameObject);
                _hull = BuildHull();
                _hullOp = _op;
                _hull.SetParent(parent, true);
            }
            _hull.SetPositionAndRotation(pos, Quaternion.LookRotation(heading, Vector3.up));
        }

        /// <summary>
        /// The boat, in the sea scene's own three primitives and paint at pier scale — recognisably
        /// the same ship. Replaced the same way that one will be, once it is worth modelling.
        /// </summary>
        private Transform BuildHull()
        {
            var root = new GameObject("BizimGemi");

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Tekne";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(4.5f, 2.0f, 11.5f);
            body.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            Paint(body, new Color(0.62f, 0.24f, 0.18f, 1f));

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Kamara";
            deck.transform.SetParent(root.transform, false);
            deck.transform.localScale = new Vector3(3.2f, 2.2f, 3.8f);
            deck.transform.localPosition = new Vector3(0f, 2.6f, -1.3f);
            Paint(deck, new Color(0.90f, 0.88f, 0.82f, 1f));

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Direk";
            mast.transform.SetParent(root.transform, false);
            mast.transform.localScale = new Vector3(0.55f, 2.2f, 0.55f);
            mast.transform.localPosition = new Vector3(0f, 4.9f, 2f);
            Paint(mast, new Color(0.42f, 0.30f, 0.19f, 1f));

            return root.transform;
        }

        private static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = MarketYardBuild.Mat(c);
            Destroy(go.GetComponent<Collider>());
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }
    }
}
