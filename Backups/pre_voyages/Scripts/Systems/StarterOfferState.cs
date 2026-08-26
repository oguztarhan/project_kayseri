using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>
    /// Pure save bookkeeping for the 48-hour starter offer attached to each island. Keeping this out of
    /// the store UI lets island travel start the clock immediately and makes the rules testable without
    /// opening a scene or waiting for real time.
    /// </summary>
    public static class StarterOfferState
    {
        public const string Sku = "offer_baslangic";
        public const string LegacyV2Sku = "offer_baslangic_v2";
        public const long WindowSeconds = 48L * 60L * 60L;
        private const string ReceiptSuffix = ":starter";

        public static bool IsStarterSku(string sku)
            => sku == Sku || sku == LegacyV2Sku;

        public static bool EnsureStarted(SaveData data, string island, long nowUnix)
        {
            if (data == null || string.IsNullOrEmpty(island) || nowUnix <= 0L || Bought(data, island))
                return false;
            EnsureLists(data);
            if (Find(data, island) != null) return false;
            data.starterOfferWindows.Add(new StarterOfferWindow
            {
                island = island,
                startedUnix = nowUnix,
            });
            return true;
        }

        public static long StartedUnix(SaveData data, string island)
        {
            StarterOfferWindow row = Find(data, island);
            return row != null ? row.startedUnix : 0L;
        }

        public static long SecondsLeft(SaveData data, string island, long nowUnix)
        {
            if (Bought(data, island)) return 0L;
            long start = StartedUnix(data, island);
            if (start <= 0L) return 0L;
            long left = WindowSeconds - Math.Max(0L, nowUnix - start);
            return left > 0L ? left : 0L;
        }

        public static bool Bought(SaveData data, string island)
        {
            if (data == null || string.IsNullOrEmpty(island) || data.islandOffersBought == null)
                return false;
            string key = Receipt(island);
            for (int i = 0; i < data.islandOffersBought.Count; i++)
                if (data.islandOffersBought[i] == key) return true;
            return false;
        }

        public static bool MarkBought(SaveData data, string island)
        {
            if (data == null || string.IsNullOrEmpty(island)) return false;
            EnsureLists(data);
            if (Bought(data, island)) return false;
            data.islandOffersBought.Add(Receipt(island));
            return true;
        }

        /// <summary>
        /// Converts the old account-wide starter state once. A paid legacy pack marks every island the
        /// player already owned, so an update never asks them to repurchase old ground. An unbought old
        /// countdown continues on the island where the update found the player.
        /// </summary>
        public static bool MigrateLegacy(SaveData data, string activeIsland,
                                         IReadOnlyList<string> currentlyOwnedIslands)
        {
            if (data == null || data.starterOffersMigrated) return false;
            EnsureLists(data);

            bool legacyBought = false;
            if (data.purchasedOffers != null)
                for (int i = 0; i < data.purchasedOffers.Count; i++)
                    if (IsStarterSku(data.purchasedOffers[i])) { legacyBought = true; break; }

            if (legacyBought && currentlyOwnedIslands != null)
                for (int i = 0; i < currentlyOwnedIslands.Count; i++)
                    MarkBought(data, currentlyOwnedIslands[i]);
            else if (data.starterOfferSeenUnix > 0L && !string.IsNullOrEmpty(activeIsland))
            {
                StarterOfferWindow row = Find(data, activeIsland);
                if (row == null)
                    data.starterOfferWindows.Add(new StarterOfferWindow
                    {
                        island = activeIsland,
                        startedUnix = data.starterOfferSeenUnix,
                    });
            }

            data.starterOffersMigrated = true;
            return true;
        }

        private static StarterOfferWindow Find(SaveData data, string island)
        {
            if (data == null || string.IsNullOrEmpty(island) || data.starterOfferWindows == null)
                return null;
            for (int i = 0; i < data.starterOfferWindows.Count; i++)
            {
                StarterOfferWindow row = data.starterOfferWindows[i];
                if (row != null && row.island == island) return row;
            }
            return null;
        }

        private static string Receipt(string island) => island + ReceiptSuffix;

        private static void EnsureLists(SaveData data)
        {
            if (data.starterOfferWindows == null)
                data.starterOfferWindows = new List<StarterOfferWindow>();
            if (data.islandOffersBought == null)
                data.islandOffersBought = new List<string>();
        }
    }
}
