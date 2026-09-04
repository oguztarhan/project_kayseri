namespace Game.Core
{
    /// <summary>Pure rules and shipped balance for the deterministic Harbor Festival module.</summary>
    public static class HarborFestival
    {
        public const int Kind = 2;
        public const int TaskCount = 6;
        public const int TierCount = 8;
        public const int CatalogueCount = 4;

        public const int TaskStart = 0;
        public const int FreeTierStart = TaskStart + TaskCount;
        public const int PremiumTierStart = FreeTierStart + TierCount;
        public const int CatalogueStart = PremiumTierStart + TierCount;
        public const int CursorStart = CatalogueStart + CatalogueCount;
        public const int ExpirySlot = CursorStart + Goals.MetricCount;
        public const int Slots = ExpirySlot + 1;

        public static int TaskSlot(int index) => TaskStart + index;
        public static int FreeTierSlot(int index) => FreeTierStart + index;
        public static int PremiumTierSlot(int index) => PremiumTierStart + index;
        public static int CatalogueSlot(int index) => CatalogueStart + index;
        public static int CursorSlot(int metric) => CursorStart + metric;

        public struct Reward
        {
            public long Gems;
            public int Cards;
            public long Charts;
            public double BoostMult;
            public double BoostSeconds;
        }

        public struct Task
        {
            public int Metric;
            public long Target;
            public int Tokens;
            public Reward Reward;
        }

        public struct Tier
        {
            public int Tokens;
            public Reward Free;
            public Reward Premium;
        }

        public struct CatalogueItem
        {
            public int Cost;
            public Reward Reward;
        }

        public struct Tuning
        {
            public Task[] Tasks;
            public Tier[] Tiers;
            public CatalogueItem[] Catalogue;
            public int TokensPerExpiryGem;
            public string PremiumSku;

            public static Tuning Default => new Tuning
            {
                Tasks = new[]
                {
                    new Task { Metric = Goals.Upgrades,      Target = 10, Tokens = 30, Reward = new Reward { Gems = 20 } },
                    new Task { Metric = Goals.Contracts,     Target = 3,  Tokens = 40, Reward = new Reward { Gems = 25 } },
                    new Task { Metric = Goals.Repairs,       Target = 6,  Tokens = 40, Reward = new Reward { Gems = 25 } },
                    new Task { Metric = Goals.ForemanLevels, Target = 2,  Tokens = 50, Reward = new Reward { Gems = 30, Cards = 1 } },
                    new Task { Metric = Goals.Upgrades,      Target = 30, Tokens = 60, Reward = new Reward { Gems = 40 } },
                    new Task { Metric = Goals.Contracts,     Target = 8,  Tokens = 80, Reward = new Reward { Gems = 50, Cards = 1 } },
                },
                Tiers = new[]
                {
                    new Tier { Tokens = 30,  Free = new Reward { Gems = 25 }, Premium = new Reward { Gems = 50 } },
                    new Tier { Tokens = 70,  Free = new Reward { Cards = 1 }, Premium = new Reward { Cards = 2 } },
                    new Tier { Tokens = 110, Free = new Reward { Gems = 40 }, Premium = new Reward { Charts = 30 } },
                    new Tier { Tokens = 150, Free = new Reward { Charts = 20 }, Premium = new Reward { Gems = 80 } },
                    new Tier { Tokens = 190, Free = new Reward { Gems = 60 }, Premium = new Reward { Cards = 3 } },
                    new Tier { Tokens = 230, Free = new Reward { Cards = 2 }, Premium = new Reward { Charts = 60 } },
                    new Tier { Tokens = 270, Free = new Reward { BoostMult = 2d, BoostSeconds = 1800d }, Premium = new Reward { Gems = 120 } },
                    new Tier { Tokens = 300, Free = new Reward { Gems = 120, Charts = 40 }, Premium = new Reward { Gems = 200, Cards = 4 } },
                },
                Catalogue = new[]
                {
                    new CatalogueItem { Cost = 40,  Reward = new Reward { Gems = 45 } },
                    new CatalogueItem { Cost = 70,  Reward = new Reward { Cards = 2 } },
                    new CatalogueItem { Cost = 100, Reward = new Reward { Charts = 60 } },
                    new CatalogueItem { Cost = 140, Reward = new Reward { BoostMult = 2d, BoostSeconds = 3600d } },
                },
                TokensPerExpiryGem = 10,
                PremiumSku = string.Empty,
            };
        }

        public static bool RewardIsValid(in Reward reward)
        {
            if (reward.Gems < 0L || reward.Cards < 0 || reward.Charts < 0L) return false;
            if (reward.BoostMult < 0d || reward.BoostSeconds < 0d) return false;
            bool hasMult = reward.BoostMult > 1d;
            bool hasSeconds = reward.BoostSeconds > 0d;
            return hasMult == hasSeconds;
        }

        public static bool IsWellFormed(in Tuning tuning)
        {
            if (tuning.Tasks == null || tuning.Tasks.Length != TaskCount) return false;
            if (tuning.Tiers == null || tuning.Tiers.Length != TierCount) return false;
            if (tuning.Catalogue == null || tuning.Catalogue.Length != CatalogueCount) return false;
            if (tuning.TokensPerExpiryGem <= 0) return false;

            int total = 0;
            for (int i = 0; i < tuning.Tasks.Length; i++)
            {
                Task task = tuning.Tasks[i];
                if (task.Metric < 0 || task.Metric >= Goals.MetricCount) return false;
                if (task.Target <= 0L || task.Tokens <= 0 || !RewardIsValid(task.Reward)) return false;
                total += task.Tokens;
            }

            int previous = 0;
            for (int i = 0; i < tuning.Tiers.Length; i++)
            {
                Tier tier = tuning.Tiers[i];
                if (tier.Tokens <= previous || tier.Tokens > total) return false;
                if (!RewardIsValid(tier.Free) || !RewardIsValid(tier.Premium)) return false;
                previous = tier.Tokens;
            }

            for (int i = 0; i < tuning.Catalogue.Length; i++)
            {
                CatalogueItem item = tuning.Catalogue[i];
                if (item.Cost <= 0 || item.Cost > total || !RewardIsValid(item.Reward)) return false;
            }
            return true;
        }

        public static int TotalTokens(in Tuning tuning)
        {
            if (tuning.Tasks == null) return 0;
            int total = 0;
            for (int i = 0; i < tuning.Tasks.Length; i++) total += tuning.Tasks[i].Tokens;
            return total;
        }
    }
}
