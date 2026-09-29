using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Pooled, non-interactive request bubbles. Positions follow the view; products follow real demand.</summary>
    public sealed class ShopCustomerRequests : MonoBehaviour
    {
        [SerializeField] private Vector2 _size = new Vector2(76f, 68f);
        [SerializeField, Min(0f)] private float _headGap = 12f;
        [SerializeField, Min(0f)] private float _minimumPersonHeight = 18f;
        [SerializeField, Range(1f, 1.5f)] private float _highlightScale = 1.2f;
        [SerializeField] private Color _paper = new Color(1f, 0.97f, 0.87f);
        [SerializeField] private Color _highlightPaper = new Color(1f, 0.8f, 0.22f);
        [SerializeField] private int _sortingOrder = 91;

        private sealed class Bubble
        {
            public RectTransform Root;
            public Image Plate, Tail, Icon;
            public Text Label;
            public Rect ScreenRect;
            public int Product = -1;
            public bool Visible, Accepted;
        }

        private MiningShopView _view;
        private Camera _camera;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Bubble[] _bubbles;
        private readonly Sprite[] _icons = new Sprite[MiningShopCampaign.ProductCount];
        private readonly string[] _names = new string[MiningShopCampaign.ProductCount];
        private LocalizationService _loc;

        public static ShopCustomerRequests Create(MiningShopView view, int capacity)
        {
            var go = new GameObject("CustomerRequests", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(view.transform, false);
            var requests = go.AddComponent<ShopCustomerRequests>();
            requests._view = view;
            requests._camera = Camera.main;
            requests._canvas = go.GetComponent<Canvas>();
            requests._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            requests._canvas.sortingOrder = requests._sortingOrder;
            requests._canvasRect = (RectTransform)go.transform;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            ShopContractConfig config = ServiceLocator.Get<ShopContractConfig>();
            for (int p = 0; p < requests._icons.Length; p++)
                requests._icons[p] = config != null ? config.ProductIcon(p) : null;
            requests._bubbles = new Bubble[capacity];
            for (int i = 0; i < capacity; i++) requests._bubbles[i] = requests.BuildBubble(i);
            requests._loc = ServiceLocator.Get<LocalizationService>();
            if (requests._loc != null) requests._loc.Changed += requests.RefreshNames;
            requests.RefreshNames();
            return requests;
        }

        private Bubble BuildBubble(int index)
        {
            var b = new Bubble();
            b.Root = UiBuild.Flat(_canvasRect, "Request" + index, _paper,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            b.Root.sizeDelta = _size;
            b.Plate = b.Root.GetComponent<Image>();
            b.Plate.sprite = UiSkin.Capsule;
            b.Plate.type = Image.Type.Sliced;
            b.Plate.raycastTarget = false;
            RectTransform tail = UiBuild.Flat(b.Root, "Tail", _paper, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            tail.sizeDelta = new Vector2(12f, 12f);
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            b.Tail = tail.GetComponent<Image>();
            b.Tail.raycastTarget = false;
            tail.SetAsFirstSibling();
            RectTransform icon = UiBuild.Flat(b.Root, "Product", Color.white, new Vector2(0.17f, 0.15f), new Vector2(0.83f, 0.85f));
            b.Icon = icon.GetComponent<Image>();
            b.Icon.preserveAspect = true;
            b.Icon.raycastTarget = false;
            b.Label = UiBuild.Label(b.Root, "Name", string.Empty, 22, TextAnchor.MiddleCenter);
            UiBuild.Anchor(b.Label.rectTransform, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f));
            b.Label.color = new Color(0.1f, 0.16f, 0.25f);
            b.Label.raycastTarget = false;
            b.Label.resizeTextForBestFit = true;
            b.Label.resizeTextMinSize = 11;
            b.Label.resizeTextMaxSize = 22;
            b.Root.gameObject.SetActive(false);
            return b;
        }

        private void RefreshNames()
        {
            for (int p = 0; p < _names.Length; p++) _names[p] = ShopContractUI.ProductName(p);
            for (int i = 0; i < _bubbles.Length; i++) _bubbles[i].Product = -1;
        }

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= RefreshNames;
        }

        private void LateUpdate()
        {
            if (_bubbles == null) return;
            for (int i = 0; i < _bubbles.Length; i++) _bubbles[i].Accepted = false;
            if (_camera != null && _view != null && !TutorialUI.Blocking)
            {
                // Reserve room for the highlighted pickaxe request first. At a wide zoom, suppress collisions
                // instead of stacking unreadable icons over the same few pixels.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < _bubbles.Length; i++)
                    {
                        if (!_view.TryGetCustomerRequest(i, out Vector3 head, out int product, out bool highlighted) ||
                            highlighted != (pass == 0)) continue;
                        Vector3 screen = _camera.WorldToScreenPoint(head);
                        Vector3 feet = _camera.WorldToScreenPoint(head - Vector3.up * _view.CustomerHeight);
                        float canvasScale = _canvas.scaleFactor;
                        if (screen.z <= 0f || feet.z <= 0f ||
                            Mathf.Abs(screen.y - feet.y) < _minimumPersonHeight * canvasScale) continue;
                        float scale = highlighted ? _highlightScale : 1f;
                        Vector2 size = _size * (canvasScale * scale);
                        Vector2 center = (Vector2)screen + Vector2.up * (size.y * 0.5f + _headGap * canvasScale);
                        Rect rect = new Rect(center - size * 0.5f, size);
                        if (rect.xMin < Screen.safeArea.xMin || rect.xMax > Screen.safeArea.xMax ||
                            rect.yMin < Screen.safeArea.yMin || rect.yMax > Screen.safeArea.yMax) continue;
                        bool overlaps = false;
                        for (int j = 0; j < _bubbles.Length; j++)
                            if (_bubbles[j].Accepted && rect.Overlaps(_bubbles[j].ScreenRect)) { overlaps = true; break; }
                        if (overlaps) continue;
                        Bubble b = _bubbles[i];
                        b.Accepted = true;
                        b.ScreenRect = rect;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, center, null, out Vector2 local);
                        b.Root.anchoredPosition = local;
                        b.Root.localScale = Vector3.one * scale;
                        b.Plate.color = b.Tail.color = highlighted ? _highlightPaper : _paper;
                        if (b.Product == product) continue;
                        b.Product = product;
                        b.Icon.sprite = _icons[product];
                        b.Icon.enabled = _icons[product] != null;
                        b.Label.enabled = _icons[product] == null;
                        b.Label.text = _names[product];
                    }
            }
            for (int i = 0; i < _bubbles.Length; i++)
            {
                Bubble b = _bubbles[i];
                if (b.Visible == b.Accepted) continue;
                b.Visible = b.Accepted;
                b.Root.gameObject.SetActive(b.Visible);
            }
        }
    }
}
