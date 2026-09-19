using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Keeps a small control's tap area at <see cref="UiType.MinTouch"/> without redrawing it, by widening
    /// the graphic's raycast rect. Sizes are canvas units, so the result does not depend on the screen; it
    /// is worked out again whenever the rect changes size, which covers the first layout pass.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class TouchPad : MonoBehaviour
    {
        private Graphic _graphic;
        private float _downBias = 0.5f;

        /// <summary>The share of the missing height added below the control rather than above it, for one
        /// with another tappable control over it.</summary>
        public void Configure(float downBias)
        {
            _downBias = Mathf.Clamp01(downBias);
            Apply();
        }

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange() => Apply();

        private void Apply()
        {
            if (_graphic == null) _graphic = GetComponent<Graphic>();
            _graphic.raycastPadding = UiType.TouchPadding(_graphic.rectTransform.rect.size, _downBias);
        }
    }
}
