using System;
using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>Designer-authored campaign order. Editing labels/order must not rename existing saved IDs.</summary>
    [CreateAssetMenu(fileName = "MiningShopCampaign", menuName = "Ore Empire/Mining Shop Campaign")]
    public sealed class MiningShopCampaignConfig : ScriptableObject
    {
        [Serializable]
        private struct Chapter
        {
            [SerializeField] private string _id;
            [SerializeField] private string[] _islandIds;

            public string Id => _id;
            public string[] IslandIds => _islandIds;

            public Chapter(string id, string[] islandIds)
            {
                _id = id;
                _islandIds = islandIds;
            }
        }

        [Tooltip("Ordered chapters, each with its own list of stable business IDs. Append content without renaming existing IDs.")]
        [SerializeField] private Chapter[] _chapters =
        {
            new Chapter("mining-shop.chapter-01", new[]
            {
                "mining-shop.island-01-01", "mining-shop.island-01-02", "mining-shop.island-01-03",
                "mining-shop.island-01-04", "mining-shop.island-01-05", "mining-shop.island-01-06",
                "mining-shop.island-01-07"
            }),
            new Chapter("mining-shop.chapter-02", new[]
            {
                "mining-shop.island-02-01", "mining-shop.island-02-02", "mining-shop.island-02-03",
                "mining-shop.island-02-04", "mining-shop.island-02-05", "mining-shop.island-02-06"
            }),
            new Chapter("mining-shop.chapter-03", new[]
            {
                "mining-shop.island-03-01", "mining-shop.island-03-02", "mining-shop.island-03-03",
                "mining-shop.island-03-04", "mining-shop.island-03-05", "mining-shop.island-03-06",
                "mining-shop.island-03-07", "mining-shop.island-03-08", "mining-shop.island-03-09"
            })
        };

        /// <summary>Build once at registration. The catalogue copies values and is unaffected by later array edits.</summary>
        public MiningShopCampaign CreateCampaign()
        {
            if (_chapters == null) throw new InvalidOperationException("Mining-shop chapters are missing.");
            var chapterIds = new string[_chapters.Length];
            var islandIds = new string[_chapters.Length][];
            for (int i = 0; i < _chapters.Length; i++)
            {
                chapterIds[i] = _chapters[i].Id;
                islandIds[i] = _chapters[i].IslandIds;
            }
            return new MiningShopCampaign(chapterIds, islandIds);
        }
    }
}
