using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The type scale and touch minimum the UI audit's group 2 screens follow. Sizes are canvas units on
    /// the 1080-wide reference the game's canvases scale from, about 0.38 dp each on a 411 dp phone.
    ///
    /// Auto-size floors were the real cause of "tiny" text: a box shorter than the font's line box
    /// (Baloo2's is about 1.6 times its size) makes best fit shrink to its minimum, and a minimum of 10
    /// draws 10-unit text. <see cref="LineBox"/> is the box height a size needs.
    /// </summary>
    public static class UiType
    {
        /// <summary>No auto-size floor goes below this.</summary>
        public const int MinSize = 22;

        /// <summary>Secondary lines: descriptions, details, meta rows.</summary>
        public const int Body = 26;

        /// <summary>Multiplier for wrapped legacy <c>Text</c>, whose default of 1 packs diacritics together.</summary>
        public const float LineSpacing = 1.2f;

        /// <summary>The smallest tap target, 48 dp at 2.63 units per dp.</summary>
        public const float MinTouch = 126f;

        /// <summary>Height a line at <paramref name="size"/> needs so best fit does not shrink it.</summary>
        public static float LineBox(float size) => Mathf.Ceil(size * 1.6f);

        /// <summary>
        /// A label of two or more words split over two lines at the space nearest its middle.
        ///
        /// Greedy wrapping fills the first line and strands whatever is left ("NHẬN TẤT / CẢ", "KHOA
        /// TRƯỞNG CHẾ / TẠO"); a box too narrow for one line reads better as two halves of a size. Text
        /// that already carries a line break is left alone.
        /// </summary>
        public static string Balance(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') >= 0) return text;
            float middle = (text.Length - 1) * 0.5f;
            int split = -1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ' && (split < 0 || Mathf.Abs(i - middle) < Mathf.Abs(split - middle))) split = i;
            return split < 0 ? text : text.Substring(0, split) + "\n" + text.Substring(split + 1);
        }

        /// <summary>
        /// Raycast padding (left, bottom, right, top) that widens a graphic's tap area to
        /// <see cref="MinTouch"/> without redrawing it. <paramref name="downBias"/> is the share of the missing
        /// height added below rather than above, for a control with something tappable over it.
        /// </summary>
        public static Vector4 TouchPadding(Vector2 size, float downBias = 0.5f)
        {
            float w = Mathf.Max(0f, MinTouch - size.x);
            float h = Mathf.Max(0f, MinTouch - size.y);
            return new Vector4(-w * 0.5f, -h * downBias, -w * 0.5f, -h * (1f - downBias));
        }
    }
}
