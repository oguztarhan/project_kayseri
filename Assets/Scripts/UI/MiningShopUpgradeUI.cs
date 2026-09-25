using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The benches' compact panel: tap a built bench to buy its next level, tap an offered line's locked pad to build
    /// that bench once the one before it is levelled enough. Spends only through <see cref="MiningShopBusinessService"/>.
    /// Also turns the opening island shot onto the shop once the island camera has framed itself.
    /// </summary>
    [RequireComponent(typeof(MiningShopView))]
    public sealed class MiningShopUpgradeUI : MonoBehaviour
    {
        [Tooltip("Badges 90, juice 95, HUD 100.")]
        [SerializeField] private int sortingOrder = 96;
        [Tooltip("Slack around the shop when the opening camera fits it to the screen. 1 = edge to edge.")]
        [SerializeField, Min(1f)] private float shopFitMargin = 1.15f;
        [Tooltip("Shop raised on screen by this share of the view height, clear of the bench panel below it.")]
        [SerializeField] private float shopScreenLift = 0.08f;
        [Tooltip("Downward tilt of the shop shot. Steeper than the island shot so the market roof does not hide the loop.")]
        [SerializeField, Range(40f, 89f)] private float shopPitch = 72f;
        [Tooltip("Açılışta ada kamerasını dükkân yakın planıyla değiştirir. Ana ekranda adanın tamamı " +
                 "görünsün diye varsayılan olarak kapalıdır.")]
        [SerializeField] private bool focusShopOnOpen;
        [Tooltip("Screen width the HUD's left button rail covers. The shop is fitted into the width between the rails.")]
        [SerializeField, Range(0f, 0.4f)] private float hudLeftFraction = 0.15f;
        [Tooltip("Screen width the HUD's right-hand buttons cover.")]
        [SerializeField, Range(0f, 0.4f)] private float hudRightFraction = 0.12f;
        [Tooltip("World size of the small Craft / Stock / Sell signs. Kept secondary to the people and goods.")]
        [SerializeField] private float signScale = 0.22f;
        [SerializeField] private Color signColor = Color.white;
        [SerializeField] private float tapSlopPixels = 24f;
        [SerializeField] private float refreshSeconds = 0.25f;
        [SerializeField] private Vector2 panelMin = new Vector2(0.06f, 0.17f);
        [SerializeField] private Vector2 panelMax = new Vector2(0.94f, 0.34f);
        [SerializeField] private Color panelColor = new Color(0.1f, 0.12f, 0.16f, 0.92f);
        [SerializeField] private Color buyColor = new Color(0.22f, 0.62f, 0.3f);
        [SerializeField] private Color closeColor = new Color(0.35f, 0.35f, 0.38f);

        /// <summary>Panel titles in MiningShopCampaign.ProductIdAt order.</summary>
        private static readonly string[] TitleKeys =
        {
            "maden_dukkani.kazma_tezgahi", "maden_dukkani.kask_tezgahi",
            "maden_dukkani.fener_tezgahi", "maden_dukkani.canta_tezgahi"
        };

        private readonly System.Collections.Generic.List<Text> _signLabels = new System.Collections.Generic.List<Text>(3);
        private readonly System.Collections.Generic.List<string> _signKeys = new System.Collections.Generic.List<string>(3);
        private LocalizationService _loc;

        private MiningShopView _view;
        private MiningShopBusinessService _shop;
        private int _product;
        private Text _title;
        private WalletService _wallet;
        private OperationCameraBoot _boot;
        private CameraController _cameraController;
        private Camera _camera;
        private bool _framed;
        private RectTransform _panel;
        private Text _summary, _buyText;
        private Button _buy;
        private Vector2 _pressAt;
        private float _refresh;

        /// <summary>Whether the player has the product upgrade panel open.</summary>
        public bool TutorialPanelOpen => _panel != null && _panel.gameObject.activeSelf;
        /// <summary>Which bench the open panel is for — 0 the pickaxe bench, 1 the second.</summary>
        public int TutorialProduct => _product;
        /// <summary>The panel's one action: the next level on a built bench, the build on an unbuilt one.</summary>
        public RectTransform TutorialBuyRect => _buy != null ? _buy.transform as RectTransform : null;
        public RectTransform TutorialPanelRect => _panel;

        private void Awake()
        {
            _view = GetComponent<MiningShopView>();
        }

        private void Start()
        {
            MarketService market = ServiceLocator.Get<MarketService>();
            _shop = market != null ? market.MiningShopBusiness : null;
            _wallet = ServiceLocator.Get<WalletService>();
            if (_shop == null || _wallet == null) { enabled = false; return; }

            _boot = FindAnyObjectByType<OperationCameraBoot>();
            _cameraController = FindAnyObjectByType<CameraController>();
            _camera = Camera.main;
            Build();
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        /// <summary>The signs are drawn once when the camera frames the shop, so they are rewritten here.</summary>
        private void OnLanguageChanged()
        {
            for (int i = 0; i < _signLabels.Count; i++) _signLabels[i].text = Loc.T(_signKeys[i]);
        }

        private void Update()
        {
            if (!_framed)
            {
                if (focusShopOnOpen) FrameShop();
                else _framed = true;
            }

            Pointer pointer = Pointer.current;
            if (pointer != null)
            {
                Vector2 at = pointer.position.ReadValue();
                if (pointer.press.wasPressedThisFrame) _pressAt = at;
                else if (pointer.press.wasReleasedThisFrame &&
                         (at - _pressAt).sqrMagnitude < tapSlopPixels * tapSlopPixels &&
                         !CameraController.PointerOverUI())
                    TapWorld(at);
            }

            if (!_panel.gameObject.activeSelf) return;
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh > 0f) return;
            _refresh = refreshSeconds;
            Refresh();
        }

        /// <summary>After the island shot is solved, so the two never ease the camera to different places.</summary>
        private void FrameShop()
        {
            if (_view.TableCollider == null || _camera == null) return;
            if (_boot != null && !_boot.Framed) return;
            Bounds shop = _view.ShopBounds;
            Quaternion rot = Quaternion.Euler(shopPitch, _camera.transform.eulerAngles.y, 0f);
            float vTan = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float hTan = vTan * Mathf.Max(0.1f, _camera.aspect);
            // Portrait runs out of width first, and the HUD rails take some of it; ground depth is
            // foreshortened by the pitch.
            float usable = Mathf.Max(0.2f, 1f - hudLeftFraction - hudRightFraction);
            float pitchDepth = Mathf.Abs((rot * Vector3.forward).y);
            float dist = Mathf.Max(shop.extents.x / (hTan * usable), shop.extents.z * pitchDepth / vTan) * shopFitMargin;
            // Camera left moves the shop right: centre it between the rails, then lift it clear of the panel.
            Vector3 pos = shop.center - rot * Vector3.forward * dist
                        - rot * Vector3.right * ((hudLeftFraction - hudRightFraction) * dist * hTan)
                        - rot * Vector3.up * (shopScreenLift * 2f * dist * vTan);
            if (_cameraController != null) _cameraController.FrameTo(pos, rot, dist);
            else _camera.transform.SetPositionAndRotation(pos, rot);
            _framed = true;
            BuildSigns(rot);
        }

        /// <summary>
        /// One static world-space canvas of station names, turned to the shop camera. From a phone's distance a
        /// table, a pallet and a counter are just shapes; the names are what make the loop readable.
        /// </summary>
        private void BuildSigns(Quaternion facing)
        {
            var go = new GameObject("MiningShopSigns", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvas = (RectTransform)go.transform;
            // Craft and Sell sit above heads. Stock stays low (at the shop pitch a tall sign over the rack lands on the
            // table behind it) and on the rack's outer side, away from the carrier's route out of it.
            Sign(canvas, "Shop_Pickaxe_Work", new Vector3(0f, 95f, 0f), "maden_dukkani.uret", facing);
            Sign(canvas, "Shop_Pickaxe_Output", new Vector3(24f, 22f, 0f), "maden_dukkani.stok", facing);
            Sign(canvas, "Shop_Market_Shelf", new Vector3(0f, 55f, 0f), "maden_dukkani.sat", facing);
        }

        private void Sign(RectTransform canvas, string anchor, Vector3 offset, string key, Quaternion facing)
        {
            Transform at = _view.transform.Find(anchor);
            if (at == null) return;
            Text label = UiBuild.Label(canvas, "Sign", Loc.T(key), 48, TextAnchor.MiddleCenter);
            _signLabels.Add(label);
            _signKeys.Add(key);
            label.color = signColor;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            RectTransform rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(700f, 80f);
            rt.SetPositionAndRotation(at.position + offset, facing);
            rt.localScale = Vector3.one * signScale;
        }

        private void TapWorld(Vector2 screen)
        {
            if (_camera == null || _view.TableCollider == null) return;
            if (!Physics.Raycast(_camera.ScreenPointToRay(screen), out RaycastHit hit, _camera.farClipPlane)) return;
            if (!_view.TryGetProduct(hit.collider, out int product, out _)) return;
            // Tapping the bench whose panel is already open closes it; any other bench switches to that one.
            bool open = !(_panel.gameObject.activeSelf && product == _product);
            _product = product;
            _panel.gameObject.SetActive(open);
            if (open) Refresh();
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "MiningShopCanvas", sortingOrder);
            _panel = UiBuild.Box(canvas, "BenchPanel", panelColor, panelMin, panelMax);

            _title = UiBuild.Label(_panel, "Title", Loc.T(TitleKeys[0]), 44, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_title.rectTransform, new Vector2(0.05f, 0.72f), new Vector2(0.8f, 0.95f));
            _summary = UiBuild.Label(_panel, "Summary", string.Empty, 32, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_summary.rectTransform, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.7f));

            // Skinned sprites: with a skin wired, Btn leaves the image white, and a white label on the flat
            // fallback sprite disappears.
            Button close = UiBuild.Btn(_panel, "Close", "X", UiSkin.ButtonGrey, closeColor, 40, () => _panel.gameObject.SetActive(false));
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.86f, 0.74f), new Vector2(0.97f, 0.95f));

            _buy = UiBuild.Btn(_panel, "Buy", string.Empty, UiSkin.ButtonGreen, buyColor, 32, Buy);
            UiBuild.Anchor((RectTransform)_buy.transform, new Vector2(0.04f, 0.07f), new Vector2(0.96f, 0.45f));
            _buyText = _buy.GetComponentInChildren<Text>();

            _panel.gameObject.SetActive(false);
        }

        /// <summary>A built bench buys its next level; an offered, unbuilt bench is built.</summary>
        private void Buy()
        {
            if (_shop.View.ProductAt(_product).TableBuilt) _shop.TryBuyLevels(_product, 1);
            else _shop.TryBuildTable(_product);
            Refresh();
        }

        private void Refresh()
        {
            MiningShopBusinessSimulation.Snapshot v = _shop.View;
            MiningShopBusinessSimulation.ProductSnapshot product = v.ProductAt(_product);
            _title.text = Loc.T(TitleKeys[_product]);
            _summary.text = string.Format(Loc.T("maden_dukkani.ozet"), _shop.CraftSeconds(_product).ToString("0.0"),
                NumberFormatter.Format(new BigDouble(_shop.UnitPrice(_product))), product.Sold);

            if (product.TableBuilt)
            {
                Track(_buy, _buyText, string.Format(Loc.T("maden_dukkani.seviye"), product.Level),
                      _shop.LevelCost(_product), v.PendingSeconds);
                return;
            }

            // An offered bench not yet built: one build button, usable once the bench before it is levelled enough.
            string build = Loc.T("maden_dukkani.tezgah_kur");
            if (!_shop.BuildRequirementMet(_product))
            {
                bool previousBuilt = _product > 0 && v.ProductAt(_product - 1).TableBuilt;
                _buyText.text = previousBuilt
                    ? build + "\n" + string.Format(Loc.T("maden_dukkani.gereken"), Loc.T(TitleKeys[_product - 1]),
                                                   _shop.BuildRequiresLevel)
                    : build;
                _buy.interactable = false;
                return;
            }
            Track(_buy, _buyText, build, _shop.TableCost(_product), v.PendingSeconds);
        }

        private void Track(Button button, Text label, string name, double cost, double pending)
        {
            if (cost <= 0d)
            {
                label.text = name + "\n" + Loc.T("maden_dukkani.maks");
                button.interactable = false;
                return;
            }
            var price = new BigDouble(cost);
            label.text = name + "\n$" + NumberFormatter.Format(price);
            button.interactable = pending <= 0d && _wallet.CanAfford(price);
        }
    }
}
