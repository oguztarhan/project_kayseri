using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Keeps a nine-sliced frame's ornament — the league board's star bow, the popup cards' crown — at
    /// its drawn proportions whatever width the frame is laid out at.
    ///
    /// The ornament rides the TOP-CENTRE segment of the slice, which is the one part that stretches
    /// horizontally while its height stays fixed at the top border. So a frame drawn narrower than its
    /// art squeezes the ornament sideways, and a wider one smears it. The league laid its frames out at
    /// fractions of the screen tuned on 1080×1920, where the board's box is within 4% of the art. On a
    /// 20:9 phone it is 7% narrower, and the bow came out 7% squashed.
    ///
    /// The fix is the one <see cref="PillFit"/> makes for capsules, measured across instead of down:
    /// scale the slice borders by the box's width over the art's. Then the centre segment's width and
    /// height shrink or grow together, and the ornament keeps its shape exactly. The cost is a frame
    /// rim a few percent thinner or thicker than the art, which nothing reads as a distortion.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class FrameFit : MonoBehaviour
    {
        private Image _image;

        public static Image Wrap(Image image)
        {
            if (image == null) return null;
            if (image.GetComponent<FrameFit>() == null) image.gameObject.AddComponent<FrameFit>();
            return image;
        }

        private void OnEnable()
        {
            _image = GetComponent<Image>();
            Fit();
        }

        private void OnRectTransformDimensionsChange() => Fit();

        public void Fit()
        {
            if (_image == null) _image = GetComponent<Image>();
            Sprite sprite = _image.sprite;
            if (sprite == null) return;

            float art = sprite.rect.width;
            float box = ((RectTransform)transform).rect.width;
            // Image.pixelsPerUnit is the sprite's own pixels-per-unit over the canvas's reference, and
            // does NOT include the multiplier (multipliedPixelsPerUnit does) — so the answer depends
            // only on the box, and re-running it is a no-op.
            float unit = _image.pixelsPerUnit;
            if (art <= 0f || box <= 0f || unit <= 0f) return;

            float wanted = art / (box * unit);
            // The setter dirties the mesh unconditionally, and this runs on every layout pass.
            if (!Mathf.Approximately(_image.pixelsPerUnitMultiplier, wanted)) _image.pixelsPerUnitMultiplier = wanted;
        }
    }
}
