using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The league screen's design kit: the 17-piece set in <c>Assets/UI DESİGNS/leaderboard ui</c>,
    /// trimmed, split and downscaled by <c>Tools/ui/lig_tasarim_kiti.ps1</c> into
    /// <c>Art/UI/LigKiti</c> and packed into one page by <c>Resources/UI/Lig/LigKiti</c>. Only
    /// <see cref="LadderUI"/> builds from it, which is why this holds no more than that screen asks
    /// for — the sea kit's <see cref="SeaKit"/> is the same shape because the two screens have the
    /// same problem, not because either is a base for the other.
    ///
    /// LOADED THROUGH THE ATLAS, NOT FROM RESOURCES, for the reason <see cref="SeaKit"/> records:
    /// the kit sits outside Resources so the build carries the packed page only, never the page and
    /// the loose textures both, and <see cref="SpriteAtlas.GetSprite"/> hands back a CLONE on every
    /// call — so each name is fetched once and held, or every open of the board would leave a dozen
    /// orphan sprites behind it.
    ///
    /// Every getter can return null (no atlas, no such piece); callers fall back to the flat quad the
    /// way they always have.
    /// </summary>
    public static class LigKit
    {
        private const string AtlasPath = "UI/Lig/LigKiti";
        private const string BoardPath = "UI/Lig/lig_pano";

        private static SpriteAtlas _atlas;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(24);

        public static Sprite Get(string name)
        {
            Sprite sprite;
            if (Cache.TryGetValue(name, out sprite) && sprite != null) return sprite;
            if (_atlas == null) _atlas = Resources.Load<SpriteAtlas>(AtlasPath);
            sprite = _atlas != null ? _atlas.GetSprite(name) : null;
            if (sprite != null) Cache[name] = sprite;
            return sprite;
        }

        /// <summary>
        /// The board frame, with its star bow. Kept out of the atlas — at 974 px wide it would be most
        /// of the page, and it is the one piece drawn at very nearly its authored width.
        ///
        /// THAT WIDTH IS THE WHOLE REASON THE BOW SURVIVES. A nine-slice stretches its top-centre
        /// segment horizontally and the bow rides in it, so the bow is stretched by the drawn width
        /// over the art width and by nothing else. Draw this into a box about 974 units across —
        /// <see cref="LadderUI"/> uses 0.03–0.97 of a 1080-unit canvas — and that ratio is 1.
        /// </summary>
        public static Sprite Board => Resources.Load<Sprite>(BoardPath);

        /// <summary>
        /// A nine-sliced kit image. <paramref name="fitHeight"/> puts <see cref="PillFit"/> on it,
        /// which maps the art's full height onto the box — right for the capsules (the title ribbon,
        /// the player's row, the claim button, the reward strip's halves), whose end caps have to stay
        /// round however short the box is. The two frames leave it off: a frame's border should hold
        /// one thickness at every size.
        /// </summary>
        public static Image Sliced(Transform parent, string name, string art, Vector2 aMin, Vector2 aMax,
                                   bool fitHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            Sprite sprite = Get(art);
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            if (fitHeight) PillFit.Wrap(image);
            return image;
        }

        /// <summary>
        /// One of the ornamented bars drawn as its two halves, split at the ornament by the kit script.
        /// Each half nine-slices with the ornament inside its inner border, so the bar can be any width
        /// and the ornament is never stretched.
        ///
        /// Only the reward strip needs it here, and only because that bar is drawn five times wider
        /// than it is tall while its winged star sits dead centre — the one shape a plain nine-slice
        /// cannot carry.
        /// </summary>
        public static RectTransform Plate(Transform parent, string name, string art,
                                          Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            Image left = Sliced(root, "Sol", art + "_sol", Vector2.zero, new Vector2(0.5f, 1f), true);
            Image right = Sliced(root, "Sag", art + "_sag", new Vector2(0.5f, 0f), Vector2.one, true);
            // Half a unit of overlap each way: two edges meeting on a fractional pixel leave a hairline
            // of whatever is behind the bar showing down its middle.
            left.rectTransform.offsetMax = new Vector2(0.5f, 0f);
            right.rectTransform.offsetMin = new Vector2(-0.5f, 0f);
            return root;
        }

        /// <summary>
        /// The kit's green capsule as a button, with its label. Both buttons on the board use it — the
        /// one that takes a reward and the one that closes the card that shows it — so the screen has
        /// one button, not a kit one next to a generic one.
        /// </summary>
        public static Button Capsule(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax,
                                     UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var face = go.GetComponent<Image>();
            Sprite art = Get("al_butonu");
            face.sprite = art != null ? art : UiSkin.ButtonGreen;
            face.type = Image.Type.Sliced;
            face.raycastTarget = true;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            PillFit.Wrap(face);

            var press = go.GetComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.targetGraphic = face;
            press.onClick.AddListener(onClick);

            var slot = new GameObject("Yazi", typeof(RectTransform));
            slot.transform.SetParent(go.transform, false);
            UiBuild.Anchor((RectTransform)slot.transform, new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
            label = UiBuild.Label((RectTransform)slot.transform, "Text", text, 28, TextAnchor.MiddleCenter);
            label.color = new Color(0.96f, 0.97f, 1f, 1f);
            return press;
        }

        /// <summary>An aspect-locked kit icon — a chest, a medal, a reward token, the close cross.</summary>
        public static Image Icon(Transform parent, string name, string art, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            Sprite sprite = Get(art);
            if (sprite != null) image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return image;
        }
    }
}
