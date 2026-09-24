namespace Game.Core
{
    /// <summary>
    /// The 49 simulated rivals on the local league board: who they are, never how well they do. Their
    /// scores are drawn per season by <c>Game.Systems.LocalLeaderboardService</c>.
    ///
    /// THE SAME 49 EVERY SEASON. A rival keeps their handle, avatar and entrant id for good, and a
    /// season only decides where each of them finishes. The board then reads as a set of recurring
    /// opponents instead of forty-nine strangers who change every three days, and a rival the player
    /// beat last season can be recognised on this one.
    ///
    /// HANDLES, NOT NAMES. Each one is an invented gamer tag, so nothing on the board looks like a
    /// real person, and the screen labels them as simulated anyway (Docs/LEADERBOARDS.md D4). The ids
    /// are opaque and fixed-width, so the ordinal tie-break in <see cref="Leaderboards.Compare"/> is
    /// stable and an id can be read back to its rival without a lookup table.
    /// </summary>
    public static class LeagueRivals
    {
        public const int Count = Leaderboards.CohortSize - 1;

        private const string IdPrefix = "rakip-";

        /// <summary>One per rival. Fixed; a rival's index is their identity.</summary>
        public static readonly string[] Handles =
        {
            "DalgaKıran",   "KömürKralı",   "BakırTilki",   "AltınAvcı",    "PusulaUsta",
            "DerinKazma",   "Kıvılcım_7",   "FırtınaReis",  "MercanGöz",    "DemirYumruk",
            "TuzluRüzgar",  "KayaKartal",   "GümüşMartı",   "LavUstası",    "Rıhtım42",
            "KazmaBaşı",    "OcakBekçisi",  "FenerBekçi",   "KristalAy",    "MadenKurdu",
            "YelkenBulut",  "ÇelikKanat",   "PasTutmaz",    "MaviÇapa",     "KırmızıVinç",
            "KuzeyYıldız",  "BakırBaron",   "ElmasTozu",    "TaşYürek",     "KumSaati",
            "IronTide",     "CopperFox",    "SaltyAnchor",  "DeepDrill",    "GoldRush_9",
            "OreWizard",    "TideRunner",   "CoalComet",    "RustyHook",    "NightMiner",
            "StormPetrel",  "LuckyPick",    "CrateKing",    "HarborHawk",   "EmberJack",
            "QuarryQueen",  "BlueLantern",  "SilverKeel",   "MagmaMoth",
        };

        /// <summary>Entrant ids, built once: "rakip-00" … "rakip-48".</summary>
        private static readonly string[] Ids = BuildIds();

        private static string[] BuildIds()
        {
            var ids = new string[Count];
            for (int i = 0; i < Count; i++)
                ids[i] = IdPrefix + (char)('0' + i / 10) + (char)('0' + i % 10);
            return ids;
        }

        public static string IdOf(int rival) => rival >= 0 && rival < Count ? Ids[rival] : string.Empty;

        /// <summary>The rival an entrant id names, or -1 for anything else (the player included).</summary>
        public static int IndexOf(string entrantId)
        {
            if (entrantId == null || entrantId.Length != IdPrefix.Length + 2) return -1;
            if (string.CompareOrdinal(entrantId, 0, IdPrefix, 0, IdPrefix.Length) != 0) return -1;

            int tens = entrantId[IdPrefix.Length] - '0', ones = entrantId[IdPrefix.Length + 1] - '0';
            if (tens < 0 || tens > 9 || ones < 0 || ones > 9) return -1;
            int index = tens * 10 + ones;
            return index < Count ? index : -1;
        }

        public static string HandleOf(int rival) => rival >= 0 && rival < Count ? Handles[rival] : string.Empty;

        /// <summary>
        /// A rival's avatar: an index into <see cref="PlayerProfiles.AvatarSprites"/>. Stepped by 7
        /// rather than taken in order, and 7 shares no factor with the fifteen portraits, so the rivals
        /// next to each other in the list do not wear the same face.
        /// </summary>
        public static int AvatarOf(int rival)
        {
            if (rival < 0) return 0;
            return rival * 7 % PlayerProfiles.AvatarCount;
        }

        /// <summary>
        /// Which rival finishes in strength slot <paramref name="slot"/> this season. Slot 0 is the
        /// strongest target the season draws. A permutation of 0..Count-1 picked by the season seed:
        /// start at a seeded offset and step by a seeded stride that shares no factor with 49 (= 7²),
        /// so every slot gets a different rival and the same season always gets the same order.
        /// </summary>
        public static int RivalInSlot(int seed, int slot)
        {
            if (seed < 0) seed = -(seed + 1);
            int stride = 1 + seed / Count % (Count - 1);
            if (stride % 7 == 0) stride++;
            return (int)((seed + (long)slot * stride) % Count);
        }
    }
}
