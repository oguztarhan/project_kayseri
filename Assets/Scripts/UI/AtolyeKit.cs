using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The design kit the workshop, chapter and goal screens are built from: the 20-piece set in
    /// <c>Assets/UI DESİGNS/Atölye, Bölümler ve Görevler ekranları</c>, trimmed, derived and
    /// downscaled by <c>Tools/ui/atolye_tasarim_kiti.ps1</c> into <c>Art/UI/AtolyeKiti</c> and packed
    /// into one page by <c>Resources/UI/Atolye/AtolyeKiti</c>.
    ///
    /// THREE SCREENS, ONE KIT — <see cref="CraftingUI"/>, <see cref="ChapterUI"/> and
    /// <see cref="GoalsUI"/>, plus the two panels they hand their sprites on to
    /// (<see cref="InventoryUI"/> and <see cref="RewardRevealUI"/>). They are one kit because they
    /// were authored as one set, and because a claim button in a chapter and a claim button in a
    /// goal are the same button. That is the difference from <see cref="SeaKit"/> and
    /// <see cref="LigKit"/>, which are the same shape only because they have the same problem.
    ///
    /// LOADED THROUGH THE ATLAS, NOT FROM RESOURCES, for the reason <see cref="SeaKit"/> records: the
    /// kit sits outside Resources so the build carries the packed page only, never the page and the
    /// loose textures both, and <see cref="SpriteAtlas.GetSprite"/> hands back a CLONE on every call
    /// — so each name is fetched once and held, or every open of a screen would leave a dozen orphan
    /// sprites behind it.
    ///
    /// NOTHING IS KEPT OUTSIDE THE PAGE, unlike the sea backdrop and the league board: the whole kit
    /// is about 1.6M pixels against a 2048-page's 4.2M, so there is no piece big enough to be worth
    /// loading on its own.
    ///
    /// THE ART IS PRE-COLOURED, so a screen picks state by swapping the SPRITE — btn_yesil for a
    /// claim that is live, btn_al for one that is not — never by tinting. Tinting pre-coloured art
    /// only muddies it, which is what the three screens used to do and what this set replaces.
    ///
    /// Can return null (no atlas, no such piece); callers fall back the way they always have.
    /// </summary>
    public static class AtolyeKit
    {
        private const string AtlasPath = "UI/Atolye/AtolyeKiti";

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
        /// Puts a button on its live face or its dead one, and makes that swap the ONLY thing that
        /// says which it is.
        ///
        /// TURNING THE TRANSITION OFF IS THE POINT. A <see cref="Selectable"/> defaults to
        /// <see cref="Selectable.Transition.ColorTint"/>, which washes the target graphic with a
        /// half-transparent grey while the button is not interactable. Over a pre-coloured capsule
        /// that reads as a printing fault rather than as "not now" — the green comes out a sickly
        /// pale green — and it would wash the kit's own pale capsule too, on top of the swap.
        ///
        /// Falls back to the caller's tint when the kit is missing, which is why <paramref name="dead"/>
        /// may be null: nothing is swapped and nothing is turned off.
        /// </summary>
        public static bool Face(Button button, Sprite live, Sprite dead, bool canPress)
        {
            if (button == null || live == null || dead == null) return false;
            button.transition = Selectable.Transition.None;
            var face = button.GetComponent<Image>();
            face.sprite = canPress ? live : dead;
            face.color = Color.white;

            // THE INK TURNS WITH THE FACE. UiBuild.Label writes white, which is right on the green and
            // blue capsules and unreadable on the pale one — it is very nearly white itself. Every
            // label that rides a swapped face would otherwise have to remember this separately.
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = canPress ? Paper : Ink;
            return true;
        }

        private static readonly Color Ink = new Color(0.16f, 0.22f, 0.34f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);

        /// <summary>
        /// Sets a capsule button's label inside the art's end caps, and shrinks it to fit there.
        ///
        /// WHY THE INSET. <see cref="UiBuild.Btn"/> anchors its label to the button's whole rect,
        /// which is right for a rectangular face and wrong for a capsule: the caps are drawn INSIDE
        /// that rect, and <see cref="PillFit"/> scales them with the box, so on a short button they
        /// take most of its width. A label filling the rect then runs out over the rounded ends and
        /// onto whatever is behind them — which is exactly what the claim buttons on all three
        /// screens did with the long Vietnamese strings ("ĐÃ NHẬN" over the story card, "ĐÃ KHÓA"
        /// over the goal card).
        ///
        /// A fraction, not a pixel count, for the same reason PillFit works in fractions: the caps
        /// are a share of the box's height, not a fixed width.
        /// </summary>
        public static Text Label(Button button, int min, int max)
        {
            Text label = button != null ? button.GetComponentInChildren<Text>() : null;
            if (label == null) return null;
            UiBuild.Anchor(label.rectTransform, new Vector2(0.13f, 0.12f), new Vector2(0.87f, 0.88f));
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
    }
}
