using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>Inspector authoring surface for Harbor Festival balance.</summary>
    [CreateAssetMenu(fileName = "HarborFestivalConfig", menuName = "Ore Empire/Harbor Festival Config", order = 26)]
    public sealed class HarborFestivalConfig : ScriptableObject
    {
        private enum Metric
        {
            Kulce = 0,
            Yukseltme = 1,
            Kontrat = 2,
            Onarim = 3,
            Ada = 4,
            Ustabasi = 5,
        }

        [Serializable]
        private sealed class RewardRow
        {
            [Min(0)] public long gems;
            [Min(0)] public int cards;
            [Min(0)] public long charts;
            [Min(0f)] public double boostMult;
            [Min(0f)] public double boostSeconds;

            public Game.Core.HarborFestival.Reward Build() => new Game.Core.HarborFestival.Reward
            {
                Gems = gems,
                Cards = cards,
                Charts = charts,
                BoostMult = boostMult,
                BoostSeconds = boostSeconds,
            };

            public void Copy(in Game.Core.HarborFestival.Reward reward)
            {
                gems = reward.Gems;
                cards = reward.Cards;
                charts = reward.Charts;
                boostMult = reward.BoostMult;
                boostSeconds = reward.BoostSeconds;
            }
        }

        [Serializable]
        private sealed class TaskRow
        {
            public Metric metric = Metric.Yukseltme;
            [Min(1)] public long target = 1;
            [Min(1)] public int tokens = 10;
            public RewardRow reward = new RewardRow();
        }

        [Serializable]
        private sealed class TierRow
        {
            [Min(1)] public int tokens = 10;
            public RewardRow free = new RewardRow();
            public RewardRow premium = new RewardRow();
        }

        [Serializable]
        private sealed class CatalogueRow
        {
            [Min(1)] public int cost = 10;
            public RewardRow reward = new RewardRow();
        }

        [SerializeField] private TaskRow[] tasks = new TaskRow[0];
        [SerializeField] private TierRow[] tiers = new TierRow[0];
        [SerializeField] private CatalogueRow[] catalogue = new CatalogueRow[0];
        [SerializeField, Min(1)] private int tokensPerExpiryGem = 10;
        [Tooltip("Boş bırakıldığında premium şerit kapalıdır. Ürün onayından sonra mevcut mağazadaki tek seferlik SKU yazılır.")]
        [SerializeField] private string premiumSku = "";

        public Game.Core.HarborFestival.Tuning ToTuning()
        {
            if (tasks == null || tasks.Length == 0) return Game.Core.HarborFestival.Tuning.Default;

            var tuning = new Game.Core.HarborFestival.Tuning
            {
                Tasks = new Game.Core.HarborFestival.Task[tasks.Length],
                Tiers = new Game.Core.HarborFestival.Tier[tiers != null ? tiers.Length : 0],
                Catalogue = new Game.Core.HarborFestival.CatalogueItem[catalogue != null ? catalogue.Length : 0],
                TokensPerExpiryGem = tokensPerExpiryGem,
                PremiumSku = premiumSku,
            };

            for (int i = 0; i < tuning.Tasks.Length; i++)
            {
                TaskRow row = tasks[i];
                if (row == null) continue;
                tuning.Tasks[i] = new Game.Core.HarborFestival.Task
                {
                    Metric = (int)row.metric,
                    Target = row.target,
                    Tokens = row.tokens,
                    Reward = row.reward != null ? row.reward.Build() : default,
                };
            }
            for (int i = 0; i < tuning.Tiers.Length; i++)
            {
                TierRow row = tiers[i];
                if (row == null) continue;
                tuning.Tiers[i] = new Game.Core.HarborFestival.Tier
                {
                    Tokens = row.tokens,
                    Free = row.free != null ? row.free.Build() : default,
                    Premium = row.premium != null ? row.premium.Build() : default,
                };
            }
            for (int i = 0; i < tuning.Catalogue.Length; i++)
            {
                CatalogueRow row = catalogue[i];
                if (row == null) continue;
                tuning.Catalogue[i] = new Game.Core.HarborFestival.CatalogueItem
                {
                    Cost = row.cost,
                    Reward = row.reward != null ? row.reward.Build() : default,
                };
            }

            if (Game.Core.HarborFestival.IsWellFormed(tuning)) return tuning;
            Debug.LogWarning("[Liman Festivali] Ayar tablosu geçersiz; varsayılan tablo kullanılıyor.");
            return Game.Core.HarborFestival.Tuning.Default;
        }

#if UNITY_EDITOR
        [ContextMenu("Varsayılanları doldur")]
        private void FillWithDefaults()
        {
            Game.Core.HarborFestival.Tuning source = Game.Core.HarborFestival.Tuning.Default;
            tasks = new TaskRow[source.Tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = new TaskRow
                {
                    metric = (Metric)source.Tasks[i].Metric,
                    target = source.Tasks[i].Target,
                    tokens = source.Tasks[i].Tokens,
                };
                tasks[i].reward.Copy(source.Tasks[i].Reward);
            }
            tiers = new TierRow[source.Tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                tiers[i] = new TierRow { tokens = source.Tiers[i].Tokens };
                tiers[i].free.Copy(source.Tiers[i].Free);
                tiers[i].premium.Copy(source.Tiers[i].Premium);
            }
            catalogue = new CatalogueRow[source.Catalogue.Length];
            for (int i = 0; i < catalogue.Length; i++)
            {
                catalogue[i] = new CatalogueRow { cost = source.Catalogue[i].Cost };
                catalogue[i].reward.Copy(source.Catalogue[i].Reward);
            }
            tokensPerExpiryGem = source.TokensPerExpiryGem;
            premiumSku = source.PremiumSku;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
