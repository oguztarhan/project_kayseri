using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The kit the contract, mining gear and events screens are built from: pieces of the general and
    /// settings design sets, trimmed and measured by <c>Tools/ui/ekran_tasarim_kiti.ps1</c> into
    /// <c>Art/UI/EkranKiti</c> and packed by <c>Resources/UI/Ekran/EkranKiti</c>.
    ///
    /// THE SHEET, THE RIBBON, THE GREEN CAPSULE AND THE GEM COME FROM <see cref="LigKit"/>. The
    /// leaderboard set drew them and the league script already derives them, so these screens take
    /// them from there rather than packing a second copy of each — which is why the builders here take
    /// a <see cref="Sprite"/> and not a name.
    ///
    /// LOADED THROUGH THE ATLAS, fetched once per name and held, for the reason <see cref="SeaKit"/>
    /// records: <see cref="SpriteAtlas.GetSprite"/> hands back a clone on every call.
    ///
    /// THE ART IS PRE-COLOURED. A button says whether it can be pressed by swapping its face between
    /// btn_turuncu and btn_bos, never by tint — and so the transition every builder here makes is None,
    /// or Unity's disabled ColorTint would wash the swapped face as well.
    ///
    /// Every getter can return null (no atlas, no such piece); builders fall back to the flat quad.
    /// </summary>
    public static class EkranKit
    {
        private const string AtlasPath = "UI/Ekran/EkranKiti";

        private static SpriteAtlas _atlas;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(12);

        /// <summary>Navy ink for the cream inlay, the pale capsule and the white sheet.</summary>
        public static readonly Color Ink = new Color(0.09f, 0.15f, 0.29f, 1f);

        /// <summary>Paper ink for the navy card.</summary>
        public static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        /// <summary>Second-rank ink on the navy card — clocks, counts, anything under a headline.</summary>
        public static readonly Color PaperSoft = new Color(0.68f, 0.78f, 0.93f, 1f);

        /// <summary>
        /// Where a label goes on btn_turuncu: its cream inlay, measured by the kit script (x 76..476 of
        /// 553, y 0.357..0.683). The orange around the inlay is ornament, not writing space.
        ///
        /// A LITTLE TALLER THAN THE INLAY, ON PURPOSE. A TMP line is about 1.4 of its point size, and a
        /// line that does not fit its rect under Ellipsis is dropped whole rather than clipped — the
        /// first build used the inlay's exact band and every accept label came out blank. The glyphs
        /// themselves stay on the cream; only the line box reaches a few units onto the orange.
        /// </summary>
        public static readonly Vector2 InlayMin = new Vector2(0.16f, 0.33f);
        public static readonly Vector2 InlayMax = new Vector2(0.84f, 0.71f);

        /// <summary>Where a label goes on btn_bos and al_butonu: inside the round caps.</summary>
        public static readonly Vector2 CapsMin = new Vector2(0.13f, 0.14f);
        public static readonly Vector2 CapsMax = new Vector2(0.87f, 0.86f);

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
        /// A nine-sliced kit image. <paramref name="fitHeight"/> adds <see cref="PillFit"/> — right for a
        /// capsule, whose caps must stay round however short the box; wrong for a card or the sheet,
        /// whose frame should hold one thickness at every size.
        /// </summary>
        public static Image Sliced(Transform parent, string name, Sprite sprite, Vector2 aMin, Vector2 aMax,
                                   bool fitHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            if (fitHeight && sprite != null) PillFit.Wrap(image);
            return image;
        }

        /// <summary>A capsule button with no label — each screen writes its own label type into it.</summary>
        public static Button Capsule(Transform parent, string name, Sprite face, Vector2 aMin, Vector2 aMax,
                                     UnityAction onClick)
        {
            Image image = Sliced(parent, name, face, aMin, aMax, true);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>
        /// Swaps a capsule between its live and dead faces and re-fits the caps — the two faces are
        /// different heights, so the multiplier PillFit worked out for one is wrong for the other.
        /// </summary>
        public static void SetFace(Button button, Sprite live, Sprite dead, bool canPress)
        {
            if (button == null) return;
            button.interactable = canPress;
            var image = (Image)button.targetGraphic;
            Sprite want = canPress ? live : dead;
            if (want == null || image.sprite == want) return;
            image.sprite = want;
            var fit = image.GetComponent<PillFit>();
            if (fit != null) fit.Fit();
        }

        /// <summary>An aspect-locked kit icon — never stretched, whatever box it is given.</summary>
        public static Image Icon(Transform parent, string name, Sprite sprite, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return image;
        }

        /// <summary>
        /// The red close disc as a button. Built here rather than through <see cref="UiBuild.Btn"/>,
        /// which tints its face with the fallback colour when the skin had no art at build time — the
        /// dark-red and near-black close buttons these screens used to show.
        /// </summary>
        public static Button Close(Transform parent, Vector2 aMin, Vector2 aMax, UnityAction onClick)
        {
            Sprite art = Get("kapat");
            Image image = Icon(parent, "Kapat", art != null ? art : UiSkin.ButtonGrey, aMin, aMax);
            image.enabled = true;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }
    }
}
