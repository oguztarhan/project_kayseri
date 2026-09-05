using UnityEngine;

namespace Game.UI
{
    public sealed class ShipyardSafeArea : MonoBehaviour
    {
        private Rect _last;
        private Vector2Int _screen;
        private RectTransform _rect;
        private void Awake() { _rect = (RectTransform)transform; }
        private void Update()
        {
            var area = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (area == _last && size == _screen) return;
            _last = area; _screen = size;
            _rect.anchorMin = new Vector2(area.xMin / Mathf.Max(1, size.x), area.yMin / Mathf.Max(1, size.y));
            _rect.anchorMax = new Vector2(area.xMax / Mathf.Max(1, size.x), area.yMax / Mathf.Max(1, size.y));
            _rect.offsetMin = _rect.offsetMax = Vector2.zero;
        }
    }
}
