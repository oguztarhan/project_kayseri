using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Shrinks a fixed-width column until it fits the scroll view holding it.
    ///
    /// The store's sheet is one 1810-unit-wide column — two offer rows, three headings and three
    /// six-across grids, every one of them authored at that width. On a 16:9 phone the canvas is about
    /// 2370 units across and the column has room to spare. On a 4:3 iPad the canvas is 1859 and the
    /// scroll viewport 1668, so every row lost roughly 81 units off each side: half of the first card
    /// and half of the last, on every row, including the most expensive packs. The scroll view is
    /// vertical only, so nothing the player could do would bring them back.
    ///
    /// Scaling the column beats re-flowing it. Six across stays six across, the cards keep the
    /// proportions they were drawn at, and there is one layout to look at rather than one per device.
    /// The scale is clamped at 1, so a screen wide enough for the authored column is left alone —
    /// this changes nothing on the phones the sheet was drawn against.
    ///
    /// Goes on the ScrollRect's content. Unity's ScrollRect measures its content from world corners,
    /// so a scaled column scrolls and clips correctly with no help from here.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ScrollColumnFit : MonoBehaviour
    {
        [Tooltip("Sütunun iki yanında bırakılacak boşluk, sütunun kendi birimiyle. Kartlar görüntü " +
                 "alanının kenarını öpmesin diye.")]
        [SerializeField, Min(0f)] private float sideMargin = 12f;

        private RectTransform _rect;
        private float _applied = -1f;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            Fit();
        }

        /// <summary>
        /// The content is stretched to the viewport's width, so the viewport resizing is this rect
        /// resizing. Height changes under the size fitter land here too; the applied-scale check below
        /// is what keeps those from being writes.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (_rect != null) Fit();
        }

        private void Fit()
        {
            float column = WidestChild();
            if (column <= 0f) return;

            float avail = _rect.rect.width - sideMargin * 2f;
            if (avail <= 0f) return;

            float scale = Mathf.Min(1f, avail / column);
            if (Mathf.Abs(scale - _applied) < 0.0005f) return;

            _applied = scale;
            _rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// How wide the column really is, measured rather than typed. The rows are all the same width
        /// today; measuring means retuning one of them cannot quietly reintroduce the overflow.
        /// </summary>
        private float WidestChild()
        {
            float widest = 0f;
            for (int i = 0; i < _rect.childCount; i++)
            {
                var child = _rect.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                if (child.rect.width > widest) widest = child.rect.width;
            }
            return widest;
        }
    }
}
