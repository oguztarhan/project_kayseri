using UnityEngine;

namespace Game.UI
{
    /// <summary>A fixed design surface, uniformly fitted inside its safe-area parent.</summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class PortraitPageFit : MonoBehaviour
    {
        [SerializeField] private Vector2 _designSize = new Vector2(1000f, 1900f);
        [SerializeField] private float _margin = 24f;
        private RectTransform _rect, _parent;
        private Vector2 _lastSize;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = transform.parent as RectTransform;
            Fit();
        }

        private void LateUpdate()
        {
            if (_parent != null && _parent.rect.size != _lastSize) Fit();
        }

        private void Fit()
        {
            if (_parent == null) return;
            _lastSize = _parent.rect.size;
            _rect.anchorMin = _rect.anchorMax = _rect.pivot = Vector2.one * 0.5f;
            _rect.anchoredPosition = Vector2.zero;
            _rect.sizeDelta = _designSize;
            float scale = Mathf.Max(0.01f, Mathf.Min((_lastSize.x - _margin * 2) / _designSize.x,
                (_lastSize.y - _margin * 2) / _designSize.y));
            _rect.localScale = Vector3.one * scale;
        }
    }
}
