using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The sea screen's design kit: the 25-piece set in <c>Assets/UI DESİGNS/Deniz ekranı tasarım</c>,
    /// trimmed, split and downscaled by <c>Tools/ui/deniz_tasarim_kiti.ps1</c> into
    /// <c>Art/UI/DenizKiti</c> and packed into one page by <c>Resources/UI/Sea/DenizKiti</c>. Both sea
    /// screens build from it — <see cref="SeaHudUI"/> and <see cref="SeaFightUI"/> — so the route plate
    /// up top and the card titles below are the same plate.
    ///
    /// LOADED THROUGH THE ATLAS, NOT FROM RESOURCES. The kit sits outside Resources so the build carries
    /// the packed page only, never the page and the loose textures both. <see cref="SpriteAtlas.GetSprite"/>
    /// hands back a CLONE on every call, so each name is fetched once and held: a sea visit that built
    /// its screens from fresh clones would leave sixty orphan sprites behind every time it closed.
    ///
    /// Every getter can return null (no atlas, no such piece); callers fall back to the flat quad the
    /// way they always have.
    /// </summary>
    public static class SeaKit
    {
        private const string AtlasPath = "UI/Sea/DenizKiti";
        private const string BackdropPath = "UI/Sea/deniz_arka";

        private static SpriteAtlas _atlas;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(32);

        public static Sprite Get(string name)
        {
            Sprite sprite;
            if (Cache.TryGetValue(name, out sprite) && sprite != null) return sprite;
            if (_atlas == null) _atlas = Resources.Load<SpriteAtlas>(AtlasPath);
            sprite = _atlas != null ? _atlas.GetSprite(name) : null;
            if (sprite != null) Cache[name] = sprite;
            return sprite;
        }

        /// <summary>The painted sea. Kept out of the atlas — at 1536×1024 it would be most of the page.</summary>
        public static Sprite Backdrop => Resources.Load<Sprite>(BackdropPath);

        /// <summary>
        /// A nine-sliced kit image. <paramref name="fitHeight"/> puts <see cref="PillFit"/> on it, which
        /// maps the art's full height onto the box — right for capsules and for frames whose borders
        /// should scale with the box (the gear slot). A panel whose frame should hold one thickness at
        /// every size leaves it off and keeps <paramref name="scale"/>.
        /// </summary>
        public static Image Sliced(Transform parent, string name, string art, Vector2 aMin, Vector2 aMax,
                                   bool fitHeight, float scale = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            Sprite sprite = Get(art);
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = scale;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            if (fitHeight) PillFit.Wrap(image);
            return image;
        }

        /// <summary>
        /// One of the two ornamented plates — the route tab (<c>rota</c>) or the title plate
        /// (<c>plaka</c>) — drawn as its two halves, split at the ornament by the kit script. Each half
        /// nine-slices with the ornament inside its inner border, so the plate can be any width and
        /// the ornament is never stretched. The halves come back for callers that tint by state.
        /// </summary>
        public static RectTransform Plate(Transform parent, string name, string art, Vector2 aMin, Vector2 aMax,
                                          out Image left, out Image right)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            left = Sliced(root, "Sol", art + "_sol", Vector2.zero, new Vector2(0.5f, 1f), true);
            right = Sliced(root, "Sag", art + "_sag", new Vector2(0.5f, 0f), Vector2.one, true);
            // Half a unit of overlap each way: two edges meeting on a fractional pixel leave a hairline
            // of whatever is behind the plate showing down its middle.
            left.rectTransform.offsetMax = new Vector2(0.5f, 0f);
            right.rectTransform.offsetMin = new Vector2(-0.5f, 0f);
            return root;
        }

        public static RectTransform Plate(Transform parent, string name, string art, Vector2 aMin, Vector2 aMax)
        {
            Image left, right;
            return Plate(parent, name, art, aMin, aMax, out left, out right);
        }

        /// <summary>A square kit icon or button face whose width follows its anchored height.</summary>
        public static Image Square(Transform parent, string name, string art, Vector2 anchorX, Vector2 anchorY,
                                   float pivotX)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            Sprite sprite = Get(art);
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(anchorX.x, anchorY.x);
            rt.anchorMax = new Vector2(anchorX.y, anchorY.y);
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            var fit = go.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio = 1f;
            return image;
        }
    }
}
