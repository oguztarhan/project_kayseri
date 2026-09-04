namespace Game.Core
{
    /// <summary>Pure scoring and reward rules for a short, non-network production sprint.</summary>
    public static class ProductionSprint
    {
        public const int Kind = 3;
        public const int RuleCount = 4;
        public const int MilestoneCount = 5;

        public const int RuleStart = 0;
        public const int MilestoneStart = RuleStart + RuleCount;
        public const int CursorStart = MilestoneStart + MilestoneCount;
        public const int Slots = CursorStart + Goals.MetricCount;

        public static int RuleSlot(int index) => RuleStart + index;
        public static int MilestoneSlot(int index) => MilestoneStart + index;
        public static int CursorSlot(int metric) => CursorStart + metric;

        public struct Reward
        {
            public long Gems;
            public int Cards;
            /// <summary>Cash expressed as current-income minutes, calculated only when claimed.</summary>
            public double CashMinutes;
        }

        /// <summary>One explicitly scored action. The cap keeps the event finite and balanceable.</summary>
        public struct ScoringRule
        {
            public int Metric;
            public long ActionLimit;
            public int PointsPerAction;
        }

        public struct Milestone
        {
            public long Score;
            public Reward Reward;
        }

        public struct Tuning
        {
            public ScoringRule[] Rules;
            public Milestone[] Milestones;

            public static Tuning Default => new Tuning
            {
                Rules = new[]
                {
                    new ScoringRule { Metric = Goals.Upgrades,      ActionLimit = 40, PointsPerAction = 3 },
                    new ScoringRule { Metric = Goals.Contracts,     ActionLimit = 8,  PointsPerAction = 18 },
                    new ScoringRule { Metric = Goals.Repairs,       ActionLimit = 12, PointsPerAction = 10 },
                    new ScoringRule { Metric = Goals.ForemanLevels, ActionLimit = 3,  PointsPerAction = 35 },
                },
                Milestones = new[]
                {
                    new Milestone { Score = 40,  Reward = new Reward { Gems = 10, CashMinutes = 5d } },
                    new Milestone { Score = 100, Reward = new Reward { Gems = 20, CashMinutes = 15d } },
                    new Milestone { Score = 190, Reward = new Reward { Gems = 30, Cards = 1 } },
                    new Milestone { Score = 300, Reward = new Reward { Gems = 50, Cards = 2 } },
                    new Milestone { Score = 400, Reward = new Reward { Gems = 100, Cards = 3 } },
                },
            };
        }

        public static bool RewardIsValid(in Reward reward)
        {
            return reward.Gems >= 0L && reward.Cards >= 0 && reward.CashMinutes >= 0d;
        }

        public static bool IsWellFormed(in Tuning tuning)
        {
            if (tuning.Rules == null || tuning.Rules.Length != RuleCount) return false;
            if (tuning.Milestones == null || tuning.Milestones.Length != MilestoneCount) return false;

            for (int i = 0; i < tuning.Rules.Length; i++)
            {
                ScoringRule rule = tuning.Rules[i];
                if (rule.Metric < 0 || rule.Metric >= Goals.MetricCount) return false;
                if (rule.ActionLimit <= 0L || rule.PointsPerAction <= 0) return false;
                if (rule.ActionLimit > long.MaxValue / rule.PointsPerAction) return false;
            }

            long maximum = MaximumScore(tuning);
            long previous = 0L;
            for (int i = 0; i < tuning.Milestones.Length; i++)
            {
                Milestone milestone = tuning.Milestones[i];
                if (milestone.Score <= previous || milestone.Score > maximum) return false;
                if (!RewardIsValid(milestone.Reward)) return false;
                previous = milestone.Score;
            }
            return true;
        }

        public static long MaximumScore(in Tuning tuning)
        {
            if (tuning.Rules == null) return 0L;
            long total = 0L;
            for (int i = 0; i < tuning.Rules.Length; i++)
            {
                ScoringRule rule = tuning.Rules[i];
                if (rule.ActionLimit <= 0L || rule.PointsPerAction <= 0 ||
                    rule.ActionLimit > long.MaxValue / rule.PointsPerAction) return 0L;
                long contribution = rule.ActionLimit * rule.PointsPerAction;
                if (total > long.MaxValue - contribution) return 0L;
                total += contribution;
            }
            return total;
        }

        public static long RuleScore(in ScoringRule rule, long actions)
        {
            if (actions <= 0L || rule.ActionLimit <= 0L || rule.PointsPerAction <= 0) return 0L;
            long counted = actions < rule.ActionLimit ? actions : rule.ActionLimit;
            return counted * rule.PointsPerAction;
        }
    }
}
