using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Anchors its RectTransform to the device safe area (notch / punch-hole / gesture bar) so UI
    /// content never sits under a cutout. Put one on a full-stretch child of the canvas and parent the
    /// screen's content to it; full-bleed backdrops stay outside, content goes inside. Edges can be
    /// released per screen from the Inspector (a bottom sheet may want to ignore the bottom inset).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        [SerializeField] private bool applyTop = true;
        [SerializeField] private bool applyBottom = true;
        [SerializeField] private bool applyLeft = true;
        [SerializeField] private bool applyRight = true;

        private RectTransform _rect;
        private Rect _applied = new Rect(-1f, -1f, -1f, -1f);

        private void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            // Rect compare only — cheap enough per frame, and it catches rotation and
            // resolution changes without needing a callback from the OS.
            if (Screen.safeArea != _applied) Apply();
        }

        private void Apply()
        {
            Rect sa = Screen.safeArea;
            _applied = sa;
            float w = Screen.width, h = Screen.height;
            if (w <= 0f || h <= 0f) return;

            Vector2 min = new Vector2(applyLeft ? sa.xMin / w : 0f, applyBottom ? sa.yMin / h : 0f);
            Vector2 max = new Vector2(applyRight ? sa.xMax / w : 1f, applyTop ? sa.yMax / h : 1f);
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
