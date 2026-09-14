using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The pickaxe bench's compact panel: tap the bench, buy speed or value. Spends only through
    /// <see cref="MiningShopService.TryBuyUpgrade"/>. Also turns the opening island shot onto the shop once
    /// the island camera has framed itself.
    /// </summary>
    [RequireComponent(typeof(MiningShopView))]
    public sealed class MiningShopUpgradeUI : MonoBehaviour
    {
        [Tooltip("Badges 90, juice 95, HUD 100.")]
        [SerializeField] private int sortingOrder = 96;
        [Tooltip("Opening camera distance from the shop, along the island camera's own view direction.")]
        [SerializeField] private float shopViewDistance = 650f;
        [SerializeField] private float tapSlopPixels = 24f;
        [SerializeField] private float refreshSeconds = 0.25f;
        [SerializeField] private Vector2 panelMin = new Vector2(0.06f, 0.17f);
        [SerializeField] private Vector2 panelMax = new Vector2(0.94f, 0.34f);
        [SerializeField] private Color panelColor = new Color(0.1f, 0.12f, 0.16f, 0.92f);
        [SerializeField] private Color buyColor = new Color(0.22f, 0.62f, 0.3f);
        [SerializeField] private Color closeColor = new Color(0.35f, 0.35f, 0.38f);

        private MiningShopView _view;
        private MiningShopService _shop;
        private WalletService _wallet;
        private OperationCameraBoot _boot;
        private CameraController _cameraController;
        private Camera _camera;
        private bool _framed;
        private RectTransform _panel;
        private Text _summary, _speedText, _valueText;
        private Button _speed, _value;
        private Vector2 _pressAt;
        private float _refresh;

        private void Awake()
        {
            _view = GetComponent<MiningShopView>();
        }

        private void Start()
        {
            MarketService market = ServiceLocator.Get<MarketService>();
            _shop = market != null ? market.MiningShop : null;
            _wallet = ServiceLocator.Get<WalletService>();
            if (_shop == null || _wallet == null) { enabled = false; return; }

            _boot = FindAnyObjectByType<OperationCameraBoot>();
            _cameraController = FindAnyObjectByType<CameraController>();
            _camera = Camera.main;
            Build();
        }

        private void Update()
        {
            if (!_framed) FrameShop();

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
            Quaternion rot = _camera.transform.rotation;
            Vector3 pos = _view.Focus - rot * Vector3.forward * shopViewDistance;
            if (_cameraController != null) _cameraController.FrameTo(pos, rot, shopViewDistance);
            else _camera.transform.SetPositionAndRotation(pos, rot);
            _framed = true;
        }

        private void TapWorld(Vector2 screen)
        {
            if (_camera == null || _view.TableCollider == null) return;
            if (!Physics.Raycast(_camera.ScreenPointToRay(screen), out RaycastHit hit, _camera.farClipPlane)) return;
            if (hit.collider != _view.TableCollider) return;
            bool open = !_panel.gameObject.activeSelf;
            _panel.gameObject.SetActive(open);
            if (open) Refresh();
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "MiningShopCanvas", sortingOrder);
            _panel = UiBuild.Box(canvas, "BenchPanel", panelColor, panelMin, panelMax);

            Text title = UiBuild.Label(_panel, "Title", Loc.T("maden_dukkani.kazma_tezgahi"), 44, TextAnchor.MiddleLeft);
            UiBuild.Anchor(title.rectTransform, new Vector2(0.05f, 0.72f), new Vector2(0.8f, 0.95f));
            _summary = UiBuild.Label(_panel, "Summary", string.Empty, 32, TextAnchor.MiddleLeft);
            UiBuild.Anchor(_summary.rectTransform, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.7f));

            Button close = UiBuild.Btn(_panel, "Close", "X", null, closeColor, 40, () => _panel.gameObject.SetActive(false));
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.86f, 0.74f), new Vector2(0.97f, 0.95f));

            _speed = UiBuild.Btn(_panel, "Speed", string.Empty, null, buyColor, 32, () => Buy(true));
            UiBuild.Anchor((RectTransform)_speed.transform, new Vector2(0.04f, 0.07f), new Vector2(0.49f, 0.45f));
            _speedText = _speed.GetComponentInChildren<Text>();
            _value = UiBuild.Btn(_panel, "Value", string.Empty, null, buyColor, 32, () => Buy(false));
            UiBuild.Anchor((RectTransform)_value.transform, new Vector2(0.51f, 0.07f), new Vector2(0.96f, 0.45f));
            _valueText = _value.GetComponentInChildren<Text>();

            _panel.gameObject.SetActive(false);
        }

        private void Buy(bool speed)
        {
            _shop.TryBuyUpgrade(speed);
            Refresh();
        }

        private void Refresh()
        {
            MiningShopSimulation.Snapshot v = _shop.View;
            _summary.text = string.Format(Loc.T("maden_dukkani.ozet"), _shop.CraftSeconds.ToString("0.0"),
                NumberFormatter.Format(new BigDouble(_shop.UnitPrice)), v.Sold);
            Track(_speed, _speedText, "maden_dukkani.hiz", v.SpeedLevel, _shop.UpgradeCost(true), v.PendingSeconds);
            Track(_value, _valueText, "maden_dukkani.deger", v.ValueLevel, _shop.UpgradeCost(false), v.PendingSeconds);
        }

        private void Track(Button button, Text label, string key, int level, double cost, double pending)
        {
            string name = string.Format(Loc.T(key), level);
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
