using Game.Core;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The league avatars: the fifteen master portraits from the masters kit, cut down to a square
    /// head-and-shoulders shot.
    ///
    /// A CROP, NEVER A STRETCH. The portraits are full-length figures about 0.6 as wide as they are
    /// tall. Drawn whole into a 70-unit row badge they would be a smudge, and squeezed square they would
    /// be distorted. So each avatar is a new sprite over the SAME atlas texture, covering the square at
    /// the top of the figure, where every portrait has its head. It is drawn aspect-locked into a square
    /// box, so nothing is scaled unevenly.
    ///
    /// A SUB-SPRITE, NOT A MASK. Fifty rows each behind a RectMask2D would each clip on their own
    /// material, and every clip is a separate batch. A sprite over the atlas page batches with every
    /// other avatar on the board. The atlas is packed without rotation or tight packing, which is what
    /// makes <see cref="Sprite.textureRect"/> a plain rectangle that can be cut into.
    ///
    /// Built once per avatar and kept for the app's life: <see cref="SpriteAtlas.GetSprite"/> hands back
    /// a clone per call, so asking the atlas on every row refresh would leak sprites.
    /// </summary>
    public static class AvatarArt
    {
        private const string AtlasPath = "UI/Usta/UstaKiti";

        /// <summary>The crop's side as a share of the portrait's height. 0.5 takes in the head and
        /// shoulders of every master; the narrowest portrait is 0.53 of its height wide, so the square
        /// always fits inside the art.</summary>
        private const float CropShare = 0.5f;

        private static SpriteAtlas _atlas;
        private static readonly Sprite[] Heads = new Sprite[PlayerProfiles.AvatarCount];

        public static Sprite Get(int avatar)
        {
            avatar = PlayerProfiles.ClampAvatar(avatar);
            if (Heads[avatar] != null) return Heads[avatar];

            if (_atlas == null) _atlas = Resources.Load<SpriteAtlas>(AtlasPath);
            Sprite whole = _atlas != null ? _atlas.GetSprite(PlayerProfiles.AvatarSprites[avatar]) : null;
            if (whole == null || whole.texture == null) return null;

            Heads[avatar] = Crop(whole);
            return Heads[avatar];
        }

        /// <summary>The square at the top of the portrait, horizontally centred, in texture pixels.</summary>
        private static Sprite Crop(Sprite whole)
        {
            Rect tex = whole.textureRect;
            float side = Mathf.Min(tex.width, tex.height * CropShare);
            float x = tex.x + (tex.width - side) * 0.5f;
            float y = tex.y + tex.height - side;

            Sprite head = Sprite.Create(whole.texture, new Rect(x, y, side, side), new Vector2(0.5f, 0.5f),
                                        whole.pixelsPerUnit, 0u, SpriteMeshType.FullRect);
            head.name = whole.name.Replace("(Clone)", string.Empty) + "_bas";
            return head;
        }

        /// <summary>
        /// A framed avatar badge: the kit's gold square button as the frame and the head inside it.
        /// The frame is square because its box is, and the head is locked to its own (square) aspect.
        /// Returns the head image, which a row re-points at a new sprite.
        /// </summary>
        public static Image Badge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, out Image frame)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            frame = go.GetComponent<Image>();
            Sprite plate = UiSkin.ButtonYellow;
            frame.sprite = plate != null ? plate : UiSkin.Flat;
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            RectTransform box = UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);

            var face = new GameObject("Yuz", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(box, false);
            var head = face.GetComponent<Image>();
            head.preserveAspect = true;
            head.raycastTarget = false;
            UiBuild.Anchor((RectTransform)face.transform, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.90f));
            return head;
        }

        /// <summary>Points a badge at an avatar, touching the image only when it actually changes.</summary>
        public static void Show(Image head, int avatar)
        {
            if (head == null) return;
            Sprite sprite = Get(avatar);
            if (head.sprite != sprite) head.sprite = sprite;
            if (head.enabled != (sprite != null)) head.enabled = sprite != null;
        }
    }
}
