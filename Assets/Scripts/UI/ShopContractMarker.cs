using Game.Core;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The progress bar over the contract customer's head: "24/60" and a bar that fills with every hand-over.
    ///
    /// Same construction as <see cref="PortContractMarker"/> and <see cref="UpgradeReadyMarkers"/>: its own
    /// screen-space canvas, placed over a world point each frame, so it stays one size across the zoom range, costs
    /// one batch, and redraws apart from the static HUD canvases. The label is rebuilt only when the count changes.
    /// It is added by <see cref="HudUI"/> when a shop is open, so no scene has to carry it.
    /// </summary>
    public sealed class ShopContractMarker : MonoBehaviour
    {
        [Tooltip("Rozetin boyu, referans çözünürlükte piksel.")]
        [SerializeField] private Vector2 size = new Vector2(150f, 50f);
        [Tooltip("Yükseltme rozetleri 92'de; kontrat çubuğu onların altında.")]
        [SerializeField] private int sortingOrder = 91;
        [SerializeField] private float popSeconds = 0.32f;
        [Tooltip("Her teslimde rozetin ne kadar büyüyüp döneceği.")]
        [SerializeField] private float bumpScale = 0.15f;
        [SerializeField] private float bumpSeconds = 0.3f;
        [SerializeField] private Color plateColor = new Color(0.09f, 0.15f, 0.29f, 0.92f);
        [SerializeField] private Color trackColor = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] private Color fillColor = new Color(1f, 0.78f, 0.2f, 1f);

        // easeOutBack's overshoot constant, the standard 1.70158.
        private const float PopOvershoot = 1.70158f;

        private Camera _cam;
        private MarketService _market;
        private MiningShopView _view;
        private RectTransform _canvasRect;
        private RectTransform _rect;
        private RectTransform _fill;
        private Text _label;
        private bool _shown;
        private float _shownAt;
        private float _bumpLeft;
        private float _rebindIn;
        private int _delivered = -1, _quantity = -1;

        private void Awake()
        {
            var go = new GameObject("KontratCubuguKanvas", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)go.transform;

            _rect = UiBuild.Flat(_canvasRect, "KontratCubugu", plateColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _rect.sizeDelta = size;
            _rect.GetComponent<Image>().raycastTarget = false;
            RectTransform track = UiBuild.Bar(_rect, "Cubuk", trackColor, fillColor,
                new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.34f), out _fill);
            track.GetComponent<Image>().raycastTarget = false;
            _fill.GetComponent<Image>().raycastTarget = false;
            _label = UiBuild.Label(_rect, "Sayi", "", 26, TextAnchor.MiddleCenter);
            UiBuild.Anchor(_label.rectTransform, new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.98f));
            _rect.gameObject.SetActive(false);
        }

        /// <summary>The view can come and go with the scene, so it is looked for on a slow timer, never per frame.</summary>
        private void Rebind()
        {
            if (_cam == null) _cam = Camera.main;
            if (_market == null) _market = ServiceLocator.Get<MarketService>();
            if (_view == null || !_view.isActiveAndEnabled) _view = FindAnyObjectByType<MiningShopView>();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _rebindIn -= dt;
            if (_rebindIn <= 0f) { _rebindIn = 1f; Rebind(); }

            MiningShopBusinessService shop = _market != null ? _market.MiningShopBusiness : null;
            if (shop == null || _view == null || _cam == null || !_view.TryGetContractCustomer(out Vector3 head))
            {
                Hide();
                return;
            }
            MiningShopBusinessSimulation.Snapshot v = shop.View;
            if (!v.ContractActive) { Hide(); return; }

            Vector3 screen = _cam.WorldToScreenPoint(head);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height)
            {
                Hide();
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, new Vector2(screen.x, screen.y), null,
                out Vector2 local);

            if (!_shown)
            {
                _shown = true;
                _shownAt = Time.unscaledTime;
                _rect.gameObject.SetActive(true);
            }
            SetCount(v.ContractDelivered, v.ContractQuantity);

            float age = Time.unscaledTime - _shownAt;
            float scale = 1f;
            if (age < popSeconds && popSeconds > 0f)
            {
                float u = age / popSeconds - 1f;
                scale = u * u * ((PopOvershoot + 1f) * u + PopOvershoot) + 1f;
            }
            if (_bumpLeft > 0f)
            {
                _bumpLeft = Mathf.Max(0f, _bumpLeft - dt);
                scale *= 1f + Mathf.Sin((1f - _bumpLeft / bumpSeconds) * Mathf.PI) * bumpScale;
            }
            _rect.anchoredPosition = local;
            _rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Redraws the label and bar only when the count moved; a rise bumps the badge.</summary>
        private void SetCount(int delivered, int quantity)
        {
            if (delivered == _delivered && quantity == _quantity) return;
            if (_delivered >= 0 && delivered > _delivered && quantity == _quantity) _bumpLeft = bumpSeconds;
            _delivered = delivered;
            _quantity = quantity;
            _label.text = delivered + "/" + quantity;
            _fill.anchorMax = new Vector2(quantity > 0 ? Mathf.Clamp01((float)delivered / quantity) : 0f, 1f);
        }

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            _delivered = _quantity = -1;
            _rect.gameObject.SetActive(false);
        }
    }
}
