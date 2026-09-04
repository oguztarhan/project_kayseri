using System;

namespace Game.Core
{
    /// <summary>Pure rules for the Seasonal Industry Pass. Runtime state stays in LiveEventState.</summary>
    public static class SeasonalIndustryPass
    {
        public const int Kind = 4;
        public const int SourceCount = 4;
        public const int TierCount = 15;
        public const int Slots = TierCount * 2;

        public static int ProgressSlot(int source) => source;
        public static int CursorSlot(int source) => SourceCount + source;
        public static int FreeClaimSlot(int tier) => tier;
        public static int PremiumClaimSlot(int tier) => TierCount + tier;

        public struct PointSource
        {
            public int Metric;
            public int PointsPerAction;
        }

        public struct Reward
        {
            public long Gems;
            public int Cards;
            public long Charts;
            public double CashMinutes;
        }

        public struct Tier
        {
            public int Points;
            public Reward Free;
            public Reward Premium;
        }

        public struct Tuning
        {
            public string PremiumSku;
            public string FallbackPrice;
            public PointSource[] Sources;
            public Tier[] Tiers;

            public static Tuning Default => new Tuning
            {
                PremiumSku = "industry_pass_2026_09",
                FallbackPrice = "₺179,99",
                Sources = new[]
                {
                    new PointSource { Metric = Goals.Upgrades, PointsPerAction = 3 },
                    new PointSource { Metric = Goals.Contracts, PointsPerAction = 15 },
                    new PointSource { Metric = Goals.Repairs, PointsPerAction = 8 },
                    new PointSource { Metric = Goals.ForemanLevels, PointsPerAction = 20 },
                },
                Tiers = new[]
                {
                    MakeTier(30, 15, 0, 0, 30, 1, 0),
                    MakeTier(70, 20, 0, 0, 40, 1, 0),
                    MakeTier(120, 25, 1, 0, 55, 2, 0),
                    MakeTier(180, 30, 0, 0, 70, 2, 0),
                    MakeTier(250, 40, 1, 0, 90, 2, 20),
                    MakeTier(330, 45, 0, 0, 105, 3, 0),
                    MakeTier(420, 55, 1, 0, 125, 3, 0),
                    MakeTier(520, 65, 0, 25, 145, 3, 35),
                    MakeTier(630, 75, 1, 0, 170, 4, 0),
                    MakeTier(750, 90, 1, 0, 200, 4, 50),
                    MakeTier(880, 105, 0, 40, 230, 5, 0),
                    MakeTier(1020, 120, 2, 0, 260, 5, 60),
                    MakeTier(1170, 140, 1, 0, 300, 6, 0),
                    MakeTier(1330, 165, 2, 50, 350, 7, 80),
                    MakeTier(1500, 220, 3, 75, 500, 10, 120),
                },
            };
        }

        private static Tier MakeTier(int points, long freeGems, int freeCards, long freeCharts,
                                     long premiumGems, int premiumCards, long premiumCharts)
            => new Tier
            {
                Points = points,
                Free = new Reward { Gems = freeGems, Cards = freeCards, Charts = freeCharts },
                Premium = new Reward
                {
                    Gems = premiumGems,
                    Cards = premiumCards,
                    Charts = premiumCharts,
                },
            };

        public static bool IsWellFormed(in Tuning tuning)
        {
            if (string.IsNullOrWhiteSpace(tuning.PremiumSku)) return false;
            if (tuning.Sources == null || tuning.Sources.Length != SourceCount) return false;
            if (tuning.Tiers == null || tuning.Tiers.Length != TierCount) return false;

            for (int i = 0; i < tuning.Sources.Length; i++)
            {
                PointSource source = tuning.Sources[i];
                if (source.Metric < 0 || source.Metric >= Goals.MetricCount) return false;
                if (source.PointsPerAction <= 0) return false;
                for (int j = 0; j < i; j++)
                    if (tuning.Sources[j].Metric == source.Metric) return false;
            }

            int previous = 0;
            for (int i = 0; i < tuning.Tiers.Length; i++)
            {
                Tier tier = tuning.Tiers[i];
                if (tier.Points <= previous) return false;
                if (!RewardIsValid(tier.Free) || !RewardIsValid(tier.Premium)) return false;
                previous = tier.Points;
            }
            return Slots <= LiveEvents.MaxSlots;
        }

        private static bool RewardIsValid(in Reward reward)
            => reward.Gems >= 0L && reward.Cards >= 0 && reward.Charts >= 0L
               && reward.CashMinutes >= 0d && !double.IsNaN(reward.CashMinutes)
               && !double.IsInfinity(reward.CashMinutes);

        public static long AddPoints(long total, long actions, int pointsPerAction)
        {
            if (actions <= 0L || pointsPerAction <= 0) return total < 0L ? 0L : total;
            if (actions > (long.MaxValue - total) / pointsPerAction) return long.MaxValue;
            return total + actions * pointsPerAction;
        }

        public static int TiersReached(in Tuning tuning, long points)
        {
            if (tuning.Tiers == null) return 0;
            int reached = 0;
            for (int i = 0; i < tuning.Tiers.Length; i++)
                if (points >= tuning.Tiers[i].Points) reached++;
            return reached;
        }
    }
}
