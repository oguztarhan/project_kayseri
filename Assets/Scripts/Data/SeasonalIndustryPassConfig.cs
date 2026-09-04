using System;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "SeasonalIndustryPassConfig",
        menuName = "Ore Empire/Seasonal Industry Pass Config", order = 28)]
    public sealed class SeasonalIndustryPassConfig : ScriptableObject
    {
        [Serializable]
        private sealed class SourceRow
        {
            [Range(0, Game.Core.Goals.MetricCount - 1)] public int metric;
            [Min(1)] public int pointsPerAction = 1;
        }

        [Serializable]
        private sealed class RewardRow
        {
            [Min(0)] public long gems;
            [Min(0)] public int cards;
            [Min(0)] public long charts;
            [Min(0f)] public double cashMinutes;

            public Game.Core.SeasonalIndustryPass.Reward Build() =>
                new Game.Core.SeasonalIndustryPass.Reward
                {
                    Gems = gems,
                    Cards = cards,
                    Charts = charts,
                    CashMinutes = cashMinutes,
                };

            public void Copy(in Game.Core.SeasonalIndustryPass.Reward reward)
            {
                gems = reward.Gems;
                cards = reward.Cards;
                charts = reward.Charts;
                cashMinutes = reward.CashMinutes;
            }
        }

        [Serializable]
        private sealed class TierRow
        {
            [Min(1)] public int points = 1;
            public RewardRow free = new RewardRow();
            public RewardRow premium = new RewardRow();
        }

        [SerializeField] private string premiumSku = "industry_pass_2026_09";
        [SerializeField] private string fallbackPrice = "₺179,99";
        [SerializeField] private SourceRow[] sources = new SourceRow[0];
        [SerializeField] private TierRow[] tiers = new TierRow[0];

        public Game.Core.SeasonalIndustryPass.Tuning ToTuning()
        {
            var tuning = new Game.Core.SeasonalIndustryPass.Tuning
            {
                PremiumSku = premiumSku,
                FallbackPrice = fallbackPrice,
                Sources = new Game.Core.SeasonalIndustryPass.PointSource[sources != null ? sources.Length : 0],
                Tiers = new Game.Core.SeasonalIndustryPass.Tier[tiers != null ? tiers.Length : 0],
            };
            for (int i = 0; i < tuning.Sources.Length; i++)
            {
                SourceRow row = sources[i];
                if (row == null) continue;
                tuning.Sources[i] = new Game.Core.SeasonalIndustryPass.PointSource
                {
                    Metric = row.metric,
                    PointsPerAction = row.pointsPerAction,
                };
            }
            for (int i = 0; i < tuning.Tiers.Length; i++)
            {
                TierRow row = tiers[i];
                if (row == null) continue;
                tuning.Tiers[i] = new Game.Core.SeasonalIndustryPass.Tier
                {
                    Points = row.points,
                    Free = row.free != null ? row.free.Build() : default,
                    Premium = row.premium != null ? row.premium.Build() : default,
                };
            }
            if (Game.Core.SeasonalIndustryPass.IsWellFormed(tuning)) return tuning;
            Debug.LogWarning("[Sezon Bileti] Geçersiz ayar tablosu; varsayılan tablo kullanılıyor.");
            return Game.Core.SeasonalIndustryPass.Tuning.Default;
        }

#if UNITY_EDITOR
        [ContextMenu("Varsayılanları doldur")]
        public void FillWithDefaults()
        {
            Game.Core.SeasonalIndustryPass.Tuning source =
                Game.Core.SeasonalIndustryPass.Tuning.Default;
            premiumSku = source.PremiumSku;
            fallbackPrice = source.FallbackPrice;
            sources = new SourceRow[source.Sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                sources[i] = new SourceRow
                {
                    metric = source.Sources[i].Metric,
                    pointsPerAction = source.Sources[i].PointsPerAction,
                };
            }
            tiers = new TierRow[source.Tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                tiers[i] = new TierRow { points = source.Tiers[i].Points };
                tiers[i].free.Copy(source.Tiers[i].Free);
                tiers[i].premium.Copy(source.Tiers[i].Premium);
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
