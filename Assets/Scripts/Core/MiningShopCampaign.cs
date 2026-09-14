using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Authored mining-shop business order. Independent of legacy ore chapters and their saved beats.
    /// This catalogue owns no unlocks, money, claims or simulation; those belong to runtime services.
    /// </summary>
    public sealed class MiningShopCampaign
    {
        public const int ProductCount = 4;

        public readonly struct Island
        {
            public readonly string Id;
            public readonly string ChapterId;
            public readonly int ChapterNumber;
            public readonly int IslandNumber;
            /// <summary>Zero-based overall order, for difficulty tuning without chapter resets.</summary>
            public readonly int CampaignIndex;
            public readonly int AvailableProductCount;

            internal Island(string id, string chapterId, int chapterNumber, int islandNumber, int campaignIndex)
            {
                Id = id;
                ChapterId = chapterId;
                ChapterNumber = chapterNumber;
                IslandNumber = islandNumber;
                CampaignIndex = campaignIndex;
                AvailableProductCount = Math.Min(ProductCount, campaignIndex + 1);
            }

            /// <summary>Availability never grants built tables on arrival.</summary>
            public int StartingTableCount => 1;
        }

        private readonly Island[] _islands;
        private readonly Dictionary<string, int> _indices;

        public int ChapterCount { get; }
        public int IslandCount => _islands.Length;

        public MiningShopCampaign(string[] chapterIds, string[][] islandIds)
        {
            if (chapterIds == null) throw new ArgumentNullException(nameof(chapterIds));
            if (islandIds == null) throw new ArgumentNullException(nameof(islandIds));
            if (chapterIds.Length == 0 || chapterIds.Length != islandIds.Length)
                throw new ArgumentException("Campaign requires matching, nonempty chapter and island lists.");

            var chapterKeys = new HashSet<string>(StringComparer.Ordinal);
            var islandKeys = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            for (int chapter = 0; chapter < chapterIds.Length; chapter++)
            {
                if (!ValidId(chapterIds[chapter]) || !chapterKeys.Add(chapterIds[chapter]))
                    throw new ArgumentException("Chapter IDs must be unique, nonempty and trimmed.");
                string[] ids = islandIds[chapter];
                if (ids == null || ids.Length == 0)
                    throw new ArgumentException("Each chapter requires at least one island.");
                for (int island = 0; island < ids.Length; island++)
                    if (!ValidId(ids[island]) || !islandKeys.Add(ids[island]))
                        throw new ArgumentException("Business IDs must be globally unique, nonempty and trimmed.");
                total = checked(total + ids.Length);
            }

            ChapterCount = chapterIds.Length;
            _islands = new Island[total];
            _indices = new Dictionary<string, int>(total, StringComparer.Ordinal);
            int index = 0;
            for (int chapter = 0; chapter < chapterIds.Length; chapter++)
                for (int island = 0; island < islandIds[chapter].Length; island++)
                {
                    string id = islandIds[chapter][island];
                    _islands[index] = new Island(id, chapterIds[chapter], chapter + 1, island + 1, index);
                    _indices.Add(id, index++);
                }
        }

        public Island IslandAt(int campaignIndex) => _islands[campaignIndex];

        public bool TryGet(string businessId, out Island island)
        {
            if (businessId != null && _indices.TryGetValue(businessId, out int index))
            {
                island = _islands[index];
                return true;
            }
            island = default;
            return false;
        }

        /// <summary>False for an unknown ID or the end of authored content. Never wraps to 1-1.</summary>
        public bool TryGetNext(string businessId, out Island next)
        {
            if (businessId != null && _indices.TryGetValue(businessId, out int index) && index + 1 < _islands.Length)
            {
                next = _islands[index + 1];
                return true;
            }
            next = default;
            return false;
        }

        /// <summary>
        /// Shop unlock order is distinct from MiningGear's saved equipment indices (bag precedes lantern there).
        /// These IDs identify merchandise, never equipped captain items.
        /// </summary>
        public static string ProductIdAt(int unlockIndex)
        {
            switch (unlockIndex)
            {
                case 0: return "mining-shop.pickaxe";
                case 1: return "mining-shop.helmet";
                case 2: return "mining-shop.lantern";
                case 3: return "mining-shop.bag";
                default: throw new ArgumentOutOfRangeException(nameof(unlockIndex));
            }
        }

        private static bool ValidId(string id)
            => !string.IsNullOrWhiteSpace(id) && string.Equals(id, id.Trim(), StringComparison.Ordinal);
    }
}
