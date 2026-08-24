using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Keeps a capsule sprite's end caps round however short the box it is drawn into.
    ///
    /// The kit's pill art — btn_hap_mavi, btn_hap_sari, btn_hap_kirmizi, btn_hap_kalin,
    /// gosterge_grafit, bar_dolgu — is nine-sliced across only: the two end caps are semicircles the
    /// slicer must not stretch, and the flat middle carries the width. But a slice border is measured
    /// in canvas units, not in fractions of the art, so a 561×180 pill laid into a 59-unit-tall button
    /// still draws its 95-unit caps at 95 units. Three times too wide, and the button comes out an
    /// egg. Every screen that put a pill in a box shorter than the art had this, which is why the
    /// correction lives here instead of as a hand-tuned constant in each of them.
    ///
    /// The multiplier is the art's own height over the box's, recomputed whenever the box changes —
    /// so one component covers portrait, landscape, and the letterbox sizes in between.
    ///
    /// <see cref="ExecuteAlways"/> because the same correction has to hold in the Scene view: without
    /// it a prefab-authored pill looks wrong right up until you press play, which is exactly when it
    /// is too late to notice.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class PillFit : MonoBehaviour
    {
        private Image _image;

        /// <summary>Fits an image and hands it back, so a builder can wrap the one it just made.</summary>
        public static Image Wrap(Image image)
        {
            if (image == null) return null;
            if (image.GetComponent<PillFit>() == null) image.gameObject.AddComponent<PillFit>();
            return image;
        }

        private void OnEnable()
        {
            _image = GetComponent<Image>();
            Fit();
        }

        private void OnRectTransformDimensionsChange() => Fit();

        /// <summary>Recomputes the multiplier. Public so a screen that swaps the sprite can re-fit.</summary>
        public void Fit()
        {
            if (_image == null) _image = GetComponent<Image>();
            Sprite sprite = _image.sprite;
            if (sprite == null) return;

            float art = sprite.rect.height;
            float box = ((RectTransform)transform).rect.height;
            // Image.pixelsPerUnit is the sprite's own pixels-per-unit over the canvas's reference,
            // i.e. the conversion the slicer already applies before our multiplier.
            float unit = _image.pixelsPerUnit;
            if (art <= 0f || box <= 0f || unit <= 0f) return;

            float wanted = art / (box * unit);
            // The setter dirties the mesh unconditionally, and this runs on every layout pass.
            if (!Mathf.Approximately(_image.pixelsPerUnitMultiplier, wanted))
                _image.pixelsPerUnitMultiplier = wanted;
        }
    }
}
