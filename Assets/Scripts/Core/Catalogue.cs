namespace Game.Core
{
    /// <summary>
    /// The production chain the player can look up: eight ores, ten refined goods, and which
    /// island each of them needs before it is anything more than a picture.
    ///
    /// WHY THIS IS A TABLE AND NOT A CONFIG. The chain already exists twice — once as the
    /// <c>Assets/Data/Ore</c>, <c>Products</c> and <c>Recipes</c> assets the refinery model was
    /// authored against (<see cref="Refining"/> and Game.Data's <c>Recipe</c>), and once as the
    /// eight-rung island ladder the game actually runs (<c>WorldIslands</c>). Nothing reads the
    /// first set at runtime, which is why the catalogue is a table here rather than a walk over
    /// those assets: this file is Core, so a test can assert every lock in the chain without a
    /// ScriptableObject, an AssetDatabase or a scene. The rows are a transcription of those ten
    /// recipe assets, inputs and refining times included; if a recipe asset is ever retouched,
    /// this table is the other half of the change.
    ///
    /// LOCKS ARE TRANSITIVE, WHICH IS THE ONLY INTERESTING RULE IN HERE. A ruby ring is a gold bar
    /// and a cut ruby, so a player standing on Ruby Island with no gold cannot make one, and the
    /// row has to say gold rather than ruby. So <see cref="IsDiscovered"/> resolves through the
    /// inputs rather than looking at one island, and <see cref="MissingIsland"/> names the first
    /// rung that is actually missing — in ladder order, so it names the cheapest one to go and buy.
    ///
    /// ENTRY ORDER IS THE ADDRESS. Ores first, in ladder order, then products in refining order.
    /// Nothing persists an entry index — the catalogue is read-only and holds no save state at all —
    /// but the UI draws in this order, and every product's inputs are guaranteed to sit at a LOWER
    /// index than the product itself, which is what makes the recursion in IsDiscovered terminate.
    /// <c>CatalogueTests</c> pins that invariant.
    /// </summary>
    public static class Catalogue
    {
        /// <summary>The ore ladder — also the island ids in <c>SaveData.unlockedIslands</c> and the
        /// <c>cevher.*</c> loc keys, which is why they are lower-case and never translated here.</summary>
        public static readonly string[] OreKeys =
            { "coal", "copper", "iron", "silver", "gold", "ruby", "emerald", "diamond" };

        public static readonly int OreCount = OreKeys.Length;

        /// <summary>The refined goods, in the order the chain makes them. Loc keys are <c>urun.*</c>.</summary>
        public static readonly string[] ProductKeys =
        {
            "coke", "copper_bar", "steel_beam", "silver_bar", "gold_bar",
            "cut_ruby", "ruby_ring", "cut_emerald", "polished_diamond", "diamond_crown",
        };

        public static readonly int ProductCount = ProductKeys.Length;

        /// <summary>Which island's works refines it — the ore rung, not the ingredient.</summary>
        private static readonly int[] ProductIsland = { 0, 1, 2, 3, 4, 5, 5, 6, 7, 7 };

        /// <summary>Seconds one batch takes, straight off the recipe assets. Display only.</summary>
        private static readonly double[] ProductSeconds =
            { 1d, 1d, 1.5d, 1d, 1d, 1.2d, 2d, 1.2d, 1.5d, 2.5d };

        /// <summary>
        /// What each product is made of, as ENTRY indices (so an ore input and a product input
        /// address the same way). Every value here is smaller than the product's own entry index —
        /// see the class header.
        /// </summary>
        private static readonly int[][] ProductInputs =
        {
            new[] { 0 },            // coke             = coal
            new[] { 1 },            // copper bar       = copper
            new[] { 2, 0 },         // steel beam       = iron + coal
            new[] { 3 },            // silver bar       = silver
            new[] { 4 },            // gold bar         = gold
            new[] { 5 },            // cut ruby         = ruby
            new[] { 12, 13 },       // ruby ring        = gold bar + cut ruby
            new[] { 6 },            // cut emerald      = emerald
            new[] { 7 },            // polished diamond = diamond
            new[] { 12, 16 },       // diamond crown    = gold bar + polished diamond
        };

        /// <summary>Ores then products — the catalogue's whole length.</summary>
        public static int EntryCount => OreCount + ProductCount;

        public static bool IsOre(int entry) => entry >= 0 && entry < OreCount;

        public static bool IsProduct(int entry) => entry >= OreCount && entry < EntryCount;

        /// <summary>The entry's untranslated id — an ore key or a product key. Empty off the end.</summary>
        public static string KeyOf(int entry)
        {
            if (IsOre(entry)) return OreKeys[entry];
            if (IsProduct(entry)) return ProductKeys[entry - OreCount];
            return string.Empty;
        }

        /// <summary>The ore rung whose island holds it: an ore's own rung, a product's works. -1 off the end.</summary>
        public static int IslandOf(int entry)
        {
            if (IsOre(entry)) return entry;
            if (IsProduct(entry)) return ProductIsland[entry - OreCount];
            return -1;
        }

        /// <summary>One batch's time in seconds; 0 for an ore, which is dug rather than made.</summary>
        public static double SecondsOf(int entry)
            => IsProduct(entry) ? ProductSeconds[entry - OreCount] : 0d;

        public static int InputCount(int entry)
            => IsProduct(entry) ? ProductInputs[entry - OreCount].Length : 0;

        /// <summary>The entry index of one ingredient. -1 off either end.</summary>
        public static int InputAt(int entry, int i)
        {
            if (!IsProduct(entry)) return -1;
            int[] row = ProductInputs[entry - OreCount];
            return i >= 0 && i < row.Length ? row[i] : -1;
        }

        /// <summary>
        /// Whether the player can actually make this: their island owns the works, and every
        /// ingredient is itself discovered. An ore is discovered when its island is.
        ///
        /// <paramref name="ownedOre"/> is one flag per rung of <see cref="OreKeys"/>. A short or
        /// null array reads as "not owned" rather than throwing — the catalogue is a screen, and a
        /// screen asking before the world has loaded should draw locks, not an exception.
        /// </summary>
        public static bool IsDiscovered(int entry, bool[] ownedOre)
        {
            if (entry < 0 || entry >= EntryCount) return false;
            if (!Owns(IslandOf(entry), ownedOre)) return false;
            int inputs = InputCount(entry);
            for (int i = 0; i < inputs; i++)
                if (!IsDiscovered(InputAt(entry, i), ownedOre)) return false;
            return true;
        }

        /// <summary>
        /// The first island rung this entry is waiting on, in ladder order — what a locked row
        /// names. -1 once the entry is discovered, so a caller can use it as the test.
        ///
        /// Ladder order rather than recipe order on purpose: a diamond crown wants gold and
        /// diamond, and telling someone standing on silver to go and buy Diamond Island is telling
        /// them the last step instead of the next one.
        /// </summary>
        public static int MissingIsland(int entry, bool[] ownedOre)
        {
            if (entry < 0 || entry >= EntryCount) return -1;
            if (IsDiscovered(entry, ownedOre)) return -1;
            int lowest = -1;
            Missing(entry, ownedOre, ref lowest);
            return lowest;
        }

        private static void Missing(int entry, bool[] ownedOre, ref int lowest)
        {
            int island = IslandOf(entry);
            if (!Owns(island, ownedOre) && (lowest < 0 || island < lowest)) lowest = island;
            int inputs = InputCount(entry);
            for (int i = 0; i < inputs; i++) Missing(InputAt(entry, i), ownedOre, ref lowest);
        }

        private static bool Owns(int rung, bool[] ownedOre)
            => ownedOre != null && rung >= 0 && rung < ownedOre.Length && ownedOre[rung];
    }
}
