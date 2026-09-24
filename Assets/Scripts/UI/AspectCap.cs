using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Keeps a full-height column no wider than <see cref="maxWidthOverHeight"/> times its height,
    /// centred in its parent. On a phone the column is the whole parent. On a tablet it is a centred
    /// phone-shaped strip, and whatever is behind it — a screen's dim backdrop — shows at the sides.
    ///
    /// WHY A SCREEN NEEDS IT. Code-built screens lay out in fractions of a 1080×1920 canvas, and a
    /// 3:4 tablet makes that canvas about 1250 units wide. Every fraction then lands 15% wider than it
    /// was drawn for, and nine-sliced frames grow wider than their art: the league board's star bow
    /// smeared sideways, and holding its shape instead made its border tall enough to cover the text
    /// under it. Capping the column at the reference shape leaves every phone exactly as it was, and
    /// gives the tablet the layout the art was drawn for.
    ///
    /// It stretches to the parent and then insets its own left and right offsets. SafeArea and other
    /// components that rewrite anchors therefore belong on the parent, never on this object.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class AspectCap : MonoBehaviour
    {
        [SerializeField] private float maxWidthOverHeight = 1080f / 1920f;

        private bool _applying;

        public static AspectCap Wrap(RectTransform column, float maxWidthOverHeight)
        {
            var cap = column.GetComponent<AspectCap>();
            if (cap == null) cap = column.gameObject.AddComponent<AspectCap>();
            cap.maxWidthOverHeight = maxWidthOverHeight;
            cap.Apply();
            return cap;
        }

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange() => Apply();

        public void Apply()
        {
            if (_applying || maxWidthOverHeight <= 0f) return;
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;

            _applying = true;
            try
            {
                Rect box = parent.rect;
                float inset = box.width > box.height * maxWidthOverHeight
                    ? (box.width - box.height * maxWidthOverHeight) * 0.5f
                    : 0f;

                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                var min = new Vector2(inset, 0f);
                var max = new Vector2(-inset, 0f);
                if (rt.offsetMin != min) rt.offsetMin = min;
                if (rt.offsetMax != max) rt.offsetMax = max;
            }
            finally { _applying = false; }
        }
    }
}
