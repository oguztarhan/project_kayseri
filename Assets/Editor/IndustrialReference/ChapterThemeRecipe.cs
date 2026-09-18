using UnityEngine;

namespace Kayseri.IslandThemeTools
{
    /// <summary>
    /// One chapter's look as a handful of anchor colours — what <see cref="IslandThemeBuilder"/>
    /// turns into a theme when it is run in recipe mode.
    ///
    /// WHY ANCHORS AND NOT 36 COLOURS. The island reads as itself because of the contrast INSIDE each
    /// family of materials: the dark pines against the light ones, pale rock against coal. Picking 36
    /// colours by hand per chapter loses that within a chapter or two. So a chapter names one colour per
    /// family, and every member moves relative to it — same value ratio, same saturation offset — and
    /// arrives with the family's internal contrast intact. That is what "clearly a different chapter,
    /// still the same island" comes down to.
    ///
    /// WHAT A RECIPE DOES NOT TOUCH. Emissive materials (they read as light, and light is out of
    /// scope), the sea, the plank wood, the road and the colour-coded containers are not in any family,
    /// so they get no row and draw the island's own material in every chapter. The road stays because
    /// it is shared with the coin rims, and coins stay gold in every chapter. A family
    /// whose target equals its original gets no row either — chapters that keep the yellow machinery
    /// do not carry 2 copies of an unchanged material.
    ///
    /// The moods follow the chapter screen's own story lines (bolum.hikaye.*), so the island and the
    /// text the player reads beside it agree. They are moods, not ores: the island's product does not
    /// change with the chapter.
    /// </summary>
    public sealed class ChapterThemeRecipe
    {
        public string Id;
        public string DisplayName;
        public int FromChapter;

        /// <summary>grassDark's target; grassLight and the textured grass follow it.</summary>
        public Color Ground;
        /// <summary>pine's target; pineDark and pineLight follow it.</summary>
        public Color Vegetation;
        /// <summary>bark's target — the tree trunks.</summary>
        public Color Bark;
        /// <summary>rock1's target; the other rocks follow it, and coal and sand follow it at capped saturations.</summary>
        public Color Rock;
        /// <summary>yellow's target — the machinery accent — with orange following it.</summary>
        public Color Accent;
        /// <summary>What steel, steelDark, white, rubber and concrete are nudged toward, and by how much.</summary>
        public Color MetalTint;
        public float MetalAmount;

        /// <summary>
        /// The seven chapters after the first. Chapter one is the island as authored and has no theme.
        ///
        /// The accent moves only where yellow would sink into the ground: verdigris against the
        /// sunburnt hills, signal orange on the grey moor, blue against the harvest gold, orange in the
        /// jungle. On snow, crimson and crystal the original yellow already stands out, so it stays.
        /// </summary>
        public static readonly ChapterThemeRecipe[] All =
        {
            new ChapterThemeRecipe
            {
                Id = "ch2_sunburnt", DisplayName = "Ch2 Sunburnt Hills", FromChapter = 1,
                Ground = C(0.64f, 0.62f, 0.36f), Vegetation = C(0.38f, 0.48f, 0.22f), Bark = C(0.52f, 0.34f, 0.22f),
                Rock = C(0.80f, 0.56f, 0.44f), Accent = C(0.18f, 0.66f, 0.60f),
                MetalTint = C(0.80f, 0.64f, 0.52f), MetalAmount = 0.12f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch3_slate", DisplayName = "Ch3 Slate Moor", FromChapter = 2,
                Ground = C(0.50f, 0.60f, 0.48f), Vegetation = C(0.14f, 0.38f, 0.34f), Bark = C(0.36f, 0.31f, 0.28f),
                Rock = C(0.50f, 0.57f, 0.66f), Accent = C(0.96f, 0.46f, 0.12f),
                MetalTint = C(0.52f, 0.60f, 0.72f), MetalAmount = 0.15f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch4_frost", DisplayName = "Ch4 Frost", FromChapter = 3,
                Ground = C(0.91f, 0.94f, 0.98f), Vegetation = C(0.17f, 0.36f, 0.38f), Bark = C(0.44f, 0.40f, 0.38f),
                Rock = C(0.68f, 0.74f, 0.83f), Accent = Original,
                MetalTint = C(0.70f, 0.80f, 0.92f), MetalAmount = 0.10f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch5_harvest", DisplayName = "Ch5 Harvest Gold", FromChapter = 4,
                Ground = C(0.86f, 0.77f, 0.46f), Vegetation = C(0.86f, 0.58f, 0.20f), Bark = C(0.40f, 0.26f, 0.16f),
                Rock = C(0.86f, 0.77f, 0.60f), Accent = C(0.16f, 0.40f, 0.82f),
                MetalTint = C(0.86f, 0.74f, 0.56f), MetalAmount = 0.10f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch6_crimson", DisplayName = "Ch6 Crimson Dusk", FromChapter = 5,
                Ground = C(0.62f, 0.40f, 0.30f), Vegetation = C(0.60f, 0.22f, 0.12f), Bark = C(0.30f, 0.19f, 0.17f),
                Rock = C(0.54f, 0.46f, 0.49f), Accent = Original,
                MetalTint = C(0.76f, 0.58f, 0.62f), MetalAmount = 0.06f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch7_jungle", DisplayName = "Ch7 Emerald Jungle", FromChapter = 6,
                Ground = C(0.20f, 0.56f, 0.34f), Vegetation = C(0.05f, 0.40f, 0.34f), Bark = C(0.32f, 0.27f, 0.16f),
                Rock = C(0.42f, 0.53f, 0.45f), Accent = C(0.98f, 0.60f, 0.12f),
                MetalTint = C(0.56f, 0.70f, 0.62f), MetalAmount = 0.08f,
            },
            new ChapterThemeRecipe
            {
                Id = "ch8_crystal", DisplayName = "Ch8 Crystal Edge", FromChapter = 7,
                Ground = C(0.77f, 0.71f, 0.91f), Vegetation = C(0.42f, 0.30f, 0.70f), Bark = C(0.40f, 0.35f, 0.46f),
                Rock = C(0.64f, 0.88f, 0.96f), Accent = Original,
                MetalTint = C(0.74f, 0.70f, 0.90f), MetalAmount = 0.12f,
            },
        };

        /// <summary>"Leave this family as the island has it" — alpha zero is never a real target here.</summary>
        public static readonly Color Original = new Color(0f, 0f, 0f, 0f);

        public static bool IsOriginal(Color c) => c.a <= 0f;

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    }
}
